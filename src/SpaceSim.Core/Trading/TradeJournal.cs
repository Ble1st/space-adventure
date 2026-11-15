using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Trading;

/// <summary>Art der gebuchten Handelstransaktion (Runde 39).</summary>
public enum TradeKind { Buy, Sell, SellAll }

/// <summary>
/// Runde 39: eine Ware einer gebuchten Transaktion — bewegte Menge, gebuchter Betrag (Kauf: Kosten, Verkauf: Erlös),
/// Lager der Station und Kasse des Schiffs vor/nach der Buchung sowie das Abbild des Rückkauf-Postens
/// (<see cref="PlayerLedger"/>) vor und nach der Buchung.
/// </summary>
public sealed class TradeLine
{
    public GoodId Good;
    public float Units, Amount;
    public float StockBefore, StockAfter;
    public float CashBefore, CashAfter;
    public LedgerPost? PostBefore, PostAfter;
}

/// <summary>
/// Runde 39: die letzte Buchung des Handelsdienstes an einer Station (das eine rücknehmbare Geschäft). Wird im
/// <see cref="TradeJournal"/> gehalten — nicht gespeichert, nicht gehasht.
/// </summary>
public sealed class TradeEntry
{
    public ShipId Ship;
    public StationId Station;
    public long Tick;
    public TradeKind Kind;
    public List<TradeLine> Lines = new();
    /// <summary>Exportchargen (<see cref="Simulation.ExportRules"/>), die durch diesen Verkauf entstanden (noch nicht fällig).</summary>
    public List<ExportBatch> Batches = new();
    /// <summary>
    /// Runde 41 (Aufträge): Fortschritt, der durch diese Buchung entstand (Auftrags-Id, ΔVerkauft, ΔGekauft an der Zielstation)
    /// und beim Rückgängig mit zurückgesetzt wird (<see cref="Missions.MissionSystem.RollbackProgress"/>). Leer, wenn die Buchung
    /// keinen Auftrag betrifft.
    /// </summary>
    public List<(int MissionId, float DSold, float DBought)>? Progress;
}

/// <summary>Runde 39: eine Zeile des <see cref="Events.TradeUndone"/>-Ereignisses (Ware, Menge, Geldfluss; negativ = Zahlung des Spielers).</summary>
public sealed record UndoLine(GoodId Good, float Units, float Money);

/// <summary>Runde 39: Ergebnis von <see cref="TradeActions.Undo"/> — <paramref name="Amount"/> = Veränderung der Spielerkasse (positiv = Erstattung).</summary>
public sealed record UndoResult(bool Ok, string? Reason, float Amount, float Units);

/// <summary>
/// Runde 39: Journal der letzten Handelsbuchung (ein Eintrag, keine Stapel) — die Grundlage von „Rückgängig" im Handel.
/// Abgeleiteter Zustand wie <c>Universe.Events</c>: <b>nicht gespeichert, nicht gehasht</b> (ein kurzer Bedienschritt, kein
/// Weltzustand); ein neues oder geladenes Universum beginnt mit leerem Journal. Abdocken, Schiffswechsel und das Entfernen
/// der Station löschen den Eintrag (Hook an den Stellen, die <see cref="Ship.DockedAt"/> auflösen).
/// </summary>
public sealed class TradeJournal
{
    /// <summary>Zeitfenster der Rücknahme in Ticks (2 Minuten Spielzeit).</summary>
    public const long WindowTicks = 480;

    /// <summary>Die eine letzte Buchung, sonst null. Eine neue Buchung ersetzt sie; Rückgängig löscht sie.</summary>
    public TradeEntry? Last;

    public void Clear() => Last = null;

    /// <summary>Setzt den Eintrag (eine neue Buchung ersetzt die alte).</summary>
    public void Record(TradeEntry e) => Last = e;

    /// <summary>Abbild (Kopie) des rohen Rückkauf-Postens zu (Spieler-Akteur, Station, Ware); null = keiner.</summary>
    public static LedgerPost? PostSnap(Universe u, Station st, GoodId good)
    {
        var key = (u.PlayerActor.Value, st.Id.Value, good.Value);
        return u.Ledger.TryGetValue(key, out var p) ? Copy(p) : null;
    }

    /// <summary>Tiefen-Kopie eines Postens (das Journal speichert Abbild vorher/nachher).</summary>
    public static LedgerPost Copy(LedgerPost p) => new()
    {
        BoughtQty = p.BoughtQty, BoughtPrice = p.BoughtPrice, BoughtStockBefore = p.BoughtStockBefore,
        SoldQty = p.SoldQty, SoldPrice = p.SoldPrice, SoldStockBefore = p.SoldStockBefore,
        BoughtOpen = p.BoughtOpen, SoldOpen = p.SoldOpen, LastTick = p.LastTick,
        MissingQty = p.MissingQty, MissingSeen = p.MissingSeen,
    };

    /// <summary>Vergleich zweier Posten-Abbilder (Feld für Feld, mit Rundungstoleranz).</summary>
    public static bool Same(LedgerPost? a, LedgerPost? b)
    {
        if (a is null || b is null) return a is null && b is null;
        const float e = 0.0001f;
        return MathF.Abs(a.BoughtQty - b.BoughtQty) <= e && MathF.Abs(a.BoughtPrice - b.BoughtPrice) <= e && MathF.Abs(a.BoughtStockBefore - b.BoughtStockBefore) <= e
            && MathF.Abs(a.SoldQty - b.SoldQty) <= e && MathF.Abs(a.SoldPrice - b.SoldPrice) <= e && MathF.Abs(a.SoldStockBefore - b.SoldStockBefore) <= e
            && a.BoughtOpen == b.BoughtOpen && a.SoldOpen == b.SoldOpen && a.LastTick == b.LastTick
            && MathF.Abs(a.MissingQty - b.MissingQty) <= e && MathF.Abs(a.MissingSeen - b.MissingSeen) <= e;
    }

    /// <summary>
    /// Runde 41 (A1): Vergleich für die Rücknahme-Sperre — <paramref name="after"/> = Abbild nach der Buchung, <paramref name="raw"/> = der
    /// Posten jetzt. Erlaubt sind nur die Änderungen, die der Verfall (<see cref="PlayerLedger.Prune"/>) allein im Tick vornehmen kann:
    /// Teile schließen (Open-Flag fällt, Menge schrumpft, Posten wird leer und entfernt), MissingQty wird aufgefüllt. Eine Buchung
    /// (anderes eigenes Schiff) erkennt der Tick <see cref="LedgerPost.LastTick"/> oder eine wachsende Menge. Kauf direkt vor Verkauf derselben
    /// Ware an derselben Station: der Verkauf baut den Kauf-Posten ab, der Verfall schließt ihn im nächsten Tick — die Rücknahme des Verkaufs
    /// bleibt erlaubt und stellt das Abbild vor dem Verkauf wieder her (Rückkaufdeckel wie nach dem Kauf; kein Gewinn, die Beträge der
    /// Rücknahme rechnen gegen den Markt jetzt, nicht gegen das Abbild).
    /// </summary>
    public static bool SameOrPruned(LedgerPost? after, LedgerPost? raw)
    {
        if (after is null) return raw is null;
        if (raw is null) return true;   // Verfall hat den ganzen Posten entfernt
        const float e = 0.0001f;
        if (raw.LastTick != after.LastTick) return false;   // eine Buchung hat angefasst
        if (MathF.Abs(raw.BoughtPrice - after.BoughtPrice) > e || MathF.Abs(raw.BoughtStockBefore - after.BoughtStockBefore) > e) return false;
        if (MathF.Abs(raw.SoldPrice - after.SoldPrice) > e || MathF.Abs(raw.SoldStockBefore - after.SoldStockBefore) > e) return false;
        if (raw.BoughtQty > after.BoughtQty + e || raw.SoldQty > after.SoldQty + e) return false;   // Verfall schrumpft nur
        if (raw.BoughtOpen && !after.BoughtOpen) return false;
        if (raw.SoldOpen && !after.SoldOpen) return false;
        if (raw.MissingQty > after.MissingQty + e) return false;
        return true;
    }

    /// <summary>Kurztext der Buchung für Angebot und Meldung, z. B. „Kauf 10 Energie · 1:42“ (Dauer seit der Buchung als m:ss).</summary>
    public static string Describe(Universe u, TradeEntry e)
    {
        string head = e.Lines.Count == 0 ? "Buchung" : e.Kind switch
        {
            TradeKind.Buy => $"Kauf {e.Lines[0].Units:0.#} {u.GoodOf(e.Lines[0].Good).Name}",
            TradeKind.Sell => $"Verkauf {e.Lines[0].Units:0.#} {u.GoodOf(e.Lines[0].Good).Name}",
            _ => $"Verkauf alles ({e.Lines.Count} Waren)",
        };
        return $"{head} · {ExportInfo.Duration(Math.Max(0L, u.Tick - e.Tick))}";
    }
}

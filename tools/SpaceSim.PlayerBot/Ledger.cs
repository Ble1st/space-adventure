using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.PlayerBot;

/// <summary>
/// Kassenabgleich: jede Änderung der Spielerkasse wird einer Quelle zugeordnet. Bot-Aktionen (Dienste, Comms …) über <see cref="Pilot.Book"/> (Kassenänderung
/// um den Aufruf), Simulationstakte über Ereignisse; was ein Takt an Kassenänderung nicht durch Ereignisse erklärt, steht als „unerklärt“ — nichts wird
/// versteckt. <see cref="Sim"/> = Summe der Takte, <see cref="Bot"/> = Summe der Bot-Aktionen.
/// </summary>
public sealed class Ledger
{
    readonly Universe U;
    public readonly Dictionary<string, double> Sources = new();
    public readonly Dictionary<string, double> Window = new();   // seit der letzten Stunde
    double _explained, _w0;
    public double Sim, Bot, Unexplained;
    public double Start;

    public Ledger(Universe u)
    {
        U = u; Start = u.PlayerWallet.Credits;
        u.Events.Raised += OnEvent;
    }

    void Add(string k, double v)
    {
        if (v == 0) return;
        _explained += v;
        Sources[k] = Sources.GetValueOrDefault(k) + v; Window[k] = Window.GetValueOrDefault(k) + v;
    }

    bool Mine(ShipId s) => U.Ships.TryGetValue(s, out var sh) && sh.Owner == U.PlayerActor;

    string ShipKind(ShipId s) => !U.Ships.TryGetValue(s, out var sh) ? "?" : sh.Command switch { MineCommand => "Flotte Schürfen", TradeCommand => "Flotte Handel", null => "Hand", _ => "Flotte sonst" };

    readonly HashSet<string> _types = new();
    public readonly Dictionary<string, (double resid, int ticks)> ResidByType = new();
    void OnEvent(SimEvent e)
    {
        var me = U.PlayerActor;
        if (!InBot) _types.Add(e.GetType().Name);
        switch (e)
        {
            case PlayerSold s when Mine(s.Ship) && !InBot: Add("Verkauf " + ShipKind(s.Ship), s.Revenue); break;
            case PlayerBought b when Mine(b.Ship) && !InBot: Add("Einkauf " + ShipKind(b.Ship), -b.Cost); break;
            case TraderBought tb when Mine(tb.Ship): Add("Flotte Handel Einkauf", -tb.Cost); break;
            case TradeCompleted tc when Mine(tc.Ship): Add("Flotte Handel Verkauf", tc.Revenue); break;
            case TreasuryUpkeep t when t.Actor == me: Add("Kosten " + t.Purpose, -t.Amount); break;
            case TaxCollected tx when tx.Treasury == me: Add("Einnahme Vermögensabgabe eigener Stationen", tx.Amount); break;
            case TreasuryIncome t when t.Actor == me: Add("Einnahme " + t.Purpose, t.Amount); break;
            case FleetLevyPaid fl: Add("Kosten Flottenabgabe (Welle B)", -fl.Levy); break;
            case FleetUpkeepPaid f when f.Actor == me: Add("Kosten Flottenunterhalt", -f.Amount); break;
            case BerthFeePaid f when f.Actor == me: Add("Kosten Liegegebühr", -f.Amount); break;
            case RentCharged r: Add("Kosten Miete", -(r.Amount - r.Debt)); break;
            case MissionPaid m when m.Client != me: Add("Aufträge", m.Amount); break;
            case TollPaid tp when Mine(tp.Ship) && !InBot: Add("Kosten Zoll", -tp.Amount); break;
            case TollPaid tp2 when !Mine(tp2.Ship) && U.PlayerFaction is { } pf && tp2.To == pf: Add("Einnahme Zoll (fremde Schiffe)", tp2.Amount); break;
            case TributePaid tr when tr.Payer != me && U.PlayerFaction is { } pf2 && tr.To == pf2: Add("Einnahme Tribut", tr.Amount); break;
            case FuelBought fb when Mine(fb.Ship) && !InBot: Add("Kosten Treibstoff (Flotte)", -fb.Cost); break;
            case EmergencyRefuel er when Mine(er.Ship) && !InBot: Add("Kosten Notbetankung", -er.Cost); break;
            case AmmoRearmed ar when ar.Payer == me && !ar.FromTreasury && !InBot: Add("Kosten Munition", -ar.Cost); break;
            case ShipRepaired sr when Mine(sr.Ship) && !sr.AtBase && !InBot: Add("Kosten Reparatur (Flotte)", -sr.Cost); break;
            case StationCashMoved sc when !InBot: Add("Umbuchung Station", -sc.Amount); break;
        }
    }

    public bool InBot;

    bool _saveBot;
    public void BeginSim() { _types.Clear(); _explained = 0; _w0 = U.PlayerWallet.Credits; _saveBot = InBot; InBot = false; }
    public void EndSim()
    {
        InBot = _saveBot;
        double d = U.PlayerWallet.Credits - _w0;
        Sim += d;
        double un = d - _explained;
        if (Math.Abs(un) > 1.0) foreach (var t in _types) { var o = ResidByType.GetValueOrDefault(t); ResidByType[t] = (o.resid + un, o.ticks + 1); }
        if (Math.Abs(un) > 0.01) { Unexplained += un; Sources["UNERKLÄRT (Takt)"] = Sources.GetValueOrDefault("UNERKLÄRT (Takt)") + un; Window["UNERKLÄRT (Takt)"] = Window.GetValueOrDefault("UNERKLÄRT (Takt)") + un; }
    }

    public T Book<T>(string label, Func<T> f)
    {
        double w0 = U.PlayerWallet.Credits, s0 = Sim; bool outer = InBot; InBot = true;
        try { return f(); }
        finally
        {
            InBot = outer;
            double d = U.PlayerWallet.Credits - w0 - (Sim - s0);   // Takte innerhalb des Aufrufs zählen zu Sim
            Bot += d;
            if (Math.Abs(d) > 0.001)
            {
                string k = "Bot: " + label;
                Sources[k] = Sources.GetValueOrDefault(k) + d; Window[k] = Window.GetValueOrDefault(k) + d;
            }
        }
    }

    public string Report(Dictionary<string, double> src, double hours)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var kv in src.OrderByDescending(k => Math.Abs(k.Value)))
            sb.AppendLine($"  {kv.Key,-38} {kv.Value,12:0}  ({kv.Value / Math.Max(0.01, hours),9:0} Cr/h)");
        return sb.ToString();
    }

    /// <summary>Stimmt die Kasse? Start + Σ Takte + Σ Bot-Aktionen = aktuelle Kasse (muss exakt aufgehen), und der unerklärte Anteil der Takte.</summary>
    public string ResidReport() => string.Join("\n", ResidByType.OrderByDescending(k => Math.Abs(k.Value.resid)).Take(12).Select(k => $"    {k.Key}: Summe Restbetrag in Takten mit diesem Ereignis {k.Value.resid:0} ({k.Value.ticks} Takte)"));
    public string Check() =>
        $"Kassenabgleich: Start {Start:0} + Takte {Sim:0} + Bot {Bot:0} = {Start + Sim + Bot:0} (Kasse {U.PlayerWallet.Credits:0}, Differenz {U.PlayerWallet.Credits - (Start + Sim + Bot):0.##}); in Takten durch Ereignisse nicht erklärt {Unexplained:0} Cr";
}

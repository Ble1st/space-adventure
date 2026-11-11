using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>Anzeige der Sektorpolitik (rein, für Universumskarte, Sektoransicht und Station-Info).</summary>
/// <param name="Owner">Name der Besitzerfraktion.</param>
/// <param name="Stage">Kontrollstufe (Präsenz … Kontrolle), mit „umkämpft“.</param>
/// <param name="Influence">Einflussanteile, absteigend („Union 69 % · Gilde 31 %“).</param>
/// <param name="Lock">Sperre in Worten („Offen“, „Stufe 3 · Nur Verbündete · gegen Union · Ausreise frei“).</param>
/// <param name="EntryLevel">Wirksame Einreisestufe (0 bei Papier-Sperre).</param>
/// <param name="ForPlayer">Was der Sektor dem Spielerschiff an seinen Gates abverlangt („frei“, „Zoll 16 Cr“, „gesperrt: …“).</param>
/// <param name="PlayerDenied">Kommt das Spielerschiff über mindestens ein Gate nicht hinein?</param>
public sealed record SectorPolitics(string Owner, string Stage, string Influence, string Lock, int EntryLevel, string ForPlayer, bool PlayerDenied, bool Contested);

/// <summary>Anzeige eines Gates aus Sicht eines Schiffs: Zustand und Urteil des Kontrollpunkts.</summary>
public sealed record GatePolitics(string State, string Verdict, bool Denied, float Toll);

/// <summary>Politik einer Station für die Station-Info: Ruf (Beziehung des Besitzers zu dir), Zoll und Sperrstufe des Sektors, Hinweise.</summary>
public sealed record StationPolitics(string Reputation, string Customs, string Lock, IReadOnlyList<string> Notes);

/// <summary>
/// Reine Anzeigefunktionen der Phase 10 (wie <see cref="ConstructionInfo"/>): Die Oberfläche zeichnet nur, was diese Funktionen aus Core-Daten
/// liefern — Sektorbesitz, Kontrollstufe, Einfluss, Sperrstufe und das Urteil der Kontrollpunkte für das Spielerschiff.
/// </summary>
public static class AccessInfo
{
    static string FactionName(Universe u, FactionId f) => u.Factions.TryGetValue(f, out var x) ? x.Name : "niemand";

    /// <summary>Politik eines Sektors aus Sicht eines Schiffs (Spieler).</summary>
    public static SectorPolitics ForSector(Universe u, SectorId id, Ship ship)
    {
        var s = u.Sectors[id];
        var acc = s.Access;
        string stage = AccessNames.Stage(s.Control) + (s.Contested ? " · umkämpft" : "");
        string inf = s.Influence.Count == 0 ? "keine Stationen"
            : string.Join(" · ", s.Influence.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key.Value).Select(kv => $"{FactionName(u, kv.Key)} {kv.Value * 100f:0} %"));
        string lockText = acc.IsOpen ? "Offen"
            : $"Stufe {acc.EntryLevel} · {AccessNames.Level(acc.EntryLevel)}"
              + (acc.Against is { } a ? $" · gegen {FactionName(u, a)}" : "")
              + (acc.ExitLevel >= 5 ? " · keine Ausreise" : " · Ausreise frei")
              + (acc.Enforced ? "" : " · nur auf dem Papier (Kasse leer)");
        int level = acc.Level(AccessDirection.Entry);
        // Urteil für das Schiff: über jedes Gate in den Sektor hinein (das schlechteste zählt)
        var profile = GateAccess.ProfileOf(u, ship);
        GatePassage? worst = null;
        foreach (var gid in s.Gates)
        {
            var inbound = u.Gates[u.Gates[gid].Target];
            var p = GateAccess.Evaluate(u, inbound, profile, ship);
            if (worst is null || p.Policy > worst.Value.Policy || (p.Policy == worst.Value.Policy && p.Toll > worst.Value.Toll)) worst = p;
        }
        string forPlayer = worst is not { } w || w.Policy == AccessPolicy.Allow ? "frei"
            : w.Policy == AccessPolicy.Toll ? $"Zoll {w.Toll:0} Cr je Einreise" : $"gesperrt: {w.Reason}";
        return new SectorPolitics(FactionName(u, s.Owner), stage, inf, lockText, level, forPlayer, worst is { Policy: AccessPolicy.Deny }, s.Contested);
    }

    /// <summary>Gate aus Sicht eines Schiffs: Zustand und was der Kontrollpunkt verlangt.</summary>
    public static GatePolitics ForGate(Universe u, Gate gate, Ship ship)
    {
        var p = GateAccess.Evaluate(u, gate, GateAccess.ProfileOf(u, ship), ship);
        string verdict = p.Policy switch
        {
            AccessPolicy.Allow => "frei passierbar",
            AccessPolicy.Toll => $"Zoll {p.Toll:0} Cr",
            _ => $"für dich gesperrt: {p.Reason}",
        };
        return new GatePolitics(AccessNames.Gate(gate.State), verdict, p.Policy == AccessPolicy.Deny, p.Toll);
    }

    /// <summary>Politik einer Station: Ruf (Beziehung des Besitzers zum Schiffseigner), Zoll und Sperrstufe ihres Sektors, Hinweise.</summary>
    public static StationPolitics ForStation(Universe u, Station st, Ship ship)
    {
        float rel = Relations.Effective(u, st.Owner, ship.Owner);
        string rep = $"{Relations.Label(rel)} ({rel:+0.00;-0.00;0.00})";
        var sp = ForSector(u, st.Sector, ship);
        string customs = sp.ForPlayer == "frei" ? "kein Zoll" : sp.ForPlayer;
        var notes = new List<string>();
        var kind = StationProtection.KindOf(u, st);
        if (kind != GovernmentKind.None) notes.Add($"{AccessNames.Government(kind)} — {(StationProtection.IsGovernment(u, st) ? "Regierungsstation, Unterhalt aus der Kasse" : "Wache, Unterhalt aus der Kasse")}");
        if (st.Immortal) notes.Add("unzerstörbar");
        notes.Add($"Sektor: {sp.Owner} · {sp.Stage}");
        if (sp.Contested) notes.Add("Umkämpft: höchstens Stufe 1 durchsetzbar");
        if (sp.PlayerDenied) notes.Add("Einreise für dich gesperrt");
        if (!StationProtection.IsCapturable(u, st)) notes.Add("nicht eroberbar");
        return new StationPolitics(rep, customs, sp.Lock, notes);
    }
}

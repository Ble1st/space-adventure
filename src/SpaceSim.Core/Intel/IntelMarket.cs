using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Intel;

/// <summary>Ein Intel-Paket einer Fraktion: Schlüssel (<c>sector:&lt;S&gt;</c>, <c>territory</c>, <c>stations:&lt;Typ&gt;</c>), Text, Preis, Umfang, Sperrgrund.</summary>
public sealed record IntelPackage(string Key, string Text, string Detail, float Price, int Items, string? Issue);

/// <summary>
/// Ausbau Punkt 16: Intel kaufen (Comms-Diplomatie und Behörde). Nur bei bekannten Fraktionen, die nicht feindlich sind (Beziehung über
/// <see cref="EconomyRules.HostileRelation"/>, kein Krieg). Preis je Umfang: Sektor + unbekannte Stationen + unbekannte Felder; Gebietskarte mit Rabatt.
/// Das Geld geht aus der Spielerkasse in die Kasse der Fraktion (<see cref="TreasuryIncome"/> „intel“, <see cref="IntelBought"/>). Rein bis auf <see cref="Buy"/>.
/// </summary>
public static class IntelMarket
{
    /// <summary>Darf der Spieler bei dieser Fraktion Intel kaufen? null = ja.</summary>
    public static string? FactionIssue(Universe u, FactionId f)
    {
        if (!Knowledge.Limited(u)) return "Du kennst schon alles.";
        if (!u.Factions.ContainsKey(f) || u.IsPlayerFaction(f)) return "Keine fremde Fraktion.";
        if (!Knowledge.KnowsFaction(u, f)) return "Fraktion unbekannt.";
        if (Wars.PlayerWar(u, f) is not null) return "Im Krieg verkauft sie dir nichts.";
        if (Relations.Effective(u, new ActorId(f.Value), u.PlayerActor) <= u.Rules.HostileRelation) return "Sie ist dir feindlich gesinnt.";
        return null;
    }

    static IEnumerable<Sector> Territory(Universe u, FactionId f) => u.Sectors.Values.Where(s => s.Owner == f).OrderBy(s => s.Id.Value);

    /// <summary>Was eine Sektorkarte noch Neues bringt: (Sektor neu?, unbekannte Stationen, unbekannte Felder).</summary>
    static (bool Sector, int Stations, int Fields) Missing(Universe u, Sector s)
    {
        var i = Knowledge.Of(u)!;
        bool sec = i.StateOf(s.Id) < SectorKnowledge.Visited || s.Gates.Any(g => !i.Gates.Contains(g));
        int st = s.Stations.Count(id => !Knowledge.KnowsStation(u, id));
        int fl = 0;
        for (int k = 0; k < s.Fields.Count; k++) if (!i.Fields.Contains((s.Id.Value, k))) fl++;
        for (int k = 0; k < s.GasFields.Count; k++) if (!i.GasFields.Contains((s.Id.Value, k))) fl++;
        return (sec, st, fl);
    }

    static float SectorPrice(Universe u, (bool Sector, int Stations, int Fields) m) =>
        (m.Sector ? u.Rules.IntelSectorPrice : 0f) + m.Stations * u.Rules.IntelStationPrice + m.Fields * u.Rules.IntelFieldPrice;

    static string Name(Universe u, SectorId s) => u.Sectors.TryGetValue(s, out var sec) ? sec.Name.Replace("Sektor ", "") : "?";

    /// <summary>Alle Pakete der Fraktion (feste Reihenfolge: Sektorkarten nach Id, Gebietskarte, Stationslisten nach Typschlüssel). Leer bei Sperre.</summary>
    public static List<IntelPackage> Packages(Universe u, FactionId f)
    {
        var list = new List<IntelPackage>();
        if (FactionIssue(u, f) is not null) return list;
        float credits = u.PlayerWallet.Credits;
        string? Afford(float p) => credits >= p ? null : $"{p:0} Cr — zu wenig Credits.";
        float total = 0f; int sectors = 0, items = 0;
        foreach (var s in Territory(u, f))
        {
            var m = Missing(u, s);
            if (!m.Sector && m.Stations == 0 && m.Fields == 0) continue;
            float p = SectorPrice(u, m);
            total += p; sectors++; items += (m.Sector ? 1 : 0) + m.Stations + m.Fields;
            list.Add(new IntelPackage($"sector:{s.Id.Value}", $"Sektorkarte {Name(u, s.Id)}",
                $"{(m.Sector ? "Sektor und Tore" : "Sektor bekannt")}, {m.Stations} Station{(m.Stations == 1 ? "" : "en")}, {m.Fields} Feld{(m.Fields == 1 ? "" : "er")}", MathF.Round(p), (m.Sector ? 1 : 0) + m.Stations + m.Fields, Afford(MathF.Round(p))));
        }
        if (sectors >= 2)
        {
            float p = MathF.Round(total * u.Rules.IntelTerritoryDiscount);
            list.Add(new IntelPackage("territory", "Gebietskarte", $"alle {sectors} Sektoren der Fraktion mit Stationen und Feldern", p, items, Afford(p)));
        }
        // Stationslisten: Stationen eines Typs, die der Fraktion gehören oder in ihrem Gebiet liegen
        foreach (var g in Mine(u, f).Where(st => !Knowledge.KnowsStation(u, st)).GroupBy(st => st.TypeKey).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            int n = g.Count();
            float p = n * u.Rules.IntelStationPrice + 40f;
            string type = u.StationTypes.TryGetValue(g.Key, out var t) ? t.Name : g.Key;
            list.Add(new IntelPackage($"stations:{g.Key}", $"Stationsliste {type}", $"{n} unbekannte Station{(n == 1 ? "" : "en")} mit Sektor", p, n, Afford(p)));
        }
        return list;
    }

    static IEnumerable<Station> Mine(Universe u, FactionId f) =>
        u.StationsById().Where(st => !st.IsBuildSite && (u.FactionOf(st) == f || (u.Sectors.TryGetValue(st.Sector, out var sec) && sec.Owner == f)));

    /// <summary>Vorschau eines Pakets (null = unbekannt bzw. nichts mehr zu erfahren).</summary>
    public static IntelPackage? Preview(Universe u, FactionId f, string key) => Packages(u, f).FirstOrDefault(p => p.Key == key);

    /// <summary>Kauft ein Paket. Fehlschlag ändert nichts und publiziert nichts. Erfolg: Kasse → Fraktion, Wissen aufgedeckt, Ereignisse.</summary>
    public static bool Buy(Universe u, FactionId f, string key, out string message)
    {
        if (FactionIssue(u, f) is { } fi) { message = fi; return false; }
        if (Preview(u, f, key) is not { } pkg) { message = "Dieses Paket gibt es nicht (oder es ist nichts Neues darin)."; return false; }
        if (pkg.Issue is { } pi) { message = pi; return false; }
        u.PlayerWallet.Credits -= pkg.Price;
        if (u.Actors.TryGetValue(new ActorId(f.Value), out var fa)) fa.Treasury.Credits += pkg.Price;
        var d = new IntelDelta();
        SectorId at = u.PlayerSector;
        if (key.StartsWith("sector:", StringComparison.Ordinal) && int.TryParse(key[7..], out var sid)) { at = new SectorId(sid); d = Knowledge.RevealSectorFull(u, at); }
        else if (key == "territory") foreach (var s in Territory(u, f)) d.Add(Knowledge.RevealSectorFull(u, s.Id));
        else if (key.StartsWith("stations:", StringComparison.Ordinal))
            foreach (var st in Mine(u, f).Where(st => st.TypeKey == key[9..])) d.Add(Knowledge.RevealStation(u, st.Id));
        string fname = u.Factions.TryGetValue(f, out var fac) ? fac.Name : "?";
        message = $"{pkg.Text} von {fname} gekauft ({pkg.Price:0} Cr).";
        u.Events.Publish(new TreasuryIncome(new ActorId(f.Value), "intel", pkg.Price));
        u.Events.Publish(new IntelBought(f, key, pkg.Price, message));
        Knowledge.Publish(u, at, d, "intel");
        return true;
    }
}

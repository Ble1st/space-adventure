using SpaceSim.Core.Combat;
using SpaceSim.Core.Model;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;

namespace SpaceSim.PlayerBot;

/// <summary>
/// Sonnet-Test 5 (--supply): Spieler beschafft Großkampfschiff-Module selbst (Werk -> Werftregal, bis 3x Ziel), außerdem Munition/Raketen/Hüllplatten
/// an eigene oder neutrale Stationen. Nur Handelsdienst (buy:/sell:); ein Frachter des Spielers fährt (Flaggschiff-Tausch wie bei Hunter.HaulFor).
/// </summary>
public sealed partial class Empire
{
    public static bool Supply;
    public readonly List<string> SupNotes = new();
    long _supNext; public int ModHauls, ModHaulFails, ModUnits, AmmoHauls, PlateHauls;
    readonly Dictionary<string, long> _supBlock = new();
    void SupNote(string s) { SupNotes.Add($"[{U.Tick,8}] {s}"); Note("SUP: " + s); }

    Ship? Hauler(float needVol, IEnumerable<GoodId>? goods = null)
    {
        return PlayerFleet.Owned(U).Where(x => !x.IsRental() && x.Id != U.PlayerShip && x.Stats.Cargo >= needVol && (goods == null || goods.All(g => x.FreeUnits(U, g) >= 1f)) && x.UsedVolume(U) < 1f && (x.Command is TradeCommand || x.Command is MineCommand || x.Command is null) && x.Transit is null && x.Role != ShipRole.Builder)
            .OrderBy(x => x.Command is null ? 0 : 1).ThenByDescending(x => x.Stats.Cargo).FirstOrDefault();
    }

    /// <summary>Kauft good (units) beim besten Werk und liefert es bei dest ab. Gibt gelieferte Menge zurück.</summary>
    float HaulGood(GoodId gid, float units, Station dest, string tag)
    {
        var good = U.Goods[gid];
        Station? src = null; double bs = double.MaxValue;
        foreach (var st in U.StationsById())
        {
            if (st.IsBuildSite || st.Id == dest.Id || !st.Market.TryGetValue(gid, out var me) || me.Amount < 1f || !st.Produces(gid)) continue;
            if (Docking.Issue(U, st, P.Me) is not null) continue;
            var f = U.FactionOf(st); if (f is { } ff && ff != U.PlayerFaction && Wars.PlayerWar(U, ff) is not null) continue;
            if (U.FindRoute(P.Me.Sector, st.Sector, GateAccess.ProfileOf(U, P.Me)) is not { } rt) continue;
            double sc = rt.Count * 1000 + me.Price;
            if (sc < bs) { bs = sc; src = st; }
        }
        if (src is null) { SupNote($"{tag}: kein Werk mit {good.Name} auf Lager"); return 0f; }
        float stock = MathF.Floor(src.Market[gid].Amount);
        float n = MathF.Min(MathF.Ceiling(units), stock);
        n = MathF.Min(n, MathF.Floor(P.Me.FreeUnits(U, gid)));
        if (n < 1f) { SupNote($"{tag}: {good.Name} bei {src.Name} Bestand {stock:0}, Platz {P.Me.FreeUnits(U, gid):0}"); return 0f; }
        if (!P.GoToStation(src, out var why)) { SupNote($"{tag}: Weg zu {src.Name} verwehrt ({why})"); return 0f; }
        var tr = P.Svc(src, "trade"); if (tr is null) return 0f;
        var r = P.Do(src, tr, $"buy:{gid.Value}", gid, n);
        if (!r.Success) { SupNote($"{tag}: {good.Name} {n:0} bei {src.Name} kaufen: {r.Message}"); return 0f; }
        if (!P.GoToStation(dest, out var why2)) { SupNote($"{tag}: Weg zu {dest.Name} verwehrt ({why2})"); return 0f; }
        float have = P.Me.Cargo.TryGetValue(gid, out var h) ? h : 0f;
        { var ac = SpaceSim.Core.Trading.MarketInfo.Acceptance(U, dest, gid, P.Me); var de = dest.Market[gid]; SupNote($"{tag}: VOR Verkauf {good.Name} bei {dest.Name}: Bestand {de.Amount:0.0} Ziel {de.Target:0.0} Annahme {ac.Units:0.0} (Grund {ac.Reason}, Cap {ac.Cap:0.0}), Kasse Station {dest.Credits:0}, an Bord {have:0}"); }
        var rs = P.Do(dest, P.Svc(dest, "trade")!, $"sell:{gid.Value}", gid, have);
        SupNote($"{tag}: {good.Name} {n:0} bei {src.Name} für {-r.Credits:0} gekauft; bei {dest.Name} (Ziel {dest.Market[gid].Target:0}, Bestand jetzt {dest.Market[gid].Amount:0.0}) verkauft: {(rs.Success ? $"ok {rs.Credits:0}" : rs.Message)}");
        return rs.Success ? have : 0f;
    }

    bool SupplyStep()
    {
        if (!Supply || U.Tick < _supNext || Wallet < 60000f || U.PlayerFaction is null) return false;
        _supNext = U.Tick + 1000;
        var yards = U.StationsById().Where(x => !x.IsBuildSite && x.TypeKey == "shipyard" && Docking.Issue(U, x, P.Me) is null && U.FindRoute(P.Me.Sector, x.Sector, GateAccess.ProfileOf(U, P.Me)) is not null)
            .OrderBy(x => U.FindRoute(P.Me.Sector, x.Sector, GateAccess.ProfileOf(U, P.Me))!.Count).ToList();
        foreach (var key in new[] { "destroyer", "carrier", "battlecruiser", "bastion" })
            foreach (var yard in yards)
            {
                if (_supBlock.TryGetValue(key + yard.Id.Value, out var bt) && U.Tick < bt) continue;
                var q = ShipDealerService.Quote(U, yard, P.Me, key);
                if (q is null || q.Buyable || q.Missing.Count == 0 || q.Locked is not null) continue;
                var hauler = Hauler(1f, q.Missing.Select(m => m.Good).ToList());
                if (hauler is null) { SupNote("kein freier Frachter"); return false; }
                Commands.Cancel(U, hauler); B.UseFlag(hauler.Id);
                SupNote($"Modullieferung für {key} bei {yard.Name}: fehlt {string.Join(", ", q.Missing.Select(m => $"{U.Goods[m.Good].Name} {m.Units:0.#}"))}; Frachter #{hauler.Id.Value} {hauler.ModelKey} Laderaum {hauler.Stats.Cargo:0}");
                float got = 0f;
                foreach (var need in q.Missing) got += HaulGood(need.Good, need.Units, yard, key);
                B.RestoreFlag();
                var q2 = ShipDealerService.Quote(U, yard, P.Me, key);
                bool ok = q2 is { Buyable: true };
                SupNote($"Modullieferung {key}: geliefert {got:0} Stück, danach {(ok ? "LIEFERBAR" : "noch nicht lieferbar: " + string.Join(", ", q2?.Missing.Select(m => $"{U.Goods[m.Good].Name} {m.Units:0.#}") ?? Array.Empty<string>()))}");
                if (ok) ModHauls++; else { ModHaulFails++; _supBlock[key + yard.Id.Value] = U.Tick + (got > 0 ? 2000 : 15000); }
                ModUnits += (int)got;
                return true;
            }
        foreach (var (gkey, svcKey, floor, amt, label) in new[] { ("ammo", "rearm", 20f, 60f, "Munition"), ("missile", "rearm", 8f, 24f, "Raketen"), ("hull_plates", "repair", 15f, 30f, "Hüllplatten") })
        {
            var good = U.Goods.Values.FirstOrDefault(g => g.Key == gkey); if (good is null) continue;
            var dest = U.StationsById().Where(st => !st.IsBuildSite && st.Market.TryGetValue(good.Id, out var me) && me.Amount < me.Target * 0.9f && me.Target > 0 && U.StationTypes[st.TypeKey].Services.Contains(svcKey)
                    && (st.Owner == U.PlayerActor || (U.FactionOf(st) is { } ff && Relations.Effective(U, U.PlayerActor, new ActorId(ff.Value)) >= 0.05f && Wars.PlayerWar(U, ff) is null))
                    && Docking.Issue(U, st, P.Me) is null && !(_supBlock.TryGetValue(gkey + st.Id.Value, out var bt) && U.Tick < bt) && U.FindRoute(P.Me.Sector, st.Sector, GateAccess.ProfileOf(U, P.Me)) is not null)
                .OrderBy(st => st.Owner == U.PlayerActor ? 0 : 1).ThenBy(st => U.FindRoute(P.Me.Sector, st.Sector, GateAccess.ProfileOf(U, P.Me))!.Count).FirstOrDefault();
            if (dest is null) continue;
            var hauler = Hauler(amt, new[] { good.Id }); if (hauler is null) continue;
            float room = MathF.Max(1f, MathF.Floor(dest.AcceptableUnits(good.Id, U.Rules)));
            Commands.Cancel(U, hauler); B.UseFlag(hauler.Id);
            float got = HaulGood(good.Id, MathF.Min(amt, room), dest, label);
            B.RestoreFlag();
            if (got > 0) { if (gkey == "hull_plates") PlateHauls++; else AmmoHauls++; } else _supBlock[gkey + dest.Id.Value] = U.Tick + 8000;
            return true;
        }
        return false;
    }
}

using SpaceSim.Core.Comms;
using SpaceSim.Core.Flight;
using SpaceSim.Core.Missions;
using SpaceSim.Core.Model;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;
using SpaceSim.Core.Trading;

namespace SpaceSim.PlayerBot;

/// <summary>Zusatzfunktionen des Spielers: Transportaufträge und eine einmalige Probe der übrigen Dienste (nur über die Dienste).</summary>
public sealed class Extras
{
    readonly Pilot P; readonly Universe U; readonly Log L;
    public int MissionsDone, MissionsAccepted;
    readonly HashSet<string> _sampled = new();
    public Extras(Pilot p, Log l) { P = p; U = p.U; L = l; }

    /// <summary>Am angedockten Ort: den besten Transportauftrag annehmen (Belohnung je Flugsekunde), sofort ausliefern. true = ein Auftrag lief.</summary>
    public bool Missions()
    {
        var st = P.Here; var me = P.Me;
        if (st is null || P.Svc(st, "missions") is not { } svc) return false;
        MissionOffer? best = null; double bestRate = 6.0;   // mindestens 6 Cr je Sekunde
        foreach (var o in U.MissionOffers.Where(o => o.At == st.Id && o.Type == TransportMission.KeyId))
        {
            if (!U.Stations.TryGetValue(TransportMission.TargetOf(o), out var tgt)) continue;
            float vol = TransportMission.VolumeOf(o);
            if (me.CargoCapacity - me.UsedVolume(U) < vol) continue;
            float sec = PlayerTrip.Seconds(U, me.Sector, me.Position, tgt.Sector, tgt.DockPoint(me.Id), me);
            if (float.IsInfinity(sec)) continue;
            double rate = o.Reward / Math.Max(10.0, sec);
            if (rate > bestRate) { bestRate = rate; best = o; }
        }
        if (best is null) return false;
        var r = P.Do(st, svc, $"accept:{best.Id}");
        L.Event($"Auftrag {best.Id} annehmen: {(r.Success ? "ok" : r.Message)} (Belohnung {best.Reward:0})");
        if (!r.Success) return false;
        MissionsAccepted++;
        var tgt2 = U.Stations[TransportMission.TargetOf(best)];
        float before = P.Wallet;
        if (P.GoToStation(tgt2, out var why))
        {
            P.Run(5);
            L.Event($"Auftrag {best.Id}: Ziel {tgt2.Name} erreicht, Kasse {before:0}→{P.Wallet:0}");
            if (P.Wallet > before) MissionsDone++;
        }
        else L.Event($"Auftrag {best.Id}: Flug gescheitert ({why})");
        return true;
    }

    /// <summary>Einmalige Probe: jeden Dienstschlüssel einmal an einer geeigneten Station ansehen/ausführen (ohne Zwang, nur was gefahrlos geht).</summary>
    public void SampleServices()
    {
        var st = P.Here; if (st is null) return;
        foreach (var svc in P.Services.ForStation(U, st))
        {
            if (!_sampled.Add(svc.Key)) continue;
            var offers = svc.Offers(U, st, P.Me);
            string line = $"Dienst {svc.Key} bei {st.Name}: {offers.Count} Angebote";
            try
            {
                switch (svc.Key)
                {
                    case "storage":
                        if (offers.FirstOrDefault(o => o.Key.StartsWith("store:") && o.Amount > 0) is { } so)
                        { var r = P.Do(st, svc, so.Key, so.Good, 1f); line += $"; einlagern: {r.Message}"; var f = svc.Offers(U, st, P.Me).FirstOrDefault(o => o.Key.StartsWith("fetch:") && o.Amount > 0); if (f.Key is not null) line += $"; abholen: {P.Do(st, svc, f.Key, f.Good, 1f).Message}"; }
                        break;
                    case "research":
                        if (offers.FirstOrDefault(o => o.Key.StartsWith("fund:") && o.Amount > 0 && o.Price <= 600f) is { } fo) line += $"; fördern: {P.Do(st, svc, fo.Key).Message}";
                        break;
                    case "court":
                        foreach (var o in offers.Where(o => o.Amount > 0)) line += $"; Strafe: {P.Do(st, svc, o.Key).Message}";
                        break;
                    case "bounty":
                        foreach (var o in offers.Where(o => o.Key.StartsWith("claim:") && o.Amount > 0)) line += $"; Kopfgeld: {P.Do(st, svc, o.Key).Message}";
                        break;
                    case "upgrade":
                        line += $"; möglich: {offers.Count(o => o.Amount > 0)}";
                        break;
                    case "outfitter":
                        line += $"; möglich: {offers.Count(o => o.Amount > 0)}";
                        break;
                }
            }
            catch (Exception ex) { line += $"; AUSNAHME {ex.GetType().Name}: {ex.Message}"; }
            L.Event(line);
        }
    }

    /// <summary>Comms-Probe: alle Angebote mit Möglichkeit/Grund auflisten (einmal).</summary>
    public void SampleComms(CommsActions comms)
    {
        foreach (var o in comms.Offers(U)) L.Event($"Comms {o.Key}: {(o.Possible ? "möglich" : "gesperrt: " + o.Reason)} — {o.Text}");
    }
}

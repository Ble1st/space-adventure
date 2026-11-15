using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Runde 38: der Spieler ist von Anbeginn eine eigene Fraktion (Regel <see cref="EconomyRules.PlayerOwnFaction"/>). Die Fraktion trägt die Id-Zahl des
/// Spielerakteurs (Konvention „Fraktionsakteur hat dieselbe Id“), der Spielerakteur ist Mitglied seiner selbst; seine Kasse bleibt die Spielerkasse.
/// Die Spielerfraktion hat keine Polizei, kein Bauschiff, keine Steuern, keine Rücklage und keine Beziehungsdrift — ihre Beziehungen bewegen sich nur
/// durch Handlungen des Spielers (<see cref="PlayerStanding.Change"/>, Krieg). Alle KI-Fraktionsregeln sehen sie über <see cref="Universe.NpcFactionIds"/> nicht.
/// </summary>
public static class PlayerFaction
{
    /// <summary>Anzeigename der eigenen Fraktion in „Meine Fraktion“ (die Fraktion selbst trägt den Namen des Spielerakteurs, „Du“ — Konvention Fraktion = Akteur).</summary>
    public const string DisplayName = "Deine Fraktion";

    /// <summary>
    /// Legt die Spielerfraktion an (falls nicht vorhanden), macht den Spieler zu ihrem Mitglied und setzt die Startbeziehungen beider Richtungen:
    /// <see cref="EconomyRules.PlayerStartRelationOwner"/> zur Fraktion des Startsektors, <see cref="EconomyRules.PlayerStartRelationOther"/> zu jeder anderen
    /// KI-Fraktion (Gesetzlose sind keine Fraktionen und bleiben bei ihrer Vorgabe). Verbraucht keinen Zufall.
    /// </summary>
    public static Faction Create(Universe u, SectorId startSector)
    {
        var id = new FactionId(u.PlayerActor.Value);
        if (!u.Factions.TryGetValue(id, out var f))
        {
            f = new Faction { Id = id, Name = u.Player.Name };
            u.Factions[id] = f;
        }
        u.Player.Membership = id;
        FactionId? owner = u.Sectors.TryGetValue(startSector, out var sec) && u.IsNpcFaction(sec.Owner) ? sec.Owner : null;
        foreach (var other in u.NpcFactionIds())
        {
            if (u.Actors.TryGetValue(other, out var oa) && oa.Kind == ActorKind.Outlaw) continue;
            float v = other == owner ? u.Rules.PlayerStartRelationOwner : u.Rules.PlayerStartRelationOther;
            Relations.Set(u, other, u.PlayerActor, v);
            Relations.Set(u, u.PlayerActor, other, v);
        }
        return f;
    }
}

/// <summary>Runde 38: Ansehen des Spielers bei einer KI-Fraktion hat sich durch eine Spielerhandlung geändert (beide Richtungen).</summary>
public sealed record StandingChanged(FactionId Faction, float From, float To, string Reason) : SimEvent;

/// <summary>
/// Runde 38: die einzige Schreibstelle für Beziehungen zwischen Spielerfraktion und KI-Fraktionen außerhalb des Kriegsrechts (<see cref="Wars"/>): ändert beide
/// Richtungen um <paramref name="delta"/> (auf −1 … +1 begrenzt) und meldet <see cref="StandingChanged"/>. Ohne Spielerfraktion nichts. Kein Zufall.
/// </summary>
public static class PlayerStanding
{
    public static void Change(Universe u, FactionId faction, float delta, string reason)
    {
        if (u.PlayerFaction is not { } me || !u.IsNpcFaction(faction) || delta == 0f) return;
        float from = Relations.Effective(u, faction, u.PlayerActor);
        float to = Math.Clamp(from + delta, -1f, 1f);
        Relations.Set(u, faction, me, to);
        float back = Relations.Effective(u, me, faction);
        Relations.Set(u, me, faction, Math.Clamp(back + delta, -1f, 1f));
        u.TouchAccess();
        u.Events.Publish(new StandingChanged(faction, from, to, reason));
    }

    /// <summary>Setzt beide Richtungen mindestens auf <paramref name="floor"/> (Frieden mit dem Spieler).</summary>
    public static void Raise(Universe u, FactionId faction, float floor, string reason)
    {
        if (u.PlayerFaction is not { } me || !u.IsNpcFaction(faction)) return;
        float from = Relations.Effective(u, faction, me);
        if (from < floor) Relations.Set(u, faction, me, floor);
        if (Relations.Effective(u, me, faction) < floor) Relations.Set(u, me, faction, floor);
        if (from < floor) u.Events.Publish(new StandingChanged(faction, from, floor, reason));
    }
}

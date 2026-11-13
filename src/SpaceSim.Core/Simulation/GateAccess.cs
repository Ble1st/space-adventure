using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Was ein Kontrollpunkt prüft (Phase 10, POLITIK.md Abschnitt 1): Gate, Seite (Sektor und Richtung), wirksame Sperrstufe (nach Hauptroute-Ausnahme
/// und Durchsetzung), zuständige Fraktion (Besitzer des Sektors), Mobilitätsprofil und — bei einer echten Passage — das Schiff selbst (Fracht).
/// </summary>
public readonly record struct AccessContext(Universe U, Gate Gate, Sector Sector, AccessDirection Direction, int Level, FactionId Authority, MobilityProfile Profile, Ship? Ship)
{
    /// <summary>Ist das Profil Mitglied der zuständigen Fraktion (eigene Leute passieren immer)?</summary>
    public bool IsMember => U.FactionOf(Profile.Actor) == Authority;
    /// <summary>Sicht der zuständigen Fraktion auf den Besitzer des Profils (−1 … +1).</summary>
    public float Relation => Relations.Effective(U, Authority, Profile.Actor);
}

/// <summary>
/// Ergebnis einer Prüfung: Politik, Zoll (Credits), Grund (Anzeige); <see cref="Undetected"/> = die Entdeckung hat das Schiff nicht bemerkt;
/// <see cref="Crime"/> (Phase 11) = die Ablehnung ist zugleich ein Verbrechen (Schlüssel aus <see cref="EconomyRules.CrimeHeat"/>, z. B. Schmuggel).
/// </summary>
public readonly record struct AccessVerdict(AccessPolicy Policy, float Toll, string Reason, bool Undetected = false, string? Crime = null)
{
    public static readonly AccessVerdict Allow = new(AccessPolicy.Allow, 0f, "");
}

/// <summary>
/// Ein Teil der Kontrollpunkt-Prüfung (Phase 10, POLITIK.md Abschnitt 1): Politik, Ware oder Entdeckung. Neue Prüfungen (Sprungsperre,
/// Patrouille) werden als weitere Einträge registriert (<see cref="AccessChecks"/>), nicht in bestehende eingebaut.
/// </summary>
public interface IAccessCheck
{
    string Key { get; }
    AccessVerdict Check(in AccessContext ctx);
}

/// <summary>
/// Politik: Was erlaubt die Sektorfraktion diesem Schiff (POLITIK.md Abschnitt 2)? Mitglieder immer; Ausreise nur bei Abriegelung (Stufe 5) gesperrt;
/// Einreise: Stufe 4–5 niemand außer Mitgliedern, Stufe 3 keine Gegner, sonst Zoll (Verbündete frei).
/// </summary>
public sealed class PolicyCheck : IAccessCheck
{
    public string Key => "policy";

    public AccessVerdict Check(in AccessContext ctx)
    {
        if (ctx.Level <= 0 || ctx.IsMember) return AccessVerdict.Allow;
        if (ctx.Direction == AccessDirection.Exit)
            return ctx.Level >= 4 ? new AccessVerdict(AccessPolicy.Deny, 0f, $"{ctx.Sector.Name}: Abriegelung, niemand darf hinaus") : AccessVerdict.Allow;
        if (ctx.Level >= 4)
            return new AccessVerdict(AccessPolicy.Deny, 0f, $"{ctx.Sector.Name}: {AccessNames.Level(ctx.Level)} — keine Einreise für Fremde");
        var r = ctx.U.Rules;
        float rel = ctx.Relation;
        if (rel >= r.AllyRelation) return AccessVerdict.Allow;
        if (ctx.Level >= 3 && rel <= r.HostileRelation)
            return new AccessVerdict(AccessPolicy.Deny, 0f, $"{ctx.Sector.Name}: nur Verbündete und Neutrale (Beziehung {Relations.Label(rel)})");
        return new AccessVerdict(AccessPolicy.Toll, ctx.Level * r.GateTollPerLevel, $"{ctx.Sector.Name}: Zoll ({AccessNames.Level(ctx.Level)})");
    }
}

/// <summary>
/// Ware: Embargo ab Stufe 2 (beladene Schiffe von Gegnern werden abgewiesen) und Schmuggelware (<see cref="EconomyRules.Contraband"/>) ab Stufe 1,
/// geprüft an der echten Fracht, wenn ein Schiff passiert. Nur Einreise; Mitglieder und Verbündete sind ausgenommen.
/// </summary>
public sealed class CargoCheck : IAccessCheck
{
    public string Key => "cargo";

    public AccessVerdict Check(in AccessContext ctx)
    {
        if (ctx.Level <= 0 || ctx.Direction != AccessDirection.Entry || ctx.IsMember) return AccessVerdict.Allow;
        var r = ctx.U.Rules;
        float rel = ctx.Relation;
        if (rel >= r.AllyRelation) return AccessVerdict.Allow;
        if (ctx.Level >= 2 && ctx.Profile.Laden && rel <= r.HostileRelation)
            return new AccessVerdict(AccessPolicy.Deny, 0f, $"{ctx.Sector.Name}: Embargo — Fracht von Gegnern verboten");
        if (ctx.Ship is { } ship && r.Contraband.Count > 0)
            foreach (var g in ship.Cargo.Keys.OrderBy(g => g.Value))
                if (ship.Cargo[g] > 0.001f && ctx.U.Goods.TryGetValue(g, out var good) && r.Contraband.Contains(good.Key))
                    return new AccessVerdict(AccessPolicy.Deny, 0f, $"{ctx.Sector.Name}: Schmuggelware an Bord ({good.Name})", Crime: "smuggling");
        return AccessVerdict.Allow;
    }
}

/// <summary>
/// Entdeckung: Tarnstärke des Schiffs gegen die Sensorstärke des Kontrollpunkts (SCHIFFSSLOTS.md Abschnitt 6). Heute ohne Würfel (deterministisch
/// in der Routenplanung): stärker getarnt passiert unbemerkt — die Sperre ist rechtlich, also undicht. Kein Modul hat heute Tarnung.
/// </summary>
public sealed class DetectionCheck : IAccessCheck
{
    public string Key => "detection";

    public AccessVerdict Check(in AccessContext ctx) =>
        ctx.Level > 0 && ctx.Profile.Stealth > ctx.U.Rules.GateSensorStrength
            ? new AccessVerdict(AccessPolicy.Allow, 0f, $"{ctx.Sector.Name}: unentdeckt", Undetected: true)
            : AccessVerdict.Allow;
}

/// <summary>Registry der Kontrollpunkt-Prüfungen in Reihenfolge (Politik, Ware, Entdeckung); doppelte Schlüssel werfen.</summary>
public sealed class AccessChecks
{
    readonly List<IAccessCheck> _checks = new();

    public IReadOnlyList<IAccessCheck> All => _checks;

    public AccessChecks Register(IAccessCheck check)
    {
        if (_checks.Any(c => c.Key == check.Key)) throw new InvalidOperationException($"Prüfung '{check.Key}' doppelt registriert");
        _checks.Add(check);
        return this;
    }

    public static AccessChecks Default() => new AccessChecks().Register(new PolicyCheck()).Register(new CargoCheck()).Register(new DetectionCheck());

    /// <summary>Die Standardprüfungen (zustandslos, daher gemeinsam nutzbar).</summary>
    public static readonly AccessChecks Standard = Default();
}

/// <summary>Ergebnis eines Spielersprungs: erlaubt, gezahlter Zoll, Meldung (Grund der Ablehnung oder Zollquittung).</summary>
public readonly record struct PlayerJump(bool Allowed, float Toll, string Message);

/// <summary>
/// Ergebnis einer Gate-Passage: Politik, Zoll (Summe beider Seiten), Grund und die Fraktion, die den Zoll bekommt. Phase 11: <see cref="Crime"/> und
/// <see cref="Authority"/> = die Ablehnung ist ein Verbrechen im Hoheitsgebiet dieser Fraktion (Schmuggel am Kontrollpunkt).
/// </summary>
public readonly record struct GatePassage(AccessPolicy Policy, float Toll, string Reason, FactionId? TollTo, string? Crime = null, FactionId? Authority = null)
{
    public bool Allowed => Policy != AccessPolicy.Deny;
}

/// <summary>
/// Gate-Zugang (Phase 10, POLITIK.md Abschnitte 1–2, SCHIFFSSLOTS.md Abschnitt 5): physischer Zustand plus Sperrstufen beider Enden (Ausreise aus
/// dem Sektor des Gates, Einreise in den Zielsektor). Auf Gates der Hauptroute gilt höchstens <see cref="EconomyRules.MainRouteMaxLevel"/>
/// (Gate-Ausnahme von der Sektorsperre). Die Prüfungen kommen aus <see cref="AccessChecks"/>.
/// </summary>
public static class GateAccess
{
    /// <summary>Mobilitätsprofil eines Schiffs: Besitzer, beladen, Tarnstärke.</summary>
    public static MobilityProfile ProfileOf(Universe u, Ship ship) =>
        // Phase 28: Kriegsschiffe brechen durch Sperren (nur der physische Torzustand zählt)
        u.Rules.Warfare && Wars.IsWarship(ship) ? MobilityProfile.Physical :
        MobilityProfile.For(ship.Owner, ship.Cargo.Values.Any(v => v > 0.001f), ship.HasModel ? ship.Stats.Stealth : 0f);

    /// <summary>Wirksame Stufe einer Seite: Sektorsperre (falls durchgesetzt), auf der Hauptroute gedeckelt.</summary>
    public static int EffectiveLevel(Universe u, Gate gate, Sector sector, AccessDirection dir)
    {
        int level = sector.Access.Level(dir);
        return gate.MainRoute ? Math.Min(level, u.Rules.MainRouteMaxLevel) : level;
    }

    /// <summary>Prüft eine Seite mit allen registrierten Prüfungen; Ablehnung schlägt Zoll schlägt Erlaubnis, unentdeckt hebt beides auf.</summary>
    static AccessVerdict CheckSide(Universe u, Gate gate, Sector sector, AccessDirection dir, MobilityProfile profile, Ship? ship, AccessChecks checks)
    {
        int level = EffectiveLevel(u, gate, sector, dir);
        if (level <= 0) return AccessVerdict.Allow;
        var ctx = new AccessContext(u, gate, sector, dir, level, sector.Owner, profile, ship);
        var result = AccessVerdict.Allow;
        bool undetected = false;
        foreach (var c in checks.All)
        {
            var v = c.Check(ctx);
            if (v.Undetected) { undetected = true; continue; }
            if (v.Policy > result.Policy) result = v with { Toll = result.Toll + v.Toll };
            else result = result with { Toll = result.Toll + (v.Policy == AccessPolicy.Toll ? v.Toll : 0f) };
        }
        if (undetected && result.Policy != AccessPolicy.Allow) return new AccessVerdict(AccessPolicy.Allow, 0f, $"{sector.Name}: unentdeckt", true);
        if (result.Policy != AccessPolicy.Toll) result = result with { Toll = 0f };
        return result;
    }

    /// <summary>
    /// Darf ein Profil (bzw. ein Schiff, dann mit echter Fracht) dieses Gate passieren, und was kostet es? Geschlossene und zerstörte Gates weisen
    /// alle ab; sonst Ausreise aus <see cref="Gate.Sector"/> und Einreise in den Zielsektor.
    /// </summary>
    public static GatePassage Evaluate(Universe u, Gate gate, MobilityProfile profile, Ship? ship = null, AccessChecks? checks = null)
    {
        if (gate.State == GateState.Closed) return new GatePassage(AccessPolicy.Deny, 0f, "Gate geschlossen", null);
        if (gate.State == GateState.Destroyed) return new GatePassage(AccessPolicy.Deny, 0f, "Gate zerstört", null);
        if (!u.Gates.TryGetValue(gate.Target, out var far)) return new GatePassage(AccessPolicy.Deny, 0f, "Gate ohne Gegenstelle", null);
        if (far.State is GateState.Closed or GateState.Destroyed) return new GatePassage(AccessPolicy.Deny, 0f, "Gegenstelle geschlossen", null);
        if (profile.IsPhysical) return new GatePassage(AccessPolicy.Allow, 0f, "", null);
        checks ??= AccessChecks.Standard;
        var exit = CheckSide(u, gate, u.Sectors[gate.Sector], AccessDirection.Exit, profile, ship, checks);
        if (exit.Policy == AccessPolicy.Deny) return new GatePassage(AccessPolicy.Deny, 0f, exit.Reason, null, exit.Crime, u.Sectors[gate.Sector].Owner);
        var farSector = u.Sectors[far.Sector];
        var entry = CheckSide(u, far, farSector, AccessDirection.Entry, profile, ship, checks);
        if (entry.Policy == AccessPolicy.Deny) return new GatePassage(AccessPolicy.Deny, 0f, entry.Reason, null, entry.Crime, farSector.Owner);
        float toll = exit.Toll + entry.Toll;
        string reason = entry.Reason.Length > 0 ? entry.Reason : exit.Reason;
        return new GatePassage(toll > 0f ? AccessPolicy.Toll : AccessPolicy.Allow, toll, reason, toll > 0f ? farSector.Owner : null);
    }

    /// <summary>Weist das Gate dieses Profil nicht ab (Zoll ist erlaubt)? Für die Routensuche.</summary>
    public static bool CanTraverse(Universe u, Gate gate, MobilityProfile profile) => Evaluate(u, gate, profile).Allowed;

    /// <summary>
    /// Zollt eine Passage: Abbuchung von der Kasse des Schiffs (<see cref="Universe.WalletOf"/>, höchstens was da ist) an die Kasse der Sektorfraktion,
    /// Ereignis <see cref="TollPaid"/>. Gibt den gezahlten Betrag zurück.
    /// </summary>
    public static float PayToll(Universe u, Ship ship, Gate gate, GatePassage passage)
    {
        if (passage.Toll <= 0f || passage.TollTo is not { } to) return 0f;
        var wallet = u.WalletOf(ship);
        float paid = MathF.Min(MathF.Max(0f, wallet.Credits), passage.Toll);
        if (paid <= 0f) return 0f;
        wallet.Credits -= paid;
        if (u.Actors.TryGetValue(to, out var authority)) authority.Treasury.Credits += paid;
        u.Events.Publish(new TollPaid(ship.Id, gate.Id, to, paid));
        return paid;
    }

    /// <summary>
    /// Sprung des Spielers durch ein Gate (Phase 10): dieselbe Prüfung wie für NPCs mit der echten Fracht. Abgewiesen → kein Sprung, Grund als Meldung;
    /// Phase 11: ist die Ablehnung ein Verbrechen (Schmuggelware entdeckt), wird es gemeldet (Fahndung, Lagekarte). Zoll → bezahlt, soweit die Kasse
    /// reicht (<see cref="PayToll"/>); reicht sie nicht, springt der Spieler trotzdem — das ist Zollflucht (Phase 11: Verbrechen, der Rest wird Teil der
    /// Strafe; vorher wurde der Sprung abgewiesen). Liefert, ob gesprungen werden darf.
    /// </summary>
    public static PlayerJump TryPlayerJump(Universe u, Ship ship, Gate gate)
    {
        var pass = Evaluate(u, gate, ProfileOf(u, ship), ship);
        if (!pass.Allowed)
        {
            if (pass.Crime is { } crime && pass.Authority is { } auth && u.Rules.SecurityActive)
            {
                Wanted.Report(u, ship.Owner, auth, crime);
                if (u.Gates.TryGetValue(gate.Target, out var farGate)) ThreatMap.Report(u, farGate.Sector, u.Rules.SmugglingThreat);
                return new PlayerJump(false, 0f, $"Sprung verweigert: {pass.Reason} — {SecurityNames.Crime(crime)} gemeldet (Fahndung: {SecurityNames.WantedLevel(Wanted.LevelOf(u, ship.Owner, auth))})");
            }
            return new PlayerJump(false, 0f, $"Sprung verweigert: {pass.Reason}");
        }
        if (pass.Toll > 0f)
        {
            string who = pass.TollTo is { } f && u.Factions.TryGetValue(f, out var fac) ? fac.Name : "Sektor";
            float due = pass.Toll;
            if (!u.Rules.SecurityActive && u.WalletOf(ship).Credits < due) return new PlayerJump(false, 0f, $"Sprung verweigert: Zoll {due:0} Cr, zu wenig Credits");
            float paid = PayToll(u, ship, gate, pass);
            if (paid + 0.01f < due && pass.TollTo is { } authority && u.Rules.SecurityActive)
            {
                Wanted.Report(u, ship.Owner, authority, "toll_evasion", extraFine: due - paid);
                return new PlayerJump(true, paid, $"Zollflucht: {paid:0} von {due:0} Cr an {who} gezahlt — Fahndung: {SecurityNames.WantedLevel(Wanted.LevelOf(u, ship.Owner, authority))}");
            }
            if (paid + 0.01f < due) return new PlayerJump(false, 0f, $"Sprung verweigert: Zoll {due:0} Cr, zu wenig Credits");
            return new PlayerJump(true, paid, $"Zoll {paid:0} Cr an {who}");
        }
        return new PlayerJump(true, 0f, "");
    }

    /// <summary>
    /// Warum ist ein Ziel für dieses Schiff nicht erreichbar? Erstes Gate auf dem physischen Weg, das das Schiff abweist, mit Grund; null wenn der Weg
    /// frei ist oder es physisch gar keinen gibt (dann „Gates gesperrt“).
    /// </summary>
    public static string? BlockReason(Universe u, Ship ship, SectorId from, SectorId to)
    {
        var route = u.FindRoute(from, to);
        if (route is null) return to == from ? null : "kein Gate-Weg (Gates geschlossen)";
        var profile = ProfileOf(u, ship);
        foreach (var gid in route)
        {
            var pass = Evaluate(u, u.Gates[gid], profile, ship);
            if (!pass.Allowed) return pass.Reason;
        }
        return u.FindRoute(from, to, profile) is null ? "Sperre auf dem Weg" : null;
    }

    /// <summary>
    /// Setzt den physischen Zustand eines Gate-Paars (beide Enden; Wartung, Ereignis, Zerstörung oder Wiedereröffnung). Danach werden Reisen über
    /// nun gesperrte Gates neu geplant (<see cref="AccessEnforcement.Revalidate"/>).
    /// </summary>
    public static void SetState(Universe u, Gate gate, GateState state)
    {
        foreach (var g in new[] { gate, u.Gates.GetValueOrDefault(gate.Target) })
        {
            if (g is null || g.State == state) continue;
            var old = g.State;
            g.State = state;
            u.Events.Publish(new GateStateChanged(g.Id, old, state));
        }
        u.TouchAccess();
        AccessEnforcement.Revalidate(u);
    }

    /// <summary>
    /// Leitet „eingeschränkt“ aus den Sperrstufen ab: Gates, an deren Ende eine wirksame Stufe ≥ 1 gilt, sind <see cref="GateState.Restricted"/>,
    /// sonst wieder <see cref="GateState.Open"/>. Geschlossene und zerstörte Gates bleiben, wie sie sind.
    /// </summary>
    public static void SyncRestricted(Universe u)
    {
        foreach (var g in u.Gates.Values.OrderBy(g => g.Id.Value))
        {
            if (g.State is GateState.Closed or GateState.Destroyed) continue;
            var sector = u.Sectors[g.Sector];
            bool restricted = EffectiveLevel(u, g, sector, AccessDirection.Entry) > 0 || EffectiveLevel(u, g, sector, AccessDirection.Exit) > 0;
            var want = restricted ? GateState.Restricted : GateState.Open;
            if (g.State == want) continue;
            var old = g.State;
            g.State = want;
            u.Events.Publish(new GateStateChanged(g.Id, old, want));
        }
    }
}

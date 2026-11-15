using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Missions;

/// <summary>
/// Runde 41: gemeinsame Regeln der Aufträge (Annehmen, Abbrechen, Nachverhandeln, Erfüllen, Scheitern, Budget, Sperren) —
/// die fachlichen Arten liegen in <see cref="IMissionType"/> (<see cref="DeliveryMission"/>, <see cref="TransportMission"/>).
/// Geldfluss nur über Kassen mit Ereignissen: Reservierung bei Annahme (<see cref="TreasuryUpkeep"/> „mission_reserve“ aus
/// <see cref="FactionViability.Spendable"/>), Zahlung bei Erfüllung (<see cref="MissionPaid"/> an die Spielerkasse),
/// Rückbuchung bei Scheitern (<see cref="TreasuryIncome"/> „mission_return“), Pfand an die Station bei Scheitern.
/// </summary>
public static class MissionRules
{
    /// <summary>Registry der Auftragsarten (zustandslose Typen; eine gemeinsame Instanz genügt — Runde 42 registriert hier die weiteren Arten).</summary>
    public static readonly MissionTypes Types = MissionTypes.Default();

    // ───────── Prüfung ─────────

    /// <summary>Ist der Auftraggeber eine KI-Fraktion, mit der der Spieler im Krieg steht, oder wird er dort gesucht (Stufe ≥ <see cref="EconomyRules.MissionWantedBlockLevel"/>)? Dann keine Aufträge.</summary>
    public static bool Forbidden(Universe u, ActorId client)
    {
        if (u.FactionOf(client) is not { } f || !u.IsNpcFaction(f)) return false;
        if (u.PlayerFaction is { } me && Wars.AtWar(u, me, f)) return true;
        return Wanted.LevelOf(u, u.PlayerActor, f) >= u.Rules.MissionWantedBlockLevel;
    }

    /// <summary>Hat der Auftraggeber bis <paramref name="tick"/> keine neuen Aufträge (nach Scheitern/Abbruch)?</summary>
    public static bool Blocked(Universe u, int clientActor, long? tick = null)
    {
        tick ??= u.Tick;
        return u.MissionFactions.TryGetValue(clientActor, out var s) && s.BlockedUntil > tick.Value;
    }

    /// <summary>Heute schon verbrauchtes Auftrags-Budget des Auftraggebers (nur Lesezugriff — ohne Spielerhandlung entsteht kein Eintrag).</summary>
    public static float SpentToday(Universe u, int clientActor)
    {
        if (!u.MissionFactions.TryGetValue(clientActor, out var s)) return 0f;
        long today = u.Tick / Math.Max(1, u.Rules.DayTicks);
        return s.Day == today ? s.Spent : 0f;
    }

    /// <summary>Tagesbudget für Aufträge eines Auftraggebers: <see cref="EconomyRules.MissionBudgetShare"/> der Kasse (Mindestbetrag <see cref="EconomyRules.MissionMinDailyBudget"/>).</summary>
    public static float BudgetOf(Universe u, Actor a) =>
        u.Rules.MissionBudgetShare * MathF.Max(u.Rules.MissionMinDailyBudget, a.Treasury.Credits);

    /// <summary>Höchstens so viele aktive Aufträge: <see cref="EconomyRules.MissionMaxActive"/> plus eine je Größenklasse, gedeckelt auf <see cref="EconomyRules.MissionMaxActiveCap"/>.</summary>
    public static int MaxActive(Universe u) =>
        Math.Min(u.Rules.MissionMaxActiveCap, u.Rules.MissionMaxActive + (int)Commands.CapacityClass(u, u.PlayerActor));

    /// <summary>Zahl der aktiven Aufträge.</summary>
    public static int ActiveCount(Universe u) => u.Missions.Count(m => m.State == MissionState.Active);

    /// <summary>Erzeugt das Angebots-Tagesbudget mit (Reservierung schreibt erst bei Annahme).</summary>
    static void BumpSpent(Universe u, int clientActor, float amount)
    {
        long today = u.Tick / Math.Max(1, u.Rules.DayTicks);
        if (!u.MissionFactions.TryGetValue(clientActor, out var s)) u.MissionFactions[clientActor] = s = new MissionFactionState();
        if (s.Day != today) { s.Day = today; s.Spent = 0f; }
        s.Spent += amount;
    }

    // ───────── Annehmen ─────────

    /// <summary>
    /// Sperrgrund der Annahme eines Angebots (rein — keine Zustandsänderung), null = möglich. Dock: <paramref name="remote"/> false =
    /// Schiff muss an der Angebots-Station angedockt sein; Comms: remote.
    /// </summary>
    public static string? AcceptIssue(Universe u, int offerId, Ship ship, bool remote)
    {
        if (!u.Rules.MissionsActive) return "Aufträge sind abgeschaltet.";
        if (ship.Id != u.PlayerShip) return "Nur mit dem aktiven Schiff.";
        var offer = u.MissionOffers.FirstOrDefault(o => o.Id == offerId);
        if (offer is null) return "Angebot unbekannt.";
        if (offer.Expires > 0 && u.Tick >= offer.Expires) return "Angebot abgelaufen.";
        if (!remote && ship.DockedAt != offer.At) return "Dafür musst du an der Station andocken.";
        var type = Types.Get(offer.Type);
        if (type is null) return "Unbekannter Auftragstyp.";
        if (Forbidden(u, offer.Client)) return "Kein Auftrag im Krieg oder bei Fahndung.";
        if (Blocked(u, offer.Client.Value)) return "Der Auftraggeber vergibt derzeit keine Aufträge.";
        int active = ActiveCount(u);
        if (active >= MaxActive(u)) return $"Zu viele laufende Aufträge ({active}/{MaxActive(u)}).";
        // Pfand: Kasse des Spielers reicht?
        if (offer.Deposit > 0f && u.WalletOf(ship).Credits < offer.Deposit - 0.0001f) return $"Kasse reicht nicht für das Pfand ({offer.Deposit:0} Cr).";
        // Belohnung: Budget und Spendable der Auftraggeber-Kasse
        var client = u.Actors[offer.Client];
        float reward = MathF.Max(0f, offer.Reward);
        if (reward > 0f)
        {
            if (SpentToday(u, offer.Client.Value) + reward > BudgetOf(u, client) + 0.0001f) return "Tagesbudget für Aufträge erreicht.";
            if (FactionViability.Spendable(u, client) < reward - 0.0001f) return "Die Kasse des Auftraggebers reicht nicht (Rücklage).";
        }
        return type.AcceptIssue(u, offer, ship, remote);
    }

    /// <summary>
    /// Nimmt ein Angebot an (dock: <paramref name="remote"/> false = Schiff muss an der Angebots-Station angedockt sein; Comms: remote).
    /// Liefert den Auftrag oder den Sperrgrund (gleiche Prüfung wie <see cref="AcceptIssue"/>). Reserviert die Belohnung aus
    /// <see cref="FactionViability.Spendable"/> der Auftraggeber-Kasse und hält das Pfand von der Spielerkasse.
    /// </summary>
    public static (Mission? Mission, string? Issue) Accept(Universe u, int offerId, Ship ship, bool remote)
    {
        if (AcceptIssue(u, offerId, ship, remote) is { } issue) return (null, issue);
        var offer = u.MissionOffers.First(o => o.Id == offerId);
        var type = Types.Get(offer.Type)!;
        var client = u.Actors[offer.Client];
        float reward = MathF.Max(0f, offer.Reward);
        // buchen: Pfand halten, Belohnung reservieren
        if (offer.Deposit > 0f) u.WalletOf(ship).Credits -= offer.Deposit;
        if (reward > 0f)
        {
            client.Treasury.Credits -= reward;
            u.Events.Publish(new TreasuryUpkeep(client.Id, "mission_reserve", reward, 0f));
            BumpSpent(u, offer.Client.Value, reward);
        }
        var m = type.Accept(u, offer, ship, remote);
        m.Id = u.NextMissionId++;
        m.Reserved = reward;
        m.Deposit = offer.Deposit;
        m.AcceptedTick = u.Tick;
        u.MissionOffers.Remove(offer);
        u.Missions.Add(m);
        u.Events.Publish(new MissionAccepted(m.Id, m.Client, m.Type, type.Describe(u, m)));
        return (m, null);
    }

    /// <summary>
    /// Runde 41 (Liefervertrag, PLAN 2.3): einmal Nachverhandeln je Angebot — +<see cref="EconomyRules.ContractHaggleShare"/> auf den
    /// Festpreis, nur wenn der heutige Marktpreis das deckt und die Beziehung zum Auftraggeber mindestens
    /// <see cref="EconomyRules.ContractHaggleRelation"/> ist. Deterministisch, keine Schleife.
    /// </summary>
    public static (bool Ok, string? Issue, float NewPrice) Haggle(Universe u, int offerId)
    {
        var offer = u.MissionOffers.FirstOrDefault(o => o.Id == offerId);
        if (offer is null) return (false, "Angebot unbekannt.", 0f);
        if (offer.Expires > 0 && u.Tick >= offer.Expires) return (false, "Angebot abgelaufen.", 0f);
        if (offer.Args.TryGetValue("contract", out var c) is false || c != "1") return (false, "Nur Lieferverträge lassen sich nachverhandeln.", 0f);
        if (offer.Haggled) return (false, "Dieses Angebot wurde schon einmal nachverhandelt.", 0f);
        if (!offer.Args.TryGetValue("price", out var ps) || !float.TryParse(ps, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var price) || price <= 0f)
            return (false, "Kein Festpreis hinterlegt.", 0f);
        float newPrice = price * (1f + u.Rules.ContractHaggleShare);
        if (!u.Stations.TryGetValue(offer.At, out var st) || !st.Market.TryGetValue(new GoodId(int.Parse(offer.Args["good"])), out var e) || e.Price < newPrice - 0.0001f)
            return (false, "Der heutige Preis deckt den Aufschlag nicht.", 0f);
        if (u.FactionOf(offer.Client) is { } f && u.IsNpcFaction(f) && u.PlayerFaction is { } me
            && Relations.Effective(u, me, f) < u.Rules.ContractHaggleRelation) return (false, $"Beziehung zum Auftraggeber zu niedrig (mind. {u.Rules.ContractHaggleRelation:0.00} nötig).", 0f);
        offer.Haggled = true;
        offer.Args["price"] = newPrice.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
        return (true, null, newPrice);
    }

    /// <summary>Aktive Mission zur Id (null = keine).</summary>
    public static Mission? Active(Universe u, int id) => u.Missions.FirstOrDefault(m => m.Id == id && m.State == MissionState.Active);

    /// <summary>Beschreibungstext eines Angebots (so wie beim Ereignis <see cref="MissionOffered"/>).</summary>
    public static string DescribeOfferText(Universe u, MissionOffer o) => Types.Get(o.Type)?.DescribeOffer(u, o) ?? o.Type;

    /// <summary>Spieler bricht einen aktiven Auftrag ab (Folge wie Scheitern, Grund „abgebrochen“).</summary>
    public static (bool Ok, string? Issue) Abandon(Universe u, int missionId, Ship ship)
    {
        if (ship.Id != u.PlayerShip) return (false, "Nur mit dem aktiven Schiff.");
        var m = Active(u, missionId);
        if (m is null) return (false, "Kein laufender Auftrag mit dieser Id.");
        Fail(u, m, "abgebrochen");
        return (true, null);
    }

    // ───────── Erfüllen / Scheitern ─────────

    /// <summary>
    /// Erfüllt einen Auftrag: Belohnung (reserviert) an die Spielerkasse (<see cref="MissionPaid"/>), Pfand zurück,
    /// Kassen-Reservierung des Liefervertrags an die Station zurück, Beziehung +<see cref="EconomyRules.MissionRelationGain"/>.
    /// Entfernt den Auftrag aus <see cref="Universe.Missions"/>.
    /// </summary>
    public static void Complete(Universe u, Mission m)
    {
        if (m.State != MissionState.Active) return;
        m.State = MissionState.Completed;
        var wallet = u.Player.Treasury;
        if (m.Reserved > 0f)
        {
            wallet.Credits += m.Reserved;
            u.Events.Publish(new MissionPaid(m.Id, m.Client, m.Reserved, $"Auftrag #{m.Id} erfüllt — Belohnung {m.Reserved:0} Cr."));
        }
        if (m.Deposit > 0f) wallet.Credits += m.Deposit;   // Pfand zurück
        if (m.ReservedCash > 0f && u.Stations.TryGetValue(m.At, out var st0)) st0.Credits += m.ReservedCash;   // Kassen-Reservierung des Vertrags an die Station zurück
        if (u.FactionOf(m.Client) is { } f && u.IsNpcFaction(f)) PlayerStanding.Change(u, f, u.Rules.MissionRelationGain, "mission_ok");
        var type = Types.Get(m.Type);
        u.Events.Publish(new MissionCompleted(m.Id, m.Client, m.Type, m.Reserved, type?.Describe(u, m) ?? m.Type));
        u.Missions.Remove(m);
    }

    /// <summary>
    /// Auftrag scheitert (Frist, Ziel weg, Abbruch): Reservierung zurück in die Auftraggeber-Kasse (<see cref="TreasuryIncome"/>
    /// „mission_return“), Pfand verfällt an die Angebots-Station, Kassen-Reservierung zurück an die Station, versiegelte Fracht
    /// wird frei, Beziehung −<see cref="EconomyRules.MissionRelationLoss"/>, der Auftraggeber vergibt
    /// <see cref="EconomyRules.MissionBlockTicks"/> keine neuen Aufträge. Entfernt den Auftrag aus <see cref="Universe.Missions"/>.
    /// </summary>
    public static void Fail(Universe u, Mission m, string reason)
    {
        if (m.State != MissionState.Active) return;
        m.State = MissionState.Failed;
        if (u.Actors.TryGetValue(m.Client, out var client))
        {
            if (m.Reserved > 0f)
            {
                client.Treasury.Credits += m.Reserved;
                u.Events.Publish(new TreasuryIncome(client.Id, "mission_return", m.Reserved));
            }
        }
        if (u.Stations.TryGetValue(m.At, out var st))
        {
            if (m.Deposit > 0f)
            {
                st.Credits += m.Deposit;   // Pfand verfällt an die Station
                u.Events.Publish(new TreasuryIncome(st.Owner, "mission_deposit", m.Deposit));
            }
            if (m.ReservedCash > 0f) st.Credits += m.ReservedCash;
        }
        if (m.CargoVolume > 0f && m.Args.TryGetValue("ship", out var ss) && int.TryParse(ss, out var shipId)
            && u.Ships.TryGetValue(new ShipId(shipId), out var sh))
            sh.MissionCargo = MathF.Max(0f, sh.MissionCargo - m.CargoVolume);
        if (u.FactionOf(m.Client) is { } f && u.IsNpcFaction(f))
        {
            PlayerStanding.Change(u, f, -u.Rules.MissionRelationLoss, "mission_fail");
            if (!u.MissionFactions.TryGetValue(m.Client.Value, out var fs)) u.MissionFactions[m.Client.Value] = fs = new MissionFactionState();
            fs.Day = u.Tick / Math.Max(1, u.Rules.DayTicks);
            fs.BlockedUntil = u.Tick + u.Rules.MissionBlockTicks;
        }
        var type = Types.Get(m.Type);
        u.Events.Publish(new MissionFailed(m.Id, m.Client, m.Type, reason, m.Reserved, type?.Describe(u, m) ?? m.Type));
        u.Missions.Remove(m);
    }

    /// <summary>Rückgängig (Runde 39) macht Auftragsfortschritt rückgängig: bucht die deltas des Journal-Eintrags negativ.</summary>
    public static void RollbackProgress(Universe u, IReadOnlyList<(int MissionId, float DSold, float DBought)>? progress)
    {
        if (progress is null || u.Missions.Count == 0) return;
        foreach (var (id, ds, db) in progress)
        {
            var m = u.Missions.FirstOrDefault(x => x.Id == id && x.State == MissionState.Active);
            if (m is null) continue;
            m.Sold = MathF.Max(0f, m.Sold - ds);
            m.Bought = MathF.Max(0f, m.Bought - db);
        }
    }

    /// <summary>Offline-Aufholen verlängert die Fristen aktiver Aufträge um die nachgespielten Ticks (der Spieler kann offline nicht handeln).</summary>
    public static void ExtendDeadlines(Universe u, long ticks)
    {
        if (ticks <= 0 || u.Missions.Count == 0) return;
        foreach (var m in u.Missions) if (m.State == MissionState.Active) m.Deadline += ticks;
    }

    /// <summary>Angenommenes Frist-Ende: geschätzte Spielerflugzeit × <see cref="EconomyRules.MissionDeadlineFactor"/>, mindestens <see cref="EconomyRules.MissionMinDeadlineTicks"/>.</summary>
    public static long DeadlineFrom(Universe u, Ship ship, Station target)
    {
        float t = Travel.Ticks(u, ship.Sector, ship.Position, target.Sector, target.Position, ship);
        if (float.IsInfinity(t) || float.IsNaN(t)) t = u.Rules.MissionMinDeadlineTicks;
        return u.Tick + Math.Max(u.Rules.MissionMinDeadlineTicks, (long)(t * u.Rules.MissionDeadlineFactor));
    }
}

/// <summary>
/// Runde 41: erzeugt die Angebote je Station alle <see cref="EconomyRules.MissionRefreshTicks"/> Ticks (im ersten Intervall des
/// Buckets) aus dem Weltzustand mit dem eigenen Missions-RNG (<see cref="MissionRng"/>, nie <see cref="Universe.Rng"/>). Angebote
/// werden gespeichert, aber nicht gehasht; sie verbrauchen keine Zufallszahl des Welt-RNG. Veröffentlicht je neuem Comms-Angebot
/// (Liefervertrag) <see cref="MissionOffered"/>.
/// </summary>
public sealed class MissionBoardSystem : ISimSystem
{
    public const int Interval = 100;

    public void Step(Universe u, int dt)
    {
        if (!u.Rules.MissionsActive) return;
        // abgelaufene Angebote entfernen (jeder Aufruf, billige Liste)
        if (u.MissionOffers.Count > 0) u.MissionOffers.RemoveAll(o => o.Expires > 0 && u.Tick >= o.Expires);
        int refresh = Math.Max(Interval, u.Rules.MissionRefreshTicks);
        if (u.Tick % refresh >= Interval) return;   // nur im ersten Intervall jedes Buckets neu erzeugen
        long bucket = u.Tick / refresh;
        u.MissionOffers.Clear();
        foreach (var st in u.StationsById())
        {
            if (!st.Dockable && !st.IsBuildSite) continue;
            var rng = MissionRng.For(u, st.Id.Value, bucket);
            foreach (var type in MissionRules.Types.All)
            {
                var offer = type.Generate(u, st, rng, bucket);
                if (offer is null) continue;
                offer.Id = u.NextMissionId++;
                offer.At = st.Id;
                offer.Client = st.Owner;
                offer.Created = u.Tick;
                offer.Expires = u.Tick + Math.Max(Interval, u.Rules.MissionOfferTicks);
                u.MissionOffers.Add(offer);
                if (offer.Args.TryGetValue("channel", out var ch) && ch == "comms")
                    u.Events.Publish(new MissionOffered(offer.Id, offer.Client, offer.Type, type.DescribeOffer(u, offer)));
            }
        }
    }
}

/// <summary>
/// Runde 41: Fortschritt, Fristen und Abschlüsse der angenommenen Aufträge (Intervall 20; ohne Aufträge nur die Event-Anmeldung,
/// keine Zustandsänderung). Fortschritt läuft über Ereignisse (<see cref="PlayerSold"/>/<see cref="PlayerBought"/>/<see cref="TraderBought"/>
/// eigener Flottenschiffe), Schiffs-/Stationverlust scheitert betroffene Aufträge. Hängt sich wie <see cref="Comms.CommsSystem"/>
/// an den Bus, sobald es ein anderes Universum sieht.
/// </summary>
public sealed class MissionSystem : ISimSystem
{
    public const int Interval = 20;

    Universe? _sub;
    Action<SimEvent>? _handler;

    public void Step(Universe u, int dt)
    {
        if (!u.Rules.MissionsActive) return;
        if (!ReferenceEquals(_sub, u))
        {
            if (_sub is not null && _handler is not null) _sub.Events.Raised -= _handler;
            _handler = e => OnEvent(u, e);
            u.Events.Raised += _handler;
            _sub = u;
        }
        if (u.MissionOffers.Count > 0) u.MissionOffers.RemoveAll(o => o.Expires > 0 && u.Tick >= o.Expires);
        if (u.Missions.Count == 0) return;
        foreach (var m in u.Missions.Where(x => x.State == MissionState.Active).ToList())
            if (MissionRules.Types.Get(m.Type) is { } type) type.Check(u, m);
    }

    /// <summary>Ereignis → Auftragsfortschritt / Scheitern. Auch direkt aufrufbar (Tests).</summary>
    public static void OnEvent(Universe u, SimEvent e)
    {
        if (!u.Rules.MissionsActive || u.Missions.Count == 0) return;
        switch (e)
        {
            case PlayerSold or PlayerBought:
                foreach (var m in u.Missions.Where(x => x.State == MissionState.Active).ToList())
                    if (MissionRules.Types.Get(m.Type) is { } t) t.OnEvent(u, m, e);
                break;
            case TraderBought tb when u.Ships.TryGetValue(tb.Ship, out var ts) && ts.Owner == u.PlayerActor:
                foreach (var m in u.Missions.Where(x => x.State == MissionState.Active).ToList())
                    if (MissionRules.Types.Get(m.Type) is { } t) t.OnEvent(u, m, e);
                break;
            case ShipRemoved sr:
                foreach (var m in u.Missions.Where(x => x.State == MissionState.Active
                    && x.Args.TryGetValue("ship", out var ss) && int.TryParse(ss, out var sid) && sid == sr.Ship.Value).ToList())
                    MissionRules.Fail(u, m, "Schiff verloren");
                break;
            case StationRemoved stn:
                foreach (var m in u.Missions.Where(x => x.State == MissionState.Active).ToList())
                    if (MissionRules.Types.Get(m.Type) is { } t && t.TargetGone(u, m)) MissionRules.Fail(u, m, "Zielstation weg");
                break;
        }
    }
}

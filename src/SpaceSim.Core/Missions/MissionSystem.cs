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
        if (client == u.PlayerActor) return true;   // Runde 45: an der eigenen Sektorbehörde keine Aufträge an sich selbst
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

    /// <summary>
    /// Runde 42d (Handytest): Beschreibungstext ohne führendes „Auftrag: “ — für Meldungen wie „Auftrag angenommen: …“,
    /// damit nicht „Auftrag angenommen: Auftrag: …“ dasteht.
    /// </summary>
    public static string AcceptedText(Universe u, Mission m)
    {
        string d = Types.Get(m.Type)?.Describe(u, m) ?? m.Type;
        return d.StartsWith("Auftrag: ", StringComparison.Ordinal) ? d["Auftrag: ".Length..] : d;
    }

    /// <summary>
    /// Runde 42d (Handytest): Geldzeile eines laufenden Auftrags — beim Liefervertrag „Festpreis p × n = cash Cr · Kasse
    /// reserviert“ statt „Belohnung 0 Cr (reserviert)“, sonst die reservierte Belohnung.
    /// </summary>
    public static string MoneyLine(Universe u, Mission m)
    {
        if (m.Args.TryGetValue("contract", out var c) && c == "1"
            && m.Args.TryGetValue("units", out var us) && m.Args.TryGetValue("price", out var ps)
            && float.TryParse(us, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var units)
            && float.TryParse(ps, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var price))
            return $"Festpreis {price:0.0} Cr × {units:0.#} = {units * price:0} Cr · Kasse reserviert";
        return $"Belohnung {m.Reserved:0} Cr (reserviert)";
    }

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

    /// <summary>
    /// Angenommenes Frist-Ende: reale Spielerflugzeit (Ticks bei ×1, <see cref="TripTicks"/>) × Faktor (mindestens 2),
    /// mindestens <see cref="EconomyRules.MissionMinDeadlineTicks"/>. Runde 42c: nicht mehr <see cref="Travel.Ticks"/> (NPC-Tempo,
    /// beim angedockten Schiff ≈ 0 — daher Fristen wie „10:00“ für Transporte über mehrere Sprünge, die real nicht schaffbar waren).
    /// </summary>
    public static long DeadlineFrom(Universe u, Ship ship, Station target)
    {
        long trip = TripTicks(u, ship.Sector, ship.Position, target.Sector, target.DockPoint(ship.Id), ship);
        float factor = Math.Max(2f, u.Rules.MissionDeadlineFactor);
        return u.Tick + Math.Max(u.Rules.MissionMinDeadlineTicks, (long)(trip * factor));
    }

    /// <summary>Runde 42c: Fallback je Gate-Sprung in Ticks (5 min), wenn die reale Flugzeit nicht berechenbar ist.</summary>
    public const long HopFallbackTicks = 1200;

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Universe, Dictionary<long, float>> TripCache = new();

    /// <summary>
    /// Runde 42c: reale Spielerflugzeit in Sekunden bei ×1 (<see cref="Flight.PlayerTrip"/> — dieselbe Wegwahl und dasselbe Tempo
    /// wie die Ansicht, inkl. Anlauf/Abbremsen und Jump-Zeit). Gecacht je Universum (Schätzung; Positionen auf 16 Einheiten gerastert).
    /// PositiveInfinity = nicht erreichbar.
    /// </summary>
    public static float TripSeconds(Universe u, SectorId fromSector, Vec2 fromPos, SectorId toSector, Vec2 toPos, Ship? ship)
    {
        var cache = TripCache.GetOrCreateValue(u);
        long key = (uint)fromSector.Value;
        key = key * 1000003 + (int)MathF.Round(fromPos.X / 16f);
        key = key * 1000003 + (int)MathF.Round(fromPos.Y / 16f);
        key = key * 1000003 + (uint)toSector.Value;
        key = key * 1000003 + (int)MathF.Round(toPos.X / 16f);
        key = key * 1000003 + (int)MathF.Round(toPos.Y / 16f);
        key = key * 1000003 + (ship?.Id.Value ?? 0);
        if (cache.TryGetValue(key, out var s)) return s;
        s = Flight.PlayerTrip.Seconds(u, fromSector, fromPos, toSector, toPos, ship);
        cache[key] = s;
        return s;
    }

    /// <summary>
    /// Runde 42c: geschätzte Flugzeit in Ticks (×1): reale Spielerflugzeit × <see cref="SimClock.TicksPerSecond"/>;
    /// nicht berechenbar → Sprünge × <see cref="HopFallbackTicks"/> (großzügig).
    /// </summary>
    public static long TripTicks(Universe u, SectorId fromSector, Vec2 fromPos, SectorId toSector, Vec2 toPos, Ship? ship)
    {
        float sec = TripSeconds(u, fromSector, fromPos, toSector, toPos, ship);
        if (!float.IsInfinity(sec) && !float.IsNaN(sec) && sec > 0f)
            return Math.Max(1L, (long)(sec * SimClock.TicksPerSecond));
        int hops = u.Hops(fromSector, toSector);
        if (hops < 1) hops = 1;
        // Runde 44: in vergrößerten Sektoren reicht die feste Zeit je Sprung nicht — NPC-Schätzung × 1,9 (Spieler fliegt mit Anlauf/Bremsen), Untergrenze wie bisher
        float est = Simulation.Travel.Estimate(u, fromSector, fromPos, toSector, toPos, ship);
        long byEstimate = float.IsFinite(est) && est > 0f ? (long)(est * 1.9f) : 0L;
        return Math.Max(hops * HopFallbackTicks, byEstimate);
    }

    /// <summary>Runde 42c: Flugzeit als Text für Vorschauen („~2:30 min“; „?“ = unbekannt).</summary>
    public static string TripText(Universe u, SectorId fromSector, Vec2 fromPos, SectorId toSector, Vec2 toPos, Ship? ship)
    {
        float sec = TripSeconds(u, fromSector, fromPos, toSector, toPos, ship);
        return Flight.PlayerTrip.Text(sec);
    }

    /// <summary>Runde 42b: Restzeit als m:ss (geflooret, nie negativ) — statt roher Tick-Zahlen in Anzeigen.</summary>
    public static string Rest(long ticks)
    {
        long s = Math.Max(0L, ticks) / SimClock.TicksPerSecond;
        return $"{s / 60}:{s % 60:00}";
    }
}

/// <summary>
/// Runde 41: erzeugt die Angebote je Station alle <see cref="EconomyRules.MissionRefreshTicks"/> Ticks (im ersten Intervall des
/// Buckets) aus dem Weltzustand mit dem eigenen Missions-RNG (<see cref="MissionRng"/>, nie <see cref="Universe.Rng"/>). Angebote
/// werden gespeichert, aber nicht gehasht; sie verbrauchen keine Zufallszahl des Welt-RNG. Veröffentlicht je neuem Angebot
/// <see cref="MissionOffered"/> (alle Arten — die Zählung; Posteingang macht daraus nur der Comms-Kanal, <see cref="Comms.CommsSystem"/>).
/// Runde 42b: die Station, an der der Spieler gerade angedockt ist, wird sofort bei deren Andocken (einmal je Bucket) erzeugt,
/// damit an einer Station mit passendem Typ zügig Angebote bereitstehen.
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
        long bucket = u.Tick / refresh;
        bool bucketStart = u.Tick % refresh < Interval;
        if (bucketStart) u.MissionOffers.Clear();
        // Runde 42b: Andock-Station des Spielers sofort erzeugen (falls sie in diesem Bucket noch keines hat) — gleicher RNG wie beim Bucket
        if (!bucketStart && u.Ships.TryGetValue(u.PlayerShip, out var ps) && ps.DockedAt != default
            && u.Stations.TryGetValue(ps.DockedAt, out var dock) && (dock.Dockable || dock.IsBuildSite)
            && u.MissionOffers.All(o => o.At != dock.Id))
            GenerateFor(u, dock, bucket);
        if (!bucketStart) return;
        foreach (var st in u.StationsById())
        {
            if (!st.Dockable && !st.IsBuildSite) continue;
            GenerateFor(u, st, bucket);
        }
    }

    /// <summary>Erzeugt die Angebote einer Station für dieses Bucket (eigener Missions-RNG, nie <see cref="Universe.Rng"/>).</summary>
    static void GenerateFor(Universe u, Station st, long bucket)
    {
        var rng = MissionRng.For(u, st.Id.Value, bucket);
        foreach (var type in MissionRules.Types.All)
        {
            var offer = type.Generate(u, st, rng, bucket);
            if (offer is null) continue;
            // Runde 42c (Handytest): ein bereits angenommener Auftrag wird nicht erneut angeboten (dasselbe Bucket → derselbe
            // Missions-RNG → dasselbe Angebot; nach der Annahme darf es nicht wieder auftauchen)
            if (AlreadyAccepted(u, st, offer)) continue;
            offer.Id = u.NextMissionId++;
            offer.At = st.Id;
            offer.Client = st.Owner;
            offer.Created = u.Tick;
            offer.Expires = u.Tick + Math.Max(Interval, u.Rules.MissionOfferTicks);
            u.MissionOffers.Add(offer);
            u.Events.Publish(new MissionOffered(offer.Id, offer.Client, offer.Type, type.DescribeOffer(u, offer)));   // Runde 42b: alle Arten (Comms filtert den Kanal für Post)
        }
    }

    /// <summary>
    /// Runde 42c: Läuft bereits ein aktiver Auftrag, der aus genau diesem Angebot entstanden ist (gleiche Art, Station und
    /// Argumente — die Annahme kopiert die Angebots-Args), wird das Angebot nicht neu erzeugt.
    /// </summary>
    static bool AlreadyAccepted(Universe u, Station st, MissionOffer offer) =>
        u.Missions.Any(m => m.State == MissionState.Active && m.Type == offer.Type && m.At == st.Id
            && offer.Args.All(kv => m.Args.TryGetValue(kv.Key, out var v) && v == kv.Value));
}

/// <summary>
/// Runde 41: Fortschritt, Fristen und Abschlüsse der angenommenen Aufträge (Intervall 20; ohne Aufträge nur die Event-Anmeldung,
/// keine Zustandsänderung). Fortschritt läuft über Ereignisse (<see cref="PlayerSold"/>/<see cref="PlayerBought"/>/<see cref="TraderBought"/>
/// eigener Flottenschiffe), Schiffs-/Stationverlust scheitert betroffene Aufträge. Hängt sich wie <see cref="Comms.CommsSystem"/>
/// an den Bus, sobald es ein anderes Universum sieht.
/// </summary>
public sealed class MissionSystem : ISimSystem, ISimEventListener
{
    public const int Interval = 20;

    Universe? _sub;
    Action<SimEvent>? _handler;

    /// <summary>Runde 43: am Bus des Universums anmelden (jeden Tick vom Runner, siehe <see cref="ISimEventListener"/>).</summary>
    public void Attach(Universe u)
    {
        if (ReferenceEquals(_sub, u)) return;
        if (_sub is not null && _handler is not null) _sub.Events.Raised -= _handler;
        _handler = e => OnEvent(u, e);
        u.Events.Raised += _handler;
        _sub = u;
    }

    public void Step(Universe u, int dt)
    {
        if (!u.Rules.MissionsActive) return;
        Attach(u);
        if (u.MissionOffers.Count > 0) u.MissionOffers.RemoveAll(o => o.Expires > 0 && u.Tick >= o.Expires);
        if (u.Missions.Count == 0) return;
        foreach (var m in u.Missions.Where(x => x.State == MissionState.Active).ToList())
            if (MissionRules.Types.Get(m.Type) is { } type) type.Check(u, m);
    }

    /// <summary>
    /// Ereignis → Auftragsfortschritt / Scheitern. Auch direkt aufrufbar (Tests). Runde 42: jedes Ereignis geht an die Typen
    /// (<see cref="IMissionType.OnEvent"/> filtert selbst — Kopfgeldjagd wertet <see cref="CombatExited"/> aus, Eskorte
    /// <see cref="ShipArrived"/>); die Sonderfälle (eigenes Transportschiff weg, Zielstation weg) scheitern direkt.
    /// </summary>
    public static void OnEvent(Universe u, SimEvent e)
    {
        if (!u.Rules.MissionsActive || u.Missions.Count == 0) return;
        foreach (var m in u.Missions.Where(x => x.State == MissionState.Active).ToList())
        {
            if (MissionRules.Types.Get(m.Type) is not { } t) continue;
            switch (e)
            {
                case ShipRemoved sr when m.Args.TryGetValue("ship", out var ss) && int.TryParse(ss, out var sid) && sid == sr.Ship.Value:
                    MissionRules.Fail(u, m, "Schiff verloren");
                    continue;
                case StationRemoved:
                    if (t.TargetGone(u, m)) { MissionRules.Fail(u, m, "Zielstation weg"); continue; }
                    break;
                case TraderBought tb when !(u.Ships.TryGetValue(tb.Ship, out var ts) && ts.Owner == u.PlayerActor):
                    continue;   // nur Käufe eigener Flottenschiffe zählen (wie Runde 41)
            }
            t.OnEvent(u, m, e);
        }
    }
}

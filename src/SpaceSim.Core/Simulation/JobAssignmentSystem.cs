using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Simulation;

/// <summary>
/// Aufgabenwahl für Multirole-KI-Schiffe (Phase 6, SCHIFFE.md Abschnitt 6 Punkte 2–3): Ein untätiges Schiff, dessen Modell mehrere Aufgaben
/// erlaubt (<see cref="ShipModel.Jobs"/>) und das sie mit seinem Fit kann, bekommt die Aufgabe mit dem höchsten erwarteten Gewinn je Tick
/// (vorhandenes Scoring von <see cref="TraderSystem"/> und <see cref="MinerSystem"/>, gemindert durch die Auslastung der Aufgabe als
/// Herdenschutz). Gegen Flackern gelten eine Mindestdauer (<see cref="EconomyRules.JobMinTicks"/>) und eine Hysterese
/// (<see cref="EconomyRules.JobSwitchMargin"/>). Das System setzt nur das Etikett <see cref="Ship.Role"/>; die Ausführung bleibt bei den
/// Aufgaben-Systemen, die im selben Tick danach laufen. Spezialisten (ein Job oder Einheitsmodelle ohne Jobs) werden nie angefasst.
/// </summary>
public sealed class JobAssignmentSystem : ISimSystem
{
    readonly IReadOnlyList<IShipJob> _jobs;

    public JobAssignmentSystem(IReadOnlyList<IShipJob>? jobs = null) => _jobs = jobs ?? ShipJobs.Default;

    public void Step(Universe u, int dt)
    {
        foreach (var ship in u.ShipsById())   // Schnappschuss nach Id (Phase 17: zwischengespeichert statt je Tick sortiert)
        {
            if (!IsIdle(u, ship) || ship.Controller != Controller.Ai) continue;   // Phase 17: billige Prüfung vor der Aufgabenliste
            // Punkt 13: ein Modell mit weniger als zwei Aufgaben-Schlüsseln ergibt nie zwei Aufgaben (Spezialisten) — ohne Listen je Schiff und Tick
            if (!ship.HasModel || !u.ShipModels.TryGetValue(ship.ModelKey, out var shipModel) || shipModel.Jobs.Count < 2) continue;
            var jobs = ShipJobs.Of(u, ship).Where(_jobs.Contains).ToList();
            if (jobs.Count < 2) continue;
            var current = ShipJobs.ForRole(ship.Role);
            if (current is not null && !jobs.Contains(current)) current = null;
            // Mindestdauer zuerst prüfen: spart die teure Bewertung (Handelsscoring über alle Märkte)
            if (current is not null && u.Tick - ship.JobSince < u.Rules.JobMinTicks) continue;

            float currentScore = current is null ? 0f : Rated(u, ship, current);
            // Phase 20: Eine Aufgabe, die gerade nichts einbringt, verlässt das Schiff erst nach JobIdleSwitchTicks Leerlauf — direkt nach einer Lieferung ist der
            // Abnehmer oft kurz voll (Bewertung 0), und die Mehrzweckschiffe flatterten alle 600 Ticks zwischen Handel und Bergbau (F/G: 3284/5459 Wechsel)
            if (current is not null && !IdleLongEnough(u, ship, currentScore)) continue;
            IShipJob? best = null;
            float bestScore = 0f;
            foreach (var job in jobs)
            {
                if (job == current) continue;
                // Herdenschutz: keine Flucht in eine volle Aufgabe — sonst kauft die Flotte für die verlassene Aufgabe nach und wächst über ihre
                // Kapazitätsgrenzen hinaus. Phase 14: auch dann nicht, wenn die aktuelle Aufgabe gerade nichts einbringt (vorher erlaubt): Im 60000-Tick-Lauf
                // kaufte die Flotte so 24 Tinker für den Bergbau, die sofort in den vollen Handel wechselten (Handel 28/14).
                if (!HasRoom(u, ship, job)) continue;
                float score = Rated(u, ship, job);
                if (score > bestScore) { bestScore = score; best = job; }
            }
            if (best is null) continue;
            if (current is not null && !ShouldSwitch(u.Tick - ship.JobSince, currentScore, bestScore, u.Rules)) continue;

            var from = ship.Role;
            ship.Role = best.Role;
            ship.JobSince = u.Tick;
            ship.Order = null;
            ship.Phase = TradePhase.Idle;
            ship.MinePhase = MinePhase.Idle;
            u.Events.Publish(new JobChanged(ship.Id, from, best.Role, currentScore, bestScore));
        }
    }

    /// <summary>
    /// Wechselregel als reine Funktion: erst nach der Mindestdauer, nur zu einer Aufgabe mit Gewinn, und nur wenn sie um die Hysterese
    /// besser ist als die aktuelle (aktuell 0 = jede lohnende Aufgabe ist besser); Phase 24: die neue Aufgabe bringt mindestens <see cref="EconomyRules.JobMinSwitchScore"/>.
    /// </summary>
    public static bool ShouldSwitch(long ticksInJob, float currentScore, float otherScore, EconomyRules rules) =>
        ticksInJob >= rules.JobMinTicks && otherScore > 0f && otherScore >= rules.JobMinSwitchScore && otherScore > currentScore * (1f + rules.JobSwitchMargin);

    /// <summary>
    /// Phase 20: Darf das Schiff eine Aufgabe mit dieser Bewertung verlassen? Bringt sie etwas ein (&gt; 0), entscheidet nur die Hysterese; bringt sie nichts
    /// ein, muss das Schiff seit mindestens <see cref="EconomyRules.JobIdleSwitchTicks"/> untätig sein (die Ausführung sucht in der Zeit weiter).
    /// </summary>
    public static bool IdleLongEnough(Universe u, Ship ship, float currentScore) =>
        currentScore > 0f || u.Rules.JobIdleSwitchTicks <= 0 || (ship.IdleSince >= 0 && u.Tick - ship.IdleSince >= u.Rules.JobIdleSwitchTicks);

    /// <summary>
    /// Bewertung einer Aufgabe für dieses Schiff mit Herdenschutz: Score × 1 / (1 + <see cref="EconomyRules.JobCrowdPenalty"/> × Auslastung),
    /// Auslastung = belegte Kapazität der Aufgabe ohne das Schiff selbst ÷ Grenze.
    /// </summary>
    public static float Rated(Universe u, Ship ship, IShipJob job)
    {
        float score = job.Score(u, ship);
        if (score <= 0f) return 0f;
        // Phase 20: Auslastung im eigenen Kartenteil (ohne Teilung: die Welt)
        var (used, limit) = FleetCapacity.For(u, job, ship);
        if (ship.Role == job.Role) used -= ShipJobs.UnitsOf(u, ship, job);
        float load = MathF.Max(0f, used) / MathF.Max(1f, limit);
        return score / (1f + u.Rules.JobCrowdPenalty * load);
    }

    /// <summary>Hat die Aufgabe noch Platz für dieses Schiff (belegte Kapazität + eigene Einheiten ≤ Grenze + <see cref="EconomyRules.FleetCapacityOvershoot"/>)?</summary>
    public static bool HasRoom(Universe u, Ship ship, IShipJob job)
    {
        var (used, limit) = FleetCapacity.For(u, job, ship);   // Phase 20: im eigenen Kartenteil
        return used + ShipJobs.UnitsOf(u, ship, job) <= limit + u.Rules.FleetCapacityOvershoot;
    }

    /// <summary>Untätig: keine Reise, kein Auftrag, keine Fracht, beide Phasen Idle und die KI denkt in diesem Tick ohnehin nach.</summary>
    static bool IsIdle(Universe u, Ship ship) =>
        !u.Fights.IsEngaged(ship.Id) && ship.Transit is null && ship.Order is null && ship.Phase == TradePhase.Idle && ship.MinePhase == MinePhase.Idle
        && u.Tick >= ship.NextThinkTick && ship.Cargo.Values.Sum() < 0.001f;
}

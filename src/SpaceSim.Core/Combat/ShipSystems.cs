using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Combat;

/// <summary>Phase 23: Anteile des Reaktorüberschusses je Bordsystem (Summe 1).</summary>
public readonly record struct PowerSplit(float Weapons, float Shield, float Engine);

/// <summary>
/// Phase 23: Leistungsfluss eines Schiffs in einem Augenblick — Überschuss, die Leistung, die Schild, Speicherbank und Antrieb tatsächlich erhalten,
/// und daraus Schildregeneration und Ladestrom je Sekunde.
/// </summary>
public readonly record struct PowerFlow(float Surplus, float ShieldPower, float BankPower, float EnginePower, float ShieldRate, float ChargeRate);

/// <summary>
/// Phase 23 (KAMPF.md Abschnitt 3.2): die Leistungsregel als reine Funktionen und die wenigen Schreibstellen für den Laufzeitzustand
/// (<see cref="ShipSystemsState"/>). Der Reaktorüberschuss (Leistung − Dauerlast) wird nach der Leistungsverteilung aufgeteilt: der Waffenanteil lädt die
/// Speicherbank, der Schildanteil regeneriert den Schild (höchstens stats shieldRegen je Sekunde), der Antriebsanteil bleibt beim Antrieb. Ist Schild oder
/// Bank voll (oder fehlt), geht ihr Anteil an den anderen der beiden. Wer den Zustand senkt, ruft die Schreibstellen hier auf, damit
/// <see cref="ShipSystemsSystem"/> das Schiff fortschreibt, bis es wieder voll ist.
/// </summary>
public static class ShipSystems
{
    /// <summary>Anteile je Stellung: ausgeglichen je ein Drittel, sonst bekommt das gewählte System <paramref name="focus"/> (mindestens ein Drittel), die anderen teilen den Rest.</summary>
    public static PowerSplit Split(PowerMode mode, float focus)
    {
        const float third = 1f / 3f;
        float f = Math.Clamp(focus, third, 1f), rest = (1f - f) / 2f;
        return mode switch
        {
            PowerMode.Weapons => new PowerSplit(f, rest, rest),
            PowerMode.Shield => new PowerSplit(rest, f, rest),
            PowerMode.Engine => new PowerSplit(rest, rest, f),
            _ => new PowerSplit(third, third, third),
        };
    }

    /// <summary>Reaktorüberschuss = Leistung − Dauerlast (nie negativ). Spitzenlasten zieht später jeder Schuss aus der Bank.</summary>
    public static float Surplus(ShipStats s) => MathF.Max(0f, s.PowerOutput - s.PowerDraw);

    /// <summary>
    /// Leistungsfluss (rein): <paramref name="shieldNeeds"/>/<paramref name="bankNeeds"/> = Schild bzw. Bank kann gerade Leistung aufnehmen (fehlt etwas,
    /// und beim Schild: keine Pause nach einem Treffer). Ein System, das nichts aufnimmt, gibt seinen Anteil an das andere weiter.
    /// </summary>
    public static PowerFlow Flow(ShipStats s, PowerMode mode, bool shieldNeeds, bool bankNeeds, EconomyRules r)
    {
        float p = Surplus(s);
        var split = Split(mode, r.PowerFocusShare);
        float shieldP = split.Shield * p, bankP = split.Weapons * p, engineP = split.Engine * p;
        float toShield = shieldNeeds ? shieldP + (bankNeeds ? 0f : bankP) : 0f;
        float toBank = bankNeeds ? bankP + (shieldNeeds ? 0f : shieldP) : 0f;
        float shieldRate = shieldNeeds ? MathF.Min(s.ShieldRegen, toShield * r.ShieldPerPower) : 0f;
        float chargeRate = bankNeeds ? toBank * r.ChargePerPower : 0f;
        return new PowerFlow(p, toShield, toBank, engineP, shieldRate, chargeRate);
    }

    /// <summary>
    /// Schreibt Schild und Bank um <paramref name="seconds"/> fort (rein bis auf den übergebenen Zustand). <paramref name="shieldPaused"/> = Schild in der
    /// Pause nach einem Treffer (sein Anteil lädt dann die Bank). Liefert, ob noch etwas fehlt.
    /// </summary>
    public static bool Advance(ShipStats s, ShipSystemsState st, float seconds, bool shieldPaused, EconomyRules r)
    {
        bool shieldNeeds = !shieldPaused && st.ShieldMissing > 0f && s.Shield > 0f;
        bool bankNeeds = st.ChargeMissing > 0f && s.PowerStorage > 0f;
        if (shieldNeeds || bankNeeds)
        {
            var flow = Flow(s, st.Mode, shieldNeeds, bankNeeds, r);
            if (shieldNeeds) st.ShieldMissing = MathF.Max(0f, st.ShieldMissing - flow.ShieldRate * seconds);
            if (bankNeeds) st.ChargeMissing = MathF.Max(0f, st.ChargeMissing - flow.ChargeRate * seconds);
        }
        return NeedsRecovery(s, st);
    }

    /// <summary>Fehlt Schild oder Ladung (bei vorhandenem Schild bzw. vorhandener Bank)?</summary>
    public static bool NeedsRecovery(ShipStats s, ShipSystemsState st) =>
        (st.ShieldMissing > 0f && s.Shield > 0f) || (st.ChargeMissing > 0f && s.PowerStorage > 0f);

    /// <summary>Fehlt dem Schiff Schild oder Ladung?</summary>
    public static bool NeedsRecovery(Ship ship) => NeedsRecovery(ship.Stats, ship.Systems);

    /// <summary>
    /// Phase 24 (Prüfung Phase 23): Kann sich das, was fehlt, überhaupt erholen? Ohne Reaktorüberschuss lädt weder Schild noch Bank; ein Schild ohne
    /// shieldRegen lädt nie. Solche Schiffe bleiben nicht im Erholungsindex (sonst stünden sie dort für immer); ein Modulwechsel weckt sie wieder
    /// (<see cref="Ship.RecomputeStats"/>). Rein.
    /// </summary>
    public static bool CanRecover(ShipStats s, ShipSystemsState st)
    {
        if (Surplus(s) <= 0f) return false;
        return (st.ShieldMissing > 0f && s.Shield > 0f && s.ShieldRegen > 0f) || (st.ChargeMissing > 0f && s.PowerStorage > 0f);
    }

    /// <summary>Gehört das Schiff in den Erholungsindex (es fehlt etwas, und es kann sich erholen)?</summary>
    public static bool Recovers(Ship ship) => (NeedsRecovery(ship) && CanRecover(ship.Stats, ship.Systems)) || ship.Systems.Burning.Count > 0;   // Phase 26: Brand

    /// <summary>Meldet das Schiff zur Erholung an, wenn ihm etwas fehlt und es sich erholen kann (sonst nichts).</summary>
    public static void Wake(Universe u, Ship ship)
    {
        if (Recovers(ship)) u.Recovering.Add(ship.Id);
    }

    /// <summary>Phase 24: Schaden am Schild (aus der Trefferregel, schon nach Schadensart umgerechnet): Fehlbetrag steigt, Pause beginnt, Erholung angemeldet.</summary>
    public static void TakeShield(Universe u, Ship ship, float amount)
    {
        if (amount <= 0f || ship.Stats.Shield <= 0f) return;
        ship.Systems.ShieldMissing = MathF.Min(ship.Stats.Shield, ship.Systems.ShieldMissing + amount);
        ship.Systems.ShieldPauseUntil = u.Tick + u.Rules.ShieldDelayTicks;
        Wake(u, ship);
    }

    /// <summary>
    /// Schaden trifft zuerst den Schild (KAMPF.md 3.3): Der Schild fängt so viel ab, wie er hat, und pausiert dann <see cref="EconomyRules.ShieldDelayTicks"/>.
    /// Liefert den Rest, der auf die Hülle geht.
    /// </summary>
    public static float Absorb(Universe u, Ship ship, float damage)
    {
        if (damage <= 0f) return 0f;
        float shield = ship.ShieldNow;
        if (shield <= 0f) return damage;
        float taken = MathF.Min(shield, damage);
        ship.Systems.ShieldMissing = MathF.Min(ship.Stats.Shield, ship.Systems.ShieldMissing + taken);
        ship.Systems.ShieldPauseUntil = u.Tick + u.Rules.ShieldDelayTicks;
        Wake(u, ship);
        return damage - taken;
    }

    /// <summary>Entnimmt Ladung aus der Speicherbank (Phase 24: Spitzenlast je Schuss). Liefert, wie viel entnommen wurde.</summary>
    public static float Drain(Universe u, Ship ship, float amount)
    {
        float take = MathF.Min(MathF.Max(0f, amount), ship.Charge);
        if (take <= 0f) return 0f;
        ship.Systems.ChargeMissing += take;
        Wake(u, ship);
        return take;
    }

    /// <summary>Stellt die Leistungsverteilung um (Spieler über drei Tasten, NPCs künftig über ihre Taktik).</summary>
    public static void SetMode(Universe u, Ship ship, PowerMode mode)
    {
        ship.Systems.Mode = mode;
        Wake(u, ship);
    }

    /// <summary>
    /// Setzt den Zustand eines Slots (0..1) und rechnet die Schiffswerte neu (ein Modul unter <see cref="EconomyRules.ModuleFailCondition"/> fällt aus).
    /// In Phase 23 ruft das nur der Test auf; Modulschaden kommt mit Phase 26.
    /// </summary>
    public static void SetCondition(Universe u, Ship ship, int slot, float value)
    {
        if (slot < 0 || slot >= Math.Max(ship.Slots.Count, ship.Fit.Count)) return;
        var c = ship.Systems.Condition;
        while (c.Count <= slot) c.Add(1f);
        c[slot] = Math.Clamp(value, 0f, 1f);
        if (ship.Systems.AllIntact) c.Clear();   // unbeschädigt = leere Liste (nichts im Spielstand)
        ship.RecomputeStats(u);
        Wake(u, ship);
    }

    /// <summary>Munition im Magazin; mit abgeschaltetem Schalter <see cref="EconomyRules.AmmoActive"/> gilt das Magazin als voll.</summary>
    public static float Ammo(Universe u, Ship ship) => u.Rules.AmmoActive ? ship.AmmoLoaded : ship.Stats.AmmoCapacity;

    /// <summary>
    /// Tempofaktor aus der Leistungsverteilung: Stellung „Antrieb“ mit Überschuss = 1 + <see cref="EconomyRules.EngineModeSpeedBonus"/>, sonst 1. Gilt in
    /// Phase 23 nur für den Spielerflug (die Ansicht multipliziert das Höchsttempo); NPC-Reisen bleiben unverändert.
    /// </summary>
    public static float SpeedFactor(Universe u, Ship ship) =>
        ship.Systems.Mode == PowerMode.Engine && Surplus(ship.Stats) > 0f ? 1f + u.Rules.EngineModeSpeedBonus : 1f;

    /// <summary>Gerätetest: kurze Rückmeldung zur Wirkung einer Stellung (Meldung beim Umschalten).</summary>
    public static string ModeEffect(PowerMode m, bool hasBank = true) => m switch
    {
        // Gerätetest Phase 27: Überladung nur nennen, wo es eine Speicherbank gibt
        PowerMode.Weapons => hasBank ? "Leistung: Waffen — Überschuss lädt die Speicherbank, Überladung (schnelleres Nachladen)"
            : "Leistung: Waffen — Überschuss für die Waffen (ohne Speicherbank keine Überladung)",
        PowerMode.Shield => "Leistung: Schild — der Schild lädt schneller nach",
        PowerMode.Engine => "Leistung: Antrieb — +10 % Höchsttempo",
        _ => "Leistung: ausgeglichen",
    };

    /// <summary>Kurzname der Stellung (Anzeige).</summary>
    public static string ModeName(PowerMode m) => m switch
    {
        PowerMode.Weapons => "Waffen",
        PowerMode.Shield => "Schild",
        PowerMode.Engine => "Antrieb",
        _ => "ausgeglichen",
    };

    /// <summary>Spielzeit in Sekunden, die ein Schritt von <paramref name="dt"/> Ticks dauert.</summary>
    public static float Seconds(int dt) => dt * SimClock.TickInterval;
}

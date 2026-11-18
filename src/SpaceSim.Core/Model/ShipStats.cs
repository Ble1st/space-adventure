namespace SpaceSim.Core.Model;

/// <summary>
/// Reine Ableitung aller Schiffs­werte aus Klasse und Fit (<see cref="Compute"/>). Phase 3a: noch ohne
/// Verbraucher — Ship.MaxHull, Ship.CargoCapacity und Travel.ShipSpeed bleiben vorerst maßgeblich.
/// </summary>
public sealed class ShipStats
{
    /// <summary>Höchsttempo; Schub/Gesamtmasse wird darauf begrenzt.</summary>
    public const float MaxSpeed = 12f;

    /// <summary>Leistung aller Reaktoren (stats powerOutput).</summary>
    public float PowerOutput;
    /// <summary>Dauerlast aller Module (Summe powerDraw).</summary>
    public float PowerDraw;
    /// <summary>Spitzenlast aller Module (Summe peakDraw), gepuffert von der Speicherbank.</summary>
    public float PeakDraw;
    /// <summary>Kapazität der Speicherbänke (Summe stats powerStorage).</summary>
    public float PowerStorage;
    /// <summary>Laderaum: Summe der Kapazitäten (stats capacity) aller Frachtmodule; holds any zählt voll, die 70%-Regel steckt in den Modul-Daten.</summary>
    public float Cargo;
    /// <summary>Feststoff-Laderaum: Summe der Kapazitäten der Frachtmodule mit holds solid (0 = keiner).</summary>
    public float CargoSolid;
    /// <summary>Flüssigstoff-Laderaum (Kanister): Summe der Kapazitäten der Frachtmodule mit holds liquid (0 = keiner).</summary>
    public float CargoLiquid;
    /// <summary>Universal-Laderaum: Summe der Kapazitäten der Frachtmodule mit holds any; wird zwischen den Formen geteilt.</summary>
    public float CargoAny;
    /// <summary>Hülle: baseHull der Klasse + Panzerung (Summe stats armor).</summary>
    public float Hull;
    /// <summary>Gesamtmasse: baseMass der Klasse + Summe der Modulmassen.</summary>
    public float Mass;
    /// <summary>Schub: Summe stats thrust.</summary>
    public float Thrust;
    /// <summary>Tempo = Schub / Gesamtmasse, begrenzt auf <see cref="MaxSpeed"/>.</summary>
    public float Speed;
    /// <summary>Unterhalt: Summe upkeep der Module.</summary>
    public float Upkeep;
    /// <summary>Fähigkeiten: Vereinigung aller grants der Module.</summary>
    public Capability Capabilities = Capability.None;
    /// <summary>Laserstufe des Werkzeugmoduls mit Mining (stats chunkChanceMult), sonst 1.</summary>
    public float LaserChunkChanceMult = 1f;
    /// <summary>Laserstufe des Werkzeugmoduls mit Mining (stats chunkSizeMult), sonst 1.</summary>
    public float LaserChunkSizeMult = 1f;
    /// <summary>Phase 29 (Kontaktabbau): Erz je Sekunde des mechanischen Abbauwerkzeugs (Bohrkopf, Schneidrad, Schneidwalze; Modulstat <c>contactMining</c>) bei einer
    /// Asteroidenklasse mit Brockenchance 0,5 — 0 = kein Kontaktwerkzeug.</summary>
    public float ContactMining;
    /// <summary>Phase 29: ein Bergbaulaser ist eingebaut (Werkzeug mit Mining ohne <c>contactMining</c>).</summary>
    public bool HasLaser;
    /// <summary>
    /// Phase 29: Abbaustufe relativ zum Mining-Laser Mk1 (= 1) — Laser: Brockenchance × Brockengröße, Kontaktwerkzeug: Rate ÷ <see cref="Mining.MiningRules.ExpectedOrePerSecond"/>;
    /// das bessere zählt (Kapazitätseinheiten, Kaufwert, erwarteter Ertrag).
    /// </summary>
    public float MiningTier => MathF.Max(HasLaser ? LaserChunkChanceMult * LaserChunkSizeMult : 0f, ContactMining / Mining.MiningRules.ExpectedOrePerSecond);
    /// <summary>Phase 29: das Schiff baut im Kontaktmodus ab (Kontaktwerkzeug mindestens so gut wie ein eingebauter Laser).</summary>
    public bool UsesContact => ContactMining > 0f && (!HasLaser || ContactMining / Mining.MiningRules.ExpectedOrePerSecond >= LaserChunkChanceMult * LaserChunkSizeMult);
    /// <summary>Tankkapazität: baseFuel der Klasse + Summe stats fuelCapacity der Module (z. B. Tanks).</summary>
    public float FuelCapacity;
    /// <summary>Zulässige Treibstoffe der Antriebsmodule (Ware → efficiency); bei mehreren Antrieben Vereinigung, höchste efficiency je Ware.</summary>
    public List<FuelType> FuelTypes = new();
    /// <summary>Treibstoffverbrauch der Antriebsmodule: Summe stats fuelUse (Einheiten Treibstoff je geflogener Einheit, vor efficiency). 0 = kein Verbrauch.</summary>
    public float FuelUsePerUnit;
    /// <summary>Phase 8: Spaltlaser (Asteroidenbrecher, stats splitPower); 0 = kein Spaltlaser.</summary>
    public float SplitPower;
    /// <summary>Phase 10: Tarnstärke (Summe stats stealth; heute hat kein Modul sie, der Tarnfeld-Baustein ist zurückgestellt). Entdeckung an Kontrollpunkten vergleicht sie mit der Sensorstärke.</summary>
    public float Stealth;
    /// <summary>Phase 11: Waffenstärke (Summe stats weaponPower der Waffenmodule); Grundlage der abstrakten Begegnungen.</summary>
    public float WeaponPower;
    /// <summary>Phase 26: zusätzliche Löschchance je Brandrunde (Summe stats extinguish, Löschanlage); ausgefallene Anlagen löschen nicht.</summary>
    public float Extinguish;
    /// <summary>Phase 11: Schildkapazität (Summe stats shieldCapacity); zählt in der Kampfstärke mit.</summary>
    public float Shield;
    /// <summary>Phase 23: Schildregeneration je Sekunde (Summe stats shieldRegen), höchstens — gespeist aus dem Reaktorüberschuss (<see cref="Combat.ShipSystems"/>).</summary>
    public float ShieldRegen;
    /// <summary>Phase 23: Magazin für Munition (Summe stats ammoCapacity: Magazinmodul, künftig Geschütze), getrennt vom Laderaum wie der Tank.</summary>
    public float AmmoCapacity;
    /// <summary>Phase 28: Raketenmagazin (Summe stats missileCapacity der Raketenwerfer), getrennt vom Munitionsmagazin.</summary>
    public float MissileCapacity;
    /// <summary>Phase 28: Punktabwehr — Chance je anfliegender Rakete, sie abzuschießen (Summe stats pointDefense, gedeckelt in den Regeln).</summary>
    public float PointDefense;
    /// <summary>Phase 24: Panzerung (Summe stats armor; steckt auch in <see cref="Hull"/>) — zieht je Treffer einen festen Betrag ab (<see cref="Combat.Hit"/>).</summary>
    public float Armor;
    /// <summary>Sprungantrieb (SCHIFFSSLOTS.md 4): Reichweite in Gate-Hops (Modulstat <c>jumpRange</c>, das beste Modul zählt); 0 = kein Sprungantrieb.</summary>
    public int JumpRange;
    /// <summary>Sprungantrieb: Ladezeit in Ticks (<c>jumpCharge</c>).</summary>
    public float JumpChargeTicks;
    /// <summary>Sprungantrieb: Abklingzeit nach dem Sprung in Ticks (<c>jumpCooldown</c>).</summary>
    public float JumpCooldownTicks;
    /// <summary>Sprungantrieb: zusätzliche Leistung während des Ladens (<c>jumpDraw</c>, aus dem Reaktorüberschuss).</summary>
    public float JumpDraw;
    /// <summary>Sprungantrieb: Treibstoff je Sprung (<c>jumpFuel</c>; nur mit Schalter <c>fuelActive</c>).</summary>
    public float JumpFuel;
    /// <summary>Tarnfeld (SCHIFFSSLOTS.md 6): Tarnstärke, solange die Tarnung an ist (<c>cloak</c>, das beste Modul zählt); 0 = kein Tarnfeld.</summary>
    public float Cloak;
    /// <summary>Tarnfeld: zusätzliche Leistung, solange es an ist (<c>cloakDraw</c>, aus dem Reaktorüberschuss).</summary>
    public float CloakDraw;
    /// <summary>Phase 24: Waffenstärke je Schaden pro Sekunde (abgeleitete Kennzahl aus den Waffenwerten: Schaden × Treffsicherheit ÷ Nachladezeit × dieser Faktor).</summary>
    public const float WeaponPowerPerDps = 2f;

    /// <summary>
    /// Berechnet die Werte eines Schiffs aus seiner Klasse und dem Fit (ein Eintrag je Slot, null = leer).
    /// Reine Funktion ohne Universum und ohne Nebeneffekte.
    /// </summary>
    public static ShipStats Compute(ShipClass cls, IReadOnlyList<Module?> fit) => Compute(cls, fit, null, 0f);

    /// <summary>
    /// Phase 23: wie <see cref="Compute(ShipClass, IReadOnlyList{Module?})"/>, aber mit dem Zustand je Slot (<paramref name="condition"/>, fehlender
    /// Eintrag = 1). Ein Modul unter <paramref name="failBelow"/> ist ausgefallen: Es behält Masse, Tank, Magazin und Speicherbank (der Inhalt
    /// verschwindet nicht) und Panzerung — seit Phase 26 aber nicht mehr seinen Frachtraum, verliert aber Dauerlast, Fähigkeiten und alle übrigen Werte. Reaktor und Antrieb fallen nie ganz aus, sie liefern
    /// die halbe Leistung bzw. den halben Schub (KAMPF.md 3.3: kein Schiff bleibt unrettbar liegen). Reine Funktion.
    /// </summary>
    public static ShipStats Compute(ShipClass cls, IReadOnlyList<Module?> fit, IReadOnlyList<float>? condition, float failBelow)
    {
        var s = new ShipStats { Mass = cls.BaseMass, FuelCapacity = cls.BaseFuel };
        for (int slot = 0; slot < fit.Count; slot++)
        {
            var m = fit[slot];
            if (m is null) continue;
            bool failed = condition is not null && slot < condition.Count && condition[slot] < failBelow;
            if (failed)
            {
                AddFailed(s, m);
                continue;
            }
            s.Mass += m.Mass;
            s.PowerDraw += m.PowerDraw;
            s.PeakDraw += m.PeakDraw;
            s.Upkeep += m.Upkeep;
            foreach (var c in m.Grants) s.Capabilities |= c;
            if (m.Stats.TryGetValue("powerOutput", out var output)) s.PowerOutput += output;
            if (m.Stats.TryGetValue("powerStorage", out var storage)) s.PowerStorage += storage;
            if (m.Stats.TryGetValue("fuelCapacity", out var fuelCap)) s.FuelCapacity += fuelCap;
            if (m.Stats.TryGetValue("fuelUse", out var fuelUse)) s.FuelUsePerUnit += fuelUse;   // Phase 4b: Verbrauchsrate des Antriebs
            if (m.Stats.TryGetValue("splitPower", out var split)) s.SplitPower += split;      // Phase 8: Spaltlaser
            if (m.Stats.TryGetValue("stealth", out var stealth)) s.Stealth += stealth;        // Phase 10: Tarnung (Entdeckung)
            s.WeaponPower += WeaponPowerOf(m);                                                // Phase 11/24: Waffen (Kennzahl für Stärke und KI)
            if (m.Stats.TryGetValue("shieldCapacity", out var sh)) s.Shield += sh;            // Phase 11: Schild zählt in der Stärke
            if (m.Stats.TryGetValue("shieldRegen", out var regen)) s.ShieldRegen += regen;    // Phase 23: Schild lädt nach
            if (m.Stats.TryGetValue("ammoCapacity", out var ammo)) s.AmmoCapacity += ammo;    // Phase 23: Magazin
            if (m.Stats.TryGetValue("extinguish", out var ext)) s.Extinguish += ext;          // Phase 26: Löschanlage
            if (m.Stats.TryGetValue("missileCapacity", out var mc)) s.MissileCapacity += mc;  // Phase 28: Raketenmagazin
            if (m.Stats.TryGetValue("pointDefense", out var pd)) s.PointDefense += pd;        // Phase 28: Punktabwehr
            if (m.Stats.TryGetValue("jumpRange", out var jr) && (int)jr > s.JumpRange)       // Sprungantrieb: das beste Modul zählt
            {
                s.JumpRange = (int)jr;
                s.JumpChargeTicks = m.Stat("jumpCharge"); s.JumpCooldownTicks = m.Stat("jumpCooldown"); s.JumpDraw = m.Stat("jumpDraw"); s.JumpFuel = m.Stat("jumpFuel");
            }
            if (m.Stats.TryGetValue("cloak", out var ck) && ck > s.Cloak) { s.Cloak = ck; s.CloakDraw = m.Stat("cloakDraw"); }   // Tarnfeld: das beste Modul zählt
            foreach (var f in m.FuelTypes)
            {
                int idx = s.FuelTypes.FindIndex(x => x.Good == f.Good);
                if (idx < 0) s.FuelTypes.Add(f);
                else if (f.Efficiency > s.FuelTypes[idx].Efficiency) s.FuelTypes[idx] = f;   // mehrere Antriebe: höchste efficiency je Ware
            }
            if (m.Stats.TryGetValue("capacity", out var capacity))
            {
                s.Cargo += capacity;
                switch (m.Holds)
                {
                    case ModuleHolds.Solid: s.CargoSolid += capacity; break;
                    case ModuleHolds.Liquid: s.CargoLiquid += capacity; break;
                    default: s.CargoAny += capacity; break;
                }
            }
            if (m.Stats.TryGetValue("armor", out var armor)) { s.Hull += armor; s.Armor += armor; }
            if (m.Stats.TryGetValue("thrust", out var thrust)) s.Thrust += thrust;
            if (m.Grants.Contains(Capability.Mining))
            {
                if (m.Stats.TryGetValue("contactMining", out var contact)) s.ContactMining += contact;   // Phase 29: mechanisches Werkzeug
                else
                {
                    s.HasLaser = true;
                    s.LaserChunkChanceMult = m.Stats.TryGetValue("chunkChanceMult", out var ccm) ? ccm : 1f;
                    s.LaserChunkSizeMult = m.Stats.TryGetValue("chunkSizeMult", out var csm) ? csm : 1f;
                }
            }
        }
        s.LaserChunkChanceMult *= cls.ToolEfficiency;   // Phase 6: Rumpf mit geteilter Leistung (Mehrzweck) schwächt den Laser
        s.ContactMining *= cls.ToolEfficiency;
        Finish(s, cls);
        return s;
    }

    /// <summary>Phase 23: ausgefallenes Modul — Masse, Lager und Panzerung bleiben, Reaktor/Antrieb mit halber Leistung, sonst nichts.</summary>
    static void AddFailed(ShipStats s, Module m)
    {
        s.Mass += m.Mass;
        s.Upkeep += m.Upkeep;
        bool core = m.Stats.ContainsKey("powerOutput") || m.Stats.ContainsKey("thrust");
        if (core) s.PowerDraw += m.PowerDraw;   // Reaktor/Antrieb laufen mit halber Leistung weiter und ziehen ihre Last
        if (m.Stats.TryGetValue("powerOutput", out var output)) s.PowerOutput += 0.5f * output;
        if (m.Stats.TryGetValue("thrust", out var thrust)) s.Thrust += 0.5f * thrust;
        if (m.Stats.TryGetValue("armor", out var armor)) { s.Hull += armor; s.Armor += armor; }   // Panzerplatten bleiben Panzerplatten
        if (m.Stats.TryGetValue("powerStorage", out var storage)) s.PowerStorage += storage;
        if (m.Stats.TryGetValue("fuelCapacity", out var fuelCap)) s.FuelCapacity += fuelCap;
        if (m.Stats.TryGetValue("ammoCapacity", out var ammo)) s.AmmoCapacity += ammo;
        if (m.Stats.TryGetValue("missileCapacity", out var mc)) s.MissileCapacity += mc;   // Phase 28: Magazin bleibt Lager
        if (m.Stats.TryGetValue("fuelUse", out var fuelUse)) s.FuelUsePerUnit += fuelUse;   // ein halber Antrieb verbraucht weiter
        foreach (var f in m.FuelTypes)
            if (s.FuelTypes.FindIndex(x => x.Good == f.Good) < 0) s.FuelTypes.Add(f);
        // Phase 26: ein ausgefallenes Frachtmodul hält keine Fracht mehr (die überzählige Fracht fällt als Frachtbehälter ins All, ModuleDamage.Spill)
    }

    /// <summary>
    /// Phase 24: Waffenstärke eines Moduls — mit Waffenwerten abgeleitet (Schaden × Treffsicherheit ÷ Nachladezeit × <see cref="WeaponPowerPerDps"/>), sonst die
    /// freie Kennzahl <c>weaponPower</c> (alte Inhalte). Rein.
    /// </summary>
    public static float WeaponPowerOf(Module m)
    {
        if (m.IsWeapon && m.Stat("reload") > 0f)
            return m.Stat("damage") * Math.Clamp(m.Stat("accuracy"), 0f, 1f) / m.Stat("reload") * WeaponPowerPerDps;
        return m.Stats.TryGetValue("weaponPower", out var wp) ? wp : 0f;
    }

    static void Finish(ShipStats s, ShipClass cls)
    {
        s.Hull += cls.BaseHull;
        s.Speed = s.Mass > 0f ? MathF.Min(MaxSpeed, s.Thrust / s.Mass) : 0f;
    }
}

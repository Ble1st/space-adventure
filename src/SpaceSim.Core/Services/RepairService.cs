using SpaceSim.Core.Events;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Services;

/// <summary>Phase 26: Ergebnis einer Reparatur (reparierte Hüllenpunkte, Zahl der instand gesetzten Module, Material, Kosten, ob das Lager/Geld knapp war).</summary>
public readonly record struct RepairOutcome(float Hull, int Modules, float Plates, float Electronics, float Cost, bool Partial)
{
    public bool Any => Hull > 0.0001f || Modules > 0;
}

/// <summary>
/// Dienst „repair“ (Phase 4c, Phase 26 auch Module): stellt die Hülle (<see cref="Ship.MaxHull"/>) und den Zustand beschädigter Module wieder her.
/// <list type="bullet">
/// <item><b>Hülle:</b> Bedarf = fehlende Hülle × <see cref="EconomyRules.RepairPlatesPerHull"/> Hüllenplatten.</item>
/// <item><b>Modul</b> (Phase 26): Bedarf je fehlendem Zustandspunkt (0..1) und Größenstufe des Slots (S 1, M 2, L 3, XL 4)
/// <see cref="EconomyRules.ModuleRepairPlates"/> Hüllenplatten und <see cref="EconomyRules.ModuleRepairElectronics"/> Elektronik; ein repariertes Modul brennt nicht mehr.</item>
/// </list>
/// Preis = Material × Marktpreis der Station × <see cref="LaborFactor"/> (wie bisher), Material aus dem Stationslager, Geld an die Station, gezahlt aus
/// <see cref="Universe.WalletOf(Ship)"/> (Spieler: Kasse, NPC: Schiff). Fehlt Lager oder Geld, wird anteilig repariert. Strikte Schlüssel: „repair:full“, „repair:half“
/// (Hülle), „repair:module:&lt;slot&gt;“ (ein Modul), „repair:all“ (Hülle und alle Module). Unbekannte Schlüssel, nichts zu reparieren oder kein Material/Geld ändern
/// nichts und lösen kein Ereignis aus; Erfolg veröffentlicht <see cref="ServicePerformed"/> und <see cref="ShipRepaired"/>. NPC-Schiffe reparieren über
/// <see cref="Perform"/> (Simulation/NpcRepair). Amount/Units = reparierte Hüllenpunkte bzw. Zustandspunkte, Price = Preis je Einheit.
/// </summary>
public sealed class RepairService : IStationService
{
    public const string ServiceKey = "repair";
    /// <summary>Warenschlüssel der Hüllenplatten (Inhaltsverweis wie <see cref="EconomyRules.ShipKitGoodKey"/>).</summary>
    public const string PlatesGoodKey = "hull_plates";
    /// <summary>Phase 26: Warenschlüssel der Elektronik (Modulreparatur).</summary>
    public const string ElectronicsGoodKey = "electronics";
    /// <summary>Angebots-Schlüssel „repair:full“: Hülle voll wiederherstellen.</summary>
    public const string Full = "full";
    /// <summary>Angebots-Schlüssel „repair:half“: Hälfte der fehlenden Hülle wiederherstellen.</summary>
    public const string Half = "half";
    /// <summary>Phase 26: „repair:all“ — Hülle und alle Module.</summary>
    public const string All = "all";
    /// <summary>Phase 26 (Prüfung): „repair:emergency“ — Notreparatur auf Schuld (nur Funktionsfähigkeit).</summary>
    public const string Emergency = "emergency";
    /// <summary>Phase 26: „repair:module:&lt;slot&gt;“ — ein Modul.</summary>
    public const string ModulePrefix = "module:";
    /// <summary>Arbeitsaufschlag auf den Materialpreis.</summary>
    public const float LaborFactor = 1.5f;

    public string Key => ServiceKey;

    /// <summary>Die Hüllenplatten-Ware dieses Universums, sonst null (Welt ohne „hull_plates“ hat keinen Reparaturdienst).</summary>
    public static GoodId? PlatesGood(Universe u) => u.FindGood(PlatesGoodKey)?.Id;
    /// <summary>Phase 26: Elektronik-Ware (Modulreparatur), sonst null.</summary>
    public static GoodId? ElectronicsGood(Universe u) => u.FindGood(ElectronicsGoodKey)?.Id;

    /// <summary>Verfügbar, wenn die Station Hüllenplatten im Markt führt und Bestand &gt; 0 sowie Preis &gt; 0 hat.</summary>
    public bool IsAvailable(Universe u, Station station) => Available(u, station);

    /// <summary>Wie <see cref="IsAvailable"/>, statisch.</summary>
    public static bool Available(Universe u, Station station)
    {
        var good = PlatesGood(u);
        return good is { } g && station.Market.TryGetValue(g, out var e) && e.Amount > 0f && e.Price > 0f;
    }

    /// <summary>Größenstufe eines Slots für die Modulreparatur (S 1 … XL 4).</summary>
    public static float SizeFactor(Ship ship, int slot) => slot >= 0 && slot < ship.Slots.Count ? (int)ship.Slots[slot].Size + 1 : 1f;

    /// <summary>Phase 26: beschädigte Module (Slots aufsteigend, Zustand &lt; 1).</summary>
    public static List<int> DamagedSlots(Ship ship)
    {
        var list = new List<int>();
        for (int i = 0; i < ship.Fit.Count; i++)
            if (ship.Fit[i] is not null && ship.ConditionOf(i) < 0.999f) list.Add(i);
        return list;
    }

    /// <summary>Ist am Schiff etwas zu reparieren (Hülle, Modul, Brand)?</summary>
    public static bool NeedsRepair(Ship ship) =>
        ship.MaxHull - ship.Hull > 0.5f || ship.Systems.Burning.Count > 0 || DamagedSlots(ship).Count > 0;

    static float Price(Station st, GoodId? g) => g is { } id && st.Market.TryGetValue(id, out var e) ? e.Price : 0f;
    static float Stock(Station st, GoodId? g) => g is { } id && st.Market.TryGetValue(id, out var e) ? MathF.Max(0f, e.Amount) : 0f;

    /// <summary>Preis je Hüllenpunkt: Plattenmarktpreis × Plattenbedarf je Punkt × <see cref="LaborFactor"/>.</summary>
    public static float PricePerHull(Universe u, Station st) => Price(st, PlatesGood(u)) * u.Rules.RepairPlatesPerHull * LaborFactor;

    /// <summary>Phase 26: Preis je Zustandspunkt (0..1) eines Moduls in diesem Slot (Platten + Elektronik × Größenstufe × Aufschlag); 0 = Station kann keine Module.</summary>
    public static float PricePerModulePoint(Universe u, Station st, Ship ship, int slot)
    {
        var r = u.Rules;
        float pp = Price(st, PlatesGood(u)), pe = Price(st, ElectronicsGood(u));
        if (pp <= 0f || (r.ModuleRepairElectronics > 0f && pe <= 0f)) return 0f;
        return (r.ModuleRepairPlates * pp + r.ModuleRepairElectronics * pe) * SizeFactor(ship, slot) * LaborFactor;
    }

    /// <summary>
    /// Führt eine Reparatur aus (gemeinsame Regel für Spieler und NPC): erst <paramref name="hullShare"/> der fehlenden Hülle, dann die Module in
    /// <paramref name="slots"/> (aufsteigend) — jeweils so weit Lager (Platten, Elektronik) und Geld (höchstens <paramref name="maxCost"/>) reichen. Bucht Material,
    /// Geld und Zustand, löscht Brände reparierter Module und veröffentlicht <see cref="ShipRepaired"/> (ohne <see cref="ServicePerformed"/>, das setzt der Dienst).
    /// </summary>
    public static RepairOutcome Perform(Universe u, Station st, Ship ship, float hullShare, IReadOnlyList<int> slots, float maxCost, bool modulesFirst = false)
    {
        var r = u.Rules;
        var plates = PlatesGood(u);
        var elec = ElectronicsGood(u);
        if (plates is null || !Available(u, st)) return default;
        float budget = MathF.Max(0f, maxCost), cost = 0f, platesUsed = 0f, elecUsed = 0f, hullDone = 0f;
        int modulesDone = 0;
        bool partial = false;
        var wallet = u.WalletOf(ship);
        void DoHull()
        {
            float missing = MathF.Max(0f, ship.MaxHull - ship.Hull) * Math.Clamp(hullShare, 0f, 1f);
            if (missing > 0.0001f)
            {
                float pph = PricePerHull(u, st);
                float amount = missing;
                if (r.RepairPlatesPerHull > 0f) amount = MathF.Min(amount, Stock(st, plates) / r.RepairPlatesPerHull);
                if (pph > 0f) amount = MathF.Min(amount, budget / pph);
                if (amount > 0.0001f)
                {
                    float p = amount * r.RepairPlatesPerHull, c = amount * pph;
                    st.Market[plates.Value].Amount -= p;
                    platesUsed += p; cost += c; budget -= c; hullDone = amount;
                    ship.Hull = MathF.Min(ship.MaxHull, ship.Hull + amount);
                }
                partial |= amount < missing - 0.0001f;
            }
        }
        void DoModules()
        {
            foreach (int slot in slots)
            {
                if (slot < 0 || slot >= ship.Fit.Count || ship.Fit[slot] is null) continue;
                float points = 1f - ship.ConditionOf(slot);
                if (points <= 0.001f && !ship.Systems.IsBurning(slot)) continue;
                float ppm = PricePerModulePoint(u, st, ship, slot);
                if (ppm <= 0f) { partial = true; continue; }
                float size = SizeFactor(ship, slot);
                float needP = r.ModuleRepairPlates * size, needE = r.ModuleRepairElectronics * size;
                float can = points;
                if (needP > 0f) can = MathF.Min(can, Stock(st, plates) / needP);
                if (needE > 0f) can = MathF.Min(can, elec is null ? 0f : Stock(st, elec) / needE);
                can = MathF.Min(can, budget / ppm);
                if (can <= 0.001f) { partial = true; continue; }
                float p = can * needP, el = can * needE, c = can * ppm;
                if (p > 0f) st.Market[plates.Value].Amount -= p;
                if (el > 0f && elec is { } eg) st.Market[eg].Amount -= el;
                platesUsed += p; elecUsed += el; cost += c; budget -= c;
                if (ship.Systems.IsBurning(slot)) Combat.ModuleDamage.EndFire(u, ship, slot, "repaired");
                Combat.ModuleDamage.SetCondition(u, ship, slot, ship.ConditionOf(slot) + can);
                modulesDone++;
                partial |= can < points - 0.001f;
            }
        }
        // Phase 26: Reihenfolge — sonst Hülle zuerst; ein Schiff mit ausgefallenem Modul (arbeitsunfähig) setzt zuerst die Module instand
        if (modulesFirst) { DoModules(); DoHull(); } else { DoHull(); DoModules(); }
        if (cost <= 0f && hullDone <= 0f && modulesDone == 0) return new RepairOutcome(0f, 0, 0f, 0f, 0f, true);
        st.Credits += cost;
        wallet.Credits -= cost;   // Spieler: Spielerkasse, NPC: Schiff
        u.Events.Publish(new ShipRepaired(ship.Id, st.Id, hullDone, modulesDone, cost, platesUsed, elecUsed, false));
        return new RepairOutcome(hullDone, modulesDone, platesUsed, elecUsed, cost, partial);
    }

    /// <summary>
    /// Phase 26 (Prüfung, Soft-Lock): Ist das Schiff funktionsunfähig — ein Modul ausgefallen, ein Brand oder die Hülle unter
    /// <see cref="EconomyRules.EmergencyRepairHullShare"/>? Dann gibt es die Notreparatur auf Schuld.
    /// </summary>
    public static bool NeedsEmergency(Universe u, Ship ship)
    {
        if (ship.Systems.Burning.Count > 0 || ship.Hull < u.Rules.EmergencyRepairHullShare * ship.MaxHull) return true;
        for (int i = 0; i < ship.Fit.Count; i++) if (Combat.ModuleDamage.Failed(u, ship, i)) return true;
        return false;
    }

    /// <summary>
    /// Phase 26 (Prüfung): Notreparatur auf Schuld — stellt nur die Funktionsfähigkeit her: ausgefallene Module auf Ausfallschwelle +
    /// <see cref="EconomyRules.EmergencyRepairMargin"/>, Brände aus, Hülle auf <see cref="EconomyRules.EmergencyRepairHullShare"/>. Preis wie die normale Reparatur
    /// (Marktpreis, ohne Markt/Bestand der Basispreis) × <see cref="LaborFactor"/>; Material aus dem Lager, soweit vorhanden (sonst ohne Material —
    /// das Geld geht trotzdem an die Station). Die Kasse darf ins Minus gehen (Schulden, angezeigt im HUD). Liefert die Kosten.
    /// </summary>
    public static float EmergencyCost(Universe u, Station st, Ship ship, out float hullPoints, out List<(int Slot, float Points)> modules)
    {
        var r = u.Rules;
        hullPoints = MathF.Max(0f, r.EmergencyRepairHullShare * ship.MaxHull - ship.Hull);
        modules = new();
        float target = r.ModuleFailCondition + r.EmergencyRepairMargin;
        for (int i = 0; i < ship.Fit.Count; i++)
            if (Combat.ModuleDamage.Failed(u, ship, i) || ship.Systems.IsBurning(i))
                modules.Add((i, MathF.Max(0f, target - ship.ConditionOf(i))));
        float pp = UnitPrice(u, st, PlatesGood(u)), pe = UnitPrice(u, st, ElectronicsGood(u));
        float cost = hullPoints * r.RepairPlatesPerHull * pp * LaborFactor;
        foreach (var (slot, pts) in modules) cost += pts * (r.ModuleRepairPlates * pp + r.ModuleRepairElectronics * pe) * SizeFactor(ship, slot) * LaborFactor;
        return cost;
    }

    static float UnitPrice(Universe u, Station st, GoodId? g)
    {
        if (g is not { } id) return 0f;
        return st.Market.TryGetValue(id, out var e) && e.Price > 0f ? e.Price : u.Goods[id].BasePrice;
    }

    /// <summary>Notreparatur ausführen (siehe <see cref="EmergencyCost"/>). Ereignis <see cref="ShipRepaired"/>.</summary>
    public static RepairOutcome PerformEmergency(Universe u, Station st, Ship ship)
    {
        var r = u.Rules;
        float cost = EmergencyCost(u, st, ship, out float hull, out var modules);
        float platesNeed = hull * r.RepairPlatesPerHull, elecNeed = 0f;
        foreach (var (slot, pts) in modules) { platesNeed += pts * r.ModuleRepairPlates * SizeFactor(ship, slot); elecNeed += pts * r.ModuleRepairElectronics * SizeFactor(ship, slot); }
        float pUsed = Take(st, PlatesGood(u), platesNeed), eUsed = Take(st, ElectronicsGood(u), elecNeed);
        ship.Hull = MathF.Min(ship.MaxHull, ship.Hull + hull);
        Combat.ModuleDamage.ExtinguishAll(u, ship, "repaired");
        foreach (var (slot, pts) in modules) Combat.ModuleDamage.SetCondition(u, ship, slot, ship.ConditionOf(slot) + pts);
        st.Credits += cost;
        u.WalletOf(ship).Credits -= cost;   // darf ins Minus gehen (Schulden)
        u.Events.Publish(new ShipRepaired(ship.Id, st.Id, hull, modules.Count, cost, pUsed, eUsed, false));
        return new RepairOutcome(hull, modules.Count, pUsed, eUsed, cost, false);
    }

    static float Take(Station st, GoodId? g, float want)
    {
        if (g is not { } id || want <= 0f || !st.Market.TryGetValue(id, out var e)) return 0f;
        float take = MathF.Min(want, MathF.Max(0f, e.Amount));
        e.Amount -= take;
        return take;
    }

    /// <summary>Angebote: Hülle voll/halb (bei Schaden), je beschädigtem Modul eines und „alles“ (wenn Module beschädigt sind).</summary>
    public IReadOnlyList<ServiceOffer> Offers(Universe u, Station station, Ship ship)
    {
        var offers = new List<ServiceOffer>();
        var good = PlatesGood(u);
        if (good is null || !station.Market.TryGetValue(good.Value, out var e) || e.Price <= 0f) return offers;
        float credits = MathF.Max(0f, u.WalletOf(ship).Credits);
        float missing = ship.MaxHull - ship.Hull;
        float pricePerHull = PricePerHull(u, station);
        if (missing > 0.5f)
            foreach (var (key, label, share) in new[] { (Full, "Hülle voll reparieren", 1f), (Half, "Hülle halb reparieren", 0.5f) })
            {
                float amount = missing * share;
                if (u.Rules.RepairPlatesPerHull > 0f) amount = MathF.Min(amount, e.Amount / u.Rules.RepairPlatesPerHull);
                if (pricePerHull > 0f) amount = MathF.Min(amount, credits / pricePerHull);
                offers.Add(new ServiceOffer($"{ServiceKey}:{key}", label, pricePerHull, MathF.Max(0f, amount), good.Value, e.Amount, 0f));
            }
        var damaged = DamagedSlots(ship);
        foreach (int slot in damaged)
        {
            float ppm = PricePerModulePoint(u, station, ship, slot);
            float points = 1f - ship.ConditionOf(slot);
            string name = u.Modules.TryGetValue(ship.Fit[slot]!, out var m) ? m.Name : ship.Fit[slot]!;
            string? note = ppm <= 0f ? "Station führt keine Elektronik für Modulreparaturen" : null;
            float amount = ppm <= 0f ? 0f : MathF.Min(points, Affordable(u, station, ship, slot, credits));
            offers.Add(new ServiceOffer($"{ServiceKey}:{ModulePrefix}{slot}", $"{name} instand setzen ({ship.ConditionOf(slot) * 100f:0} %)",
                ppm, MathF.Max(0f, amount), default, 0f, points) { Note = note });
        }
        if (NeedsEmergency(u, ship))
        {
            float ec = EmergencyCost(u, station, ship, out _, out _);
            offers.Add(new ServiceOffer($"{ServiceKey}:{Emergency}", "Notreparatur (auf Schuld, nur Funktion)", ec, 1f, good.Value, e.Amount, 0f)
                { Note = "Kasse darf ins Minus gehen" });
        }
        if (damaged.Count > 0)
        {
            float total = (missing > 0.5f ? missing * pricePerHull : 0f) + damaged.Sum(s => (1f - ship.ConditionOf(s)) * PricePerModulePoint(u, station, ship, s));
            offers.Add(new ServiceOffer($"{ServiceKey}:{All}", "Alles reparieren (Hülle und Module)", total, total > 0f && credits > 0f ? 1f : 0f, good.Value, e.Amount, 0f));
        }
        return offers;
    }

    /// <summary>Zustandspunkte, die Lager und Geld für dieses Modul hergeben (rein).</summary>
    static float Affordable(Universe u, Station st, Ship ship, int slot, float credits)
    {
        var r = u.Rules;
        float size = SizeFactor(ship, slot), can = float.MaxValue;
        if (r.ModuleRepairPlates > 0f) can = MathF.Min(can, Stock(st, PlatesGood(u)) / (r.ModuleRepairPlates * size));
        if (r.ModuleRepairElectronics > 0f) can = MathF.Min(can, Stock(st, ElectronicsGood(u)) / (r.ModuleRepairElectronics * size));
        float ppm = PricePerModulePoint(u, st, ship, slot);
        if (ppm > 0f) can = MathF.Min(can, credits / ppm);
        return can;
    }

    /// <summary>
    /// Führt ein Angebot aus (strikte Schlüssel, siehe Klassenbeschreibung). Spieler zahlen aus ihrer Kasse (nie ins Minus), NPC aus ihrem Schiff.
    /// </summary>
    public ServiceResult Execute(Universe u, Station station, Ship ship, ServiceRequest request)
    {
        string prefix = ServiceKey + ":";
        if (!request.Offer.StartsWith(prefix, StringComparison.Ordinal)) return new ServiceResult(false, 0f, 0f, $"Unbekanntes Angebot '{request.Offer}'.");
        string rest = request.Offer[prefix.Length..];
        float hullShare;
        List<int> slots;
        if (rest == Emergency)
        {
            if (!NeedsEmergency(u, ship)) return new ServiceResult(false, 0f, 0f, "Das Schiff ist funktionsfähig — keine Notreparatur nötig.");
            if (PlatesGood(u) is null) return new ServiceResult(false, 0f, 0f, $"Dienst '{Key}' ist an dieser Station nicht verfügbar.");
            var eo = PerformEmergency(u, station, ship);
            u.Events.Publish(new ServicePerformed(ship.Id, station.Id, Key, request.Offer, PlatesGood(u)!.Value, eo.Hull + eo.Modules, -eo.Cost));
            float w = u.WalletOf(ship).Credits;
            return new ServiceResult(true, eo.Hull + eo.Modules, -eo.Cost,
                $"Notreparatur für {eo.Cost:0} Cr: {eo.Modules} Modul(e) wieder funktionsfähig, Hülle {ship.Hull:0}/{ship.MaxHull:0}" + (w < 0f ? $" — Schulden {-w:0} Cr." : "."));
        }
        if (rest == Full) { hullShare = 1f; slots = new(); }
        else if (rest == Half) { hullShare = 0.5f; slots = new(); }
        else if (rest == All) { hullShare = 1f; slots = DamagedSlots(ship).OrderBy(s => Combat.ModuleDamage.Failed(u, ship, s) || ship.Systems.IsBurning(s) ? 0 : 1).ThenBy(s => s).ToList(); }
        else if (rest.StartsWith(ModulePrefix, StringComparison.Ordinal) && int.TryParse(rest[ModulePrefix.Length..], System.Globalization.NumberStyles.None,
                     System.Globalization.CultureInfo.InvariantCulture, out int slot) && rest[ModulePrefix.Length..] == slot.ToString(System.Globalization.CultureInfo.InvariantCulture))
        {
            if (slot < 0 || slot >= ship.Fit.Count || ship.Fit[slot] is null || ship.ConditionOf(slot) >= 0.999f)
                return new ServiceResult(false, 0f, 0f, "Dieses Modul ist unbeschädigt.");
            hullShare = 0f; slots = new() { slot };
        }
        else return new ServiceResult(false, 0f, 0f, $"Unbekanntes Angebot '{request.Offer}'.");
        if (!IsAvailable(u, station))
            return new ServiceResult(false, 0f, 0f, $"Dienst '{Key}' ist an dieser Station nicht verfügbar.");
        if (!NeedsRepair(ship))
            return new ServiceResult(false, 0f, 0f, "Schiff ist unbeschädigt.");
        if (hullShare > 0f && slots.Count == 0 && ship.MaxHull - ship.Hull <= 0.5f)
            return new ServiceResult(false, 0f, 0f, "Hülle ist unbeschädigt.");
        float credits = MathF.Max(0f, u.WalletOf(ship).Credits);
        if (credits <= 0f) return new ServiceResult(false, 0f, 0f, "Nicht genügend Credits für die Reparatur.");
        // Phase 26 (Prüfung): „alles“ bei knappem Geld zuerst die funktionswichtigen (ausgefallenen/brennenden) Module, dann die Hülle
        var o = Perform(u, station, ship, hullShare, slots, credits, modulesFirst: rest == All);
        if (!o.Any)
        {
            var plates = PlatesGood(u)!.Value;
            return new ServiceResult(false, 0f, 0f, station.Market[plates].Amount <= 0f ? "Keine Hüllenplatten mehr auf Lager."
                : slots.Count > 0 && hullShare <= 0f ? "Für die Modulreparatur fehlen Material oder Credits." : "Nicht genügend Credits für die Reparatur.");
        }
        u.Events.Publish(new ServicePerformed(ship.Id, station.Id, Key, request.Offer, PlatesGood(u)!.Value, o.Hull + o.Modules, -o.Cost));
        string what = (o.Hull > 0f ? $"Hülle +{o.Hull:0.#} auf {ship.Hull:0.#}/{ship.MaxHull:0}" : "")
            + (o.Modules > 0 ? (o.Hull > 0f ? ", " : "") + $"{o.Modules} Modul(e) instand gesetzt" : "");
        string mat = $"{o.Plates:0.#} Platten" + (o.Electronics > 0f ? $", {o.Electronics:0.#} Elektronik" : "");
        return new ServiceResult(true, o.Hull + o.Modules, -o.Cost,
            $"{what} für {o.Cost:0} Cr ({mat} verbraucht)" + (o.Partial ? " – Lager oder Credits reichten nicht für mehr." : "."));
    }
}

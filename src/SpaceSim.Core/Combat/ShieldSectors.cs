using SpaceSim.Core.Model;

namespace SpaceSim.Core.Combat;

/// <summary>
/// Richtungsschilde (KAMPF.md „Richtungsschilde“): reine Regeln und die wenigen Schreibstellen für die Schildsektoren eines Schiffs. Vier Sektoren (0 vorne,
/// 1 rechts, 2 hinten, 3 links, relativ zum Bug). Kapazität je Sektor = Schildkapazität × Anteil der Ausrichtung (ausgeglichen je ein Viertel). Ein Treffer
/// belastet den Sektor, aus dessen Richtung er kommt (räumlich aus den Positionen, auf der Gefechtslinie: hinten auf der Flucht, sonst vorne); ist der Sektor
/// leer, geht der Rest auf Panzer und Hülle, auch wenn andere Sektoren noch voll sind. Die Summe der Sektorfehlbeträge ist immer
/// <see cref="ShipSystemsState.ShieldMissing"/> — so bleiben Stärke, Anzeige, Erholung und alle anderen Leser unverändert. Ohne Schalter
/// <see cref="EconomyRules.ShieldSectors"/> tut diese Klasse nichts. Kein Zufall.
/// </summary>
public static class ShieldSectors
{
    public const int Count = 4;
    public const int Front = 0, Right = 1, Back = 2, Left = 3;

    /// <summary>Kapazitätsanteil eines Sektors bei der Ausrichtung (rein; Summe über alle Sektoren 1).</summary>
    public static float Share(EconomyRules r, ShieldFocus focus, int sector)
    {
        if (focus == ShieldFocus.Balanced) return 1f / Count;
        float f = Math.Clamp(r.ShieldFocusShare, 0f, 1f), o = Math.Clamp(r.ShieldFocusOppositeShare, 0f, 1f - f);
        int fs = (int)focus - 1;
        if (sector == fs) return f;
        if (sector == (fs + 2) % Count) return o;
        return (1f - f - o) / 2f;
    }

    /// <summary>Kapazität eines Sektors.</summary>
    public static float Capacity(ShipStats s, EconomyRules r, ShieldFocus focus, int sector) => s.Shield * Share(r, focus, sector);

    /// <summary>Fehlbetrag eines Sektors (ohne gespeicherte Verteilung: nach dem Anteil der Ausrichtung).</summary>
    public static float MissingOf(ShipSystemsState st, EconomyRules r, int sector) =>
        st.SectorMissing is { } a ? a[sector] : st.ShieldMissing * Share(r, st.Focus, sector);

    /// <summary>Ladung eines Sektors.</summary>
    public static float ChargeOf(ShipStats s, ShipSystemsState st, EconomyRules r, int sector) =>
        MathF.Max(0f, Capacity(s, r, st.Focus, sector) - MissingOf(st, r, sector));

    /// <summary>Ladung eines Sektors des Schiffs.</summary>
    public static float ChargeOf(Universe u, Ship ship, int sector) => ChargeOf(ship.Stats, ship.Systems, u.Rules, sector);

    /// <summary>Schild, der einem Treffer aus <paramref name="sector"/> entgegensteht (−1 oder Schalter aus = ganzer Schild).</summary>
    public static float Facing(Universe u, Ship ship, int sector)
    {
        if (sector < 0 || !u.Rules.ShieldSectors) return ship.ShieldNow;
        Sync(ship.Systems, ship.Stats, u.Rules);
        return ChargeOf(u, ship, sector);
    }

    /// <summary>
    /// Gleicht die gespeicherte Verteilung mit dem Gesamtfehlbetrag ab, falls andere Stellen ihn geändert haben (Auffüllen beim Träger, Kappen nach einem
    /// Modulwechsel): Sektoren werden anteilig skaliert und auf ihre Kapazität begrenzt; voll = keine Verteilung mehr.
    /// </summary>
    public static void Sync(ShipSystemsState st, ShipStats s, EconomyRules r)
    {
        if (st.SectorMissing is not { } a) return;
        if (st.ShieldMissing <= 0f || s.Shield <= 0f) { st.SectorMissing = null; return; }
        float sum = 0f;
        for (int i = 0; i < Count; i++) sum += a[i];
        if (sum != st.ShieldMissing)
        {
            if (sum <= 0f) { st.SectorMissing = null; return; }
            float k = st.ShieldMissing / sum;
            for (int i = 0; i < Count; i++) a[i] *= k;
        }
        float total = 0f;
        for (int i = 0; i < Count; i++) { a[i] = Math.Clamp(a[i], 0f, Capacity(s, r, st.Focus, i)); total += a[i]; }
        st.ShieldMissing = total;
        if (total <= 0f) st.SectorMissing = null;
    }

    static float[] Materialize(ShipSystemsState st, EconomyRules r)
    {
        if (st.SectorMissing is { } a) return a;
        a = new float[Count];
        for (int i = 0; i < Count; i++) a[i] = st.ShieldMissing * Share(r, st.Focus, i);
        st.SectorMissing = a;
        return a;
    }

    static void Total(ShipSystemsState st)
    {
        if (st.SectorMissing is not { } a) return;
        float t = 0f;
        for (int i = 0; i < Count; i++) t += a[i];
        st.ShieldMissing = t;
        if (t <= 0f) st.SectorMissing = null;
    }

    /// <summary>
    /// Schild-Schaden auf einen Sektor (aus der Trefferregel, schon nach Schadensart umgerechnet und durch die Ladung des Sektors begrenzt). Liefert true,
    /// wenn er hier verbucht wurde (Schalter an, Sektor gültig); sonst bucht der Aufrufer wie bisher auf den Gesamtschild.
    /// </summary>
    public static bool Take(Universe u, Ship ship, int sector, float amount)
    {
        if (!u.Rules.ShieldSectors || sector < 0 || sector >= Count) return false;
        var st = ship.Systems;
        Sync(st, ship.Stats, u.Rules);
        float take = MathF.Min(amount, ChargeOf(ship.Stats, st, u.Rules, sector));
        if (take > 0f)
        {
            var a = Materialize(st, u.Rules);
            a[sector] += take;
            Total(st);
        }
        return true;
    }

    /// <summary>
    /// Ungerichteter Schild-Schaden (abstrakte Begegnung, Treffer ohne Herkunft): verteilt sich nach der Ladung der Sektoren. Mit der Normalverteilung bleibt
    /// sie die Normalverteilung (nichts gespeichert). Liefert, wie viel abgefangen wurde.
    /// </summary>
    public static float TakeSpread(Universe u, Ship ship, float amount)
    {
        var st = ship.Systems;
        Sync(st, ship.Stats, u.Rules);
        float shield = ship.ShieldNow, take = MathF.Min(MathF.Max(0f, amount), shield);
        if (take <= 0f) return 0f;
        if (st.SectorMissing is { } a)
        {
            float[] charge = new float[Count];
            for (int i = 0; i < Count; i++) charge[i] = ChargeOf(ship.Stats, st, u.Rules, i);
            for (int i = 0; i < Count; i++) a[i] += shield > 0f ? take * charge[i] / shield : 0f;
            Total(st);
        }
        else st.ShieldMissing = MathF.Min(ship.Stats.Shield, st.ShieldMissing + take);
        return take;
    }

    /// <summary>
    /// Regeneration (rein bis auf den Zustand): verteilt <paramref name="amount"/> auf die Sektoren, denen etwas fehlt, gewichtet nach ihrem Kapazitätsanteil;
    /// was ein voller Sektor nicht braucht, geht an die anderen. Ohne gespeicherte Verteilung genügt der Gesamtfehlbetrag.
    /// </summary>
    public static void Regen(ShipSystemsState st, ShipStats s, EconomyRules r, float amount)
    {
        if (amount <= 0f) return;
        Sync(st, s, r);
        if (st.SectorMissing is not { } a) { st.ShieldMissing = MathF.Max(0f, st.ShieldMissing - amount); return; }
        float left = amount;
        for (int pass = 0; pass < Count && left > 1e-7f; pass++)
        {
            float w = 0f;
            for (int i = 0; i < Count; i++) if (a[i] > 0f) w += Share(r, st.Focus, i);
            if (w <= 0f) break;
            float given = 0f;
            for (int i = 0; i < Count; i++)
            {
                if (a[i] <= 0f) continue;
                float g = MathF.Min(a[i], left * Share(r, st.Focus, i) / w);
                a[i] -= g; given += g;
            }
            left -= given;
            if (given <= 0f) break;
        }
        Total(st);
    }

    /// <summary>Sektor, aus dem ein Treffer von <paramref name="from"/> kommt, für ein Ziel an <paramref name="at"/> mit Bugrichtung <paramref name="bow"/> (rein).</summary>
    public static int SectorFrom(Vec2 at, Vec2 bow, Vec2 from)
    {
        var rel = from - at;
        if (rel.Length < 1e-4f) return Front;
        var d = bow.Normalized();
        var right = new Vec2(-d.Y, d.X);
        float f = rel.Dot(d), s = rel.Dot(right);
        if (MathF.Abs(f) >= MathF.Abs(s)) return f >= 0f ? Front : Back;
        return s > 0f ? Right : Left;
    }

    /// <summary>
    /// Sektor eines Treffers auf <paramref name="target"/> (−1 = ohne Sektoren: Schalter aus, kein Schild). Räumlich aus der Position des Schützen
    /// (<paramref name="from"/>), auf der Gefechtslinie aus der Bewegung des Ziels: auf der Flucht hinten, sonst vorne.
    /// </summary>
    public static int Incoming(Universe u, Encounter e, CombatParticipant target, Vec2? from)
    {
        if (!u.Rules.ShieldSectors) return -1;
        if (e.Spatial && target.InSpace && from is { } f) return SectorFrom(target.Pos, target.Dir, f);
        return target.Move == CombatMove.Flee ? Back : Front;
    }

    /// <summary>Ausrichtung zu einem Sektor.</summary>
    public static ShieldFocus FocusOf(int sector) => (ShieldFocus)(Math.Clamp(sector, 0, Count - 1) + 1);

    /// <summary>
    /// Stellt die Ausrichtung um: die vorhandene Ladung verteilt sich neu nach den neuen Anteilen (die Summe bleibt), und der Schild pausiert seine
    /// Regeneration wie nach einem Treffer (<see cref="EconomyRules.ShieldDelayTicks"/>) — das ist der Preis des Umlenkens. Liefert false, wenn nichts zu tun ist.
    /// </summary>
    public static bool SetFocus(Universe u, Ship ship, ShieldFocus focus)
    {
        if (!u.Rules.ShieldSectors || ship.Stats.Shield <= 0f) return false;
        var st = ship.Systems;
        if (st.Focus == focus) return false;
        Sync(st, ship.Stats, u.Rules);
        st.Focus = focus;
        st.SectorMissing = null;   // Ladung anteilig neu verteilt
        if (st.ShieldMissing > 0f) { st.ShieldPauseUntil = u.Tick + u.Rules.ShieldDelayTicks; ShipSystems.Wake(u, ship); }
        u.Events.Publish(new Events.ShieldFocusChanged(ship.Id, focus));
        return true;
    }

    /// <summary>Spielerknopf: ausgeglichen → vorne → hinten → ausgeglichen (seitliche Ausrichtung nur für die NPC-Regel).</summary>
    public static ShieldFocus NextPlayerFocus(ShieldFocus f) => f switch
    {
        ShieldFocus.Balanced => ShieldFocus.Front,
        ShieldFocus.Front => ShieldFocus.Back,
        _ => ShieldFocus.Balanced,
    };

    /// <summary>
    /// NPC-Regel (deterministisch, kein Zufall): jedes aktive NPC-Schiff mit Schild richtet ihn zum Angreifer aus — zum ersten aktiven Gegner, der es
    /// anvisiert, sonst zum eigenen Ziel. Räumlich nach der Richtung, auf der Linie hinten auf der Flucht, sonst vorne. Das aktive Spielerschiff entscheidet selbst.
    /// </summary>
    public static void AlignNpc(Universe u, Encounter e)
    {
        if (!u.Rules.ShieldSectors) return;
        foreach (var p in e.Participants)
        {
            if (!p.Active || p.Ship == u.PlayerShip || !u.Ships.TryGetValue(p.Ship, out var ship) || ship.Stats.Shield <= 0f) continue;
            int sector;
            if (e.Spatial && p.InSpace)
            {
                CombatParticipant? att = null;
                foreach (var q in e.Participants) if (q.Active && q.Side != p.Side && q.Target == p.Ship) { att = q; break; }
                att ??= p.Target.Value != 0 ? e.Of(p.Target) : null;
                if (att is null) continue;
                sector = SectorFrom(p.Pos, p.Dir, att.Pos);
            }
            else sector = p.Move == CombatMove.Flee ? Back : Front;
            SetFocus(u, ship, FocusOf(sector));
        }
    }

    /// <summary>Setzt die Bugrichtung des aktiven Spielers im räumlichen Gefecht (die Ansicht kennt sein Heading; Grad wie <see cref="CombatAutopilot.FromBearing"/>).</summary>
    public static void SetPlayerBow(Universe u, float headingDeg)
    {
        if (!u.Rules.ShieldSectors || u.Fights.Of(u.PlayerShip) is not { } e) return;
        if (e.Of(u.PlayerShip) is { Active: true, InSpace: true } me) me.Dir = CombatAutopilot.FromBearing(headingDeg);
    }

    /// <summary>Kurzname der Ausrichtung (Knopf).</summary>
    public static string FocusName(ShieldFocus f) => f switch
    {
        ShieldFocus.Front => "vorne",
        ShieldFocus.Back => "hinten",
        ShieldFocus.Right => "rechts",
        ShieldFocus.Left => "links",
        _ => "ausgeglichen",
    };

    /// <summary>Ladung je Sektor als Anteil seiner Kapazität (Anzeige, schreibt nichts; Reihenfolge vorne, rechts, hinten, links). null = ohne Sektoren.</summary>
    public static float[]? Fill(Universe u, Ship ship)
    {
        if (!u.Rules.ShieldSectors || ship.Stats.Shield <= 0f) return null;
        // rein (die Ansicht liest nur): der Abgleich wird auf einer Kopie gerechnet, nie in den Zustand geschrieben
        var st = ship.Systems;
        var copy = new ShipSystemsState { ShieldMissing = st.ShieldMissing, Focus = st.Focus, SectorMissing = st.SectorMissing is { } a ? (float[])a.Clone() : null };
        Sync(copy, ship.Stats, u.Rules);
        var f = new float[Count];
        for (int i = 0; i < Count; i++)
        {
            float cap = Capacity(ship.Stats, u.Rules, copy.Focus, i);
            f[i] = cap > 0f ? Math.Clamp(ChargeOf(ship.Stats, copy, u.Rules, i) / cap, 0f, 1f) : 0f;
        }
        return f;
    }
}

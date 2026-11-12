using System.Linq;
using SpaceSim.Core.Model;
using SpaceSim.Core.Simulation;

namespace SpaceAdventure.Game;

/// <summary>
/// Phase 26: Bergung in der Ansicht (KAMPF.md 4.4) — eigene Komponente statt Logik in <c>Main</c>. Hält nur das gewählte Wrack; Fracht zieht der Traktorstrahl
/// über <see cref="Wrecks.TakeCargo"/>, Module birgt der Schacht „Bergen“ über <see cref="Wrecks.TakeModule"/> (Core-Regeln, in der Simulation gebucht).
/// </summary>
public sealed class SalvageController
{
    /// <summary>Fracht je Sekunde, die der Traktorstrahl aus einem Wrack zieht.</summary>
    public const float PullPerSecond = 6f;

    /// <summary>Gewähltes Wrack (0 = keins; sonst das nächste in Reichweite).</summary>
    public int Selected { get; private set; }
    public string LastMessage { get; private set; } = "";
    /// <summary>Insgesamt in dieser Sitzung per Traktor gezogene Einheiten (Selbsttest).</summary>
    public float Pulled { get; private set; }

    public void Select(int id) => Selected = id;
    public void Clear() => Selected = 0;

    /// <summary>Wrack nahe einem Bodenpunkt (Antippen), sonst null.</summary>
    public static Wreck? Pick(Universe u, Vec2 at, float radius) =>
        Wrecks.In(u, u.PlayerSector).Select(w => (w, d: Vec2.Distance(w.Position, at))).Where(x => x.d <= radius)
            .OrderBy(x => x.d).Select(x => x.w).FirstOrDefault();

    /// <summary>Gewähltes Wrack (noch vorhanden, im Spielersektor), sonst das nächste in Bergungsreichweite (mit Fracht bzw. Modulen).</summary>
    public Wreck? Current(Universe u, Vec2 pos, bool wantModules)
    {
        if (Selected != 0 && Wrecks.Get(u, Selected) is { } sel && sel.Sector == u.PlayerSector) return sel;
        Selected = 0;
        return Wrecks.In(u, u.PlayerSector).Where(w => wantModules ? w.Modules.Count > 0 : w.Cargo.Count > 0)
            .Select(w => (w, d: Vec2.Distance(w.Position, pos))).Where(x => x.d <= u.Rules.SalvageRange).OrderBy(x => x.d).Select(x => x.w).FirstOrDefault();
    }

    /// <summary>Traktorstrahl an: Fracht aus dem gewählten/nächsten Wrack in Reichweite ziehen. Liefert die Einheiten dieses Frames.</summary>
    public float Pull(Universe u, Ship ship, bool tractorOn, float delta)
    {
        if (!tractorOn || !u.Rules.DamageActive || Current(u, ship.Position, wantModules: false) is not { } w || w.Cargo.Count == 0) return 0f;
        float moved = Wrecks.TakeCargo(u, ship, w, PullPerSecond * delta, out var issue);
        Pulled += moved;
        if (issue is not null) LastMessage = issue;
        return moved;
    }

    /// <summary>Schacht „Bergen“: bestes bergbares Modul aus dem gewählten/nächsten Wrack. true = geborgen.</summary>
    public bool Grab(Universe u, Ship ship)
    {
        var w = Current(u, ship.Position, wantModules: true);
        if (w is null) { LastMessage = "Kein Wrack mit Modulen in Reichweite."; return false; }
        int best = Wrecks.BestModule(u, ship, w);
        if (best < 0)
        {
            LastMessage = w.Modules.Count == 0 ? "Keine Module im Wrack." : Wrecks.ModuleIssue(u, ship, w, 0) ?? "Nichts Bergbares im Wrack.";
            return false;
        }
        bool ok = Wrecks.TakeModule(u, ship, w, best, out var msg);
        LastMessage = msg;
        return ok;
    }
}

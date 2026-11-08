# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Mobile 2.5D space game (inspired by Interstellar Pilot 2) with a fully autonomous economy and factions that run without player input. The world consists of multiple sectors; each sector is its own instance, and the only way between them is via jumpgates. Planned engine: Godot 4 with C# (Android first; C# iOS export in Godot is immature). The Godot project does not exist yet; only the simulation core does.

## Commands

The SDK installed is .NET 10, but `SpaceSim.Core` targets `net8.0` (what Godot 4 C# requires), so avoid APIs newer than .NET 8 in Core. The test project targets `net10.0` because only the .NET 10 runtime is installed.

```
dotnet build
dotnet test
dotnet test --filter "FullyQualifiedName~Economy_RunsStably_WithoutPlayer"   # single test
```

## Architecture

**Simulation core is engine-independent.** `src/SpaceSim.Core` must never reference Godot. Godot code (to be added) is only a view over it. This keeps the economy testable headless and fast-forwardable (e.g. thousands of ticks in a unit test).

**Two layers:**
1. Simulation layer: every sector exists as plain data in `Universe` and is always simulated.
2. Presentation layer: only the sector containing the player (`Universe.PlayerSector`) is materialized as a Godot scene. On a jump it is unloaded and the target sector is built from data. Remote sectors are simulated abstractly (LOD) to save CPU and battery on mobile.

**Abstract travel instead of physics.** Ships outside the player's sector have no position; they carry a `Transit` (start/arrival tick, target sector). Travel time is computed from gate hops (`Universe.Hops`, BFS over open gates), not flown. When the player enters a sector, ships there are meant to be materialized by interpolating from their transit state, and frozen again on leaving (not implemented yet).

**Data model conventions:**
- Entities reference each other by typed ID structs (`SectorId`, `ShipId`, ...), never by object reference. Everything lives in `Dictionary<Id, T>` on `Universe`. This keeps save/load (JSON) simple and avoids cycles.
- Jumpgates are the edges of the sector graph; each `Gate.Target` points to the counterpart gate in the neighboring sector. A closed gate (`Open = false`) blocks routing, which is the hook for war/blockades.
- Ship behavior is expressed as `ShipOrder` records (`TradeOrder`, `MineOrder`, ...) and consumed with pattern matching.
- Use `Universe.MoveShip` to change a ship's sector so `Sector.Ships` stays consistent.

**Systems pipeline.** `SimulationRunner` runs `ISimSystem`s in order each tick, then increments `Universe.Tick`. Default order matters: `ProductionSystem` → `MarketSystem` → `RegenSystem` → `MinerSystem` → `TraderSystem`. Add new simulation behavior as a new `ISimSystem` rather than extending existing ones.

**Economy model.** Price is derived per station and good: `BasePrice * clamp(Target / max(Amount, 1), 0.25, 4)`. Stations produce through `Recipe`s (inputs → outputs per cycle). Traders pick the best price margin between a buy and a sell station that are connected by gates, travel via `Transit`, and trade on arrival. Miners (`MinerSystem`) extract from `Sector.Fields`, pick field + station by best price per travel time, and refuse to mine when the price is below `MinPriceFactor` × base price, which caps overproduction. Trader and miner travel both go through `Travel.Start`. Stability of this loop is the core design risk, so verify changes with long headless runs (see `EconomyTests`).

## Not yet implemented

Fighter/patrol AI, faction AI and relations logic, ship materialization on sector entry, save/load, combat, missions, and any connection between the Godot project and `SpaceSim.Core`.

## Godot project (`godot/`)

Uses the **.NET build** of Godot 4.7.2: run it with `godot-mono` (`~/.local/bin/godot-mono` -> `~/.local/opt/godot-mono/`). Plain `godot` is the standard build and cannot run C#.

```
godot-mono --path godot                                   # run the game
godot-mono --path godot -e                                # open the editor
godot-mono --headless --path godot --build-solutions --quit   # build C# via Godot
godot-mono --headless --path godot --quit-after 300       # headless smoke test (look for errors)
dotnet build godot                                        # build only the Godot C# project
```

`godot/SpaceAdventure.csproj` (Godot.NET.Sdk/4.7.2, net8.0) references `../src/SpaceSim.Core`. It is deliberately **not** in `SpaceSim.slnx`, so root-level `dotnet test` stays independent of Godot.

`godot/scripts/Main.cs` is the single scene script (scene `scenes/main.tscn` is just a `Node3D` with it). It builds `DemoUniverse`, steps `SimulationRunner` at a fixed 4 ticks/s from `_Process`, and materializes only `Universe.PlayerSector`: stations, gates and asteroids from Core data, plus markers for traders currently in that sector (`Transit == null`). Flying into a gate calls `Universe.MoveShip`, rebuilds the sector scene and places the ship at the counterpart gate. Space plane is XZ (`Vec2.Y` -> world Z) with a tilted perspective camera for 2.5D. Controls: arrows/WASD, hold mouse/touch to fly toward the pointer, `T` fast-forwards the simulation.

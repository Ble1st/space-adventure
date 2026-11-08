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

**Abstract travel with materialization.** Ships outside the player's sector are not simulated physically. `Travel.Plan` turns a trip into contiguous `Leg`s (one straight flight per sector: start -> gate, gate -> gate, ..., gate -> destination; speed `Travel.ShipSpeed` units/tick); `Transit` stores them and `ArrivalTick` is the last leg's end. A ship physically exists in exactly one sector at any time, so `Materializer.Snapshot(universe, sector, fractionalTick)` can derive positions of all NPC ships in the player's sector (moving ships by interpolating the active leg, parked ones at `Ship.Position`). The Godot view only renders that snapshot; it must not invent ship positions. `Ship.Position`/`Ship.Sector` are only updated on arrival; mid-trip location comes from the legs. `Sector.Ships` therefore lists ships by their last arrival, not live location. AI scoring uses `Travel.Ticks` (same plan, so estimates match reality).

**Obstacles, avoidance, collision.** Asteroids (`Sector.Asteroids`, generated deterministically by `Demo/AsteroidGenerator`) and station bodies (`Station.BodyRadius`) are circular `Obstacle`s (`Universe.GetObstacles`). `PathPlanner` routes legs around them with waypoints, so travel time, materialized flight paths and AI estimates all include detours; it ignores obstacles that already contain the start or end point. Ships never target a station/field center but `Station.DockPoint(shipId)` / `ResourceField.WorkPoint(shipId)` (outside the obstacle, inside the asteroid-free zone). Collision (`Collision.Resolve`) is only applied to the player ship in the view, plus a cosmetic dodge offset (`Avoidance.Desired`: ships step aside from each other and from the player, then get pushed back out of obstacles) in `Main.cs`; neither feeds back into the simulation, so two NPC fleets may still cross in the data model. `PathPlanner` recursion is bounded by `MaxDepth` and a `MaxExpansions` node budget and pushes waypoints out of neighbouring obstacles. Keep asteroid sizes in the view equal to `Obstacle.Radius`.

**Data model conventions:**
- Entities reference each other by typed ID structs (`SectorId`, `ShipId`, ...), never by object reference. Everything lives in `Dictionary<Id, T>` on `Universe`. This keeps save/load (JSON) simple and avoids cycles.
- Jumpgates are the edges of the sector graph; each `Gate.Target` points to the counterpart gate in the neighboring sector. A closed gate (`Open = false`) blocks routing, which is the hook for war/blockades.
- Ship behavior is expressed as `ShipOrder` records (`TradeOrder`, `MineOrder`, ...) and consumed with pattern matching.
- Use `Universe.MoveShip` to change a ship's sector so `Sector.Ships` stays consistent.

**Systems pipeline.** `SimulationRunner` runs `ISimSystem`s in order each tick, then increments `Universe.Tick`. Default order matters: `ProductionSystem` → `MarketSystem` → `RegenSystem` → `MinerSystem` → `TraderSystem`. Add new simulation behavior as a new `ISimSystem` rather than extending existing ones.

**Economy model.** Price is derived per station and good: `BasePrice * clamp(Target / max(Amount, 1), 0.25, 4)`. Stations produce through `Recipe`s (inputs → outputs per cycle). Traders pick the best price margin between a buy and a sell station that are connected by gates, travel via `Transit`, and trade on arrival. Miners (`MinerSystem`) extract from `Sector.Fields`, pick field + station by best price per travel time, and refuse to mine when the price is below `MinPriceFactor` × base price, which caps overproduction. Trader and miner travel both go through `Travel.Start`. Stability of this loop is the core design risk, so verify changes with long headless runs (see `EconomyTests`).

## Not yet implemented

Fighter/patrol AI, faction AI and relations logic, save/load, combat, missions, and any connection between the Godot project and `SpaceSim.Core`.

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

`godot/scripts/Main.cs` is the single scene script (scene `scenes/main.tscn` is just a `Node3D` with it). It builds `DemoUniverse`, steps `SimulationRunner` at a fixed 4 ticks/s from `_Process`, and materializes only `Universe.PlayerSector`: stations, gates and asteroids from Core data, plus NPC ships from `Materializer`. Flying into a gate calls `Universe.MoveShip`, rebuilds the sector scene and places the ship at the counterpart gate. Space plane is XZ (`Vec2.Y` -> world Z) with a tilted perspective camera for 2.5D. Controls: arrows/WASD, hold mouse/touch to fly toward the pointer, `T` fast-forwards the simulation.

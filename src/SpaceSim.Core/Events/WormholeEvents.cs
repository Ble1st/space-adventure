using SpaceSim.Core.Model;

namespace SpaceSim.Core.Events;

/// <summary>Ein instabiles Wurmloch ist aufgerissen (Ansicht: Meldung im Spielersektor).</summary>
public sealed record WormholeOpened(int Wormhole, SectorId Sector, Vec2 Position, SectorId Target) : SimEvent;

/// <summary>Ein instabiles Wurmloch ist zusammengefallen.</summary>
public sealed record WormholeCollapsed(int Wormhole, SectorId Sector) : SimEvent;

/// <summary>Der Spieler hat ein instabiles Wurmloch durchflogen.</summary>
public sealed record WormholeTraversed(ShipId Ship, SectorId From, SectorId To, int Wormhole) : SimEvent;

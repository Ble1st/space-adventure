using SpaceSim.Core.Model;

namespace SpaceSim.Core.Events;

/// <summary>Universumsereignis begonnen (Art, Sektor, Ort, Kurztext) — Ansicht: Toast/Info nach <see cref="Simulation.UniverseEventInfo.Weight"/>.</summary>
public sealed record UniverseEventStarted(int Event, string Kind, SectorId Sector, Vec2 Position, string Title) : SimEvent;

/// <summary>Universumsereignis zu Ende: <paramref name="Reason"/> „expired“ (Frist um) oder „exhausted“ (alles genutzt).</summary>
public sealed record UniverseEventEnded(int Event, string Kind, SectorId Sector, string Reason) : SimEvent;

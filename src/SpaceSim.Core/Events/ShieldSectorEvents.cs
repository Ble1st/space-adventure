using SpaceSim.Core.Model;

namespace SpaceSim.Core.Events;

/// <summary>Richtungsschilde: Ausrichtung eines Schilds umgestellt (Spielerknopf oder NPC-Regel). Nur Anzeige, nicht gespeichert.</summary>
public sealed record ShieldFocusChanged(ShipId Ship, ShieldFocus Focus) : SimEvent;

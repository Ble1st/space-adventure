namespace SpaceSim.Core.Events;

/// <summary>Piloten: der Rang des Spielers hat sich geändert (Aufstieg oder Abstieg; nur Anzeige).</summary>
public sealed record PilotRankChanged(int OldRank, int NewRank, float Rating) : SimEvent;

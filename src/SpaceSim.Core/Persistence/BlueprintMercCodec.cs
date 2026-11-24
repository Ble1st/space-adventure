using System.Text.Json.Serialization;
using SpaceSim.Core.Model;

namespace SpaceSim.Core.Persistence;

/// <summary>Gespeicherter Bauplan (<see cref="ShipBlueprint"/>) eines Akteurs.</summary>
public sealed class BlueprintSave
{
    public int Actor { get; set; }
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Model { get; set; } = "";
    public List<string?> Fit { get; set; } = new();
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public long Created { get; set; }
}

/// <summary>Gespeicherter Söldnervertrag (<see cref="MercContract"/>).</summary>
public sealed class MercContractSave
{
    public int Ship { get; set; }
    public int Actor { get; set; }
    public int Home { get; set; }
    public long Since { get; set; }
    public int Days { get; set; }
    public int PaidDays { get; set; }
    public float Fee { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Ending { get; set; }
}

/// <summary>Speichern, Laden und Hash der Baupläne (Akteur) und Söldnerverträge (Universum) — additiv, Spielstand bleibt v17.</summary>
public static class BlueprintMercCodec
{
    public static void ToSave(Universe u, SaveGame s)
    {
        var bps = u.Actors.Values.OrderBy(a => a.Id.Value).SelectMany(a => a.Blueprints.OrderBy(b => b.Id).Select(b => new BlueprintSave
        {
            Actor = a.Id.Value, Id = b.Id, Name = b.Name, Model = b.ModelKey, Fit = new List<string?>(b.Fit), Created = b.Created,
        })).ToList();
        if (bps.Count > 0) s.Blueprints = bps;
        if (u.Mercenaries.Count > 0)
            s.Mercenaries = u.Mercenaries.Select(c => new MercContractSave
            {
                Ship = c.Ship.Value, Actor = c.Actor.Value, Home = c.Home.Value, Since = c.Since, Days = c.Days, PaidDays = c.PaidDays, Fee = c.FeePerDay, Ending = c.Ending,
            }).ToList();
    }

    public static void FromSave(SaveGame s, Universe u)
    {
        if (s.Blueprints is not null)
            foreach (var b in s.Blueprints)
                if (u.Actors.TryGetValue(new ActorId(b.Actor), out var a))
                    a.Blueprints.Add(new ShipBlueprint { Id = b.Id, Name = b.Name, ModelKey = b.Model, Fit = new List<string?>(b.Fit), Created = b.Created });
        if (s.Mercenaries is not null)
            foreach (var c in s.Mercenaries)
                u.Mercenaries.Add(new MercContract
                {
                    Ship = new ShipId(c.Ship), Actor = new ActorId(c.Actor), Home = new StationId(c.Home), Since = c.Since, Days = c.Days, PaidDays = c.PaidDays,
                    FeePerDay = c.Fee, Ending = c.Ending,
                });
    }

    /// <summary>Hash: Baupläne nur mit Spielerpolitik, Söldner immer — beides nur, wenn vorhanden (Welten ohne mischen nichts).</summary>
    public static void Mix(Universe u, bool includePlayerPolitics, Action<ulong> mix, Action<float> mixF, Action<string> mixString)
    {
        if (includePlayerPolitics)
            foreach (var a in u.Actors.Values.OrderBy(a => a.Id.Value))
                foreach (var b in a.Blueprints.OrderBy(b => b.Id))
                {
                    mix(0xB1E0u); mix((ulong)a.Id.Value); mix((ulong)b.Id); mixString(b.Name); mixString(b.ModelKey);
                    foreach (var k in b.Fit) mixString(k ?? "");
                }
        foreach (var c in u.Mercenaries)
        {
            mix(0x3E2Cu); mix((ulong)c.Ship.Value); mix((ulong)c.Actor.Value); mix((ulong)c.Home.Value); mix((ulong)c.Since); mix((ulong)c.Days); mix((ulong)c.PaidDays);
            mixF(c.FeePerDay); mix(c.Ending ? 1u : 0u);
        }
    }
}

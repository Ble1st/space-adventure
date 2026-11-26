using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Comms;

/// <summary>
/// Welle C (2): Comms-Aktion „rest“ — eigene Behörde ruhen lassen (kein Unterhalt, kein Einfluss, keine Sperren, kein Gericht) oder wieder anschalten
/// (<see cref="PlayerUpkeepOrder"/>). Schlüssel streng: <c>rest:on:&lt;StationId&gt;</c> (ruhen lassen), <c>rest:off:&lt;StationId&gt;</c> (wieder an).
/// </summary>
public sealed class RestAction : ICommsAction
{
    public const string KeyId = "rest";
    public string Key => KeyId;
    public bool Handles(string offer) => offer.StartsWith(KeyId + ":", StringComparison.Ordinal);

    public IReadOnlyList<CommsOffer> Offers(Universe u)
    {
        var list = new List<CommsOffer>();
        if (!PlayerUpkeepOrder.Active(u)) return list;
        foreach (var st in PlayerUpkeepOrder.Governments(u)) list.Add(Preview(u, $"{KeyId}:{(st.Dormant ? "off" : "on")}:{st.Id.Value}"));
        return list;
    }

    static bool Parse(string offer, out bool rest, out StationId id)
    {
        rest = false; id = default;
        if (CommsActions.TryId(offer, $"{KeyId}:on", out var a)) { rest = true; id = new StationId(a); return true; }
        if (CommsActions.TryId(offer, $"{KeyId}:off", out var b)) { id = new StationId(b); return true; }
        return false;
    }

    public CommsOffer Preview(Universe u, string offer)
    {
        if (!Parse(offer, out bool rest, out var id)) return new(offer, "Behörde ruhen lassen", "", 0f, false, $"Unbekanntes Angebot '{offer}'.");
        string? issue = PlayerUpkeepOrder.RestIssue(u, id, rest);
        string name = u.Stations.TryGetValue(id, out var st) ? st.Name : "?";
        float perDay = st is not null && u.StationTypes.TryGetValue(st.TypeKey, out var t) ? PlayerUpkeep.PlayerPerTick(u, t) * u.Rules.DayTicks : 0f;
        string text = rest ? $"Ruhen lassen – {name}" : $"Wieder anschalten – {name}";
        string pre = rest ? $"Spart {perDay:0} Cr/Tag; kein Einfluss, keine Sperren, kein Gericht, solange sie ruht — jederzeit wieder an."
                          : $"Unterhalt {perDay:0} Cr/Tag ab jetzt; Einfluss und Behörde laufen wieder, wenn die Kasse reicht.";
        return new(offer, text, issue ?? pre, 0f, issue is null, issue);
    }

    public ServiceResult Execute(Universe u, string offer)
    {
        if (!Parse(offer, out bool rest, out var id)) return new ServiceResult(false, 0f, 0f, $"Unbekanntes Angebot '{offer}'.");
        if (PlayerUpkeepOrder.RestIssue(u, id, rest) is { } issue) return new ServiceResult(false, 0f, 0f, issue);
        PlayerUpkeepOrder.SetDormant(u, id, rest);
        string name = u.Stations[id].Name;
        string msg = rest ? $"{name} ruht — kein Unterhalt, kein Einfluss." : $"{name} wieder angeschaltet.";
        u.Events.Publish(new CommsActionPerformed(KeyId, id.Value, 0f, msg));
        return new ServiceResult(true, 1f, 0f, msg);
    }
}

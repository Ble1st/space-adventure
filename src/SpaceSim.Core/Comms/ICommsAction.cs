using SpaceSim.Core.Events;
using SpaceSim.Core.Model;
using SpaceSim.Core.Services;
using SpaceSim.Core.Simulation;

namespace SpaceSim.Core.Comms;

/// <summary>
/// Runde 40: ein Angebot einer Comms-Aktion (Vorschau und Anzeige; <see cref="CommsOffer.Possible"/> false = gesperrt mit <see cref="CommsOffer.Reason"/>).
/// </summary>
/// <param name="Key">Strenger Angebots-Schlüssel, z. B. „fine:1“, „war:2“, „ceasefire:2“, „peace:2“, „tribute:2:5000“; identifiziert es bei <see cref="ICommsAction.Execute"/>.</param>
/// <param name="Text">Anzeige-Text (Knopf/Zeile).</param>
/// <param name="Preview">Reine Folgenvorschau (Kosten, Wirkung, Sperrgrund) — dieselbe Rechnung wie bei der Ausführung.</param>
/// <param name="Cost">Credit-Kosten (negativ = Zahlung des Spielers; 0 = kostenlos).</param>
/// <param name="Possible">Ist das Angebot jetzt ausführbar?</param>
/// <param name="Reason">Sperrgrund (null = ausführbar).</param>
public readonly record struct CommsOffer(string Key, string Text, string Preview, float Cost, bool Possible, string? Reason);

/// <summary>
/// Runde 40: eine Comms-Aktion (Diplomatie des Spielers gegen Fraktionen: Strafe zahlen, Krieg erklären, Waffenstillstand, Frieden, Tribut —
/// nach dem Muster von <see cref="IStationService"/>). Schlüssel streng: ein unbekannter oder gesperrter Schlüssel schlägt fehl, ohne etwas zu ändern
/// und ohne ein Ereignis. <see cref="Offers"/> und <see cref="Preview"/> sind rein (Vorschau); nur <see cref="Execute"/> bewegt Zustand.
/// Platzhalter für Aufträge/Verträge (Runde 41): <c>mission:accept:&lt;id&gt;</c>, <c>contract:haggle:&lt;id&gt;</c> — reserviert, noch ohne Wirkung.
/// </summary>
public interface ICommsAction
{
    /// <summary>Eindeutiger Schlüssel der Aktion, z. B. „fine“.</summary>
    string Key { get; }

    /// <summary>Behandelt diese Aktion den Angebots-Schlüssel (strenger Präfix-Match, z. B. „fine:")?</summary>
    bool Handles(string offer);

    /// <summary>Alle aktuell verfügbaren Angebote dieser Aktion (rein; in fester Reihenfolge).</summary>
    IReadOnlyList<CommsOffer> Offers(Universe u);

    /// <summary>Reine Vorschau eines Angebots-Schlüssels (liest nur; unbekannter Schlüssel = nicht möglich mit Grund).</summary>
    CommsOffer Preview(Universe u, string offer);

    /// <summary>
    /// Führt ein Angebot aus. Fehlschlag (unbekannter Schlüssel, Sperrgrund) ändert keinen Zustand und veröffentlicht kein Ereignis
    /// (außer der reinen Missbrauchs-Diagnose — nein: gar keins). Erfolg veröffentlicht <see cref="CommsActionPerformed"/> (bzw. die fachlichen Ereignisse).
    /// </summary>
    ServiceResult Execute(Universe u, string offer);
}

/// <summary>
/// Runde 40: Registry aller Comms-Aktionen (wie <see cref="StationServices"/>): Registrierungsreihenfolge, doppelter Schlüssel wirft
/// <see cref="ArgumentException"/>. Ausführung und Vorschau laufen nur über diese Registry.
/// </summary>
public sealed class CommsActions
{
    readonly List<ICommsAction> _actions = new();
    readonly Dictionary<string, ICommsAction> _byKey = new();

    public void Register(ICommsAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (_byKey.ContainsKey(action.Key))
            throw new ArgumentException($"Comms-Aktion '{action.Key}' ist bereits registriert.", nameof(action));
        _byKey.Add(action.Key, action);
        _actions.Add(action);
    }

    public bool TryGet(string key, out ICommsAction action) => _byKey.TryGetValue(key, out action!);

    public IReadOnlyList<string> Keys => _actions.Select(a => a.Key).ToArray();

    /// <summary>Aktion, die den Angebots-Schlüssel behandelt (erste in Registrierungsreihenfolge); null = unbekannt.</summary>
    public ICommsAction? ForOffer(string offer)
    {
        foreach (var a in _actions) if (a.Handles(offer)) return a;
        return null;
    }

    /// <summary>Alle Angebote aller Aktionen (rein, in Registrierungsreihenfolge).</summary>
    public IReadOnlyList<CommsOffer> Offers(Universe u) => _actions.SelectMany(a => a.Offers(u)).ToList();

    /// <summary>Reine Vorschau eines Angebots-Schlüssels (unbekannt = nicht möglich).</summary>
    public CommsOffer Preview(Universe u, string offer) =>
        ForOffer(offer) is { } a ? a.Preview(u, offer) : new CommsOffer(offer, "Unbekanntes Angebot", "", 0f, false, $"Unbekanntes Angebot '{offer}'.");

    /// <summary>Führt ein Angebot aus (strenger Schlüssel; Fehlschlag ändert nichts und publiziert nichts).</summary>
    public ServiceResult Execute(Universe u, string offer) =>
        ForOffer(offer) is { } a ? a.Execute(u, offer) : new ServiceResult(false, 0f, 0f, $"Unbekanntes Angebot '{offer}'.");

    /// <summary>
    /// Standard-Registry in fester Reihenfolge: Strafe (fine), Krieg (war), Waffenstillstand (ceasefire), Frieden (peace), Tribut (tribute),
    /// Auftrag annehmen (mission:accept, Runde 41), Liefervertrag nachverhandeln (contract:haggle, Runde 41).
    /// </summary>
    public static CommsActions Default()
    {
        var r = new CommsActions();
        r.Register(new FineAction());
        r.Register(new WarAction());
        r.Register(new CeasefireAction());
        r.Register(new PeaceAction());
        r.Register(new TributeAction());
        r.Register(new MissionAcceptAction());     // Runde 41: Aufträge
        r.Register(new MissionAbandonAction());    // Runde 41: Auftrag abbrechen (Reiter „Aufträge“)
        r.Register(new ContractHaggleAction());    // Runde 41: Lieferverträge
        r.Register(new DemandAction());            // Runde 45: Tribut fordern
        r.Register(new AllianceAction());          // Runde 45: Bündnis vorschlagen/verlassen/Beistand
        r.Register(new SeatAction());              // Runde 45: Sektorbehörde gründen/schließen
        r.Register(new LockAction());              // Runde 45: eigene Sperrstufe
        return r;
    }

    /// <summary>Parst „prefix:&lt;Id&gt;“ streng (nur Ziffern, Id &gt; 0) — Runde 41: Auftrags-/Angebots-Ids. false = kein Match.</summary>
    public static bool TryId(string offer, string prefix, out int id)
    {
        id = 0;
        if (!offer.StartsWith(prefix + ":", StringComparison.Ordinal)) return false;
        return TryId(offer[(prefix.Length + 1)..], out id) && id > 0;
    }

    /// <summary>Parst „prefix:&lt;Fraktions-Id&gt;“ streng (nur Ziffern, Id &gt; 0); false = kein Match.</summary>
    public static bool TryFaction(string offer, string prefix, out FactionId f)
    {
        f = default;
        if (!offer.StartsWith(prefix + ":", StringComparison.Ordinal)) return false;
        return TryId(offer[(prefix.Length + 1)..], out var v) && v > 0 && (f = new FactionId(v)).Value == v;
    }

    /// <summary>Parst „prefix:&lt;F&gt;:&lt;Betrag&gt;“ streng.</summary>
    public static bool TryFactionAmount(string offer, string prefix, out FactionId f, out float amount)
    {
        f = default; amount = 0f;
        if (!offer.StartsWith(prefix + ":", StringComparison.Ordinal)) return false;
        var tail = offer[(prefix.Length + 1)..];
        int colon = tail.IndexOf(':');
        if (colon <= 0) return false;
        if (!TryId(tail[..colon], out var fv) || fv <= 0) return false;
        if (!float.TryParse(tail[(colon + 1)..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out amount) || amount <= 0f || float.IsNaN(amount) || float.IsInfinity(amount)) return false;
        f = new FactionId(fv);
        return true;
    }

    static bool TryId(string s, out int v)
    {
        v = 0;
        return s.Length > 0 && s.All(c => c is >= '0' and <= '9') && int.TryParse(s, out v);
    }
}

using SpaceSim.Core.Events;

namespace SpaceSim.Core.Model;

/// <summary>
/// Lücken gegenüber Interstellar Pilot 2: Anzeigenamen von Schiffen und das Umbenennen eigener Schiffe. Ein Schiff ohne eigenen Namen heißt wie bisher
/// „#Nummer Modell“; mit eigenem Namen „Name (#Nummer)“. Der Name ist nur Anzeige (gespeichert, nicht gehasht).
/// </summary>
public static class ShipNames
{
    public const int MaxLength = 24;

    /// <summary>Modellname eines Schiffs (Schlüssel, wenn das Modell unbekannt ist).</summary>
    public static string Model(Universe u, Ship s) => u.ShipModels.TryGetValue(s.ModelKey, out var m) ? m.Name : (s.ModelKey.Length > 0 ? s.ModelKey : "Schiff");

    /// <summary>Anzeigename: eigener Name mit Nummer, sonst „#Nummer Modell“.</summary>
    public static string Display(Universe u, Ship s) => s.CustomName.Length > 0 ? $"{s.CustomName} (#{s.Id.Value})" : $"#{s.Id.Value} {Model(u, s)}";

    /// <summary>Anzeigename nach Id („?“ wenn es das Schiff nicht mehr gibt).</summary>
    public static string Display(Universe u, ShipId id) => u.Ships.TryGetValue(id, out var s) ? Display(u, s) : "?";

    /// <summary>Kurzname (eigener Name oder Modell, ohne Nummer).</summary>
    public static string Short(Universe u, Ship s) => s.CustomName.Length > 0 ? s.CustomName : Model(u, s);

    /// <summary>Bereinigter Name (Leerraum gekürzt und zusammengefasst).</summary>
    public static string Clean(string? name) => string.Join(' ', (name ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Warum dieser Name nicht vergeben werden kann (null = möglich). Leer heißt: zurück zum Modellnamen.</summary>
    public static string? RenameIssue(Universe u, Ship ship, string? name)
    {
        if (ship.Owner != u.PlayerActor) return "Nur eigene Schiffe";
        var n = Clean(name);
        if (n.Length > MaxLength) return $"Höchstens {MaxLength} Zeichen";
        foreach (var ch in n) if (char.IsControl(ch)) return "Ungültiges Zeichen";
        return null;
    }

    /// <summary>Benennt um (leer = Modellname); false ohne Änderung, wenn <see cref="RenameIssue"/> etwas meldet.</summary>
    public static bool Rename(Universe u, Ship ship, string? name)
    {
        if (RenameIssue(u, ship, name) is not null) return false;
        ship.CustomName = Clean(name);
        u.Events.Publish(new ShipRenamed(ship.Id, ship.CustomName));
        return true;
    }
}

/// <summary>
/// Lücken gegenüber Interstellar Pilot 2: Name und dreistelliges Kürzel der eigenen Fraktion ändern. Das Kürzel besteht aus drei Großbuchstaben oder Ziffern
/// und ist eindeutig unter allen Akteuren (<see cref="ActorTags"/>). Ändert Akteur und — wenn es sie gibt — die Spielerfraktion.
/// </summary>
public static class PlayerIdentity
{
    public const int MinName = 2, MaxName = 32;

    /// <summary>Kürzel in der gespeicherten Form (Großbuchstaben, Leerraum entfernt).</summary>
    public static string NormalTag(string? tag) => (tag ?? "").Trim().ToUpperInvariant();

    /// <summary>Warum Name und Kürzel nicht gesetzt werden können (null = möglich).</summary>
    public static string? RenameIssue(Universe u, string? name, string? tag)
    {
        if (!u.Actors.TryGetValue(u.PlayerActor, out var me)) return "Kein Spielerakteur";
        var n = ShipNames.Clean(name);
        if (n.Length < MinName) return $"Name: mindestens {MinName} Zeichen";
        if (n.Length > MaxName) return $"Name: höchstens {MaxName} Zeichen";
        foreach (var ch in n) if (char.IsControl(ch)) return "Name: ungültiges Zeichen";
        var t = NormalTag(tag);
        if (t.Length != 3) return "Kürzel: genau drei Zeichen";
        foreach (var ch in t) if (!(ch is >= 'A' and <= 'Z' || ch is >= '0' and <= '9')) return "Kürzel: nur A–Z und 0–9";
        foreach (var a in u.Actors.Values)
            if (a.Id != me.Id && string.Equals(a.Tag, t, StringComparison.OrdinalIgnoreCase)) return $"Kürzel {t} ist schon vergeben ({a.Name})";
        foreach (var a in u.Actors.Values)
            if (a.Id != me.Id && string.Equals(a.Name, n, StringComparison.OrdinalIgnoreCase)) return "Name ist schon vergeben";
        return null;
    }

    /// <summary>Setzt Name und Kürzel des Spielers (und seiner Fraktion); false ohne Änderung bei einem Problem.</summary>
    public static bool Rename(Universe u, string? name, string? tag)
    {
        if (RenameIssue(u, name, tag) is not null) return false;
        var me = u.Actors[u.PlayerActor];
        me.Name = ShipNames.Clean(name);
        me.Tag = NormalTag(tag);
        if (u.Factions.TryGetValue(new FactionId(me.Id.Value), out var f)) f.Name = me.Name;
        u.Events.Publish(new PlayerFactionRenamed(me.Name, me.Tag));
        return true;
    }
}

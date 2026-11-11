using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpaceAdventure.Ui.Kit;
using SpaceSim.Core.Model;
using SpaceSim.Core.Services;
using static SpaceAdventure.Ui.Kit.UiKit;

namespace SpaceAdventure.Ui.Screens;

/// <summary>
/// Dock-Bereich „Lager“ (Phase 8, Dienst „storage“ am Lagerhaus): links die Fracht an Bord mit „Einlagern“, rechts das persönliche Lager
/// an dieser Station mit „Abholen“. Zeilen ausschließlich aus <see cref="IStationService.Offers"/>, ausgeführt nur über
/// <see cref="IStationService.Execute"/>; Meldung aus <see cref="ServiceResult.Message"/>. Kopf: belegt/Kapazität und Gebühr je Einheit.
/// </summary>
public sealed partial class StorageScreen : DockAreaScreen
{
	public override string Id => "dock.storage";
	public StorageScreen() => AreaId = DockAreas.Storage;
	protected override bool ShowShip => true;

	/// <summary>Wird nach jedem Execute ausgelöst (true = Erfolg) – Main spielt den Ton.</summary>
	public event Action<bool>? Executed;

	string _message = "";
	bool _msgOk = true;
	int _rows;

	IStationService? Service => Ctx.Services.ForStation(Ctx.U, Ctx.Station).FirstOrDefault(s => s.Key == StorageService.ServiceKey);

	protected override void BuildContent(Control inner)
	{
		float w = inner.Size.X > 0 ? inner.Size.X : 670f;
		_rows = 0;
		var u = Ctx.U;
		float used = StorageService.Used(Ctx.Station);
		At(inner, Text(_message.Length > 0 ? _message
				: $"Persönliches Lager: {used:0}/{u.Rules.StorageCapacity:0} Einheiten · Einlagern {u.Rules.StorageFeePerUnit:0.##} Cr je Einheit, Abholen frei.",
			15, _message.Length > 0 ? (_msgOk ? Green : Red) : new Color(Dim, 0.85f)), 0, 0, w, 40);
		var svc = Service;
		IReadOnlyList<ServiceOffer> offers = svc is null ? Array.Empty<ServiceOffer>() : svc.Offers(u, Ctx.Station, Ctx.Ship);
		float half = (w - 16) / 2f;
		Column(inner, "An Bord → Lager", offers.Where(o => o.Key.StartsWith(StorageService.Store + ":")).ToList(), "Nichts an Bord.", 0, half, true);
		Column(inner, "Im Lager → an Bord", offers.Where(o => o.Key.StartsWith(StorageService.Fetch + ":")).ToList(), "Hier ist nichts eingelagert.", half + 16, half, false);
	}

	void Column(Control inner, string title, List<ServiceOffer> offers, string empty, float x, float w, bool store)
	{
		At(inner, Text(title, 20, Cyan), x, 48, w, 28);
		float y = 82f;
		if (offers.Count == 0) { At(inner, Text(empty, 15, new Color(Dim, 0.8f)), x, y, w, 24); return; }
		foreach (var o in offers.Take(7))
		{
			bool can = o.Amount >= 0.01f;
			At(inner, Text(Ctx.U.Goods[o.Good].Name, 17, White), x, y + 4, w - 140, 24);
			string detail = store ? $"{o.Held:0} an Bord · {o.Amount:0} möglich · {o.Amount * o.Price:0} Cr" : $"{o.Stock:0} eingelagert · {o.Amount:0} passen";
			At(inner, Text(detail, 13, Dim), x, y + 28, w - 140, 20);
			var b = At(inner, new KitButton(store ? "Einlagern" : "Abholen", new Vector2(130, 56)) { FontSize = 16, State = can ? KitButtonState.Ready : KitButtonState.Disabled }, x + w - 132, y);
			string key = o.Key;
			if (can) b.Pressed += () => Execute(key);
			y += 62f;
			_rows++;
		}
	}

	/// <summary>Führt ein Angebot über den Dienst aus (ganze verfügbare Menge) und zeigt dessen Meldung.</summary>
	public ServiceResult Execute(string offer)
	{
		var svc = Service;
		var r = svc is null
			? new ServiceResult(false, 0f, 0f, "Kein Lager an dieser Station.")
			: svc.Execute(Ctx.U, Ctx.Station, Ctx.Ship, new ServiceRequest(offer, default, 0f));
		_message = r.Message;
		_msgOk = r.Success;
		Executed?.Invoke(r.Success);
		Invalidate();
		return r;
	}

	// ── Zugriff für Selbsttest/Bildschirmfotos ──
	public int TestRowCount => _rows;
	public string TestMessage => _message;
}

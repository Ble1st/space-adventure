using System;
using System.Linq;
using Godot;
using SpaceAdventure.Ui.Kit;
using static SpaceAdventure.Ui.Kit.UiKit;

namespace SpaceAdventure.Ui.Screens;

/// <summary>
/// Kommandozentrale „Comms“ (UI.md Abschnitt 4): Posteingang und Verhandlungsfenster komplett als
/// Platzhalter – Nachrichten, Antwortknöpfe mit Folgenvorschau (Ruf/Kosten/Chance), alle Texte und Zahlen
/// zentral in <see cref="UiPlaceholders"/>, deutlich als „Platzhalter / Vorschau“ gekennzeichnet, ohne Wirkung.
/// Der Zähler ungelesener Nachrichten steht am Hamburger-Menü (<see cref="UiPlaceholders.CommsUnread"/>).
/// </summary>
public sealed partial class CommsScreen : CommandScreen
{
	public override string Id => "cc.comms";

	int _sel;

	/// <summary>Test-Hook: Nachricht wählen.</summary>
	public void TestSelect(int i) { _sel = i; Invalidate(); }

	protected override void BuildContent(Control inner)
	{
		var msgs = UiPlaceholders.CommsMessages;
		if (_sel >= msgs.Length) _sel = 0;

		// Hinweis: alles Platzhalter
		At(inner, Text($"{UiPlaceholders.Tag} / Vorschau – keine echten Nachrichten, Antworten haben keine Wirkung ({UiPlaceholders.Soon})", 14, new Color(Orange, 0.9f)), 12, 10, 998, 22);

		// ── Posteingang links ──
		var inbox = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		inbox.AddThemeConstantOverride("separation", 6);
		At(inner, inbox, 12, 40, 340, 472);
		for (int i = 0; i < msgs.Length; i++)
		{
			var m = msgs[i];
			bool isSel = i == _sel;
			var row = new PanelContainer { MouseFilter = MouseFilterEnum.Stop };
			row.AddThemeStyleboxOverride("panel", Box(isSel ? new Color(0.14f, 0.24f, 0.42f, 0.95f) : Card, isSel ? Cyan : null, 8, isSel ? 2 : 0, 10));
			row.CustomMinimumSize = new Vector2(328, 74);
			int idx = i;
			row.GuiInput += e =>
			{
				if (Pointer.TryRead(e, out _, out _, out bool pressed, out bool motion) && pressed && !motion) { _sel = idx; Invalidate(); row.AcceptEvent(); }
			};
			row.AddChild(new InboxRow(m, isSel));
			inbox.AddChild(row);
		}
		At(inner, Text($"{UiPlaceholders.CommsUnread} ungelesen (Platzhalter-Zähler)", 13, new Color(Dim, 0.8f)), 12, 494, 340, 18);

		// ── Verhandlungsfenster rechts ──
		var dlg = CardBg(inner, 364, 40, 646, 472, Card);
		var msg = msgs[_sel];
		At(dlg, Text(msg.From, 21, White), 16, 12, 500, 28);
		At(dlg, Text(msg.Subject, 15, Cyan), 16, 42, 500, 20);
		At(dlg, Text(UiPlaceholders.Tag, 12, new Color(Orange, 0.8f)), 530, 44, 100, 18);
		WrappedText(dlg, msg.Body, 16, 70, 614, 15, White);

		At(dlg, Text("Deine Antwort (Folgenvorschau: Ruf/Kosten/Chance)", 16, Cyan), 16, 150, 614, 22);
		float y = 178;
		foreach (var a in msg.Answers)
		{
			var b = At(dlg, new KitButton(a.Text, new Vector2(614, 52)) { FontSize = 16, AlignLeft = true, State = KitButtonState.Disabled, Sub = a.EffectPreview, Detail = UiPlaceholders.Tag, DetailColor = new Color(Orange, 0.8f) }, 16, y);
			y += 58;
		}
		At(dlg, Text("Ignorieren kostet keinen Ruf, Angebote verfallen – alles " + UiPlaceholders.Tag + " (" + UiPlaceholders.Soon + ").", 12, new Color(Dim, 0.75f)), 16, 452, 614, 18);
	}

	/// <summary>Eine Posteingangszeile: Absender, Betreff, Ungelesen-Punkt.</summary>
	sealed partial class InboxRow : Control
	{
		readonly UiPlaceholders.CommsMessage _m; readonly bool _sel;
		public InboxRow(UiPlaceholders.CommsMessage m, bool sel)
		{
			_m = m; _sel = sel;
			MouseFilter = MouseFilterEnum.Ignore;
			CustomMinimumSize = new Vector2(308, 54);
			Size = CustomMinimumSize;
		}

		public override void _Draw()
		{
			var font = ThemeDB.FallbackFont;
			DrawString(font, new Vector2(14, 24), _m.From, HorizontalAlignment.Left, -1, 15, White);
			DrawString(font, new Vector2(14, 46), _m.Subject, HorizontalAlignment.Left, -1, 13, Dim);
			if (_m.IsNew) DrawCircle(new Vector2(292, 20), 6f, Orange);
		}
	}
}

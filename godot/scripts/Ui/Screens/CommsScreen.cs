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
public sealed partial class CommsScreen : CommandScreen, ISessionScreen
{
	public override string Id => "cc.comms";
	/// <summary>Gerätetest Phase 27: Auswahl und Meldung bei neuer Sitzung zurücksetzen.</summary>
	public void ResetSession() { _sel = 0; }

	int _sel;

	/// <summary>Test-Hook: Nachricht wählen.</summary>
	public void TestSelect(int i) { _sel = i; Invalidate(); }

	protected override void BuildContent(Control inner)
	{
		var msgs = UiPlaceholders.CommsMessages;
		if (_sel >= msgs.Length) _sel = 0;

		// Hinweis: alles Platzhalter
		At(inner, Text($"{UiPlaceholders.Tag} / Vorschau – keine echten Nachrichten, Antworten haben keine Wirkung ({UiPlaceholders.Soon})", 14, new Color(Orange, 0.9f)), 12, 10, InnerW(inner) - 24f, 22);

		// ── Posteingang links ──
		var inbox = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		inbox.AddThemeConstantOverride("separation", 6);
		bool narrow = Narrow(inner);   // Runde 35: schmal — Posteingang oben, Nachricht darunter
		// Runde 35b (Prüfung: schmal lagen Posteingang, Zähler und Nachricht übereinander): Höhe aus der Zahl der Zeilen, alles darunter folgt
		float inboxH = narrow ? msgs.Length * 80f : 472f;
		At(inner, inbox, 12, 40, narrow ? InnerW(inner) - 24f : 340, inboxH);
		for (int i = 0; i < msgs.Length; i++)
		{
			var m = msgs[i];
			bool isSel = i == _sel;
			var row = new PanelContainer { MouseFilter = MouseFilterEnum.Stop };
			row.AddThemeStyleboxOverride("panel", Box(isSel ? new Color(0.14f, 0.24f, 0.42f, 0.95f) : Card, isSel ? Cyan : null, 8, isSel ? 2 : 0, 10));
			row.CustomMinimumSize = new Vector2(narrow ? InnerW(inner) - 24f : 328, 74);
			int idx = i;
			row.GuiInput += e =>
			{
				if (Pointer.TryRead(e, out _, out _, out bool pressed, out bool motion) && pressed && !motion) { _sel = idx; Invalidate(); row.AcceptEvent(); }
			};
			row.AddChild(new InboxRow(m, isSel));
			inbox.AddChild(row);
		}
		float cy = narrow ? 40 + inboxH + 2 : 494;
		At(inner, Text($"{UiPlaceholders.CommsUnread} ungelesen (Platzhalter-Zähler)", 13, new Color(Dim, 0.8f)), 12, cy, 340, 18);

		// ── Verhandlungsfenster rechts (schmal: darunter) ──
		float dw = narrow ? InnerW(inner) - 24f : 646f;
		var dlg = narrow ? CardBg(inner, 12, cy + 26, dw, 472, Card) : CardBg(inner, 364, 40, 646, 472, Card);
		float tw = dw - 32f;
		var msg = msgs[_sel];
		At(dlg, Text(msg.From, 21, White), 16, 12, tw - 110, 28);
		At(dlg, Text(msg.Subject, 15, Cyan), 16, 42, tw - 110, 20);
		At(dlg, Text(UiPlaceholders.Tag, 12, new Color(Orange, 0.8f)), dw - 116, 44, 100, 18);
		float by = WrappedText(dlg, msg.Body, 16, 70, tw, 15, White);

		float hy = MathF.Max(150f, by + 6f);
		At(dlg, Text("Deine Antwort (Folgenvorschau: Ruf/Kosten/Chance)", 16, Cyan), 16, hy, tw, 22);
		float y = hy + 28;
		foreach (var a in msg.Answers)
		{
			var b = At(dlg, new KitButton(a.Text, new Vector2(tw, 72)) { FontSize = 16, AlignLeft = true, State = KitButtonState.Disabled, Sub = a.EffectPreview, Detail = UiPlaceholders.Tag, DetailColor = new Color(Orange, 0.8f) }, 16, y);
			y += 80;   // Gerätetest Phase 27: drei Zeilen (Antwort, Folgen, Platzhalter) brauchen 72 px
		}
		At(dlg, Text("Ignorieren kostet keinen Ruf, Angebote verfallen – alles " + UiPlaceholders.Tag + " (" + UiPlaceholders.Soon + ").", 12, new Color(Dim, 0.75f)), 16, MathF.Max(452f, y + 4), tw, 18);
		if (y + 26 > 472) dlg.CustomMinimumSize = new Vector2(dw, y + 30);
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
			DrawnText.Clear(this);
			UiKit.DrawFit(this, 0, font, new Vector2(14, 24), _m.From, 15, Size.X - 40f, White);
			UiKit.DrawFit(this, 1, font, new Vector2(14, 46), _m.Subject, 13, Size.X - 24f, Dim);
			if (_m.IsNew) DrawCircle(new Vector2(Size.X - 16f, 20), 6f, Orange);
		}
	}
}

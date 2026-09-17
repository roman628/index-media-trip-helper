using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace MediaTrip.UI.Shell
{
    /// <summary>The five schemes in light and dark, with the contrast ratios that matter. Tap a card to apply.</summary>
    public static class ThemeSheet
    {
        public static void Open(AppController app) => app.ShowSheet(() => Build(app));

        private static VisualElement Build(AppController app)
        {
            var head = app.SheetHead("Theme", app.CloseSheet,
                U.Toggle("Dark", app.Theme.Dark, () => { app.Theme.ToggleDark(); Open(app); }));
            var grid = U.Row().Cls("wrap top-align");
            foreach (var name in ThemeManager.Names)
                foreach (var dark in new[] { false, true })
                    grid.Add(Card(app, name, dark));
            return app.Sheet(1040, app.CloseSheet, head, U.Scroll(grid));
        }

        private static VisualElement Card(AppController app, string name, bool dark)
        {
            var v = ThemeManager.ReadVariables(name, dark);
            Color C(string k, Color fb) => v.TryGetValue(k, out var s) ? ThemeManager.ParseColor(s, fb) : fb;
            var bg = C("bg", Color.white); var tx = C("tx", Color.black); var tx2 = C("tx2", Color.gray);
            var ac = C("ac", Color.blue); var acTx = C("acTx", Color.white); var ok = C("ok", Color.green); var okTx = C("okTx", Color.white); var bad = C("bad", Color.red); var bd = C("bd", Color.gray);
            var on = app.Theme.Theme == name && app.Theme.Dark == dark;
            var card = U.Tap(() => { app.Theme.Set(name, dark); app.CloseSheet(); }, "swcard").On(on);
            card.style.backgroundColor = bg;
            card.style.borderTopColor = card.style.borderBottomColor = card.style.borderLeftColor = card.style.borderRightColor = on ? ac : bd;
            var title = U.Text(name + " · " + (dark ? "dark" : "light"), "h3").Mb(6); title.style.color = tx; card.Add(title);
            var note = U.Sub(ThemeManager.Notes[name]).Mb(8); note.style.color = tx2; card.Add(note);
            var sw = U.Row().Cls("wrap").Mb(6);
            foreach (var k in new[] { "bg", "sf", "sf2", "bd", "tx", "tx2", "ac", "ok", "warn", "bad" })
            {
                var box = new VisualElement().Cls("sw"); box.style.backgroundColor = C(k, Color.magenta); box.style.borderTopColor = box.style.borderBottomColor = box.style.borderLeftColor = box.style.borderRightColor = bd; sw.Add(box);
            }
            card.Add(sw);
            foreach (var (label, ratio) in new[]
            {
                ("Text / bg", ThemeManager.Contrast(tx, bg)), ("Text 2 / bg", ThemeManager.Contrast(tx2, bg)),
                ("Accent text / accent", ThemeManager.Contrast(acTx, ac)), ("Captured text / fill", ThemeManager.Contrast(okTx, ok)),
            })
            {
                var r = U.Row(U.Text(label).Font(12).Cls("grow"), U.Text(ratio.ToString("0.0") + ":1").Font(12).Bold()).Mt(3);
                r.style.width = Length.Percent(100);
                foreach (var l in r.Children().OfType<Label>()) l.style.color = tx2;
                if (ratio < 4.5f) ((Label)r.Children().Last()).style.color = bad;
                card.Add(r);
            }
            return card;
        }
    }

    /// <summary>A yes / no question before something that cannot be undone.</summary>
    public static class ConfirmSheet
    {
        public static void Open(AppController app, string title, string body, string action, System.Action onYes)
        {
            app.ShowSheet(() =>
            {
                var buttons = U.Row(U.Btn("Cancel", app.CloseSheet, "big"), U.Grow(), U.Btn(action, () => { app.CloseSheet(); onYes(); }, "big danger"));
                buttons.style.flexShrink = 0;
                return app.Sheet(560, app.CloseSheet, U.H2(title).Mb(8), U.Scroll(U.Body(body)).Mb(16), buttons);
            });
        }
    }
}

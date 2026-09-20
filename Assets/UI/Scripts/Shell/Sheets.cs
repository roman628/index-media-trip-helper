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

    /// <summary>
    /// A question with more than two answers. Cancel is always there and always does nothing,
    /// so the user can go and look at something and come back. The first option is the default
    /// (drawn as the primary action).
    /// </summary>
    public static class ChoiceSheet
    {
        public sealed class Option
        {
            public string Label;
            public System.Action Act;
            public bool Danger;
            public Option(string label, System.Action act, bool danger = false) { Label = label; Act = act; Danger = danger; }
        }

        public static void Open(AppController app, string title, System.Collections.Generic.IEnumerable<string> lines, params Option[] options)
        {
            app.ShowSheet(() =>
            {
                var body = U.Scroll();
                foreach (var line in lines ?? new string[0]) body.Add(U.Body(U.Esc(line)).Mb(8));
                var buttons = U.Col();
                buttons.style.flexShrink = 0;
                bool first = true;
                foreach (var o in options)
                {
                    var opt = o;
                    buttons.Add(U.Btn(opt.Label, () => { app.CloseSheet(); opt.Act?.Invoke(); }, "big mb8" + (opt.Danger ? " danger" : first ? " pri" : "")));
                    first = false;
                }
                buttons.Add(U.Btn("Cancel", app.CloseSheet, "big ghost"));
                return app.Sheet(600, app.CloseSheet, U.H2(title).Mb(10), body.Mb(12), buttons);
            });
        }
    }

    /// <summary>The keystrokes that work on the screen being looked at. Offered in the ⋯ menu when a keyboard is attached.</summary>
    public static class ShortcutsSheet
    {
        public static (string keys, string what)[] For(Screen screen, bool edit)
        {
            var m = Shortcuts.Meta;
            var list = new System.Collections.Generic.List<(string, string)>
            {
                (m + " F", "Search"), (m + " 1 … 5", "Trip · Shot list · Outlines · Covers · Summary"), (m + " S", "Save now"), ("Esc", "Close what is on top"),
            };
            if (Screens.HasEdit(screen)) list.Add((m + " E", edit ? "Done editing" : "Edit"));
            if (screen == Screen.Outlines && edit)
            {
                list.Add(("Enter", "In a chapter name: its first section. In a section name: the next section"));
                list.Add((m + " Enter", "A new chapter after this one"));
                list.Add(("Tab", "In a section name: into its bullets"));
                list.Add(("Alt ↑ ↓", "Move the chapter or section up or down"));
                list.Add(("↑ ↓", "Previous or next name"));
            }
            if ((screen == Screen.Outlines || screen == Screen.ShotList) && edit)
            {
                list.Add(("Enter", "In a bullet: a new bullet below"));
                list.Add(("Shift Enter", "In a bullet: a new bullet inside it"));
                list.Add(("Tab · Shift Tab", "In a bullet: indent · outdent"));
                list.Add(("Alt ↑ ↓", "In a bullet: move it up or down"));
                list.Add(("Backspace", "On an empty bullet: delete it"));
            }
            if (screen == Screen.ShotList && edit) list.Add(("Enter", "In a video or photo name: done, collapse the row"));
            if (screen == Screen.ShotList && !edit) list.Add((m + " ] · " + m + " [", "Expand all · Collapse all"));
            return list.ToArray();
        }

        public static VisualElement Build(AppController app)
        {
            var st = app.State;
            void Close() { st.Shortcuts = false; app.Render(); }
            var body = U.Scroll();
            foreach (var (keys, what) in For(st.Screen, st.Edit))
            {
                var row = U.Row(U.Text(U.Esc(keys), "bold").W(app.Layout.Phone ? 120 : 170), U.Text(U.Esc(what), "body-sm grow")).MinH(40).Cls("top-align");
                row.style.paddingTop = 6; row.style.paddingBottom = 6;
                body.Add(row);
            }
            return app.Sheet(640, Close, app.SheetHead("Shortcuts · " + Screens.Label(st.Screen), Close), body);
        }
    }
}

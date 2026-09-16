using System;
using System.Collections.Generic;
using System.Linq;
using MediaTrip.Model;
using MediaTrip.Persistence;
using MediaTrip.UI.Transfer;
using MediaTrip.UI.ViewModels;
using UnityEngine;
using UnityEngine.UIElements;

namespace MediaTrip.UI.Shell
{
    /// <summary>Keyboard and touch reference (Cmd+/ or the keyboard button).</summary>
    public static class HelpSheet
    {
        public static VisualElement Build(AppController app)
        {
            var sheet = new VisualElement().Cls("sheet xwide");
            var m = Shortcuts.Meta;
            sheet.Add(U.Row(U.H2("Keyboard and touch").Cls("grow"), U.Btn("Close", () => { app.State.Help = false; app.Render(); }, "sm")).Mb(12));
            var cols = U.Row().Cls("stretch");
            cols.style.alignItems = Align.FlexStart;
            var left = U.Col(U.Eyebrow("Lists and outliners").Mb(8)).Cls("grow").Mr(24);
            foreach (var (k, t) in new[]
            {
                ("↑ ↓", "Select previous / next"), ("Enter", "New sibling below"), ("⇧Enter", "New child"),
                ("Tab ⇧Tab", "Indent / outdent (outliners) · next field (forms)"), ("⌥↑ ⌥↓", "Move up / down"),
                (m + "D", "Duplicate"), (m + "⌫", "Delete (⌫ on an empty bullet)"), (m + "⇧V", "Paste as nested bullets"),
                ("Esc", "Leave the field, keep the row selected"),
            }) left.Add(RowOf(k, t));
            var right = U.Col(U.Eyebrow("Everywhere").Mb(8)).Cls("grow");
            foreach (var (k, t) in new[] { (m + "F", "Search"), (m + "1 … " + m + "7", "Authoring tabs"), (m + "/", "This sheet"), (m + "S", "Save now") })
                right.Add(RowOf(k, t));
            right.Add(U.Eyebrow("Touch equivalents").Mt(16).Mb(8));
            right.Add(U.Body("Tap to select · tap text to edit · every outline operation is on the bar at the bottom · Up / Down on that bar reorder · the ≡ handle marks what the bar acts on."));
            cols.Add(left); cols.Add(right);
            sheet.Add(cols);
            return app.Overlay(sheet, () => { app.State.Help = false; app.Render(); });
        }

        private static VisualElement RowOf(string keys, string text)
        {
            var r = U.Row().MinH(40);
            var kk = U.Row().W(160);
            foreach (var k in keys.Split(' ')) { var kb = U.Kbd(k); kb.style.marginLeft = 0; kb.Mr(4); kk.Add(kb); }
            r.Add(kk);
            r.Add(U.Body(text).Cls("grow"));
            return r;
        }
    }

    /// <summary>The five schemes in light and dark, with the contrast ratios that matter. Tap a card to apply.</summary>
    public static class ThemeSheet
    {
        public static void Open(AppController app) => app.ShowSheet(() => Build(app));

        private static VisualElement Build(AppController app)
        {
            var sheet = new VisualElement().Cls("sheet wide");
            sheet.style.maxHeight = 760;
            sheet.Add(U.Row(U.Col(U.H2("Color scheme"), U.Sub("Ledger light is the default. Beacon is the sun mode (also on the ☀ button). Status never relies on hue: ring, filled check, half circle, X, grey arrow.")).Cls("grow"),
                U.Toggle(app.Theme.Dark ? "Dark" : "Light", app.Theme.Dark, () => { app.Theme.ToggleDark(); ThemeSheet.Open(app); }).Mr(10),
                U.Btn("Close", app.CloseSheet, "sm")).Mb(12));
            var grid = U.Row().Cls("wrap");
            grid.style.alignItems = Align.FlexStart;
            foreach (var name in ThemeManager.Names)
                foreach (var dark in new[] { false, true })
                    grid.Add(Card(app, name, dark));
            sheet.Add(U.Scroll(grid));
            return app.Overlay(sheet, app.CloseSheet);
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
                ("Accent text / accent", ThemeManager.Contrast(acTx, ac)), ("Accent / bg", ThemeManager.Contrast(ac, bg)),
                ("Captured text / fill", ThemeManager.Contrast(okTx, ok)), ("Dropped / bg", ThemeManager.Contrast(bad, bg)),
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

    /// <summary>Trip library: open, create, duplicate, delete, export.</summary>
    public static class LibrarySheet
    {
        public static void Open(AppController app) => app.ShowSheet(() => Build(app));

        private static VisualElement Build(AppController app)
        {
            var sheet = new VisualElement().Cls("sheet wide");
            sheet.style.maxHeight = 720;
            var head = U.Row(U.Col(U.H1("Media trips"), U.Sub("Stored on this device · JSON")).Cls("grow"),
                U.Btn("+ New trip", () => NewTrip(app), "pri"),
                U.Btn("Import…", () => { app.CloseSheet(); ImportFlow.Begin(app); }, "ml10"),
                U.Btn("Close", app.CloseSheet, "sm ml10")).Mb(16);
            sheet.Add(head);
            var list = U.Scroll();
            var trips = TripLibrary.ListTrips();
            if (trips.Count == 0) list.Add(U.Sub("No trips on this device yet."));
            foreach (var t in trips)
            {
                var current = app.Session != null && t.TripId == app.Session.Data.TripId;
                var card = U.Card().Cls("row").Mb(12).MinH(96);
                if (current) card.Cls("ac");
                string sub;
                if (t.LoadError != null) sub = "Cannot open: " + t.LoadError;
                else
                {
                    sub = (string.IsNullOrEmpty(t.City) ? "" : t.City + " · ") + U.FmtDate(t.Arrive) + " – " + U.FmtDate(t.Depart);
                }
                var open = U.Tap(() => { if (t.LoadError == null && !current) { app.CloseSheet(); app.OpenTrip(t.TripId); app.Toast("Opened " + t.DisplayName); } else app.CloseSheet(); }, "grow row",
                    U.Col(U.Row(U.H2(U.Esc(t.DisplayName)).Mr(12), current ? U.Pill("Open", "ac") : null), U.Sub(sub)).Cls("grow"));
                open.MinH(64);
                card.Add(open);
                card.Add(U.Btn("Duplicate", () => Duplicate(app, t.TripId), "sm ml10"));
                card.Add(U.Btn("Delete", () => ConfirmDelete(app, t), "sm danger ml8"));
                list.Add(card);
            }
            sheet.Add(list);
            return app.Overlay(sheet, app.CloseSheet);
        }

        private static void NewTrip(AppController app)
        {
            var created = TripLibrary.CreateTrip("NEW", "TRIP");
            app.CloseSheet();
            app.OpenTrip(created.TripId);
            app.Nav(Screen.Trip);
            app.Toast("New trip · fill in client, program, phase and dates");
        }

        private static void Duplicate(AppController app, string tripId)
        {
            try
            {
                var copy = TripLibrary.DuplicateTrip(tripId, includeCaptures: false);
                app.CloseSheet();
                app.OpenTrip(copy.TripId);
                app.Nav(Screen.Trip);
                app.Toast("Duplicated · captures cleared, plan and outlines kept");
            }
            catch (Exception ex) { app.Toast("Duplicate failed: " + ex.Message); }
        }

        private static void ConfirmDelete(AppController app, TripSummary t)
        {
            app.ShowSheet(() =>
            {
                var sheet = new VisualElement().Cls("sheet");
                sheet.Add(U.H2("Delete " + U.Esc(t.DisplayName) + "?").Mb(8));
                sheet.Add(U.Body("This removes the trip's folder from this device. Export it first if you want a copy.").Mb(16));
                sheet.Add(U.Row(U.Btn("Cancel", () => Open(app), "big"), U.Grow(), U.Btn("Delete trip", () =>
                {
                    var wasCurrent = app.Session != null && t.TripId == app.Session.Data.TripId;
                    if (wasCurrent) app.CloseTrip();
                    TripLibrary.DeleteTrip(t.TripId);
                    app.CloseSheet();
                    if (wasCurrent)
                    {
                        var next = TripLibrary.ListTrips().FirstOrDefault(x => x.LoadError == null);
                        if (next != null) app.OpenTrip(next.TripId); else app.Render();
                    }
                    app.Toast("Deleted " + t.DisplayName);
                }, "big danger")));
                return app.Overlay(sheet, () => Open(app));
            });
        }
    }
}

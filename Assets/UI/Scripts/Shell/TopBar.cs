using System;
using System.Linq;
using MediaTrip.Model;
using MediaTrip.UI.ViewModels;
using UnityEngine.UIElements;

namespace MediaTrip.UI.Shell
{
    /// <summary>Top chrome: identity, field tabs, search with results, help, validation badge, theme controls, mode switch, Log capture.</summary>
    public static class TopBar
    {
        public static VisualElement Build(AppController app)
        {
            var s = app.State;
            var d = app.Session.Data;
            var top = new VisualElement().Cls("top");
            var row = new VisualElement().Cls("toprow");
            top.Add(row);

            // identity → trip library
            var identity = U.Tap(() => LibrarySheet.Open(app), "identity");
            identity.Add(U.Text(Fmt.Identity(d.Trip), "title"));
            var subText = s.Mode == Mode.Field
                ? Fmt.Place(d.Trip) + " · " + Fmt.DayOf(d.Trip, app.TodayId) + " · Trips"
                : Fmt.Place(d.Trip) + " · Authoring" + (app.LastSaved != null ? " · autosaved " + app.LastSaved.Value.ToString("HH:mm") : app.Session.IsDirty ? " · unsaved" : "");
            identity.Add(U.Text(subText, "sub"));
            row.Add(identity);

            if (s.Mode == Mode.Field)
            {
                foreach (var tab in Screens.FieldTabs)
                {
                    var b = TabButton(app, tab, Screens.Label(tab), tab == Screen.Heroes ? GlyphKind.Star : GlyphKind.None, null);
                    row.Add(b);
                }
            }

            row.Add(SearchField(app));

            if (Shortcuts.HardwareKeyboardPresent)
            {
                var help = U.Tap(() => { s.Help = !s.Help; app.Render(); }, "iconb", new Glyph(GlyphKind.Keyboard, 24));
                help.On(s.Help);
                help.tooltip = "Keyboard shortcuts";
                row.Add(help);
            }

            if (s.Mode == Mode.Author)
            {
                var v = app.CurrentValidation;
                if (v.Any)
                {
                    var badge = U.Tap(() => app.Nav(Screen.Json), "badge" + (v.Errors > 0 ? "" : " warn"), U.Text(v.BadgeText));
                    row.Add(badge);
                }
            }

            // theme: sun (Beacon) one-tap, and the scheme sheet (light/dark toggle lives at its top)
            var sun = U.Tap(() => app.Theme.ToggleSun(), "iconb", new Glyph(GlyphKind.Sun, 26, 3f));
            sun.On(app.Theme.IsSun);
            sun.tooltip = app.Theme.IsSun ? "Leave sun mode" : "Sun mode (Beacon)";
            row.Add(sun);
            var palette = U.Tap(() => ThemeSheet.Open(app), "iconb", new Glyph(app.Theme.Dark ? GlyphKind.Moon : GlyphKind.Circle, 20));
            palette.tooltip = "Color scheme, light / dark";
            row.Add(palette);

            var modeBtn = U.Tap(() => app.SetMode(s.Mode == Mode.Field ? Mode.Author : Mode.Field), "tab outlined ml10",
                new Glyph(s.Mode == Mode.Field ? GlyphKind.Pencil : GlyphKind.Circle, 16), U.Text(s.Mode == Mode.Field ? "Author" : "Field").Ml(8));
            row.Add(modeBtn);

            if (s.Mode == Mode.Field)
            {
                var log = U.Btn("+ Log capture", () => app.OpenCapture(), "pri ml8").H(52);
                log.style.paddingLeft = 14; log.style.paddingRight = 14;
                row.Add(log);
            }

            if (s.Mode == Mode.Author)
            {
                var tabs = new VisualElement().Cls("tabrow");
                for (int i = 0; i < Screens.AuthorTabs.Length; i++)
                {
                    var tab = Screens.AuthorTabs[i];
                    tabs.Add(TabButton(app, tab, Screens.Label(tab), GlyphKind.None, Shortcuts.HardwareKeyboardPresent ? Shortcuts.Meta + (i + 1) : null));
                }
                top.Add(tabs);
            }
            return top;
        }

        private static Button TabButton(AppController app, Screen target, string label, GlyphKind glyph, string kbd)
        {
            var b = U.Tap(() => app.Nav(target), "tab");
            if (glyph != GlyphKind.None) { b.Add(new Glyph(glyph, 16)); }
            var l = U.Text(label); if (glyph != GlyphKind.None) l.Ml(6);
            b.Add(l);
            if (kbd != null) b.Add(U.Kbd(kbd));
            b.On(app.State.Screen == target || (target == Screen.Today && app.State.Screen == Screen.Capture));
            return b;
        }

        private static VisualElement SearchField(AppController app)
        {
            var s = app.State;
            var search = new VisualElement().Cls("search").On(!string.IsNullOrEmpty(s.Query));
            search.Add(new Glyph(GlyphKind.Search, 20).Mr(10));
            var placeholder = s.Mode == Mode.Field ? "Search · ch2 hero, lock, permit" : "Search videos, photos, people, sections";
            var tf = U.Input(s.Query, placeholder);
            tf.name = "q";
            search.Add(tf);
            var clear = U.Tap(() => { s.Query = ""; app.RenderKeepFocus("q"); }, "x", new Glyph(GlyphKind.Cross, 16));
            clear.Hide(string.IsNullOrEmpty(s.Query));
            search.Add(clear);
            app.ClosePopupWhenBlurred(tf);
            void Refresh()
            {
                if (string.IsNullOrWhiteSpace(s.Query)) { app.ClosePopup(); return; }
                var box = SearchDropdown.Build(app);
                app.ShowPopup(search, box, 620, 520, -120);
            }
            if (!string.IsNullOrWhiteSpace(s.Query)) search.schedule.Execute(Refresh).StartingIn(1);
            tf.RegisterValueChangedCallback(e =>
            {
                s.Query = e.newValue;
                search.On(!string.IsNullOrEmpty(s.Query));
                clear.Hide(string.IsNullOrEmpty(s.Query));
                Refresh();
            });
            return search;
        }
    }

    public static class SearchDropdown
    {
        /// <summary>The results list, built for the popup layer.</summary>
        public static VisualElement Build(AppController app)
        {
            var s = app.State;
            var d = app.Session.Data;
            var r = GlobalSearch.Run(app.Session, s.Query, includePeople: s.Mode == Mode.Author);
            var box = new VisualElement().Cls("sugg wide");
            box.style.position = Position.Relative; box.style.top = 0; box.style.left = 0; box.style.marginTop = 0;
            var sv = U.Scroll();
            sv.style.maxHeight = 512;
            box.Add(sv);

            foreach (var m in r.Items)
            {
                var it = m.Item;
                var b = U.Tap(() =>
                {
                    s.Query = "";
                    if (s.Mode == Mode.Field) { app.Nav(Screen.Plan); s.PlanVideo = it.Id; s.PlanBook = it.BookId; app.Render(); }
                    else
                    {
                        app.Nav(Screen.ShotList);
                        var v = d.FindVideo(it.Id) ?? (it.SourceIds.Count > 0 ? d.FindVideo(it.SourceIds[0]) : null);
                        if (v != null) { s.SL.Pane = "videos"; s.SL.SelKind = "video"; s.SL.SelId = v.Id; s.SL.Book = v.BookId; }
                        app.Render();
                    }
                }, "sugg-row");
                b.Add(U.Num(it.DisplayNumber));
                var ch = d.FindChapter(it.ChapterId);
                b.Add(U.Col(U.Text(U.Esc(it.Title), "bold"), U.Sub("Book " + (d.FindBook(it.BookId)?.Number.ToString() ?? "?") + " › " + (ch != null ? "Ch." + ch.Number : "no chapter"))).Cls("grow"));
                b.Add(U.StatusPill(it.Status));
                sv.Add(b);
            }
            foreach (var m in r.Photos)
            {
                var p = m.Item;
                var b = U.Tap(() =>
                {
                    s.Query = "";
                    if (s.Mode == Mode.Field) app.Nav(Screen.Heroes);
                    else { app.Nav(Screen.ShotList); s.SL.Pane = "photos"; s.SL.SelKind = "photo"; s.SL.SelId = p.Id; s.SL.Book = p.BookId; app.Render(); }
                }, "sugg-row");
                b.Add(U.StPhoto(p.Status, 32, p.HeroType != HeroType.None));
                var ch = d.FindChapter(p.ChapterId);
                b.Add(U.Col(U.Text(U.Esc(p.Description), "bold"), U.Sub((p.HeroType != HeroType.None ? "★ " + U.HeroLabel(p.Photo, ch) + " · " : "") + "Book " + (d.FindBook(p.BookId)?.Number.ToString() ?? "?") + " · photo")).Cls("grow ml12"));
                if (!p.IsCaptured) b.Add(U.Pill("Missing", "bad"));
                sv.Add(b);
            }
            foreach (var m in r.People)
            {
                var p = m.Item;
                var b = U.Tap(() => { s.Query = ""; app.Nav(Screen.People); s.PeopleSel = p.Id; app.Render(); }, "sugg-row");
                b.Add(new Glyph(GlyphKind.Person, 24).Mr(12));
                b.Add(U.Col(U.Text(U.Esc(p.FullName), "bold"), U.Sub(U.Esc(p.Title ?? "") + " · " + p.Org.ToString().ToLowerInvariant())).Cls("grow"));
                sv.Add(b);
            }
            foreach (var m in r.Sections)
            {
                var h = m.Item;
                var count = d.Captures.OutlineAssignments.Count(a => a.SectionId == h.Section.Id && a.BookId == h.BookId);
                var b = U.Tap(() =>
                {
                    s.Query = "";
                    if (s.Mode == Mode.Field) { s.CovBook = h.BookId; app.Nav(Screen.Coverage); }
                    else { s.OL.Book = h.BookId; s.OL.Chapter = h.Chapter.Id; s.OL.Sel = "sec:" + h.Section.Id; app.Nav(Screen.Outline); }
                }, "sugg-row");
                b.Add(U.Num(h.Section.Number ?? ""));
                b.Add(U.Col(U.Text(U.Esc(h.Section.Name), "bold"), U.Sub("Book " + (d.FindBook(h.BookId)?.Number.ToString() ?? "?") + " › Ch." + h.Chapter.Number + " · " + U.Plural(count, "media item", "media items") + " assigned")).Cls("grow"));
                sv.Add(b);
            }
            if (r.IsEmpty) sv.Add(U.Sub("No match.").Pad(14));
            return box;
        }
    }
}

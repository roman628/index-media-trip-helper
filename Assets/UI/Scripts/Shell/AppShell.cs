using System.Collections.Generic;
using System.Linq;
using MediaTrip.Persistence;
using MediaTrip.UI.Transfer;
using MediaTrip.UI.ViewModels;
using UnityEngine.UIElements;

namespace MediaTrip.UI.Shell
{
    /// <summary>
    /// The frame around every document: the rail in landscape, the bottom bar in portrait and
    /// on the phone, and the header (title, Edit, sun, search, the ⋯ menu).
    /// </summary>
    public static class AppShell
    {
        public static VisualElement Build(AppController app, string title, VisualElement body, bool edit = false)
        {
            var land = app.Layout.Land;
            var root = U.Row().Cls("grow stretch fullsize minh0");
            if (land) root.Add(Rail(app));

            var col = U.Col().Cls("grow minh0");
            col.Add(Header(app, title, edit, back: !land));
            var host = U.Col(body).Cls("grow minh0");
            col.Add(host);
            if (!land) col.Add(TabBar(app));
            root.Add(col);
            return root;
        }

        private static VisualElement Header(AppController app, string title, bool edit, bool back)
        {
            var st = app.State;
            var h = new VisualElement().Cls("hdr");
            if (back) h.Add(U.IconBtn(GlyphKind.ChevronLeft, () => app.Nav(Screen.Library), "ghost back"));
            h.Add(U.Text(title, "t grow"));
            if (edit) h.Add(U.Btn(st.Edit ? "Done" : "Edit", app.ToggleEdit, "sm mr8" + (st.Edit ? " pri" : "")));
            h.Add(U.IconBtn(GlyphKind.Search, () => { st.Search = ""; st.Menu = false; app.RenderKeepFocus("search"); }));
            h.Add(U.IconBtn(GlyphKind.Dots, () => { st.Menu = !st.Menu; app.Render(); }));
            return h;
        }

        private static bool IsOn(AppController app, Screen tab) =>
            app.State.Screen == tab || (tab == app.State.Film.Return && app.State.Screen == Screen.Film);

        private static Button Tab(AppController app, Screen s)
        {
            var b = U.Tap(() => app.Nav(s), "ti", new Glyph(Screens.Glyph(s), 24), U.Text(Screens.Label(s)));
            return b.On(IsOn(app, s));
        }

        private static VisualElement Rail(AppController app)
        {
            var rail = new VisualElement().Cls("rail");
            var t = app.Session.Data.Trip;
            var id = t.Identity;
            var top = string.IsNullOrEmpty(id?.ClientAbbrev) ? "Trips" : id.ClientAbbrev;
            var rest = string.Join(" · ", new[] { id?.ProgramAbbrev, id?.PhaseNumber != null ? "P" + id.PhaseNumber : null }.Where(x => !string.IsNullOrEmpty(x)));
            var ident = U.Tap(() => app.Nav(Screen.Library), "ti ident", U.Text(U.Esc(top)), rest.Length > 0 ? U.Text(U.Esc(rest)) : null);
            rail.Add(ident);
            foreach (var s in Screens.Tabs) rail.Add(Tab(app, s));
            return rail;
        }

        private static VisualElement TabBar(AppController app)
        {
            var bar = new VisualElement().Cls("tabbar");
            foreach (var s in Screens.Tabs) bar.Add(Tab(app, s));
            return bar;
        }

        // ------------------------------------------------------------------ the ⋯ menu

        public static VisualElement Menu(AppController app)
        {
            var st = app.State; var s = app.Session; var d = s.Data;
            void Close() { st.Menu = false; app.Render(); }
            var card = U.Card().Cls("menu");

            void Item(string text, System.Action act) => card.Add(U.Tap(() => { st.Menu = false; act(); }, "item h52", U.Text(text, "bold")));

            Item("Share trip…", () => { app.Render(); app.Transfer.ShareTrip(d, m => app.Toast(m), m => app.Toast(m)); });
            // Every export says which document it is; nothing is just "the shot list".
            foreach (var request in ExportsFor(app))
            {
                var r = request;
                Item(MediaTrip.Export.TripExports.Label(r.Kind) + "…", () => { app.Render(); app.Transfer.Export(s, r, m => app.Toast(m), m => app.Toast(m)); });
            }
            Item("Import…", () => { app.Render(); ImportFlow.Begin(app); });
            Item("Theme…", () => { app.Render(); ThemeSheet.Open(app); });
            if (Shortcuts.HardwareKeyboardPresent) Item("Shortcuts", () => { st.Shortcuts = true; app.Render(); });
            card.style.width = 300;
            return app.Overlay(card, Close, "clear");
        }

        /// <summary>The exports that belong to the screen being looked at.</summary>
        private static List<MediaTrip.Export.ExportRequest> ExportsFor(AppController app)
        {
            var list = new List<MediaTrip.Export.ExportRequest>();
            switch (app.State.Screen)
            {
                case Screen.Trip: list.Add(new MediaTrip.Export.ExportRequest(MediaTrip.Export.ExportKind.Trip)); break;
                case Screen.ShotList:
                    list.Add(new MediaTrip.Export.ExportRequest(MediaTrip.Export.ExportKind.OriginalShotList));
                    list.Add(new MediaTrip.Export.ExportRequest(MediaTrip.Export.ExportKind.WorkingShotList));
                    list.Add(new MediaTrip.Export.ExportRequest(MediaTrip.Export.ExportKind.Changes));
                    break;
                case Screen.Summary: list.Add(new MediaTrip.Export.ExportRequest(MediaTrip.Export.ExportKind.MediaSummary)); break;
                case Screen.Outlines:
                    var b = app.State.OL.Book;
                    if (b != null && app.Session.Data.Outlines.ContainsKey(b)) list.Add(new MediaTrip.Export.ExportRequest(MediaTrip.Export.ExportKind.Outline, b));
                    break;
            }
            return list;
        }
    }
}

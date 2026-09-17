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
            h.Add(SunButton(app));
            h.Add(U.IconBtn(GlyphKind.Search, () => { st.Search = ""; st.Menu = false; app.RenderKeepFocus("search"); }));
            h.Add(U.IconBtn(GlyphKind.Dots, () => { st.Menu = !st.Menu; app.Render(); }));
            return h;
        }

        /// <summary>One tap to the high-contrast sun scheme and back.</summary>
        public static Button SunButton(AppController app) =>
            U.IconBtn(GlyphKind.Sun, () => { app.Theme.ToggleSun(); app.Toast(app.Theme.IsSun ? "Sun mode" : "Sun mode off"); }, app.Theme.IsSun ? "pri" : "ghost");

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
            var doc = DocumentOf(app);
            if (doc != null)
            {
                var (kind, bookId) = doc.Value;
                var name = TripTransfer.DocumentFileName(d, kind, bookId);
                Item("Share " + name.Substring(name.LastIndexOf('/') + 1) + "…", () => { app.Render(); app.Transfer.ShareDocument(d, kind, bookId, m => app.Toast(m), m => app.Toast(m)); });
            }
            Item("Import…", () => { app.Render(); ImportFlow.Begin(app); });
            Item("Theme…", () => { app.Render(); ThemeSheet.Open(app); });
            return app.Overlay(card, Close, "clear");
        }

        /// <summary>The document behind the screen being looked at, for "Share this document".</summary>
        private static (DocumentKind, string)? DocumentOf(AppController app)
        {
            switch (app.State.Screen)
            {
                case Screen.Trip: return (DocumentKind.Trip, null);
                case Screen.ShotList: return (DocumentKind.ShotList, null);
                case Screen.Summary: return (DocumentKind.Captures, null);
                case Screen.Outlines:
                    var b = app.State.OL.Book;
                    return b != null && app.Session.Data.Outlines.ContainsKey(b) ? (DocumentKind.Outline, b) : ((DocumentKind, string)?)null;
                default: return null;
            }
        }
    }
}

using MediaTrip.Model;
using MediaTrip.Status;
using MediaTrip.UI.ViewModels;
using UnityEngine.UIElements;

namespace MediaTrip.UI.Shell
{
    /// <summary>
    /// "Did we get X?" Videos, photos, outline sections and people in one list with status
    /// inline. Results lie over whatever screen is showing and go straight to the item.
    /// </summary>
    public static class SearchSheet
    {
        public static VisualElement Build(AppController app)
        {
            var st = app.State; var s = app.Session; var d = s.Data;
            void Close() { st.Search = null; st.Focus = null; app.Render(); }

            var results = U.Scroll().Mt(10);
            var field = U.Input(st.Search, "Did we get…", null, false, "tall grow");
            field.name = "search";
            field.RegisterValueChangedCallback(e => { st.Search = e.newValue; Fill(app, results); });
            var top = U.Row(field, U.Btn("Close", Close, "sm ml8"));
            top.style.flexShrink = 0;
            Fill(app, results);
            return app.Sheet(680, Close, true, top, results);
        }

        private static void Fill(AppController app, ScrollView host)
        {
            host.Clear();
            var st = app.State; var s = app.Session; var d = s.Data;
            var q = st.Search ?? "";
            if (string.IsNullOrWhiteSpace(q)) return;
            var r = GlobalSearch.Run(s, q, includePeople: true, maxItems: 6);

            foreach (var m in r.Items)
            {
                var it = m.Item;
                host.Add(U.Tap(() => app.OpenVideo(it.Id), "item",
                    U.Num(ShotListView.NumberOf(it)),
                    U.Col(U.Text(it.IsDropped ? U.Strike(it.Title) : U.Esc(it.Title), "title"), U.Sub(U.Esc(Fmt.BookChapter(d, it.BookId, it.ChapterId)))).Cls("grow"),
                    U.St(it.EffectiveStatus, 36)));
            }
            foreach (var m in r.Photos)
            {
                var p = m.Item;
                var sub = Fmt.PhotoWhere(d, p);
                var icon = new Glyph(GlyphKind.Frame, 20); icon.style.marginLeft = 12; icon.style.marginRight = 24;
                host.Add(U.Tap(() => app.Nav(Screen.Covers), "item", icon,
                    U.Col(U.Text(U.Esc(p.Description), "title"), U.Sub(sub)).Cls("grow"),
                    U.StPhoto(p.Status, 36, p.HeroType != HeroType.None)));
            }
            foreach (var m in r.Sections)
            {
                var hit = m.Item;
                host.Add(U.Tap(() => app.OpenSection(hit.BookId, hit.Section.Id), "item",
                    U.Num(U.Esc(hit.Section.Number ?? "")),
                    U.Col(U.Text(U.Esc(hit.Section.Name), "title"), U.Sub("Outline · Ch." + hit.Chapter?.Number)).Cls("grow")));
            }
            foreach (var m in r.People)
            {
                var p = m.Item;
                var icon = new Glyph(GlyphKind.Person, 22); icon.style.marginLeft = 11; icon.style.marginRight = 23;
                host.Add(U.Tap(() => { app.Nav(Screen.Trip); st.Trip.ScrollTo = "people"; app.Render(); }, "item", icon,
                    U.Col(U.Text(U.Esc(p.FullName), "title"), U.Sub(U.Esc(p.Title ?? ""))).Cls("grow")));
            }
            if (r.IsEmpty) host.Add(U.Sub("No match.").Pad(14));
        }
    }
}

using System.Linq;
using MediaTrip.Status;
using UnityEngine.UIElements;

namespace MediaTrip.UI.Field
{
    /// <summary>The printed shot list unfolded: books, then chapters and videos, then the selected video.</summary>
    public static class PlanScreen
    {
        public static VisualElement Build(AppController app)
        {
            var st = app.State;
            var s = app.Session;
            var d = s.Data;
            var q = s.Queries;
            if (st.PlanBook == null || d.FindBook(st.PlanBook) == null) st.PlanBook = d.Trip.Books.FirstOrDefault()?.Id;
            if (st.PlanVideo == null || s.Plan.FindItem(st.PlanVideo) == null) st.PlanVideo = s.Plan.ItemsOfBook(st.PlanBook).FirstOrDefault()?.Id;

            var root = U.Row().Cls("grow stretch");
            root.style.minHeight = 0;

            // ---- books
            var books = new VisualElement().Cls("colA").W(250);
            var bh = new VisualElement().Cls("head"); bh.Add(U.H2("Books")); bh.Add(U.Grow()); bh.Add(U.Sub(s.Queries.Remaining().Count + q.RemainingPhotos().Count + " left"));
            books.Add(bh);
            var bb = U.Scroll().Cls("bodyA");
            foreach (var b in d.Trip.Books)
            {
                var stats = q.BookStats(b.Id);
                var item = U.Tap(() => { st.PlanBook = b.Id; st.PlanVideo = s.Plan.ItemsOfBook(b.Id).FirstOrDefault()?.Id; app.Render(); }, "item vertical mb6",
                    U.Eyebrow("Book " + b.Number), U.H3(U.Esc(b.Name)).Mt(2).Mb(8),
                    U.Row(U.Bar(stats.VideoFraction).Cls("grow"), U.Sub(stats.VideosDone + "/" + stats.VideosTotal).Ml(10).Cls("nowrap")).Cls("fill"));
                item.style.alignItems = Align.FlexStart;
                item.style.justifyContent = Justify.Center;
                item.MinH(92);
                item.On(st.PlanBook == b.Id);
                bb.Add(item);
            }
            books.Add(bb);
            root.Add(books);

            // ---- chapters + videos
            var list = new VisualElement().Cls("colA").W(380);
            var lh = new VisualElement().Cls("head");
            var book = d.FindBook(st.PlanBook);
            lh.Add(U.H2("Book " + (book?.Number.ToString() ?? "?")).Cls("grow"));
            var tog = U.Toggle("Remaining", st.RemainingOnly, () => { st.RemainingOnly = !st.RemainingOnly; app.Render(); }).H(44);
            lh.Add(tog);
            list.Add(lh);
            var lb = U.Scroll().Cls("bodyA");
            bool any = false;
            foreach (var ch in d.ChaptersOf(st.PlanBook))
            {
                var cs = q.ChapterStats(ch.Id);
                var hero = q.ChapterHero(ch.Id);
                var rows = s.Plan.ItemsOfChapter(ch.Id).Where(i => !st.RemainingOnly || (i.Status != PlanItemStatus.Captured && !i.IsDropped && !i.IsSuperseded)).ToList();
                var showHero = hero != null && (!st.RemainingOnly || !hero.IsCaptured);
                if (rows.Count == 0 && !showHero) continue;
                any = true;
                var chap = new VisualElement().Cls("chapA");
                chap.Add(U.Text("CH." + ch.Number + " · " + U.Esc(ch.Name).ToUpperInvariant()).Cls("grow"));
                chap.Add(U.Text(cs.VideosDone + "/" + cs.VideosTotal));
                lb.Add(chap);
                if (showHero)
                {
                    var hb = U.Tap(() => app.TogglePhoto(hero.Id), "item", U.StPhoto(hero.Status, 34, true).Mr(12),
                        U.Col(U.Text("Chapter hero shot", "bold").Font(15), U.Sub(U.Esc(U.ShortDescription(hero.Description)))).Cls("grow"));
                    hb.MinH(52);
                    lb.Add(hb);
                }
                foreach (var i in rows)
                {
                    var inactive = i.IsDropped || i.IsSuperseded;
                    var row = U.Row().Cls("fill");
                    var it = U.Tap(() => { st.PlanVideo = i.Id; app.Render(); }, "item grow",
                        U.Num(i.DisplayNumber),
                        U.Text(inactive ? U.Strike(i.Title) : U.Esc(i.Title), "bold body-sm" + (inactive ? " strike" : "")).Cls("grow"));
                    it.style.paddingLeft = 10;
                    it.On(st.PlanVideo == i.Id);
                    row.Add(it);
                    row.Add(U.StBtn(i.Status, 46, () => app.ToggleItem(i.Id)).Ml(8));
                    lb.Add(row);
                }
            }
            var orphans = s.Plan.ItemsOfBook(st.PlanBook).Where(i => d.FindChapter(i.ChapterId) == null).ToList();
            if (orphans.Count > 0)
            {
                var chap = new VisualElement().Cls("chapA bad"); chap.Add(U.Text("NO CHAPTER")); lb.Add(chap);
                foreach (var i in orphans)
                {
                    var row = U.Row().Cls("fill");
                    row.Add(U.Tap(() => { st.PlanVideo = i.Id; app.Render(); }, "item grow", U.Num(i.DisplayNumber), U.Text(U.Esc(i.Title), "bold").Cls("grow")).On(st.PlanVideo == i.Id));
                    row.Add(U.StBtn(i.Status, 46, () => app.ToggleItem(i.Id)).Ml(8));
                    lb.Add(row);
                }
                any = true;
            }
            if (!any) lb.Add(U.Sub("Nothing remaining in this book.").Pad(20));
            list.Add(lb);
            root.Add(list);

            // ---- detail
            var detail = U.Col().Cls("grow").Pad(20, 22);
            detail.style.height = Length.Percent(100);
            if (st.PlanVideo != null) detail.Add(VideoDetail.Body(app, st.PlanVideo, null, id => { st.PlanVideo = id; app.Render(); }));
            else detail.Add(U.Sub("Select a video."));
            root.Add(detail);
            return root;
        }
    }
}

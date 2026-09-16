using System.Linq;
using MediaTrip.Model;
using MediaTrip.Status;
using MediaTrip.UI.ViewModels;
using UnityEngine.UIElements;

namespace MediaTrip.UI.Field
{
    /// <summary>Today first: the day strip, what was shot in order (the summary being written), what is left, and the plan accordion.</summary>
    public static class TodayScreen
    {
        public static VisualElement Build(AppController app)
        {
            var st = app.State;
            var s = app.Session;
            var d = s.Data;
            var q = s.Queries;
            var root = U.Col().Cls("grow");
            root.style.minHeight = 0;

            // ---- day strip
            var dayb = new VisualElement().Cls("dayb");
            var todayIso = System.DateTime.Now.ToString("yyyy-MM-dd");
            if (d.Trip.Days.Count == 0) dayb.Add(U.Sub("No days yet · the first check-off creates today"));
            foreach (var day in d.Trip.Days)
            {
                var label = (day.Label ?? "Day") + " · " + U.FmtDate(day.Date) + (day.Date == todayIso ? " · today" : "");
                dayb.Add(U.Tap(() => { st.DayId = day.Id; app.Render(); }, "daychip", U.Text(label)).On(st.DayId == day.Id));
            }
            dayb.Add(U.Grow());
            dayb.Add(U.Toggle("Remaining only", st.RemainingOnly, () => { st.RemainingOnly = !st.RemainingOnly; app.Render(); }));
            root.Add(dayb);

            var split = U.Row().Cls("grow stretch");
            split.style.minHeight = 0;

            // ---- today column
            var today = U.Scroll().Cls("todaycol");
            var dayId = st.DayId ?? d.Trip.Days.LastOrDefault()?.Id;
            var summary = dayId != null ? q.DaySummary(dayId) : null;
            if (!st.RemainingOnly && summary != null)
            {
                today.Add(U.Eyebrow("Shot " + (summary.Day?.Label ?? "") + " · in capture order · this is the summary").Mb(10));
                foreach (var e in summary.Entries)
                {
                    var c = e.Capture;
                    var row = U.Tap(() => { if (e.Item != null) app.OpenDetail(e.Item.Id); }, "rowB");
                    row.Add(U.Text(c.CapturedOrder.ToString(), "ordB"));
                    var photos = c.Photos?.Count(p => p.Captured) ?? 0;
                    var sub = Fmt.OriginNote(e.Item, c) + " · " + Fmt.BookChapter(d, c.BookId, c.ChapterId)
                              + (c.People != null && c.People.Count > 0 ? " · " + U.Esc(string.Join(", ", c.People.Select(p => p.Name))) : "")
                              + " · " + U.Plural(photos, "photo")
                              + (string.IsNullOrEmpty(c.Description) && photos == 0 ? " · details pending" : "");
                    row.Add(U.Col(U.H3(U.Esc(c.Title)), U.Sub(sub)).Cls("grow"));
                    row.Add(U.St(PlanItemStatus.Captured, 44));
                    today.Add(row);
                    foreach (var pc in e.PhotosUnder) today.Add(PhotoCaptureRow(app, pc));
                }
                foreach (var pc in summary.AdditionalPhotography) today.Add(PhotoCaptureRow(app, pc));
                if (summary.IsEmpty) today.Add(U.Sub("Nothing logged this day yet.").Mb(16));
            }

            var left = s.Plan.Items.Where(i => !i.IsDropped && !i.IsSuperseded && i.Status != PlanItemStatus.Captured).ToList();
            var leftPhotos = q.RemainingPhotos();
            today.Add(U.Eyebrow("Remaining · " + U.Plural(left.Count, "video") + " · " + U.Plural(leftPhotos.Count, "photo")).Mt(st.RemainingOnly ? 0 : 16).Mb(10));
            foreach (var i in left)
            {
                var row = new VisualElement().Cls("rowB");
                row.Add(U.Check(false, () => app.ToggleItem(i.Id), "", i.Status == PlanItemStatus.PartiallyCaptured ? GlyphKind.Half : GlyphKind.None));
                var ch = d.FindChapter(i.ChapterId);
                var sub = i.DisplayNumber + " · Book " + (d.FindBook(i.BookId)?.Number.ToString() ?? "?") + (ch != null ? " Ch." + ch.Number : " · <color=#c00000>no chapter</color>");
                var sme = Fmt.SmeOf(d, i);
                if (!string.IsNullOrEmpty(sme)) sub += " · " + U.Esc(sme);
                var open = U.Tap(() => app.OpenDetail(i.Id), "grow col ml16", U.H3(U.Esc(i.Title)), U.Sub(sub));
                open.MinH(56);
                open.style.justifyContent = Justify.Center;
                row.Add(open);
                today.Add(row);
            }
            foreach (var p in leftPhotos)
            {
                var row = new VisualElement().Cls("rowB");
                row.Add(U.Check(false, () => app.TogglePhoto(p.Id), "", p.HeroType != HeroType.None ? GlyphKind.Star : GlyphKind.None));
                var ch = d.FindChapter(p.ChapterId);
                var sub = (p.HeroType != HeroType.None ? "★ " + U.HeroLabel(p.Photo, ch) + " · " : "") + "Book " + (d.FindBook(p.BookId)?.Number.ToString() ?? "?") + " · photo";
                row.Add(U.Col(U.H3(U.Esc(p.Description)), U.Sub(sub)).Cls("grow ml16"));
                today.Add(row);
            }
            if (left.Count == 0 && leftPhotos.Count == 0) today.Add(U.Card(U.Sub("Everything on the plan is captured.")));
            split.Add(today);

            // ---- plan accordion
            var plan = U.Scroll().Cls("grow").Pad(16, 20);
            plan.Add(U.Eyebrow("Plan · books › chapters › videos").Mb(10));
            foreach (var b in d.Trip.Books)
            {
                var stats = q.BookStats(b.Id);
                var open = st.OpenBooks.Contains(b.Id);
                var head = U.Tap(() => { if (!st.OpenBooks.Remove(b.Id)) st.OpenBooks.Add(b.Id); app.Render(); }, "planB book",
                    U.Col(U.H3("Book " + b.Number + " · " + U.Esc(b.Name)), U.Sub(stats.VideosDone + "/" + stats.VideosTotal + " videos · " + stats.PhotosDone + "/" + stats.PhotosTotal + " photos")).Cls("grow"),
                    new Glyph(open ? GlyphKind.ChevronUp : GlyphKind.ChevronDown, 22).Cls("muted"));
                plan.Add(head);
                if (!open) continue;
                foreach (var ch in d.ChaptersOf(b.Id))
                {
                    plan.Add(U.Eyebrow("Ch." + ch.Number + " · " + ch.Name).Mt(8).Mb(4).Ml(12));
                    var hero = q.ChapterHero(ch.Id);
                    if (hero != null)
                    {
                        var hb = U.Tap(() => app.TogglePhoto(hero.Id), "planB", U.StPhoto(hero.Status, 32, true).Mr(12), U.Text("Chapter hero", "sub bold").Cls("grow"));
                        hb.H(52);
                        plan.Add(hb);
                    }
                    foreach (var i in s.Plan.ItemsOfChapter(ch.Id))
                    {
                        if (st.RemainingOnly && (i.Status == PlanItemStatus.Captured || i.IsDropped || i.IsSuperseded)) continue;
                        var inactive = i.IsDropped || i.IsSuperseded;
                        var r = U.Tap(() => app.OpenDetail(i.Id), "planB",
                            U.Num(i.DisplayNumber),
                            U.Text(inactive ? U.Strike(i.Title) : U.Esc(i.Title), "bold body-sm" + (inactive ? " strike" : "")).Cls("grow ellipsis"),
                            U.St(i.Status, 34));
                        plan.Add(r);
                    }
                }
            }
            split.Add(plan);
            root.Add(split);
            return root;
        }

        private static VisualElement PhotoCaptureRow(AppController app, PhotoCapture pc)
        {
            var d = app.Session.Data;
            var row = new VisualElement().Cls("rowB").MinH(60);
            row.Add(U.Text(pc.CapturedOrder.ToString(), "ordB photo"));
            var text = pc.PhotoId != null ? d.FindPhoto(pc.PhotoId)?.Description ?? pc.PhotoId : pc.Text;
            row.Add(U.Col(U.Text(U.Esc(text), "bold"), U.Sub("photo · " + (pc.Section == PhotoCaptureSection.UnderVideo ? "under video" : "additional photography"))).Cls("grow"));
            row.Add(U.Btn("Remove", () => { app.Session.RemovePhotoCapture(pc.Id); app.Toast("Photo capture removed"); }, "sm"));
            return row;
        }
    }
}

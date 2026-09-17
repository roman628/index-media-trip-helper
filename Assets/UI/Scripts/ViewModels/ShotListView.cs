using System.Collections.Generic;
using System.Linq;
using MediaTrip.Model;
using MediaTrip.Session;
using MediaTrip.Status;

namespace MediaTrip.UI.ViewModels
{
    public enum ShotListMode { Working, Original, Changes }

    /// <summary>
    /// The shot list as the three views show it. Working is the plan as it now stands (dropped
    /// and superseded items leave, combined and added ones appear). Original is the printed
    /// list, struck through where it changed. Changes is the amendment log in time order.
    /// </summary>
    public sealed class ShotListView
    {
        public sealed class Row
        {
            public PlanItem Item;
            public string Number;
            public string Title;
            public string Sub;
            public PlanItemStatus Status;
            /// <summary>Dropped or superseded: drawn struck through.</summary>
            public bool Dim;
        }

        public sealed class ChapterGroup
        {
            public Chapter Chapter;
            public PhotoItem Hero;
            public List<Row> Rows = new List<Row>();
        }

        public sealed class BookGroup
        {
            public Book Book;
            public List<ChapterGroup> Chapters = new List<ChapterGroup>();
        }

        public sealed class Change
        {
            public Amendment Amendment;
            /// <summary>"Combined", "Renamed", ...</summary>
            public string Label;
            /// <summary>Pill style: "bad" for drop, "ok" for add, "ac" otherwise.</summary>
            public string Style;
            /// <summary>Rich text: "#1 + #2 → <b>New title</b>".</summary>
            public string Line;
            public string Reason;
            /// <summary>The plan item to open when the change is tapped.</summary>
            public string OpenItemId;
        }

        public List<BookGroup> Books = new List<BookGroup>();
        public List<Change> Changes = new List<Change>();

        /// <param name="keepEmpty">Edit state: keep chapters and books with nothing in them so they can be added to.</param>
        public static ShotListView Build(TripSession s, ShotListMode mode, bool hideDone, bool keepEmpty = false)
        {
            var view = new ShotListView();
            if (mode == ShotListMode.Changes) { view.Changes = BuildChanges(s); return view; }

            var d = s.Data;
            var original = mode == ShotListMode.Original;
            foreach (var book in d.Trip.Books.OrderBy(b => b.Number))
            {
                var bg = new BookGroup { Book = book };
                foreach (var ch in d.ChaptersOf(book.Id))
                {
                    var cg = new ChapterGroup { Chapter = ch };
                    IEnumerable<PlanItem> items = original
                        ? s.Plan.Items.Where(i => i.Origin == PlanItemOrigin.Planned && (i.OriginalChapterId ?? i.ChapterId) == ch.Id)
                        : s.Plan.ItemsOfChapter(ch.Id).Where(i => !i.IsDropped && !i.IsSuperseded);
                    foreach (var it in items)
                    {
                        if (hideDone && (it.Status == PlanItemStatus.Captured || it.IsDropped || it.IsSuperseded)) continue;
                        cg.Rows.Add(RowOf(d, it, original));
                    }
                    var hero = s.Queries.ChapterHero(ch.Id);
                    if (hero != null && !(hideDone && hero.Status != PhotoStatus.NotCaptured)) cg.Hero = hero;
                    if (cg.Rows.Count > 0 || cg.Hero != null || keepEmpty) bg.Chapters.Add(cg);
                }
                if (bg.Chapters.Count > 0 || keepEmpty) view.Books.Add(bg);
            }
            return view;
        }

        public bool IsEmpty => Books.Count == 0 && Changes.Count == 0;

        public static Row RowOf(TripData d, PlanItem it, bool original)
        {
            var sub = Fmt.SmeOf(d, it);
            if (it.Origin == PlanItemOrigin.Added) sub = (sub.Length > 0 ? sub + " · " : "") + "added on site";
            return new Row
            {
                Item = it,
                Number = NumberOf(it),
                Title = original ? (it.OriginalTitle ?? it.Title) : it.Title,
                Sub = sub,
                Status = it.Status,
                Dim = it.IsDropped || it.IsSuperseded,
            };
        }

        /// <summary>"4", "1+2", "5a", and "+" for something added on site.</summary>
        public static string NumberOf(PlanItem it) => it.Origin == PlanItemOrigin.Added ? "+" : it.DisplayNumber;

        private static List<Change> BuildChanges(TripSession s)
        {
            var d = s.Data;
            var list = new List<Change>();
            foreach (var a in PlanResolver.OrderedAmendments(d.Captures.Amendments))
            {
                var targets = (a.Targets ?? new List<string>()).Select(t => s.Plan.FindItem(t)).Where(x => x != null).ToList();
                string Num(PlanItem i) => "#" + NumberOf(i);
                var title = "<b>" + Esc(a.NewTitle) + "</b>";
                var c = new Change { Amendment = a, Reason = a.Reason, Style = "ac" };
                switch (a.Type)
                {
                    case AmendmentType.Combine:
                        c.Label = "Combined";
                        c.Line = string.Join(" + ", targets.Select(Num)) + " → " + title;
                        c.OpenItemId = a.Results?.FirstOrDefault();
                        break;
                    case AmendmentType.Split:
                        c.Label = "Split";
                        c.Line = string.Join(", ", targets.Select(Num)) + " → " + string.Join(" · ", (a.NewTitles ?? new List<string>()).Select(t => "<b>" + Esc(t) + "</b>"));
                        c.OpenItemId = a.Results?.FirstOrDefault();
                        break;
                    case AmendmentType.Rename:
                        c.Label = "Renamed";
                        c.Line = string.Join(", ", targets.Select(Num)) + " → " + title;
                        c.OpenItemId = targets.FirstOrDefault()?.Id;
                        break;
                    case AmendmentType.Move:
                        c.Label = "Moved";
                        c.Line = string.Join(", ", targets.Select(Num)) + " → " + Esc(Fmt.BookChapter(d, a.NewBookId, a.NewChapterId));
                        c.OpenItemId = targets.FirstOrDefault()?.Id;
                        break;
                    case AmendmentType.Add:
                        c.Label = "Added";
                        c.Style = "ok";
                        c.Line = title + " · " + Esc(Fmt.BookChapter(d, a.NewBookId, a.NewChapterId));
                        c.OpenItemId = a.Results?.FirstOrDefault();
                        break;
                    case AmendmentType.Drop:
                        c.Label = "Dropped";
                        c.Style = "bad";
                        c.Line = string.Join(", ", targets.Select(t => Num(t) + " <s>" + Esc(t.OriginalTitle ?? t.Title) + "</s>"));
                        c.OpenItemId = targets.FirstOrDefault()?.Id;
                        break;
                }
                if (string.IsNullOrEmpty(c.Line)) c.Line = Esc(a.NewTitle ?? a.Id);
                list.Add(c);
            }
            return list;
        }

        private static string Esc(string s) => (s ?? "").Replace("<", "‹").Replace(">", "›");
    }
}

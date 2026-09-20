using System.Collections.Generic;
using System.Linq;
using MediaTrip.Authoring;
using MediaTrip.Model;
using MediaTrip.Session;
using MediaTrip.Status;
using Newtonsoft.Json.Linq;

namespace MediaTrip.UI.ViewModels
{
    public enum ShotListMode { Working, Original, Changes }

    /// <summary>
    /// The shot list as the three views show it. Working is the plan as it now stands (dropped
    /// and superseded items leave; combined, added and revised ones appear). Original is the
    /// printed list, struck through where it changed. Changes is the record: what changed during
    /// the trip, and, under their own heading, corrections made to the original.
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
            /// <summary>Small tags after the title: "new", "revised", "was 4", "was named …".</summary>
            public List<string> Tags = new List<string>();
        }

        public sealed class PhotoRow
        {
            public PhotoItem Photo;
            public string Text;
            /// <summary>"Cover", "Ch.2 hero", or null for a detail.</summary>
            public string Use;
            public bool Dim;
            public List<string> Tags = new List<string>();
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
            public List<PhotoRow> Photos = new List<PhotoRow>();
        }

        public sealed class Change
        {
            public Amendment Amendment;
            public ChangeRecord Record;
            /// <summary>"Combined", "Renamed", "Revised", "Corrected", ...</summary>
            public string Label;
            /// <summary>Pill style: "bad" for drop, "ok" for add, "ac" otherwise.</summary>
            public string Style;
            /// <summary>Rich text: "#1 + #2 → <b>New title</b>".</summary>
            public string Line;
            public string Reason;
            public string At;
            /// <summary>The plan item to open when the change is tapped.</summary>
            public string OpenItemId;
        }

        public List<BookGroup> Books = new List<BookGroup>();
        /// <summary>What changed during the trip, in time order.</summary>
        public List<Change> Changes = new List<Change>();
        /// <summary>Fixes to the original (the app and the paper disagreed). Never mixed with real changes.</summary>
        public List<Change> Corrections = new List<Change>();

        public int ChangeCount => Changes.Count + Corrections.Count;

        /// <param name="keepEmpty">Edit state: keep chapters and books with nothing in them so they can be added to.</param>
        public static ShotListView Build(TripSession s, ShotListMode mode, bool hideDone, bool keepEmpty = false)
        {
            var view = new ShotListView();
            if (mode == ShotListMode.Changes) { BuildChanges(s, view); return view; }

            var d = s.Data;
            var original = mode == ShotListMode.Original;
            foreach (var book in d.Trip.Books)
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
                        cg.Rows.Add(RowOf(s, it, original));
                    }
                    var hero = s.Queries.ChapterHero(ch.Id);
                    if (hero != null && !(hideDone && hero.Status != PhotoStatus.NotCaptured)) cg.Hero = hero;
                    // a chapter with no planned media is not shown in the read state
                    if (cg.Rows.Count > 0 || cg.Hero != null || keepEmpty) bg.Chapters.Add(cg);
                }
                foreach (var p in original ? s.Plan.Photos.Where(p => !p.IsNew && p.Planned.BookId == book.Id) : s.Queries.PhotosOfBook(book.Id, false).Where(p => p.Status != PhotoStatus.Dropped))
                {
                    if (hideDone && p.Status != PhotoStatus.NotCaptured) continue;
                    bg.Photos.Add(PhotoRowOf(d, p, original));
                }
                if (bg.Chapters.Count > 0 || bg.Photos.Count > 0 || keepEmpty) view.Books.Add(bg);
            }
            return view;
        }

        public bool IsEmpty => Books.Count == 0 && Changes.Count == 0 && Corrections.Count == 0;

        public static Row RowOf(TripSession s, PlanItem it, bool original)
        {
            var d = s.Data;
            var sme = original && it.PlannedVideo != null
                ? string.Join(", ", (it.PlannedVideo.SmeIds ?? new List<string>()).Select(d.FindPerson).Where(p => p != null).Select(p => p.FullName).DefaultIfEmpty(it.PlannedVideo.SmeText ?? ""))
                : Fmt.SmeOf(d, it);
            var row = new Row
            {
                Item = it,
                Number = NumberOf(it),
                Title = original ? (it.OriginalTitle ?? it.Title) : it.Title,
                Sub = sme ?? "",
                Status = it.Status,
                Dim = it.IsDropped || it.IsSuperseded,
            };
            if (it.Origin == PlanItemOrigin.Added) row.Tags.Add("new");
            if (!original && it.Amendments.Any(a => a.Type == AmendmentType.Revise)) row.Tags.Add("revised");
            if (it.PlannedVideo != null)
            {
                var wasN = PlanEdits.WasNumber(s, it.Id);
                if (wasN != null) row.Tags.Add("was " + wasN);
                var wasT = PlanEdits.WasTitle(s, it.Id);
                if (wasT != null) row.Tags.Add("was named " + wasT);
            }
            if (!original && it.Origin == PlanItemOrigin.Planned && it.OriginalTitle != it.Title) row.Tags.Add("was named " + it.OriginalTitle);
            return row;
        }

        public static PhotoRow PhotoRowOf(TripData d, PhotoItem p, bool original)
        {
            var photo = original ? p.Planned : p.Photo;
            var ch = d.FindChapter(photo.ChapterId);
            var row = new PhotoRow
            {
                Photo = p,
                Text = original ? p.OriginalDescription : p.Description,
                Use = photo.HeroType == HeroType.BookCover ? "Cover" : photo.HeroType == HeroType.ChapterHero ? "Ch." + (ch?.Number.ToString() ?? "?") + " hero" : null,
                Dim = original && p.Status == PhotoStatus.Dropped,
            };
            if (p.IsNew) row.Tags.Add("new");
            if (!original && p.WasMoved) row.Tags.Add("moved from " + (d.FindBook(p.Planned.BookId)?.Name ?? "another book"));
            if (!original && !p.IsNew && p.Description != p.OriginalDescription) row.Tags.Add("was " + p.OriginalDescription);
            return row;
        }

        /// <summary>"4", "1+2", "5a", and "+" for something added on site.</summary>
        public static string NumberOf(PlanItem it) => it.Origin == PlanItemOrigin.Added ? "+" : it.DisplayNumber;

        /// <summary>How a video is referred to in a sentence: "#4", "#1+2", and "new" for something added on site (it has no number).</summary>
        public static string Ref(PlanItem it) => it.Origin == PlanItemOrigin.Added ? "new" : "#" + it.DisplayNumber;

        // ------------------------------------------------------------------ changes

        private static void BuildChanges(TripSession s, ShotListView view)
        {
            var d = s.Data;
            string Name(string id)
            {
                var item = s.Plan.FindItem(id);
                if (item != null) return item.Origin == PlanItemOrigin.Added ? "“" + Esc(item.OriginalTitle) + "”" : Ref(item);
                var photo = s.Plan.FindPhoto(id);
                return photo != null ? "photo “" + Esc(photo.OriginalDescription) + "”" : Esc(id);
            }
            foreach (var a in PlanResolver.OrderedAmendments(d.Captures.Amendments))
            {
                var ids = a.Targets ?? new List<string>();
                var names = string.Join(", ", ids.Select(Name));
                var title = "<b>" + Esc(a.NewTitle) + "</b>";
                var c = new Change { Amendment = a, Reason = a.Reason, At = a.At, Style = "ac", OpenItemId = ids.FirstOrDefault(id => s.Plan.FindItem(id) != null) };
                switch (a.Type)
                {
                    case AmendmentType.Combine:
                        c.Label = "Combined"; c.Line = string.Join(" + ", ids.Select(Name)) + " → " + title; c.OpenItemId = a.Results?.FirstOrDefault(); break;
                    case AmendmentType.Split:
                        c.Label = "Split"; c.Line = names + " → " + string.Join(" · ", (a.NewTitles ?? new List<string>()).Select(t => "<b>" + Esc(t) + "</b>")); c.OpenItemId = a.Results?.FirstOrDefault(); break;
                    case AmendmentType.Rename:
                        c.Label = "Renamed"; c.Line = names + " → " + title; break;
                    case AmendmentType.Move:
                        c.Label = "Moved"; c.Line = names + " → " + Esc(Fmt.BookChapter(d, a.NewBookId, a.NewChapterId)); break;
                    case AmendmentType.Add:
                        c.Label = a.NewMedia == MediaKind.Photo ? "New photo" : "New video"; c.Style = "ok";
                        c.Line = title + " · " + Esc(Fmt.BookChapter(d, a.NewBookId, a.NewChapterId));
                        c.OpenItemId = a.NewMedia == MediaKind.Photo ? null : a.Results?.FirstOrDefault(); break;
                    case AmendmentType.Drop:
                        c.Label = "Dropped"; c.Style = "bad";
                        c.Line = string.Join(", ", ids.Select(id => { var it = s.Plan.FindItem(id); return it != null ? "#" + NumberOf(it) + " <s>" + Esc(it.OriginalTitle ?? it.Title) + "</s>" : "<s>" + Name(id) + "</s>"; }));
                        break;
                    case AmendmentType.Revise:
                        c.Label = "Revised";
                        c.Line = names + " · " + string.Join(" · ", (a.Changes ?? new List<FieldChange>()).Select(f => Describe(d, f.Field, f.Before, f.After)));
                        break;
                }
                if (string.IsNullOrEmpty(c.Line)) c.Line = Esc(a.NewTitle ?? a.Id);
                view.Changes.Add(c);
            }
            foreach (var e in d.Captures.Edits.OrderBy(e => PlanResolver.ParseTimestamp(e.At) ?? System.DateTimeOffset.MaxValue))
            {
                var what = Esc(string.IsNullOrEmpty(e.Label) ? e.Entity : e.Label);
                var c = new Change
                {
                    Record = e, At = e.At, Style = e.Kind == ChangeRecordKind.Correction ? "" : "ac",
                    Label = e.Field == FieldChange.Deleted ? "Deleted" : e.Field == FieldChange.Added ? "Added" : e.Kind == ChangeRecordKind.Correction ? "Corrected" : "Changed",
                    Line = e.Field == FieldChange.Deleted ? Esc(e.Entity) + " <s>" + what + "</s>" + (e.After != null && e.After.Type == JTokenType.String ? " · " + Esc((string)e.After) : "")
                        : e.Field == FieldChange.Added ? "New " + Esc(e.Entity)
                        : Esc(e.Entity) + " “" + what + "” · " + Describe(d, e.Field, e.Before, e.After),
                    OpenItemId = e.Entity == "video" && s.Plan.FindItem(e.EntityId) != null ? e.EntityId : null,
                };
                (e.Kind == ChangeRecordKind.Correction ? view.Corrections : view.Changes).Add(c);
            }
            view.Changes = view.Changes.OrderBy(c => PlanResolver.ParseTimestamp(c.At) ?? System.DateTimeOffset.MaxValue).ToList();
        }

        /// <summary>"scene: “before” → “after”", "notes: 3 → 4 bullets", "SME: Terry → Nina".</summary>
        public static string Describe(TripData d, string field, JToken before, JToken after)
        {
            string Label()
            {
                switch (field)
                {
                    case FieldChange.Title: return "title";
                    case FieldChange.Scene: return "scene";
                    case FieldChange.SmeIds: case FieldChange.SmeText: return "SME";
                    case FieldChange.Notes: return "notes";
                    case FieldChange.PhotoRefs: return "photos";
                    case FieldChange.Description: return "description";
                    case FieldChange.Name: return "name";
                    case FieldChange.Number: return "number";
                    case FieldChange.Chapter: return "chapter";
                    case FieldChange.HeroType: return "use";
                    default: return field;
                }
            }
            string Value(JToken t)
            {
                if (t == null || t.Type == JTokenType.Null) return "nothing";
                if (t is JArray arr)
                {
                    if (field == FieldChange.Notes) return arr.Count + (arr.Count == 1 ? " bullet" : " bullets");
                    var names = arr.Select(x => (string)x).Select(id =>
                        field == FieldChange.SmeIds ? d.FindPerson(id)?.FullName ?? id
                        : field == FieldChange.PhotoRefs ? ShortPhoto(d, id) : id).ToList();
                    return names.Count == 0 ? "none" : string.Join(", ", names);
                }
                if (field == FieldChange.Chapter) { var ch = d.FindChapter((string)t); return ch != null ? "Ch." + ch.Number + " " + ch.Name : (string)t; }
                var s = t.ToString();
                if (s.Length == 0) return "nothing";
                return field == FieldChange.Number ? s : "“" + (s.Length > 60 ? s.Substring(0, 57) + "…" : s) + "”";
            }
            return Label() + ": " + Esc(Value(before)) + " → <b>" + Esc(Value(after)) + "</b>";
        }

        private static string ShortPhoto(TripData d, string id)
        {
            var p = d.FindPhoto(id);
            if (p != null) return Trim(p.Description);
            var am = d.Captures.Amendments.FirstOrDefault(a => a.Type == AmendmentType.Add && (a.Results?.Contains(id) ?? false));
            return Trim(am?.NewTitle ?? id);
        }

        private static string Trim(string s) => string.IsNullOrEmpty(s) ? "" : s.Length > 40 ? s.Substring(0, 37) + "…" : s;

        private static string Esc(string s) => (s ?? "").Replace("<", "‹").Replace(">", "›");
    }
}

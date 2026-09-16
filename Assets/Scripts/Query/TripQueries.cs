using System;
using System.Collections.Generic;
using System.Linq;
using MediaTrip.Model;
using MediaTrip.Status;

namespace MediaTrip.Query
{
    /// <summary>A hero shot (book cover or chapter hero) with its status, for the Heroes view.</summary>
    public class HeroItem
    {
        public PhotoItem Photo;
        public Book Book;
        /// <summary>Shot-list chapter for chapter heroes; null for book covers.</summary>
        public Chapter Chapter;
        public PhotoStatus Status => Photo.Status;
        public bool IsCaptured => Photo.IsCaptured;
        public string Label =>
            Photo.HeroType == HeroType.BookCover
                ? "Book cover"
                : Chapter != null ? $"Ch.{Chapter.Number} hero" : "Chapter hero";
        public override string ToString() => $"{Book?.Name}: {Label} ({Status})";
    }

    /// <summary>One video capture in a day's timeline with the photo captures listed under it.</summary>
    public class DayEntry
    {
        public Capture Capture;
        /// <summary>The plan item the capture references; null for unplanned/unmatched.</summary>
        public PlanItem Item;
        /// <summary>Photo captures placed under this video, in capture order.</summary>
        public List<PhotoCapture> PhotosUnder = new List<PhotoCapture>();
    }

    /// <summary>A day of the trip as the Media Trip Summary lays it out.</summary>
    public class DaySummary
    {
        public Day Day;
        public List<DayEntry> Entries = new List<DayEntry>();
        /// <summary>Photo captures in the "Additional photography" section, in capture order.</summary>
        public List<PhotoCapture> AdditionalPhotography = new List<PhotoCapture>();
        public bool IsEmpty => Entries.Count == 0 && AdditionalPhotography.Count == 0;
    }

    /// <summary>A piece of media assigned to an outline location, resolved for display.</summary>
    public class AssignedMedia
    {
        public OutlineAssignment Assignment;
        public MediaRefKind Kind => Assignment.MediaRef?.Kind ?? MediaRefKind.Capture;
        public Capture Capture;
        public PhotoCapture PhotoCapture;
        public PhotoItem PlannedPhoto;
        public string Title;
        public bool Confirmed => Assignment.Confirmed;
        public bool IsAutoUnconfirmed => Assignment.Source == AssignmentSource.Auto && !Assignment.Confirmed;
        public bool IsDangling => Capture == null && PhotoCapture == null && PlannedPhoto == null;
    }

    public class SectionCoverage
    {
        public OutlineSection Section;
        public List<AssignedMedia> Assigned = new List<AssignedMedia>();
        /// <summary>Assignments pinned to a specific bullet inside the section.</summary>
        public List<AssignedMedia> AssignedToNodes = new List<AssignedMedia>();
        public IEnumerable<AssignedMedia> All => Assigned.Concat(AssignedToNodes);
        public bool IsEmpty => Assigned.Count == 0 && AssignedToNodes.Count == 0;
        public bool HasConfirmed => All.Any(a => a.Confirmed);
        public bool OnlyUnconfirmed => !IsEmpty && !HasConfirmed;
    }

    public class ChapterCoverage
    {
        public OutlineChapter Chapter;
        /// <summary>Assignments at chapter level (no section).</summary>
        public List<AssignedMedia> AssignedToChapter = new List<AssignedMedia>();
        public List<SectionCoverage> Sections = new List<SectionCoverage>();
        public int SectionsCovered => Sections.Count(s => !s.IsEmpty);
        public int SectionsEmpty => Sections.Count(s => s.IsEmpty);
    }

    public class BookCoverage
    {
        public Book Book;
        /// <summary>Null when the book has no outline file yet.</summary>
        public OutlineDocument Outline;
        public bool HasOutline => Outline != null;
        public List<ChapterCoverage> Chapters = new List<ChapterCoverage>();
        /// <summary>Assignments to this book whose chapter/section could not be found in the outline.</summary>
        public List<AssignedMedia> Unplaced = new List<AssignedMedia>();
        public int SectionsCovered => Chapters.Sum(c => c.SectionsCovered);
        public int SectionsTotal => Chapters.Sum(c => c.Sections.Count);
    }

    /// <summary>
    /// The read API the UI sits on. Every view is a filter over the same resolved plan; nothing
    /// here mutates data. Construct once per data change (cheap) or use TripSession.Queries,
    /// which caches until the next mutation.
    /// </summary>
    public class TripQueries
    {
        public TripData Data { get; }
        public ResolvedPlan Plan { get; }

        public TripQueries(TripData data, ResolveOptions options = null)
            : this(data, PlanResolver.Resolve(data, options)) { }

        public TripQueries(TripData data, ResolvedPlan plan)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));
            Plan = plan ?? throw new ArgumentNullException(nameof(plan));
        }

        // ------------------------------------------------------------------
        // Shot list views
        // ------------------------------------------------------------------

        /// <summary>The full plan as printed, with amendments applied and superseded items still listed.</summary>
        public IReadOnlyList<PlanItem> FullShotList() => Plan.Items;

        /// <summary>Full list without the items that were replaced by a combine/split.</summary>
        public List<PlanItem> CurrentShotList() => Plan.Items.Where(i => !i.IsSuperseded).ToList();

        /// <summary>Items still needing work: not captured, or partially captured. Superseded and dropped items are excluded.</summary>
        public List<PlanItem> Remaining() =>
            Plan.Items.Where(i => i.Status == PlanItemStatus.NotCaptured || i.Status == PlanItemStatus.PartiallyCaptured).ToList();

        /// <summary>Items fully captured.</summary>
        public List<PlanItem> Captured() => ByStatus(PlanItemStatus.Captured);

        public List<PlanItem> Dropped() => ByStatus(PlanItemStatus.Dropped);

        public List<PlanItem> ByStatus(PlanItemStatus status) => Plan.Items.Where(i => i.Status == status).ToList();

        public PlanItem Item(string id) => Plan.FindItem(id);

        /// <summary>What a planned ID resolves to after combines/splits (the item itself if untouched).</summary>
        public IReadOnlyList<PlanItem> Resolve(string id) => Plan.Resolve(id);

        public List<Capture> CapturesFor(string planId) => Plan.CapturesFor(planId);

        /// <summary>Items grouped book -> chapter, in book/chapter number order. Items whose chapter is unknown land under a null chapter.</summary>
        public List<(Book book, List<(Chapter chapter, List<PlanItem> items)> chapters)> ShotListByBook(IEnumerable<PlanItem> items = null)
        {
            items = items ?? CurrentShotList();
            var list = items.ToList();
            var result = new List<(Book, List<(Chapter, List<PlanItem>)>)>();
            foreach (var book in Data.Trip.Books.OrderBy(b => b.Number))
            {
                var chapters = new List<(Chapter, List<PlanItem>)>();
                foreach (var ch in Data.ChaptersOf(book.Id))
                {
                    var inChapter = list.Where(i => i.ChapterId == ch.Id).ToList();
                    if (inChapter.Count > 0) chapters.Add((ch, inChapter));
                }
                var orphans = list.Where(i => i.BookId == book.Id && (i.ChapterId == null || Data.FindChapter(i.ChapterId) == null)).ToList();
                if (orphans.Count > 0) chapters.Add((null, orphans));
                if (chapters.Count > 0) result.Add((book, chapters));
            }
            return result;
        }

        // ------------------------------------------------------------------
        // Photos and heroes
        // ------------------------------------------------------------------

        public IReadOnlyList<PhotoItem> AllPhotos() => Plan.Photos;

        public PhotoItem Photo(string id) => Plan.FindPhoto(id);

        /// <summary>Master photo list for one book in its printed order.</summary>
        public List<PhotoItem> PhotosOfBook(string bookId) =>
            Plan.Photos.Where(p => p.BookId == bookId).OrderBy(p => p.Photo.Order).ToList();

        /// <summary>Every book cover and chapter hero across all books, with status, in book then photo order.</summary>
        public List<HeroItem> Heroes()
        {
            var bookOrder = Data.Trip.Books.Select((b, i) => (b.Id, i)).ToDictionary(t => t.Id, t => t.i);
            return Plan.Photos
                .Where(p => p.HeroType != HeroType.None)
                .OrderBy(p => bookOrder.TryGetValue(p.BookId ?? "", out var i) ? i : int.MaxValue)
                .ThenBy(p => p.HeroType == HeroType.BookCover ? 0 : 1)
                .ThenBy(p => p.Photo.Order)
                .Select(p => new HeroItem { Photo = p, Book = Data.FindBook(p.BookId), Chapter = Data.FindChapter(p.ChapterId) })
                .ToList();
        }

        public List<HeroItem> RemainingHeroes() => Heroes().Where(h => h.Status == PhotoStatus.NotCaptured).ToList();

        public (int captured, int total) HeroProgress()
        {
            var heroes = Heroes().Where(h => h.Status != PhotoStatus.Dropped).ToList();
            return (heroes.Count(h => h.IsCaptured), heroes.Count);
        }

        // ------------------------------------------------------------------
        // Days (the summary)
        // ------------------------------------------------------------------

        /// <summary>Video captures for a day in capture order.</summary>
        public List<Capture> DayCaptures(string dayId) =>
            Data.Captures.Captures
                .Select((c, i) => (c, i))
                .Where(t => t.c.DayId == dayId)
                .OrderBy(t => t.c.CapturedOrder).ThenBy(t => t.i)
                .Select(t => t.c).ToList();

        public List<PhotoCapture> DayPhotoCaptures(string dayId) =>
            Data.Captures.PhotoCaptures
                .Select((c, i) => (c, i))
                .Where(t => t.c.DayId == dayId)
                .OrderBy(t => t.c.CapturedOrder).ThenBy(t => t.i)
                .Select(t => t.c).ToList();

        /// <summary>
        /// A day laid out as the summary prints it: captures in order, each with the photo
        /// captures filed under it, then the "Additional photography" section.
        /// An "underVideo" photo capture with no afterCaptureId is filed under the last capture
        /// whose order precedes it.
        /// </summary>
        public DaySummary DaySummary(string dayId)
        {
            var summary = new DaySummary { Day = Data.FindDay(dayId) };
            var captures = DayCaptures(dayId);
            var byId = new Dictionary<string, DayEntry>();
            foreach (var c in captures)
            {
                var entry = new DayEntry { Capture = c, Item = Plan.FindItem(c.PlanVideoId) };
                summary.Entries.Add(entry);
                if (!string.IsNullOrEmpty(c.Id)) byId[c.Id] = entry;
            }

            foreach (var pc in DayPhotoCaptures(dayId))
            {
                if (pc.Section == PhotoCaptureSection.AdditionalPhotography)
                {
                    summary.AdditionalPhotography.Add(pc);
                    continue;
                }
                DayEntry target = null;
                if (!string.IsNullOrEmpty(pc.AfterCaptureId)) byId.TryGetValue(pc.AfterCaptureId, out target);
                if (target == null)
                    target = summary.Entries.LastOrDefault(e => e.Capture.CapturedOrder <= pc.CapturedOrder);
                if (target == null) summary.AdditionalPhotography.Add(pc);
                else target.PhotosUnder.Add(pc);
            }
            return summary;
        }

        /// <summary>Every trip day in date order, plus a trailing pseudo-day for captures on unknown day IDs.</summary>
        public List<DaySummary> AllDays()
        {
            var days = Data.Trip.Days.OrderBy(d => d.Date ?? "", StringComparer.Ordinal).ToList();
            var result = days.Select(d => DaySummary(d.Id)).ToList();
            var known = new HashSet<string>(days.Select(d => d.Id));
            var strayDayIds = Data.Captures.Captures.Select(c => c.DayId)
                .Concat(Data.Captures.PhotoCaptures.Select(p => p.DayId))
                .Where(id => !known.Contains(id ?? "")).Distinct().ToList();
            foreach (var id in strayDayIds)
            {
                var s = DaySummary(id);
                s.Day = new Day { Id = id, Label = "(unknown day " + (id ?? "null") + ")" };
                result.Add(s);
            }
            return result;
        }

        /// <summary>The day whose ISO date matches, or null.</summary>
        public Day DayFor(DateTime date)
        {
            var iso = date.ToString("yyyy-MM-dd");
            return Data.Trip.Days.FirstOrDefault(d => d.Date == iso);
        }

        public DaySummary Today(DateTime now)
        {
            var day = DayFor(now);
            return day == null ? null : DaySummary(day.Id);
        }

        /// <summary>Next free capturedOrder for a day (max + 1 across captures and photo captures).</summary>
        public int NextCapturedOrder(string dayId)
        {
            int max = 0;
            foreach (var c in Data.Captures.Captures) if (c.DayId == dayId && c.CapturedOrder > max) max = c.CapturedOrder;
            foreach (var p in Data.Captures.PhotoCaptures) if (p.DayId == dayId && p.CapturedOrder > max) max = p.CapturedOrder;
            return max + 1;
        }

        // ------------------------------------------------------------------
        // Outline coverage
        // ------------------------------------------------------------------

        public AssignedMedia ResolveAssignment(OutlineAssignment a)
        {
            var media = new AssignedMedia { Assignment = a };
            var kind = a.MediaRef?.Kind;
            var id = a.MediaRef?.Id;
            switch (kind)
            {
                case MediaRefKind.Capture:
                    media.Capture = Data.FindCapture(id);
                    media.Title = media.Capture?.Title;
                    break;
                case MediaRefKind.PhotoCapture:
                    media.PhotoCapture = Data.FindPhotoCapture(id);
                    if (media.PhotoCapture != null)
                    {
                        var planned = Plan.FindPhoto(media.PhotoCapture.PhotoId);
                        media.Title = !string.IsNullOrEmpty(media.PhotoCapture.Text) ? media.PhotoCapture.Text : planned?.Description;
                    }
                    break;
                case MediaRefKind.PlannedPhoto:
                    media.PlannedPhoto = Plan.FindPhoto(id);
                    media.Title = media.PlannedPhoto?.Description;
                    break;
            }
            if (media.Title == null) media.Title = "(missing " + kind + " " + id + ")";
            return media;
        }

        public List<AssignedMedia> AssignmentsFor(MediaRefKind kind, string mediaId) =>
            Data.Captures.OutlineAssignments
                .Where(a => a.MediaRef != null && a.MediaRef.Kind == kind && a.MediaRef.Id == mediaId)
                .Select(ResolveAssignment).ToList();

        /// <summary>Which sections of a book's outline have media assigned and which are empty.</summary>
        public BookCoverage OutlineCoverage(string bookId)
        {
            var coverage = new BookCoverage { Book = Data.FindBook(bookId), Outline = Data.FindOutline(bookId) };
            var assignments = Data.Captures.OutlineAssignments.Where(a => a.BookId == bookId).Select(ResolveAssignment).ToList();

            if (coverage.Outline == null)
            {
                coverage.Unplaced.AddRange(assignments);
                return coverage;
            }

            var sectionById = new Dictionary<string, SectionCoverage>();
            var chapterById = new Dictionary<string, ChapterCoverage>();
            foreach (var ch in coverage.Outline.Chapters)
            {
                var cc = new ChapterCoverage { Chapter = ch };
                coverage.Chapters.Add(cc);
                if (ch.Id != null) chapterById[ch.Id] = cc;
                foreach (var s in ch.Sections)
                {
                    var sc = new SectionCoverage { Section = s };
                    cc.Sections.Add(sc);
                    if (s.Id != null) sectionById[s.Id] = sc;
                }
            }

            foreach (var a in assignments)
            {
                if (!string.IsNullOrEmpty(a.Assignment.SectionId) && sectionById.TryGetValue(a.Assignment.SectionId, out var sc))
                {
                    if (!string.IsNullOrEmpty(a.Assignment.NodeId)) sc.AssignedToNodes.Add(a); else sc.Assigned.Add(a);
                }
                else if (!string.IsNullOrEmpty(a.Assignment.ChapterId) && chapterById.TryGetValue(a.Assignment.ChapterId, out var cc))
                {
                    cc.AssignedToChapter.Add(a);
                }
                else
                {
                    coverage.Unplaced.Add(a);
                }
            }
            return coverage;
        }

        /// <summary>Coverage for every book, in book order (books without an outline included with HasOutline = false).</summary>
        public List<BookCoverage> OutlineCoverageAll() =>
            Data.Trip.Books.OrderBy(b => b.Number).Select(b => OutlineCoverage(b.Id)).ToList();

        /// <summary>Captures and photo captures that have no outline assignment at all.</summary>
        public (List<Capture> captures, List<PhotoCapture> photoCaptures) UnassignedMedia()
        {
            var assignedCaptures = new HashSet<string>();
            var assignedPhotoCaptures = new HashSet<string>();
            foreach (var a in Data.Captures.OutlineAssignments)
            {
                if (a.MediaRef == null || a.MediaRef.Id == null) continue;
                if (a.MediaRef.Kind == MediaRefKind.Capture) assignedCaptures.Add(a.MediaRef.Id);
                else if (a.MediaRef.Kind == MediaRefKind.PhotoCapture) assignedPhotoCaptures.Add(a.MediaRef.Id);
            }
            return (
                Data.Captures.Captures.Where(c => !assignedCaptures.Contains(c.Id ?? "")).ToList(),
                Data.Captures.PhotoCaptures.Where(p => !assignedPhotoCaptures.Contains(p.Id ?? "")).ToList());
        }

        // ------------------------------------------------------------------
        // People
        // ------------------------------------------------------------------

        public List<Person> PeopleWithRole(PersonRole role) => Data.Trip.People.Where(p => p.HasRole(role)).ToList();
        public List<Person> PeopleOfOrg(Org org) => Data.Trip.People.Where(p => p.Org == org).ToList();
    }
}

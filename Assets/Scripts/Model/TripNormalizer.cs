using System;
using System.Collections.Generic;
using System.Linq;

namespace MediaTrip.Model
{
    /// <summary>
    /// Keeps the rules that span documents true after a load, an import or an edit:
    ///
    /// 1. A chapter is one thing, owned by the shot list. An outline attaches sections to a
    ///    chapter by id. An outline chapter that does not name a plan chapter of its book is
    ///    matched by number, then by name; if nothing matches, the chapter is created in the
    ///    plan, because typing a chapter into an outline is creating that chapter.
    /// 2. Notes are one list in captures.json, hung off a chapter or a section. Older files kept
    ///    chapter notes in the outline and section notes in their own list; both are folded in.
    ///
    /// Running it twice changes nothing the second time.
    /// </summary>
    public static class TripNormalizer
    {
        public sealed class Result
        {
            public bool ShotListChanged, CapturesChanged;
            public HashSet<string> OutlinesChanged = new HashSet<string>();
            public List<string> Log = new List<string>();
            public bool Any => ShotListChanged || CapturesChanged || OutlinesChanged.Count > 0;
        }

        public static Result Normalize(TripData d)
        {
            var r = new Result();
            if (d?.Trip == null || d.ShotList == null || d.Captures == null) return r;
            if (d.Captures.Notes == null) d.Captures.Notes = new List<PlaceNote>();
            if (d.Captures.Edits == null) d.Captures.Edits = new List<ChangeRecord>();

            foreach (var kv in d.Outlines.ToList())
            {
                var bookId = kv.Key; var outline = kv.Value;
                if (outline == null || d.FindBook(bookId) == null) continue;
                if (outline.Chapters == null) outline.Chapters = new List<OutlineChapter>();
                AttachChapters(d, bookId, outline, r);
            }
            FoldLegacySectionNotes(d, r);
            SyncOutlines(d, r);
            return r;
        }

        /// <summary>
        /// The cheap part, safe to run after every edit: every outline lists exactly the plan's
        /// chapters of its book, in plan order, echoing their number and name, with sections
        /// numbered "c.n".
        /// </summary>
        public static Result SyncOutlines(TripData d, Result r = null)
        {
            r = r ?? new Result();
            foreach (var kv in d.Outlines)
            {
                var bookId = kv.Key; var outline = kv.Value;
                if (outline == null || d.FindBook(bookId) == null) continue;
                if (outline.Chapters == null) outline.Chapters = new List<OutlineChapter>();
                // never drop sections: a chapter the plan does not know yet is attached (or created) first
                if (outline.Chapters.Any(oc => oc.Id == null || d.FindChapter(oc.Id)?.BookId != bookId)) AttachChapters(d, bookId, outline, r);
                var plan = d.ChaptersOf(bookId).ToList();
                var byId = new Dictionary<string, OutlineChapter>();
                foreach (var oc in outline.Chapters) if (oc.Id != null && !byId.ContainsKey(oc.Id)) byId[oc.Id] = oc;

                var ordered = new List<OutlineChapter>();
                bool changed = false;
                foreach (var ch in plan)
                {
                    if (!byId.TryGetValue(ch.Id, out var oc)) { oc = new OutlineChapter { Id = ch.Id }; changed = true; }
                    if (oc.Sections == null) oc.Sections = new List<OutlineSection>();
                    if (oc.Number != ch.Number || oc.Name != ch.Name) { oc.Number = ch.Number; oc.Name = ch.Name; changed = true; }
                    for (int i = 0; i < oc.Sections.Count; i++)
                    {
                        var n = ch.Number + "." + (i + 1);
                        if (oc.Sections[i].Number != n) { oc.Sections[i].Number = n; changed = true; }
                    }
                    ordered.Add(oc);
                }
                if (!changed && (ordered.Count != outline.Chapters.Count || !ordered.SequenceEqual(outline.Chapters))) changed = true;
                outline.Chapters = ordered;
                if (changed) r.OutlinesChanged.Add(bookId);
            }
            return r;
        }

        private static void AttachChapters(TripData d, string bookId, OutlineDocument outline, Result r)
        {
            var plan = d.ChaptersOf(bookId).ToList();
            var taken = new HashSet<string>();
            // ids first, so a number or name match never steals a chapter that another outline chapter names outright
            foreach (var oc in outline.Chapters)
                if (oc.Id != null && plan.Any(c => c.Id == oc.Id)) taken.Add(oc.Id);

            var merged = new Dictionary<string, OutlineChapter>();
            foreach (var oc in outline.Chapters.ToList())
            {
                Chapter match = plan.FirstOrDefault(c => c.Id == oc.Id);
                if (match == null && oc.Number > 0) match = plan.FirstOrDefault(c => c.Number == oc.Number && !taken.Contains(c.Id));
                if (match == null && !string.IsNullOrWhiteSpace(oc.Name)) match = plan.FirstOrDefault(c => !taken.Contains(c.Id) && SameName(c.Name, oc.Name));
                if (match == null)
                {
                    match = new Chapter { Id = string.IsNullOrEmpty(oc.Id) || d.FindChapter(oc.Id) != null ? Ids.New("c") : oc.Id, BookId = bookId, Number = plan.Count + 1, Name = oc.Name };
                    d.ShotList.Chapters.Add(match);
                    plan.Add(match);
                    r.ShotListChanged = true;
                    r.Log.Add("Outline chapter '" + oc.Name + "' was added to the shot list.");
                }
                taken.Add(match.Id);
                if (string.IsNullOrWhiteSpace(match.Name) && !string.IsNullOrWhiteSpace(oc.Name)) { match.Name = oc.Name; r.ShotListChanged = true; }

                if (oc.Id != match.Id)
                {
                    var old = oc.Id;
                    foreach (var a in d.Captures.OutlineAssignments.Where(a => a.BookId == bookId && a.ChapterId == old)) { a.ChapterId = match.Id; r.CapturesChanged = true; }
                    foreach (var n in d.Captures.Notes.Where(n => n.BookId == bookId && n.ChapterId == old)) { n.ChapterId = match.Id; r.CapturesChanged = true; }
                    oc.Id = match.Id;
                    r.OutlinesChanged.Add(bookId);
                }

                if (!string.IsNullOrWhiteSpace(oc.Notes))
                {
                    if (FindNote(d, match.Id, null) == null)
                    {
                        d.Captures.Notes.Add(new PlaceNote { BookId = bookId, ChapterId = match.Id, SectionId = null, Text = oc.Notes });
                        r.CapturesChanged = true;
                    }
                }
                if (oc.Notes != null) { oc.Notes = null; r.OutlinesChanged.Add(bookId); }

                if (merged.TryGetValue(match.Id, out var first))
                {
                    first.Sections.AddRange(oc.Sections ?? new List<OutlineSection>());
                    outline.Chapters.Remove(oc);
                    r.OutlinesChanged.Add(bookId);
                }
                else merged[match.Id] = oc;
            }
        }

        private static void FoldLegacySectionNotes(TripData d, Result r)
        {
            var legacy = d.Captures.SectionNotes;
            if (legacy == null) return;
            foreach (var sn in legacy)
            {
                if (string.IsNullOrWhiteSpace(sn.Text) || sn.SectionId == null) continue;
                var bookId = sn.BookId;
                OutlineChapter ch = null;
                foreach (var kv in d.Outlines)
                {
                    if (bookId != null && kv.Key != bookId) continue;
                    ch = kv.Value?.Chapters?.FirstOrDefault(c => c.Sections != null && c.Sections.Any(s => s.Id == sn.SectionId));
                    if (ch != null) { bookId = kv.Key; break; }
                }
                if (FindNote(d, ch?.Id, sn.SectionId) == null)
                    d.Captures.Notes.Add(new PlaceNote { BookId = bookId, ChapterId = ch?.Id, SectionId = sn.SectionId, Text = sn.Text });
            }
            d.Captures.SectionNotes = null;
            r.CapturesChanged = true;
        }

        public static PlaceNote FindNote(TripData d, string chapterId, string sectionId) =>
            sectionId != null
                ? d.Captures.Notes.FirstOrDefault(n => n.SectionId == sectionId)
                : d.Captures.Notes.FirstOrDefault(n => n.SectionId == null && n.ChapterId == chapterId);

        private static bool SameName(string a, string b) =>
            string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(a);
    }
}

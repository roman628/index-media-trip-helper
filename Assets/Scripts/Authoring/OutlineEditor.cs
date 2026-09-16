using System;
using System.Collections.Generic;
using System.Linq;
using MediaTrip.Model;
using MediaTrip.Persistence;
using MediaTrip.Session;

namespace MediaTrip.Authoring
{
    /// <summary>
    /// Editing of outline mockups (one document per book): chapters, sections, per-chapter
    /// notes and the recursive bullets. Chapters are numbered 1..N and sections "c.n" after
    /// every change. Removing a chapter or section that outline assignments point at is
    /// refused unless <c>cascadeAssignments</c> is passed, which deletes those assignments.
    /// </summary>
    public sealed class OutlineEditor
    {
        private readonly TripSession _s;
        private TripData D => _s.Data;

        internal OutlineEditor(TripSession session) { _s = session; }

        /// <summary>The outline for a book, created from the shot-list chapters if it does not exist yet.</summary>
        public OutlineDocument Ensure(string bookId) => D.FindOutline(bookId) ?? _s.CreateOutline(bookId);

        public OutlineChapter FindChapter(string bookId, string chapterId) =>
            D.FindOutline(bookId)?.Chapters.FirstOrDefault(c => c.Id == chapterId);

        public OutlineSection FindSection(string bookId, string sectionId) => D.FindOutlineSection(bookId, sectionId);

        public OutlineChapter ChapterOfSection(string bookId, string sectionId) =>
            D.FindOutline(bookId)?.Chapters.FirstOrDefault(c => c.Sections.Any(s => s.Id == sectionId));

        // ------------------------------------------------------------------
        // Document-level
        // ------------------------------------------------------------------

        public void UpdateHeader(string bookId, Action<OutlineDocument> edit)
        {
            var o = Ensure(bookId);
            var keepBook = o.BookId; var keepTrip = o.TripId; var chapters = o.Chapters;
            edit(o);
            o.BookId = keepBook; o.TripId = keepTrip; o.Chapters = chapters;
            Finish(bookId);
        }

        // ------------------------------------------------------------------
        // Chapters
        // ------------------------------------------------------------------

        /// <summary>Add a chapter. Pass <paramref name="id"/> to link it to a shot-list chapter (same ID), which is how the sample does it.</summary>
        public OutlineChapter AddChapter(string bookId, string name, int? index = null, string id = null)
        {
            var o = Ensure(bookId);
            id = id ?? Ids.New("c");
            if (o.Chapters.Any(c => c.Id == id)) throw new InvalidOperationException("Outline already has chapter " + id);
            var ch = new OutlineChapter { Id = id, Name = name, Notes = "" };
            o.Chapters.Insert(Clamp(index ?? o.Chapters.Count, o.Chapters.Count), ch);
            Finish(bookId);
            return ch;
        }

        public void UpdateChapter(string bookId, string chapterId, Action<OutlineChapter> edit)
        {
            var ch = RequireChapter(bookId, chapterId);
            var keepId = ch.Id; var keepNumber = ch.Number; var sections = ch.Sections;
            edit(ch);
            ch.Id = keepId; ch.Number = keepNumber; ch.Sections = sections ?? new List<OutlineSection>();
            Finish(bookId);
        }

        public void SetChapterName(string bookId, string chapterId, string name) => UpdateChapter(bookId, chapterId, c => c.Name = name);
        public void SetChapterNotes(string bookId, string chapterId, string notes) => UpdateChapter(bookId, chapterId, c => c.Notes = notes ?? "");

        public void RemoveChapter(string bookId, string chapterId, bool cascadeAssignments = false)
        {
            var o = Ensure(bookId);
            var ch = RequireChapter(bookId, chapterId);
            var refs = ReferenceFinder.FindOutlineRefs(D, bookId, chapterId);
            foreach (var s in ch.Sections) refs.AddRange(ReferenceFinder.FindOutlineRefs(D, bookId, null, s.Id));
            refs = refs.GroupBy(r => r.Id).Select(g => g.First()).ToList();
            if (refs.Count > 0 && !cascadeAssignments)
                throw new PlanEditBlockedException(chapterId, refs, "Pass cascadeAssignments: true to delete those assignments too.", "Removing outline chapter");
            RemoveAssignments(refs);
            o.Chapters.Remove(ch);
            Finish(bookId);
        }

        public void ReorderChapter(string bookId, string chapterId, int newIndex)
        {
            var o = Ensure(bookId);
            var ch = RequireChapter(bookId, chapterId);
            o.Chapters.Remove(ch);
            o.Chapters.Insert(Clamp(newIndex, o.Chapters.Count), ch);
            Finish(bookId);
        }

        // ------------------------------------------------------------------
        // Sections
        // ------------------------------------------------------------------

        public OutlineSection AddSection(string bookId, string chapterId, string name, int? index = null)
        {
            var ch = RequireChapter(bookId, chapterId);
            var s = new OutlineSection { Id = Ids.New("s"), Name = name };
            ch.Sections.Insert(Clamp(index ?? ch.Sections.Count, ch.Sections.Count), s);
            Finish(bookId);
            return s;
        }

        public void UpdateSection(string bookId, string sectionId, Action<OutlineSection> edit)
        {
            var s = RequireSection(bookId, sectionId);
            var keepId = s.Id; var keepNumber = s.Number; var nodes = s.Nodes;
            edit(s);
            s.Id = keepId; s.Number = keepNumber; s.Nodes = nodes ?? new List<Node>();
            Finish(bookId);
        }

        public void SetSectionName(string bookId, string sectionId, string name) => UpdateSection(bookId, sectionId, s => s.Name = name);

        public void RemoveSection(string bookId, string sectionId, bool cascadeAssignments = false)
        {
            var ch = ChapterOfSection(bookId, sectionId) ?? throw new KeyNotFoundException("No section " + sectionId + " in outline " + bookId);
            var s = ch.Sections.First(x => x.Id == sectionId);
            var refs = ReferenceFinder.FindOutlineRefs(D, bookId, null, sectionId);
            if (refs.Count > 0 && !cascadeAssignments)
                throw new PlanEditBlockedException(sectionId, refs, "Pass cascadeAssignments: true to delete those assignments too.", "Removing outline section");
            RemoveAssignments(refs);
            ch.Sections.Remove(s);
            Finish(bookId);
        }

        public void ReorderSection(string bookId, string sectionId, int newIndex)
        {
            var ch = ChapterOfSection(bookId, sectionId) ?? throw new KeyNotFoundException("No section " + sectionId);
            var s = ch.Sections.First(x => x.Id == sectionId);
            ch.Sections.Remove(s);
            ch.Sections.Insert(Clamp(newIndex, ch.Sections.Count), s);
            Finish(bookId);
        }

        /// <summary>Move a section to another chapter of the same outline. Assignments follow (their chapterId is updated).</summary>
        public void MoveSection(string bookId, string sectionId, string targetChapterId, int? index = null)
        {
            var from = ChapterOfSection(bookId, sectionId) ?? throw new KeyNotFoundException("No section " + sectionId);
            var to = RequireChapter(bookId, targetChapterId);
            var s = from.Sections.First(x => x.Id == sectionId);
            from.Sections.Remove(s);
            to.Sections.Insert(Clamp(index ?? to.Sections.Count, to.Sections.Count), s);
            bool touchedCaptures = false;
            foreach (var a in D.Captures.OutlineAssignments.Where(a => a.BookId == bookId && a.SectionId == sectionId))
            {
                if (a.ChapterId != to.Id) { a.ChapterId = to.Id; touchedCaptures = true; }
            }
            if (touchedCaptures) _s.MarkDirty(DocumentKind.Captures);
            Finish(bookId);
        }

        // ------------------------------------------------------------------
        // Bullets
        // ------------------------------------------------------------------

        /// <summary>Editor for a section's bullets. Relabels with the scheme the section already uses (default a / 1 / i).</summary>
        public NodeListEditor Bullets(string bookId, string sectionId)
        {
            var s = RequireSection(bookId, sectionId);
            if (s.Nodes == null) s.Nodes = new List<Node>();
            return new NodeListEditor(() => s.Nodes, n => s.Nodes = n, LabelScheme.Infer(s.Nodes, LabelScheme.Outline), () => Finish(bookId));
        }

        // ------------------------------------------------------------------
        // Numbering
        // ------------------------------------------------------------------

        /// <summary>Chapters 1..N, sections "c.n".</summary>
        public void Renumber(string bookId)
        {
            var o = D.FindOutline(bookId);
            if (o == null) return;
            for (int c = 0; c < o.Chapters.Count; c++)
            {
                var ch = o.Chapters[c];
                ch.Number = c + 1;
                if (ch.Sections == null) ch.Sections = new List<OutlineSection>();
                for (int s = 0; s < ch.Sections.Count; s++)
                    ch.Sections[s].Number = (c + 1) + "." + (s + 1);
            }
        }

        private void Finish(string bookId)
        {
            Renumber(bookId);
            _s.MarkDirty(DocumentKind.Outline, bookId);
        }

        private void RemoveAssignments(List<PlanReference> refs)
        {
            if (refs.Count == 0) return;
            var ids = new HashSet<string>(refs.Select(r => r.Id));
            D.Captures.OutlineAssignments.RemoveAll(a => ids.Contains(a.Id));
            _s.MarkDirty(DocumentKind.Captures);
        }

        private OutlineChapter RequireChapter(string bookId, string chapterId) =>
            FindChapter(bookId, chapterId) ?? throw new KeyNotFoundException("No chapter " + chapterId + " in outline " + bookId);

        private OutlineSection RequireSection(string bookId, string sectionId) =>
            FindSection(bookId, sectionId) ?? throw new KeyNotFoundException("No section " + sectionId + " in outline " + bookId);

        private static int Clamp(int index, int count) => index < 0 ? 0 : (index > count ? count : index);
    }
}

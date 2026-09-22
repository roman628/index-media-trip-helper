using System;
using System.Collections.Generic;
using System.Linq;
using MediaTrip.Model;
using MediaTrip.Persistence;
using MediaTrip.Session;

namespace MediaTrip.Authoring
{
    /// <summary>
    /// Editing of outline mockups (one document per book): sections and their recursive
    /// bullets, attached to the book's chapters. A chapter is the shot list's chapter, so the
    /// chapter calls here edit the plan. Sections are numbered "c.n" after every change.
    /// Removing a section that media is placed in is refused unless <c>cascadeAssignments</c>
    /// is passed, which unassigns that media (it stays in the summary, unplaced).
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
        // Chapters: one thing, owned by the shot list. These calls edit that chapter.
        // ------------------------------------------------------------------

        /// <summary>
        /// Add a chapter to the book. It is created in the shot list (typing a chapter into an
        /// outline is creating that chapter) and the outline attaches to it by id.
        /// </summary>
        public OutlineChapter AddChapter(string bookId, string name, int? index = null)
        {
            Ensure(bookId);
            var ch = _s.PlanEditor.AddChapter(bookId, name, index);
            return FindChapter(bookId, ch.Id);
        }

        public void SetChapterName(string bookId, string chapterId, string name)
        {
            RequireChapter(bookId, chapterId);
            _s.PlanEditor.UpdateChapter(chapterId, c => c.Name = name);
        }

        /// <summary>
        /// Remove the chapter from the book (plan and outline alike). Refused when anything
        /// depends on it unless <paramref name="cascade"/>; then its planned videos and photos
        /// move to <paramref name="moveContentsTo"/> or, with no target, are detached.
        /// See <see cref="ShotListEditor.RemoveChapter(string, string)"/>.
        /// </summary>
        public void RemoveChapter(string bookId, string chapterId, bool cascade = false, string moveContentsTo = null)
        {
            RequireChapter(bookId, chapterId);
            var impact = Deletions.ForChapter(D, chapterId);
            if (impact.NeedsConfirm && !cascade)
                throw new PlanEditBlockedException(chapterId, ReferenceFinder.Find(D, chapterId), "Confirm first: " + string.Join(" ", impact.Lines()), "Removing chapter");
            _s.PlanEditor.RemoveChapter(chapterId, moveContentsTo);
        }

        public void ReorderChapter(string bookId, string chapterId, int newIndex)
        {
            RequireChapter(bookId, chapterId);
            _s.PlanEditor.ReorderChapter(chapterId, newIndex);
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

        /// <summary>Chapter numbers come from the shot list; sections are "c.n".</summary>
        public void Renumber(string bookId) => TripNormalizer.SyncOutlines(D);

        private void Finish(string bookId)
        {
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

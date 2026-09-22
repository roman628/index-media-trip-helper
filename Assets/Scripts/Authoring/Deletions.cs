using System.Collections.Generic;
using System.Linq;
using MediaTrip.Model;
using MediaTrip.Session;

namespace MediaTrip.Authoring
{
    /// <summary>
    /// What deleting something would touch. Nothing is ever silently orphaned: the UI shows this
    /// before it deletes, and the delete then detaches or moves what is listed here.
    /// </summary>
    public sealed class DeletionImpact
    {
        /// <summary>"chapter", "section", "video", "photo".</summary>
        public string Kind;
        public string Id;
        public string Label;
        public List<OutlineSection> Sections = new List<OutlineSection>();
        public List<Video> Videos = new List<Video>();
        public List<Photo> Photos = new List<Photo>();
        /// <summary>Media placed in the chapter or section (it becomes unplaced, it is not deleted).</summary>
        public int PlacedMedia;
        public int Notes;
        public int HeroAssignments;
        /// <summary>Captures that name the thing (they stay in the summary).</summary>
        public int Captures;
        /// <summary>Recorded changes that name the thing.</summary>
        public int Amendments;
        /// <summary>Other chapters of the same book that the chapter's videos could move to.</summary>
        public List<Chapter> MoveTargets = new List<Chapter>();

        /// <summary>True when the delete needs a confirmation because something depends on the thing.</summary>
        public bool NeedsConfirm => Sections.Count > 0 || Videos.Count > 0 || Photos.Count > 0 || PlacedMedia > 0 || Notes > 0 || HeroAssignments > 0 || Captures > 0 || Amendments > 0;

        /// <summary>One line per thing affected, in plain words.</summary>
        public List<string> Lines()
        {
            var lines = new List<string>();
            string N(int n, string one, string many = null) => n + " " + (n == 1 ? one : many ?? one + "s");
            if (Videos.Count > 0) lines.Add(N(Videos.Count, "planned video") + ": " + string.Join(", ", Videos.Take(4).Select(v => "#" + v.Number + " " + v.Title)) + (Videos.Count > 4 ? ", …" : ""));
            if (Photos.Count > 0) lines.Add(N(Photos.Count, "planned photo") + (Photos.Any(p => p.HeroType == HeroType.ChapterHero) ? ", including the chapter's hero" : ""));
            if (Sections.Count > 0) lines.Add(N(Sections.Count, "outline section") + ": " + string.Join(", ", Sections.Take(4).Select(s => s.Number + " " + s.Name)) + (Sections.Count > 4 ? ", …" : ""));
            if (PlacedMedia > 0) lines.Add(N(PlacedMedia, "placed item") + " will become unplaced. The media stays in the summary.");
            if (Notes > 0) lines.Add(N(Notes, "note") + " will be deleted.");
            if (HeroAssignments > 0) lines.Add(N(HeroAssignments, "cover or hero assignment") + " will be removed.");
            if (Captures > 0) lines.Add(N(Captures, "capture") + " will lose the link to it. The capture itself is kept.");
            if (Amendments > 0) lines.Add(N(Amendments, "recorded change") + " will stop naming it.");
            return lines;
        }
    }

    public static class Deletions
    {
        public static DeletionImpact ForChapter(TripData d, string chapterId)
        {
            var ch = d.FindChapter(chapterId);
            var i = new DeletionImpact { Kind = "chapter", Id = chapterId, Label = ch?.Name };
            if (ch == null) return i;
            i.Videos = d.ShotList.Videos.Where(v => v.ChapterId == chapterId).ToList();
            i.Photos = d.ShotList.Photos.Where(p => p.ChapterId == chapterId).ToList();
            var oc = d.FindOutline(ch.BookId)?.Chapters?.FirstOrDefault(c => c.Id == chapterId);
            i.Sections = oc?.Sections?.ToList() ?? new List<OutlineSection>();
            var sectionIds = new HashSet<string>(i.Sections.Select(s => s.Id));
            i.PlacedMedia = d.Captures.OutlineAssignments.Count(a => a.ChapterId == chapterId || (a.SectionId != null && sectionIds.Contains(a.SectionId)));
            i.Notes = d.Captures.Notes.Count(n => !string.IsNullOrWhiteSpace(n.Text) && (n.ChapterId == chapterId || (n.SectionId != null && sectionIds.Contains(n.SectionId))));
            i.HeroAssignments = d.Captures.HeroAssignments.Count(h => h.ChapterId == chapterId);
            i.Captures = d.Captures.Captures.Count(c => c.ChapterId == chapterId);
            i.Amendments = d.Captures.Amendments.Count(a => a.NewChapterId == chapterId);
            i.MoveTargets = d.ChaptersOf(ch.BookId).Where(c => c.Id != chapterId).ToList();
            return i;
        }

        public static DeletionImpact ForSection(TripData d, string bookId, string sectionId)
        {
            var sec = d.FindOutlineSection(bookId, sectionId);
            var i = new DeletionImpact { Kind = "section", Id = sectionId, Label = sec?.Name };
            if (sec == null) return i;
            i.PlacedMedia = d.Captures.OutlineAssignments.Count(a => a.SectionId == sectionId);
            i.Notes = d.Captures.Notes.Count(n => n.SectionId == sectionId && !string.IsNullOrWhiteSpace(n.Text));
            return i;
        }

        public static DeletionImpact ForVideo(TripData d, string videoId)
        {
            var v = d.FindVideo(videoId);
            var i = new DeletionImpact { Kind = "video", Id = videoId, Label = v?.Title };
            i.Captures = d.Captures.Captures.Count(c => c.PlanVideoId == videoId);
            i.Amendments = d.Captures.Amendments.Count(a => (a.Targets?.Contains(videoId) ?? false) || (a.Results?.Contains(videoId) ?? false));
            return i;
        }

        public static DeletionImpact ForPhoto(TripData d, string photoId)
        {
            var p = d.FindPhoto(photoId);
            var i = new DeletionImpact { Kind = "photo", Id = photoId, Label = p?.Description };
            i.Captures = d.Captures.Captures.Count(c => (c.Photos ?? new List<CapturePhoto>()).Any(x => x.PhotoId == photoId)) + d.Captures.PhotoCaptures.Count(x => x.PhotoId == photoId);
            i.HeroAssignments = d.Captures.HeroAssignments.Count(h => h.PhotoId == photoId);
            i.PlacedMedia = d.Captures.OutlineAssignments.Count(a => a.MediaRef?.Kind == MediaRefKind.PlannedPhoto && a.MediaRef.Id == photoId);
            i.Amendments = d.Captures.Amendments.Count(a => (a.Targets?.Contains(photoId) ?? false));
            return i;
        }

        /// <summary>Delete an outline section: its placed media is unassigned (and so listed as unplaced on the chapter) and its note goes with it.</summary>
        public static void DeleteSection(TripSession s, string bookId, string sectionId)
        {
            s.Data.Captures.Notes.RemoveAll(n => n.SectionId == sectionId);
            s.Outline.RemoveSection(bookId, sectionId, cascadeAssignments: true);
            s.MarkDirty(Persistence.DocumentKind.Captures);
        }
    }
}

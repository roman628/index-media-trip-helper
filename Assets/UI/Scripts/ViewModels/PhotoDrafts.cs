using System.Collections.Generic;
using System.Linq;
using MediaTrip.Model;
using MediaTrip.Query;
using MediaTrip.Session;
using MediaTrip.Status;

namespace MediaTrip.UI.ViewModels
{
    /// <summary>The Summary's "Photos" sheet: tick planned photos not yet shot, type unplanned ones, append them all to a day.</summary>
    public sealed class PhotoBatchDraft
    {
        public string DayId;
        public HashSet<string> Picked = new HashSet<string>();
        public List<string> Extra = new List<string>();
        public string Text = "";

        public bool CanSave => Picked.Count > 0 || Extra.Count > 0 || !string.IsNullOrWhiteSpace(Text);

        /// <summary>Planned photos that nobody has shot yet, in printed order.</summary>
        public static List<PhotoItem> NotYetShot(TripSession s) =>
            s.Queries.AllPhotos().Where(p => p.Status == PhotoStatus.NotCaptured).ToList();

        public void Toggle(string photoId) { if (!Picked.Remove(photoId)) Picked.Add(photoId); }

        public void AddExtra()
        {
            if (string.IsNullOrWhiteSpace(Text)) return;
            Extra.Add(Text.Trim());
            Text = "";
        }

        /// <summary>Append one photo capture per picked or typed photo. Text still in the field counts. Returns what was added.</summary>
        public List<PhotoCapture> Save(TripSession s)
        {
            AddExtra();
            var added = new List<PhotoCapture>();
            foreach (var p in NotYetShot(s).Where(p => Picked.Contains(p.Id)))
                added.Add(s.AddPhotoCapture(new PhotoCapture { DayId = DayId, PhotoId = p.Id, Section = PhotoCaptureSection.AdditionalPhotography }));
            foreach (var t in Extra)
                added.Add(s.AddPhotoCapture(new PhotoCapture { DayId = DayId, Text = t, Section = PhotoCaptureSection.AdditionalPhotography }));
            Picked.Clear();
            Extra.Clear();
            return added;
        }
    }

    /// <summary>What the Covers slot sheet does. A slot is assigned to, never ticked.</summary>
    public static class CoverActions
    {
        /// <summary>A planned cover or hero was shot: log it on the day. It then shows as assigned.</summary>
        public static PhotoCapture GotPlanned(TripSession s, CoverSlot slot, string dayId, string photoId = null)
        {
            var planned = photoId != null ? slot?.PlannedPhotos.FirstOrDefault(p => p.Id == photoId) : slot?.Planned;
            if (planned == null || planned.Status == PhotoStatus.Captured) return null;
            return s.AddPhotoCapture(new PhotoCapture { DayId = dayId, PhotoId = planned.Id, Section = PhotoCaptureSection.AdditionalPhotography });
        }

        /// <summary>Take "got it" back. The planned photo is unassigned again because it is no longer recorded as shot.</summary>
        public static int UngotPlanned(TripSession s, string photoId) => s.UncapturePhoto(photoId);

        /// <summary>
        /// Something new decided on the spot. It goes through the one path for new media, so it
        /// is on the working shot list in this book and chapter, marked new and listed in
        /// Changes; it is logged on the day, assigned to the slot, and, for a chapter's slot,
        /// placed on that chapter in Outlines.
        /// </summary>
        public static HeroAssignment AssignNew(TripSession s, CoverSlot slot, string text, string dayId)
        {
            if (slot == null || string.IsNullOrWhiteSpace(text)) return null;
            var id = s.CreateMedia(MediaKind.Photo, text, slot.Book.Id, slot.Chapter?.Id, "Added from Covers for " + (slot.IsCover ? "the cover" : "the chapter hero") + ".");
            var pc = s.AddPhotoCapture(new PhotoCapture { DayId = dayId, PhotoId = id, Section = PhotoCaptureSection.AdditionalPhotography });
            if (slot.Chapter != null)
                s.Assign(new MediaRef(MediaRefKind.PhotoCapture, pc.Id), slot.Book.Id, slot.Chapter.Id, null, null, AssignmentSource.Manual, null, true);
            return s.AssignHero(slot.Book.Id, slot.Chapter?.Id, id, null, pc.Id);
        }

        /// <summary>
        /// An existing photo takes the slot. When it belongs to another book the caller asks
        /// first (see <see cref="IsFromAnotherBook"/>): <paramref name="move"/> moves it to this
        /// book in the working copy; otherwise it stays where it is and serves both.
        /// </summary>
        public static HeroAssignment AssignExisting(TripSession s, CoverSlot slot, string photoId, bool move = false)
        {
            if (slot == null) return null;
            if (move) s.MovePhoto(photoId, slot.Book.Id, slot.Chapter?.Id, "Moved from Covers.");
            return s.AssignHero(slot.Book.Id, slot.Chapter?.Id, photoId, null);
        }

        /// <summary>
        /// A photo typed in the field takes the slot: it is filed as a real photo of this book
        /// (and chapter) on the working list, the records that logged it point at it, and it
        /// is assigned. A stand-alone photo capture of it is placed on the chapter in Outlines.
        /// </summary>
        public static HeroAssignment FileAndAssign(TripSession s, CoverSlot slot, LoosePhoto loose)
        {
            if (slot == null || loose == null) return null;
            var id = s.FilePhoto(loose.Text, slot.Book.Id, slot.Chapter?.Id, "Filed from Covers for " + (slot.IsCover ? "the cover" : "the chapter hero") + ".");
            var pc = loose.PhotoCaptures.FirstOrDefault();
            if (slot.Chapter != null && pc != null)
                s.Assign(new MediaRef(MediaRefKind.PhotoCapture, pc.Id), slot.Book.Id, slot.Chapter.Id, null, null, AssignmentSource.Manual, null, true);
            return s.AssignHero(slot.Book.Id, slot.Chapter?.Id, id, null, pc?.Id);
        }

        public static bool IsFromAnotherBook(CoverSlot slot, PhotoItem photo) => photo != null && !photo.Photo.BelongsToBook(slot.Book.Id);

        /// <summary>Where a photo already is: its own book and use, and every slot it is assigned to. For the "already assigned" dialog.</summary>
        public static List<string> WhereAssigned(TripSession s, PhotoItem photo)
        {
            var d = s.Data;
            var lines = new List<string> { Fmt.PhotoWhere(d, photo) };
            foreach (var h in d.Captures.HeroAssignments.Where(h => h.PhotoId == photo.Id))
            {
                var ch = d.FindChapter(h.ChapterId);
                lines.Add("Assigned: " + Fmt.BookName(d.FindBook(h.BookId)) + " · " + (ch == null ? "Cover" : "Ch." + ch.Number + " hero"));
            }
            return lines;
        }

        /// <summary>
        /// Fuzzy search over every photo of every book for the slot's one input. What already
        /// fills the slot is left out; photos of the slot's own book come first on a tie.
        /// </summary>
        public static List<PhotoItem> Search(TripSession s, CoverSlot slot, string query, int max = 6)
        {
            if (string.IsNullOrWhiteSpace(query)) return new List<PhotoItem>();
            var taken = new HashSet<string>(slot.Assigned.Where(a => a.Photo != null).Select(a => a.Photo.Id));
            return s.Search.SearchPhotos(query.Trim(), max + taken.Count + 4)
                .Where(m => !taken.Contains(m.Item.Id) && m.Item.Status != PhotoStatus.Dropped)
                .Select((m, i) => (m, i))
                .OrderByDescending(t => System.Math.Round(t.m.Score, 2)).ThenBy(t => t.m.Item.Photo.BelongsToBook(slot.Book.Id) ? 0 : 1).ThenBy(t => t.i)
                .Select(t => t.m.Item).Take(max).ToList();
        }

        /// <summary>True when the typed text names one of the results outright, so "create new" is not offered for it.</summary>
        public static bool IsExactMatch(string query, IEnumerable<PhotoItem> results)
        {
            var q = Normalize(query);
            return q.Length > 0 && results.Any(p => Normalize(p.Description) == q || Normalize(U_ShortDescription(p.Description)) == q);
        }

        private static string Normalize(string s) => new string((s ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

        private static string U_ShortDescription(string description)
        {
            if (string.IsNullOrEmpty(description)) return "";
            var idx = description.IndexOf(": ", System.StringComparison.Ordinal);
            return idx > 0 && idx < 16 ? description.Substring(idx + 2) : description;
        }

        public static int EmptyCount(List<CoverBook> board) => board.SelectMany(b => b.Slots).Count(x => x.IsEmpty);
        public static int SlotCount(List<CoverBook> board) => board.Sum(b => b.Slots.Count);
    }
}

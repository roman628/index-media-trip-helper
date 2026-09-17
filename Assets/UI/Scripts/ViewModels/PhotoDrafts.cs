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
        /// <summary>The planned hero was shot: log it on the day. It then shows as assigned.</summary>
        public static PhotoCapture GotPlanned(TripSession s, CoverSlot slot, string dayId)
        {
            if (slot?.Planned == null || slot.Planned.Status == PhotoStatus.Captured) return null;
            return s.AddPhotoCapture(new PhotoCapture { DayId = dayId, PhotoId = slot.Planned.Id, Section = PhotoCaptureSection.AdditionalPhotography });
        }

        /// <summary>Something new decided on the spot: logged on the day and assigned to the slot.</summary>
        public static HeroAssignment AssignNew(TripSession s, CoverSlot slot, string text, string dayId)
        {
            if (slot == null || string.IsNullOrWhiteSpace(text)) return null;
            var pc = s.AddPhotoCapture(new PhotoCapture { DayId = dayId, Text = text.Trim(), Section = PhotoCaptureSection.AdditionalPhotography });
            return s.AssignHero(slot.Book.Id, slot.Chapter?.Id, null, text, pc.Id);
        }

        /// <summary>An existing master-list photo takes the slot.</summary>
        public static HeroAssignment AssignExisting(TripSession s, CoverSlot slot, string photoId) =>
            slot == null ? null : s.AssignHero(slot.Book.Id, slot.Chapter?.Id, photoId, null);

        /// <summary>Master-list photos of the slot's book that could take it: not the planned hero, not already assigned. Shot ones first.</summary>
        public static List<PhotoItem> Candidates(TripSession s, CoverSlot slot)
        {
            var taken = new HashSet<string>(slot.Assigned.Where(a => a.Photo != null).Select(a => a.Photo.Id));
            if (slot.Planned != null) taken.Add(slot.Planned.Id);
            return s.Queries.PhotosOfBook(slot.Book.Id, true)
                .Where(p => !taken.Contains(p.Id) && p.Status != PhotoStatus.Dropped)
                .OrderBy(p => p.Status == PhotoStatus.Captured ? 0 : 1)
                .ToList();
        }

        public static int EmptyCount(List<CoverBook> board) => board.SelectMany(b => b.Slots).Count(x => x.IsEmpty);
        public static int SlotCount(List<CoverBook> board) => board.Sum(b => b.Slots.Count);
    }
}

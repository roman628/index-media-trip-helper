using System;
using System.Collections.Generic;
using System.Linq;
using MediaTrip.Model;
using MediaTrip.Session;
using MediaTrip.Status;
using Newtonsoft.Json.Linq;

namespace MediaTrip.Authoring
{
    /// <summary>
    /// Original = the list as printed. Editing it is authoring before the trip, or, once media
    /// exists, correcting a transcription mistake (logged lightly as a correction).
    /// Working = the plan as it now stands. Editing it is changing the plan during the trip, and
    /// every change is recorded as an amendment shown in Changes.
    /// </summary>
    public enum PlanEditMode { Original, Working }

    /// <summary>
    /// The edit state of the shot list, in either mode, behind one set of calls. The screen does
    /// not decide what gets recorded; this does:
    ///
    /// Original: the shot list itself is edited. Nothing is recorded while the trip has no media.
    /// Once it has, each field edit is logged as a correction (field, before, after), and a
    /// delete detaches what pointed at the item instead of being refused.
    ///
    /// Working: the shot list is not touched. A retitle is a rename amendment, a delete is a
    /// drop, a new video or photo is an add, a chapter change is a move, and content (scene,
    /// SME, notes, photos) is a revise amendment carrying before and after.
    /// </summary>
    public sealed class PlanEdits
    {
        private readonly TripSession _s;
        public PlanEditMode Mode { get; }
        private TripData D => _s.Data;
        private ShotListEditor Ed => _s.PlanEditor;

        public PlanEdits(TripSession session, PlanEditMode mode) { _s = session; Mode = mode; }

        public bool Working => Mode == PlanEditMode.Working;
        /// <summary>Original edits are logged as corrections once the trip has media.</summary>
        public bool LogsCorrections => Mode == PlanEditMode.Original && _s.HasFieldMedia;

        private void Correct(string entity, string id, string label, string field, JToken before, JToken after)
        {
            if (LogsCorrections) _s.Record(ChangeRecordKind.Correction, entity, id, label, field, before, after);
        }

        private Video OriginalVideo(string id) => D.FindVideo(id);

        // ------------------------------------------------------------------ videos

        public void SetTitle(string itemId, string title)
        {
            if (Working) { _s.RenameWorking(itemId, title); return; }
            var v = OriginalVideo(itemId) ?? throw new KeyNotFoundException("Not on the original list: " + itemId);
            var before = v.Title;
            Ed.SetVideoTitle(itemId, title, force: true);
            Correct("video", itemId, before, FieldChange.Title, before, title);
        }

        public void SetScene(string itemId, string text)
        {
            if (Working) { _s.Revise(itemId, FieldChange.Scene, TripSession.ToToken(text)); return; }
            var v = OriginalVideo(itemId); var before = v.SceneDescription;
            Ed.UpdateVideo(itemId, x => x.SceneDescription = text);
            Correct("video", itemId, v.Title, FieldChange.Scene, before, text);
        }

        public void SetSmeIds(string itemId, List<string> ids)
        {
            ids = ids ?? new List<string>();
            if (Working) { _s.Revise(itemId, FieldChange.SmeIds, TripSession.ToToken(ids)); return; }
            var v = OriginalVideo(itemId); var before = TripSession.ToToken(v.SmeIds ?? new List<string>());
            Ed.UpdateVideo(itemId, x => x.SmeIds = ids.ToList());
            Correct("video", itemId, v.Title, FieldChange.SmeIds, before, TripSession.ToToken(ids));
        }

        public void SetSmeText(string itemId, string text)
        {
            if (Working) { _s.Revise(itemId, FieldChange.SmeText, TripSession.ToToken(text)); return; }
            var v = OriginalVideo(itemId); var before = v.SmeText;
            Ed.UpdateVideo(itemId, x => x.SmeText = text);
            Correct("video", itemId, v.Title, FieldChange.SmeText, before, text);
        }

        public void SetPhotoRefs(string itemId, List<string> photoIds)
        {
            photoIds = photoIds ?? new List<string>();
            if (Working) { _s.Revise(itemId, FieldChange.PhotoRefs, TripSession.ToToken(photoIds)); return; }
            var v = OriginalVideo(itemId); var before = TripSession.ToToken(v.PhotoRefs ?? new List<string>());
            // only photos on the original list can be written into it
            var planned = photoIds.Where(id => D.FindPhoto(id) != null).ToList();
            Ed.SetPhotoRefs(itemId, planned);
            Correct("video", itemId, v.Title, FieldChange.PhotoRefs, before, TripSession.ToToken(planned));
        }

        /// <summary>The notes editor for the mode: the original tree, or a working copy written back as revisions.</summary>
        public NodeListEditor Notes(string itemId)
        {
            if (Working) return _s.WorkingNotes(itemId);
            var v = OriginalVideo(itemId);
            if (!LogsCorrections) return Ed.Notes(itemId);
            var before = TripSession.ToToken(NodeTree.Clone(v.Notes ?? new List<Node>(), newIds: false));
            var inner = Ed.Notes(itemId);
            return new NodeListEditor(() => inner.Roots, n => inner.Replace(n), inner.Scheme,
                () => { _s.MarkDirty(Persistence.DocumentKind.ShotList); Correct("video", itemId, v.Title, FieldChange.Notes, before, TripSession.ToToken(v.Notes)); });
        }

        /// <summary>Add a video to a chapter. Original: a planned video (numbers shift). Working: an "add", marked new.</summary>
        public string AddVideo(string chapterId, string title)
        {
            if (Working)
            {
                var ch = D.FindChapter(chapterId);
                return _s.CreateMedia(MediaKind.Video, string.IsNullOrWhiteSpace(title) ? "New video" : title, ch?.BookId, chapterId, "Added while editing the working list.");
            }
            var numbers = SnapshotNumbers();
            var v = Ed.AddVideo(chapterId, title);
            LogRenumbering(numbers);
            return v.Id;
        }

        /// <summary>
        /// Delete a video. Working: exactly what Drop does. Original: removed from the list;
        /// captures of it are kept and become unplanned, recorded changes stop naming it.
        /// </summary>
        public void DeleteVideo(string itemId, string reason = null)
        {
            if (Working) { _s.Drop(itemId, reason); return; }
            var v = OriginalVideo(itemId);
            var numbers = SnapshotNumbers();
            Correct("video", itemId, v.Title, FieldChange.Deleted, v.Title, null);
            Ed.RemoveVideo(itemId, detach: true);
            LogRenumbering(numbers);
        }

        /// <summary>Reorder within the chapter (original only; the working copy keeps the printed order).</summary>
        public void ReorderVideo(string videoId, int newIndex)
        {
            if (Working) throw new InvalidOperationException("The working copy keeps the printed order.");
            var numbers = SnapshotNumbers();
            Ed.ReorderVideo(videoId, newIndex);
            LogRenumbering(numbers);
        }

        /// <summary>Move to another chapter. Working: a move amendment. Original: moved in the list, numbers follow.</summary>
        public void MoveVideo(string itemId, string chapterId)
        {
            var ch = D.FindChapter(chapterId) ?? throw new KeyNotFoundException("No chapter " + chapterId);
            if (Working) { _s.Move(itemId, ch.BookId, chapterId); return; }
            var v = OriginalVideo(itemId); var before = v.ChapterId;
            var numbers = SnapshotNumbers();
            D.ShotList.Videos.Remove(v);
            v.ChapterId = ch.Id; v.BookId = ch.BookId;
            ShotListEditor.Insert(D.ShotList.Videos, x => x.ChapterId == ch.Id, v, null);
            Ed.Renumber();
            _s.MarkDirty(Persistence.DocumentKind.ShotList);
            Correct("video", itemId, v.Title, FieldChange.Chapter, before, chapterId);
            LogRenumbering(numbers);
        }

        // ------------------------------------------------------------------ photos

        public string AddPhoto(string bookId, string chapterId, string description)
        {
            if (Working) return _s.CreateMedia(MediaKind.Photo, string.IsNullOrWhiteSpace(description) ? "New photo" : description, bookId, chapterId, "Added while editing the working list.");
            return Ed.AddPhoto(bookId, description, HeroType.None, chapterId).Id;
        }

        public void SetPhotoDescription(string photoId, string text)
        {
            if (Working) { _s.RenameWorking(photoId, text); return; }
            var p = D.FindPhoto(photoId) ?? throw new KeyNotFoundException("Not on the original list: " + photoId);
            var before = p.Description;
            Ed.UpdatePhoto(photoId, x => x.Description = text);
            Correct("photo", photoId, before, FieldChange.Description, before, text);
        }

        /// <summary>Cover, a chapter's hero, or a detail. Part of the printed list, so original only.</summary>
        public void SetPhotoUse(string photoId, HeroType type, string chapterId)
        {
            if (Working) throw new InvalidOperationException("What a photo is planned as belongs to the original list. Assign it in Covers instead.");
            var p = D.FindPhoto(photoId);
            var before = p.HeroType + (p.ChapterId != null ? ":" + p.ChapterId : "");
            Ed.UpdatePhoto(photoId, x => { x.HeroType = type; x.ChapterId = type == HeroType.ChapterHero ? chapterId : (type == HeroType.BookCover ? null : x.ChapterId); });
            Correct("photo", photoId, p.Description, FieldChange.HeroType, before, p.HeroType + (p.ChapterId != null ? ":" + p.ChapterId : ""));
        }

        public void DeletePhoto(string photoId, string reason = null)
        {
            if (Working) { _s.Drop(photoId, reason); return; }
            var p = D.FindPhoto(photoId);
            Correct("photo", photoId, p.Description, FieldChange.Deleted, p.Description, null);
            Ed.RemovePhoto(photoId, detach: true);
        }

        // ------------------------------------------------------------------ chapters (one thing, in either mode)

        public Chapter AddChapter(string bookId, string name)
        {
            var ch = Ed.AddChapter(bookId, name);
            Structure("chapter", ch.Id, name, FieldChange.Added, null, "chapter");
            return ch;
        }

        public void SetChapterName(string chapterId, string name)
        {
            var ch = D.FindChapter(chapterId); var before = ch.Name;
            Ed.UpdateChapter(chapterId, c => c.Name = name);
            Structure("chapter", chapterId, before, FieldChange.Name, before, name);
        }

        public void ReorderChapter(string chapterId, int newIndex)
        {
            var ch = D.FindChapter(chapterId); var before = ch.Number;
            var numbers = SnapshotNumbers();
            Ed.ReorderChapter(chapterId, newIndex);
            Structure("chapter", chapterId, ch.Name, FieldChange.Number, before, ch.Number);
            LogRenumbering(numbers);
        }

        /// <summary>Delete a chapter, moving its planned videos and photos to <paramref name="moveContentsTo"/> (null detaches them). See <see cref="Deletions.ForChapter"/> for what to show first.</summary>
        public void DeleteChapter(string chapterId, string moveContentsTo)
        {
            var ch = D.FindChapter(chapterId);
            var numbers = SnapshotNumbers();
            var target = moveContentsTo != null ? D.FindChapter(moveContentsTo) : null;
            Structure("chapter", chapterId, ch.Name, FieldChange.Deleted, ch.Name, target != null ? "contents moved to " + target.Name : null, force: true);
            Ed.RemoveChapter(chapterId, moveContentsTo);
            LogRenumbering(numbers);
        }

        /// <summary>A chapter is not part of an amendment, so a change to one is logged: as a change in Working, as a correction in Original once media exists.</summary>
        private void Structure(string entity, string id, string label, string field, JToken before, JToken after, bool force = false)
        {
            if (Working) _s.Record(ChangeRecordKind.Change, entity, id, label, field, before, after);
            else if (LogsCorrections) _s.Record(ChangeRecordKind.Correction, entity, id, label, field, before, after);
        }

        // ------------------------------------------------------------------ "was N"

        private Dictionary<string, int> SnapshotNumbers() => D.ShotList.Videos.ToDictionary(v => v.Id, v => v.Number);

        /// <summary>The crew's paper still has the old numbers: once media exists, every shifted number is logged so the list can show "was N".</summary>
        private void LogRenumbering(Dictionary<string, int> before)
        {
            if (!_s.HasFieldMedia) return;
            foreach (var v in D.ShotList.Videos)
                if (before.TryGetValue(v.Id, out var was) && was != v.Number)
                    _s.Record(ChangeRecordKind.Correction, "video", v.Id, v.Title, FieldChange.Number, was, v.Number);
        }

        /// <summary>The number a video had when the first capture-era edit shifted it; null when it never moved.</summary>
        public static int? WasNumber(TripSession s, string videoId)
        {
            var t = s.FirstRecordedValue(videoId, FieldChange.Number);
            if (t == null || t.Type != JTokenType.Integer) return null;
            var was = (int)t;
            return s.Data.FindVideo(videoId)?.Number == was ? (int?)null : was;
        }

        /// <summary>The title the original had before it was corrected; null when never corrected.</summary>
        public static string WasTitle(TripSession s, string videoId)
        {
            var t = s.FirstRecordedValue(videoId, FieldChange.Title);
            if (t == null || t.Type != JTokenType.String) return null;
            var was = (string)t;
            return s.Data.FindVideo(videoId)?.Title == was ? null : was;
        }
    }
}

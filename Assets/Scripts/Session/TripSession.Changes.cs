using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MediaTrip.Authoring;
using MediaTrip.Model;
using MediaTrip.Persistence;
using MediaTrip.Status;
using Newtonsoft.Json.Linq;

namespace MediaTrip.Session
{
    /// <summary>
    /// Notes, the change log, working-copy revisions, the one path for creating new media, and
    /// the summary's day and time edits.
    /// </summary>
    public sealed partial class TripSession
    {
        // ------------------------------------------------------------------
        // Notes: one concept, on a chapter or on one of its sections
        // ------------------------------------------------------------------

        /// <summary>The note on a chapter as a whole (<paramref name="sectionId"/> null) or on a section. "" when there is none.</summary>
        public string NoteText(string chapterId, string sectionId = null) =>
            TripNormalizer.FindNote(Data, chapterId, sectionId)?.Text ?? "";

        /// <summary>Set or clear a note. Covers and Outlines call this with the same chapter id, so they show the same text.</summary>
        public void SetNote(string bookId, string chapterId, string sectionId, string text)
        {
            var notes = Data.Captures.Notes;
            var n = TripNormalizer.FindNote(Data, chapterId, sectionId);
            if (string.IsNullOrWhiteSpace(text)) { if (n != null) notes.Remove(n); }
            else if (n == null) notes.Add(new PlaceNote { BookId = bookId, ChapterId = chapterId, SectionId = sectionId, Text = text });
            else { n.Text = text; n.BookId = bookId; n.ChapterId = chapterId; }
            MarkDirty(DocumentKind.Captures);
        }

        // ------------------------------------------------------------------
        // Edit sessions: typing produces one record per field, not one per keystroke
        // ------------------------------------------------------------------

        private readonly Dictionary<string, string> _openRecords = new Dictionary<string, string>();   // "entityId|field" -> record id
        private readonly Dictionary<string, string> _openAmendments = new Dictionary<string, string>(); // "itemId|type" -> amendment id

        /// <summary>Call when an edit state ends. The next edit of the same field starts a new record.</summary>
        public void EndEditSession()
        {
            _openRecords.Clear();
            _openAmendments.Clear();
        }

        /// <summary>True once anything has been recorded against the plan: the original is no longer freely editable.</summary>
        public bool HasFieldMedia =>
            Data.Captures.Captures.Count > 0 || Data.Captures.PhotoCaptures.Count > 0 || Data.Captures.HeroAssignments.Count > 0 ||
            Data.Captures.OutlineAssignments.Count > 0 || Data.Captures.Amendments.Count > 0;

        // ------------------------------------------------------------------
        // The light log: corrections to the original, and structural changes
        // ------------------------------------------------------------------

        /// <summary>
        /// Log that a field went from <paramref name="before"/> to <paramref name="after"/>.
        /// Within one edit session repeated edits of the same field update the one record (the
        /// first "before" is kept), and a field put back to what it was leaves no record.
        /// </summary>
        public ChangeRecord Record(ChangeRecordKind kind, string entity, string entityId, string label, string field, JToken before, JToken after)
        {
            var key = entityId + "|" + field + "|" + kind;
            ChangeRecord rec = null;
            if (_openRecords.TryGetValue(key, out var openId)) rec = Data.Captures.Edits.FirstOrDefault(e => e.Id == openId);
            if (rec != null)
            {
                rec.After = after;
                if (JToken.DeepEquals(Norm(rec.Before), Norm(after))) { Data.Captures.Edits.Remove(rec); _openRecords.Remove(key); rec = null; }
            }
            else if (!JToken.DeepEquals(Norm(before), Norm(after)))
            {
                rec = new ChangeRecord { Id = Ids.New("ed"), At = Now(), Kind = kind, Entity = entity, EntityId = entityId, Label = label, Field = field, Before = before, After = after };
                Data.Captures.Edits.Add(rec);
                _openRecords[key] = rec.Id;
            }
            MarkDirty(DocumentKind.Captures);
            return rec;
        }

        public void RemoveRecord(string recordId)
        {
            Data.Captures.Edits.RemoveAll(e => e.Id == recordId);
            MarkDirty(DocumentKind.Captures);
        }

        private static JToken Norm(JToken t) => t == null || t.Type == JTokenType.Null || (t.Type == JTokenType.String && string.IsNullOrEmpty((string)t)) ? JValue.CreateNull() : t;

        /// <summary>The value at the start of the oldest record of a field, e.g. the number a video had on the paper list. Null when never recorded.</summary>
        public JToken FirstRecordedValue(string entityId, string field) =>
            Data.Captures.Edits.Where(e => e.EntityId == entityId && e.Field == field)
                .OrderBy(e => PlanResolver.ParseTimestamp(e.At) ?? DateTimeOffset.MaxValue).FirstOrDefault()?.Before;

        // ------------------------------------------------------------------
        // Working copy: content revisions and coalesced renames
        // ------------------------------------------------------------------

        public static JToken ToToken(object value) => value == null ? JValue.CreateNull() : JToken.FromObject(value, TripJson.CreateSerializer());

        /// <summary>
        /// Change one content field of a video in the working copy (scene description, SME ids or
        /// text, notes, photo refs). The shot list is not touched: a "revise" amendment carries
        /// the before and after, and shows in Changes.
        /// </summary>
        public Amendment Revise(string itemId, string field, JToken after, string reason = null)
        {
            var item = Plan.FindItem(itemId) ?? throw new KeyNotFoundException("No plan item " + itemId);
            var key = itemId + "|revise";
            Amendment am = null;
            if (_openAmendments.TryGetValue(key, out var openId)) am = Data.FindAmendment(openId);
            if (am == null)
            {
                var before = CurrentValue(item, field);
                if (JToken.DeepEquals(Norm(before), Norm(after))) return null;
                am = new Amendment
                {
                    Id = Ids.New("am"), At = Now(), Type = AmendmentType.Revise, Reason = reason,
                    Targets = new List<string> { itemId }, Results = new List<string> { itemId },
                    Changes = new List<FieldChange> { new FieldChange { Field = field, Before = before, After = after } },
                };
                Data.Captures.Amendments.Add(am);
                _openAmendments[key] = am.Id;
            }
            else
            {
                var c = am.Changes.FirstOrDefault(x => x.Field == field);
                if (c == null) am.Changes.Add(c = new FieldChange { Field = field, Before = CurrentValue(item, field), After = after });
                else c.After = after;
                if (JToken.DeepEquals(Norm(c.Before), Norm(c.After))) am.Changes.Remove(c);
                if (am.Changes.Count == 0) { Data.Captures.Amendments.Remove(am); _openAmendments.Remove(key); am = null; }
            }
            MarkDirty(DocumentKind.Captures);
            return am;
        }

        private static JToken CurrentValue(PlanItem item, string field)
        {
            switch (field)
            {
                case FieldChange.Scene: return ToToken(item.SceneDescription);
                case FieldChange.SmeText: return ToToken(item.SmeText);
                case FieldChange.SmeIds: return ToToken(item.SmeIds ?? new List<string>());
                case FieldChange.PhotoRefs: return ToToken(item.PhotoRefs ?? new List<string>());
                case FieldChange.Notes: return ToToken(item.Notes ?? new List<Node>());
                default: throw new ArgumentException("Not a revisable field: " + field);
            }
        }

        /// <summary>
        /// Editor for a video's notes in the working copy. It edits a private copy of the tree;
        /// every change is written as a revision, so the original notes stay as printed.
        /// </summary>
        public NodeListEditor WorkingNotes(string itemId)
        {
            var item = Plan.FindItem(itemId) ?? throw new KeyNotFoundException("No plan item " + itemId);
            var copy = NodeTree.Clone(item.Notes ?? new List<Node>(), newIds: false);
            return new NodeListEditor(() => copy, n => copy = n, LabelScheme.Infer(copy, LabelScheme.ShotListNotes),
                () => Revise(itemId, FieldChange.Notes, ToToken(copy)));
        }

        /// <summary>
        /// Rename in the working copy (a video or a photo). Exactly what the Rename action does;
        /// while an edit session is open, retyping updates the one amendment.
        /// </summary>
        public Amendment RenameWorking(string id, string newTitle, string reason = null)
        {
            var key = id + "|rename";
            if (_openAmendments.TryGetValue(key, out var openId))
            {
                var open = Data.FindAmendment(openId);
                if (open != null)
                {
                    open.NewTitle = newTitle;
                    SyncCaptureTitles(id, newTitle);
                    MarkDirty(DocumentKind.Captures);
                    return open;
                }
            }
            var am = Rename(id, newTitle, reason);
            SyncCaptureTitles(id, newTitle);
            _openAmendments[key] = am.Id;
            return am;
        }

        private void SyncCaptureTitles(string planItemId, string title)
        {
            foreach (var c in Data.Captures.Captures.Where(c => c.PlanVideoId == planItemId)) c.Title = title;
        }

        // ------------------------------------------------------------------
        // One path for creating new media
        // ------------------------------------------------------------------

        /// <summary>
        /// Create a video or photo that is not on the original list. Wherever it is created from
        /// (Covers, Outlines, the shot list, filming), it goes into the working copy in the given
        /// book and chapter as an "add" amendment, so it shows in the shot list marked new, in
        /// Changes, and is there to be placed in Outlines and assigned in Covers. Returns its id.
        /// </summary>
        public string CreateMedia(MediaKind kind, string title, string bookId, string chapterId, string reason = null)
        {
            if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("New media needs a title.");
            var ch = Data.FindChapter(chapterId);
            if (ch != null) bookId = ch.BookId;
            if (Data.FindBook(bookId) == null) throw new ArgumentException("New media needs a book.");
            var id = Ids.New(kind == MediaKind.Photo ? "pha" : "va");
            AddAmendment(new Amendment
            {
                Type = AmendmentType.Add, Reason = reason, NewMedia = kind == MediaKind.Photo ? MediaKind.Photo : (MediaKind?)null,
                Targets = new List<string>(), Results = new List<string> { id },
                NewTitle = title.Trim(), NewBookId = bookId, NewChapterId = chapterId,
            });
            return id;
        }

        /// <summary>Move a photo to another book (and chapter) in the working copy. The original list keeps it where it was printed.</summary>
        public Amendment MovePhoto(string photoId, string bookId, string chapterId, string reason = null)
        {
            if (Plan.FindPhoto(photoId) == null) throw new KeyNotFoundException("No photo " + photoId);
            return AddAmendment(new Amendment
            {
                Type = AmendmentType.Move, Reason = reason,
                Targets = new List<string> { photoId }, Results = new List<string> { photoId },
                NewBookId = bookId, NewChapterId = chapterId,
            });
        }

        /// <summary>Undo "got it": every record that says the photo was captured is withdrawn. Returns how many.</summary>
        public int UncapturePhoto(string photoId)
        {
            int n = 0;
            foreach (var c in Data.Captures.Captures)
                foreach (var cp in c.Photos ?? new List<CapturePhoto>())
                    if (cp.PhotoId == photoId && cp.Captured) { cp.Captured = false; n++; }
            foreach (var pc in Data.Captures.PhotoCaptures.Where(p => p.PhotoId == photoId).ToList()) { RemovePhotoCapture(pc.Id); n++; }
            MarkDirty(DocumentKind.Captures);
            return n;
        }

        // ------------------------------------------------------------------
        // Summary: the day and time of an entry
        // ------------------------------------------------------------------

        /// <summary>Move a capture or photo capture to another day. It goes to the bottom of that day; both days are renumbered.</summary>
        public void MoveEntryToDay(string entryId, string dayId)
        {
            if (Data.FindDay(dayId) == null) throw new KeyNotFoundException("No day " + dayId);
            var cap = Data.FindCapture(entryId);
            var pc = cap == null ? Data.FindPhotoCapture(entryId) : null;
            if (cap == null && pc == null) throw new KeyNotFoundException("No summary entry " + entryId);
            var from = cap != null ? cap.DayId : pc.DayId;
            if (from == dayId) return;
            var next = Queries.NextCapturedOrder(dayId);
            if (cap != null) { cap.DayId = dayId; cap.CapturedOrder = next; }
            else { pc.DayId = dayId; pc.CapturedOrder = next; pc.AfterCaptureId = null; pc.Section = PhotoCaptureSection.AdditionalPhotography; }
            MarkDirty(DocumentKind.Captures);
            if (from != null) ReorderDay(from, new List<string>());
        }

        /// <summary>Set the time of day of an entry, keeping its date (the day's date when it had no timestamp).</summary>
        public void SetEntryTime(string entryId, int hour, int minute)
        {
            var cap = Data.FindCapture(entryId);
            var pc = cap == null ? Data.FindPhotoCapture(entryId) : null;
            if (cap == null && pc == null) throw new KeyNotFoundException("No summary entry " + entryId);
            var at = cap != null ? cap.At : pc.At;
            var day = Data.FindDay(cap != null ? cap.DayId : pc.DayId);
            DateTime date;
            var parsed = PlanResolver.ParseTimestamp(at);
            if (day != null && DateTime.TryParseExact(day.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dd)) date = dd;
            else date = parsed?.LocalDateTime.Date ?? DateTime.Now.Date;
            var local = new DateTime(date.Year, date.Month, date.Day, Math.Max(0, Math.Min(23, hour)), Math.Max(0, Math.Min(59, minute)), 0, DateTimeKind.Local);
            var text = local.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
            if (cap != null) cap.At = text; else pc.At = text;
            MarkDirty(DocumentKind.Captures);
        }

        /// <summary>"9:40", "09:40", "9:40 am", "2 PM", "1430" → hour and minute. False when it is not a time.</summary>
        public static bool TryParseTime(string text, out int hour, out int minute)
        {
            hour = 0; minute = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var t = text.Trim().ToLowerInvariant().Replace(".", "");
            bool pm = t.EndsWith("pm") || t.EndsWith("p"), am = t.EndsWith("am") || t.EndsWith("a");
            t = t.TrimEnd('a', 'p', 'm', ' ');
            string hs, ms = "0";
            if (t.Contains(":")) { var parts = t.Split(':'); if (parts.Length != 2) return false; hs = parts[0]; ms = parts[1]; }
            else if (t.Length > 2 && t.All(char.IsDigit)) { hs = t.Substring(0, t.Length - 2); ms = t.Substring(t.Length - 2); }
            else hs = t;
            if (!int.TryParse(hs, out hour) || !int.TryParse(ms, out minute)) return false;
            if (pm && hour < 12) hour += 12;
            if (am && hour == 12) hour = 0;
            return hour >= 0 && hour <= 23 && minute >= 0 && minute <= 59;
        }
    }
}

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MediaTrip.Model
{
    public enum AmendmentType { Rename, Combine, Split, Drop, Move, Add, Revise }
    /// <summary>What an "add" amendment creates. Absent means a video.</summary>
    public enum MediaKind { Video, Photo }
    /// <summary>A correction fixes a transcription mistake in the original; a change is a real change made during the trip.</summary>
    public enum ChangeRecordKind { Correction, Change }
    /// <summary>What an outline placement points at: something shot (a capture or photo capture), or something on the working shot list that may not be shot yet.</summary>
    public enum MediaRefKind { Capture, PhotoCapture, PlannedPhoto, PlannedVideo }
    public enum PhotoCaptureSection { UnderVideo, AdditionalPhotography }
    public enum AssignmentSource { Auto, Manual }

    /// <summary>captures.json: amendments to the plan, the capture log, outline assignments.</summary>
    public class CapturesDocument : DocumentBase
    {
        public List<Amendment> Amendments { get; set; } = new List<Amendment>();
        public List<Capture> Captures { get; set; } = new List<Capture>();
        public List<PhotoCapture> PhotoCaptures { get; set; } = new List<PhotoCapture>();
        public List<OutlineAssignment> OutlineAssignments { get; set; } = new List<OutlineAssignment>();
        /// <summary>Cover and chapter-hero slots filled in the field. The planned hero stays in the shot list untouched.</summary>
        public List<HeroAssignment> HeroAssignments { get; set; } = new List<HeroAssignment>();
        /// <summary>Notes hung off a chapter (sectionId null) or off one of its outline sections.</summary>
        public List<PlaceNote> Notes { get; set; } = new List<PlaceNote>();
        /// <summary>
        /// The light log: corrections to the original (a transcription fix: field, before, after)
        /// and structural changes that are not amendments (a chapter renamed or removed).
        /// </summary>
        public List<ChangeRecord> Edits { get; set; } = new List<ChangeRecord>();
        /// <summary>Read only, for files written before notes were unified; folded into <see cref="Notes"/> on load.</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public List<SectionNote> SectionNotes { get; set; }
    }

    /// <summary>
    /// A change to the plan. Combine/split create new item IDs in <see cref="Results"/> and every
    /// ID in <see cref="Targets"/> becomes superseded, resolving to those results.
    /// Rename/move keep the same ID in both lists. Drop has no results. Add has no targets.
    /// Revise carries field-level before/after values for content edited in the working copy.
    /// Rename, move and drop may target a master-list photo as well as a video.
    /// </summary>
    public class Amendment
    {
        public string Id { get; set; }
        public AmendmentType Type { get; set; }
        /// <summary>ISO-8601 timestamp.</summary>
        public string At { get; set; }
        public string Reason { get; set; }
        public List<string> Targets { get; set; } = new List<string>();
        public List<string> Results { get; set; } = new List<string>();
        public string NewTitle { get; set; }
        /// <summary>Split only: one title per result, in order.</summary>
        public List<string> NewTitles { get; set; }
        public string NewBookId { get; set; }
        public string NewChapterId { get; set; }
        /// <summary>Add only: what is created. Absent means a video.</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public MediaKind? NewMedia { get; set; }
        /// <summary>Revise only: the fields that changed.</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public List<FieldChange> Changes { get; set; }
        [JsonExtensionData] public IDictionary<string, JToken> Extra { get; set; }
    }

    /// <summary>One field of a revise amendment or a change record. Values are JSON: text, a list of ids, or a node tree.</summary>
    public class FieldChange
    {
        public const string Title = "title", Scene = "sceneDescription", SmeIds = "smeIds", SmeText = "smeText",
            Notes = "notes", PhotoRefs = "photoRefs", Description = "description", Name = "name", Number = "number",
            Chapter = "chapterId", HeroType = "heroType", Deleted = "deleted", Added = "added", Order = "order";

        public string Field { get; set; }
        public JToken Before { get; set; }
        public JToken After { get; set; }
        [JsonExtensionData] public IDictionary<string, JToken> Extra { get; set; }
    }

    /// <summary>An entry of the light log (see <see cref="CapturesDocument.Edits"/>).</summary>
    public class ChangeRecord
    {
        public string Id { get; set; }
        public string At { get; set; }
        public ChangeRecordKind Kind { get; set; }
        /// <summary>"video", "photo", "chapter", "section".</summary>
        public string Entity { get; set; }
        public string EntityId { get; set; }
        /// <summary>What the thing was called when the record was made, so the log still reads after a delete.</summary>
        public string Label { get; set; }
        public string Field { get; set; }
        public JToken Before { get; set; }
        public JToken After { get; set; }
        [JsonExtensionData] public IDictionary<string, JToken> Extra { get; set; }
    }

    /// <summary>One video actually shot. The Media Trip Summary is this list grouped by day.</summary>
    public class Capture
    {
        public string Id { get; set; }
        public string DayId { get; set; }
        /// <summary>Order shot that day, which is the summary's ordering.</summary>
        public int CapturedOrder { get; set; }
        /// <summary>When it was logged (ISO-8601). Optional; older files do not have it.</summary>
        public string At { get; set; }
        /// <summary>Plan item ID (planned video or an amendment result). Null if unplanned.</summary>
        public string PlanVideoId { get; set; }
        /// <summary>Original shot-list number, kept so drift is visible.</summary>
        public int? PlannedNumber { get; set; }
        /// <summary>
        /// Set when the plan item this was shot against was removed (deleted, dropped, or an
        /// add undone) and the capture was kept: "video 4: Try Step Verification". The capture
        /// is unplanned from then on, and this says what it used to be.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string WasPlannedAs { get; set; }
        public string Title { get; set; }
        public string BookId { get; set; }
        public string ChapterId { get; set; }
        public string Description { get; set; }
        public string SceneDescription { get; set; }
        /// <summary>HPI = Human Performance Improvement. Free text; never a fixed picker.</summary>
        public string HpiToolSuggestion { get; set; }
        public List<string> Keywords { get; set; } = new List<string>();
        /// <summary>Free text, "N/A" when none. Yes/no is derived; see <see cref="HasBRoll"/>.</summary>
        public string BRoll { get; set; }
        public int? CameraCount { get; set; }
        public List<CapturePerson> People { get; set; } = new List<CapturePerson>();
        public string Location { get; set; }
        public string Notes { get; set; }
        public List<CapturePhoto> Photos { get; set; } = new List<CapturePhoto>();
        [JsonExtensionData] public IDictionary<string, JToken> Extra { get; set; }

        [JsonIgnore] public bool IsUnplanned => string.IsNullOrEmpty(PlanVideoId);

        /// <summary>Derived B-roll yes/no. Empty, "N/A", "NA", "none" and "-" all mean no.</summary>
        [JsonIgnore]
        public bool HasBRoll
        {
            get
            {
                if (string.IsNullOrWhiteSpace(BRoll)) return false;
                var t = BRoll.Trim();
                return !(t.Equals("N/A", StringComparison.OrdinalIgnoreCase)
                         || t.Equals("NA", StringComparison.OrdinalIgnoreCase)
                         || t.Equals("none", StringComparison.OrdinalIgnoreCase)
                         || t == "-");
            }
        }
    }

    public class CapturePerson
    {
        /// <summary>Null when the person was typed on site and is not in the registry.</summary>
        public string PersonId { get; set; }
        public string Name { get; set; }
        public string Title { get; set; }
        [JsonExtensionData] public IDictionary<string, JToken> Extra { get; set; }
    }

    /// <summary>A photo taken as part of a video capture: a master-list ref, or free text.</summary>
    public class CapturePhoto
    {
        public string PhotoId { get; set; }
        public string Text { get; set; }
        public bool Captured { get; set; } = true;
        [JsonExtensionData] public IDictionary<string, JToken> Extra { get; set; }
    }

    /// <summary>A photo capture not attached to a video ("Photos" after video N, or "Additional photography").</summary>
    public class PhotoCapture
    {
        public string Id { get; set; }
        public string DayId { get; set; }
        public int CapturedOrder { get; set; }
        /// <summary>When it was logged (ISO-8601). Optional.</summary>
        public string At { get; set; }
        public string PhotoId { get; set; }
        public string Text { get; set; }
        /// <summary>Set when the planned photo this recorded was removed and the record kept: "photo: Detail: retrieval line".</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string WasPlannedAs { get; set; }
        public string AfterCaptureId { get; set; }
        public PhotoCaptureSection Section { get; set; } = PhotoCaptureSection.AdditionalPhotography;
        [JsonExtensionData] public IDictionary<string, JToken> Extra { get; set; }
    }

    public class MediaRef
    {
        public MediaRefKind Kind { get; set; }
        public string Id { get; set; }
        [JsonExtensionData] public IDictionary<string, JToken> Extra { get; set; }

        public MediaRef() { }
        public MediaRef(MediaRefKind kind, string id) { Kind = kind; Id = id; }
    }

    /// <summary>Where a piece of media sits in a book outline. Auto guesses stay unconfirmed until tapped.</summary>
    public class OutlineAssignment
    {
        public string Id { get; set; }
        public MediaRef MediaRef { get; set; }
        public string BookId { get; set; }
        public string ChapterId { get; set; }
        public string SectionId { get; set; }
        public string NodeId { get; set; }
        public AssignmentSource Source { get; set; } = AssignmentSource.Manual;
        /// <summary>Fuzzy score when <see cref="Source"/> is Auto; null otherwise.</summary>
        public double? Confidence { get; set; }
        public bool Confirmed { get; set; }
        [JsonExtensionData] public IDictionary<string, JToken> Extra { get; set; }
    }

    /// <summary>
    /// What fills a hero slot (a book cover when <see cref="ChapterId"/> is null, else that
    /// chapter's hero): a master-list photo by id, or a new photo described on the spot.
    /// The planned hero photo is not changed; planned and assigned are shown side by side.
    /// </summary>
    public class HeroAssignment
    {
        public string Id { get; set; }
        public string BookId { get; set; }
        /// <summary>Null for the book cover slot.</summary>
        public string ChapterId { get; set; }
        /// <summary>Master-list photo, or null when <see cref="Text"/> describes a new one.</summary>
        public string PhotoId { get; set; }
        public string Text { get; set; }
        public string At { get; set; }
        /// <summary>The photo capture logged for a new photo, when one was.</summary>
        public string PhotoCaptureId { get; set; }
        [JsonExtensionData] public IDictionary<string, JToken> Extra { get; set; }
    }

    /// <summary>Notes on a chapter as a whole (sectionId null) or on one outline section. Covers and Outlines edit the same chapter-level note.</summary>
    public class PlaceNote
    {
        public string BookId { get; set; }
        public string ChapterId { get; set; }
        public string SectionId { get; set; }
        public string Text { get; set; }
        [JsonExtensionData] public IDictionary<string, JToken> Extra { get; set; }
    }

    /// <summary>Legacy shape of a section note; see <see cref="PlaceNote"/>.</summary>
    public class SectionNote
    {
        public string BookId { get; set; }
        public string SectionId { get; set; }
        public string Text { get; set; }
        [JsonExtensionData] public IDictionary<string, JToken> Extra { get; set; }
    }
}

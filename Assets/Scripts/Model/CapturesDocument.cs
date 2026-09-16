using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MediaTrip.Model
{
    public enum AmendmentType { Rename, Combine, Split, Drop, Move, Add }
    public enum MediaRefKind { Capture, PhotoCapture, PlannedPhoto }
    public enum PhotoCaptureSection { UnderVideo, AdditionalPhotography }
    public enum AssignmentSource { Auto, Manual }

    /// <summary>captures.json: amendments to the plan, the capture log, outline assignments.</summary>
    public class CapturesDocument : DocumentBase
    {
        public List<Amendment> Amendments { get; set; } = new List<Amendment>();
        public List<Capture> Captures { get; set; } = new List<Capture>();
        public List<PhotoCapture> PhotoCaptures { get; set; } = new List<PhotoCapture>();
        public List<OutlineAssignment> OutlineAssignments { get; set; } = new List<OutlineAssignment>();
    }

    /// <summary>
    /// A change to the plan. Combine/split create new item IDs in <see cref="Results"/> and every
    /// ID in <see cref="Targets"/> becomes superseded, resolving to those results.
    /// Rename/move keep the same ID in both lists. Drop has no results. Add has no targets.
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
        [JsonExtensionData] public IDictionary<string, JToken> Extra { get; set; }
    }

    /// <summary>One video actually shot. The Media Trip Summary is this list grouped by day.</summary>
    public class Capture
    {
        public string Id { get; set; }
        public string DayId { get; set; }
        /// <summary>Order shot that day, which is the summary's ordering.</summary>
        public int CapturedOrder { get; set; }
        /// <summary>Plan item ID (planned video or an amendment result). Null if unplanned.</summary>
        public string PlanVideoId { get; set; }
        /// <summary>Original shot-list number, kept so drift is visible.</summary>
        public int? PlannedNumber { get; set; }
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
        public string PhotoId { get; set; }
        public string Text { get; set; }
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
}

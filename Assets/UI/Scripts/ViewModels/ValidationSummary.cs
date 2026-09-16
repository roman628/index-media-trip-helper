using System.Collections.Generic;
using System.Linq;
using MediaTrip.Model;
using MediaTrip.Persistence;
using MediaTrip.Validation;

namespace MediaTrip.UI.ViewModels
{
    /// <summary>
    /// Validation issues shaped for the UI: the top-bar badge, per-entity counts for tree
    /// badges, which authoring tab fixes an issue, and which document to highlight in the
    /// JSON inspector.
    /// </summary>
    public sealed class ValidationSummary
    {
        public List<ValidationIssue> Issues;
        public int Errors => Issues.Count(i => i.Severity == IssueSeverity.Error);
        public int Warnings => Issues.Count(i => i.Severity == IssueSeverity.Warning);
        public bool Any => Issues.Count > 0;

        private readonly Dictionary<string, List<ValidationIssue>> _byEntity = new Dictionary<string, List<ValidationIssue>>();

        public static ValidationSummary Of(TripData data) => new ValidationSummary(TripValidator.Validate(data));

        public ValidationSummary(List<ValidationIssue> issues)
        {
            Issues = issues ?? new List<ValidationIssue>();
            foreach (var i in Issues)
            {
                if (string.IsNullOrEmpty(i.EntityId)) continue;
                if (!_byEntity.TryGetValue(i.EntityId, out var list)) _byEntity[i.EntityId] = list = new List<ValidationIssue>();
                list.Add(i);
            }
        }

        public List<ValidationIssue> For(string entityId) =>
            entityId != null && _byEntity.TryGetValue(entityId, out var l) ? l : new List<ValidationIssue>();

        public int CountFor(string entityId) => For(entityId).Count;

        public List<ValidationIssue> InDocument(DocumentKind kind, string bookId = null) =>
            Issues.Where(i => i.Document == kind && (kind != DocumentKind.Outline || bookId == null || i.BookId == bookId)).ToList();

        /// <summary>"3 errors" or "2 warnings" for the badge; null when clean.</summary>
        public string BadgeText
        {
            get
            {
                if (Errors > 0) return Errors + (Errors == 1 ? " error" : " errors");
                if (Warnings > 0) return Warnings + (Warnings == 1 ? " warning" : " warnings");
                return null;
            }
        }

        /// <summary>Which authoring screen fixes an issue, from the entity it points at.</summary>
        public static Screen FixScreen(ValidationIssue i, TripData data)
        {
            if (i.EntityId != null)
            {
                if (data.FindVideo(i.EntityId) != null || data.FindPhoto(i.EntityId) != null || data.FindChapter(i.EntityId) != null) return Screen.ShotList;
                if (data.FindPerson(i.EntityId) != null) return Screen.People;
                if (data.FindCapture(i.EntityId) != null || data.FindAmendment(i.EntityId) != null || data.FindPhotoCapture(i.EntityId) != null) return Screen.Amend;
                if (data.FindBook(i.EntityId) != null || data.FindDay(i.EntityId) != null) return Screen.Trip;
                if (data.Outlines.ContainsKey(i.EntityId)) return Screen.Outline;
            }
            switch (i.Document)
            {
                case DocumentKind.ShotList: return Screen.ShotList;
                case DocumentKind.Trip: return Screen.Trip;
                case DocumentKind.Captures: return Screen.Amend;
                case DocumentKind.Outline: return Screen.Outline;
                default: return Screen.Json;
            }
        }

        public static string FixLabel(Screen s) => s == Screen.ShotList ? "Shot list" : s == Screen.People ? "People" : s == Screen.Amend ? "Amendments" : s == Screen.Outline ? "Outline" : s == Screen.Trip ? "Trip" : "JSON";
    }
}

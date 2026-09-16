using System;
using System.Collections.Generic;
using System.Linq;
using MediaTrip.Model;

namespace MediaTrip.Authoring
{
    /// <summary>Something outside the plan that points at an entity ID.</summary>
    public class PlanReference
    {
        /// <summary>"capture", "photoCapture", "amendment", "outlineAssignment", "video", "bookTeam".</summary>
        public string Kind;
        /// <summary>ID of the referencing entity.</summary>
        public string Id;
        public string Field;
        public string Description;
        public override string ToString() => $"{Kind} {Id} ({Field})";
    }

    /// <summary>
    /// Raised when a structural edit would orphan something recorded in the field. Once a
    /// capture or amendment points at a plan item, the plan is the printed truth people hold
    /// on paper; changing it silently would leave the paper, the capture log and the app
    /// disagreeing with no record. The fix is an amendment (drop, move, rename), which is the
    /// audit trail for exactly that. <see cref="Suggestion"/> says which one.
    /// </summary>
    public class PlanEditBlockedException : InvalidOperationException
    {
        public string EntityId { get; }
        public IReadOnlyList<PlanReference> References { get; }
        public string Suggestion { get; }

        public PlanEditBlockedException(string entityId, IReadOnlyList<PlanReference> references, string suggestion, string what)
            : base($"{what} is blocked: '{entityId}' is referenced by {Describe(references)}. {suggestion}")
        {
            EntityId = entityId;
            References = references;
            Suggestion = suggestion;
        }

        private static string Describe(IReadOnlyList<PlanReference> refs) =>
            refs == null || refs.Count == 0 ? "nothing" : string.Join(", ", refs.Take(5)) + (refs.Count > 5 ? $" and {refs.Count - 5} more" : "");
    }

    /// <summary>Finds everything in captures.json, plus people references, that points at an ID.</summary>
    public static class ReferenceFinder
    {
        public static List<PlanReference> Find(TripData d, string id)
        {
            var refs = new List<PlanReference>();
            if (string.IsNullOrEmpty(id)) return refs;
            void Add(string kind, string refId, string field, string desc) =>
                refs.Add(new PlanReference { Kind = kind, Id = refId, Field = field, Description = desc });

            foreach (var c in d.Captures.Captures)
            {
                if (c.PlanVideoId == id) Add("capture", c.Id, "planVideoId", c.Title);
                if (c.BookId == id) Add("capture", c.Id, "bookId", c.Title);
                if (c.ChapterId == id) Add("capture", c.Id, "chapterId", c.Title);
                if (c.DayId == id) Add("capture", c.Id, "dayId", c.Title);
                foreach (var p in c.Photos ?? Enumerable.Empty<CapturePhoto>())
                    if (p.PhotoId == id) Add("capture", c.Id, "photos.photoId", c.Title);
                foreach (var p in c.People ?? Enumerable.Empty<CapturePerson>())
                    if (p.PersonId == id) Add("capture", c.Id, "people.personId", c.Title);
            }
            foreach (var pc in d.Captures.PhotoCaptures)
            {
                if (pc.PhotoId == id) Add("photoCapture", pc.Id, "photoId", pc.Text);
                if (pc.AfterCaptureId == id) Add("photoCapture", pc.Id, "afterCaptureId", pc.Text);
                if (pc.DayId == id) Add("photoCapture", pc.Id, "dayId", pc.Text);
            }
            foreach (var am in d.Captures.Amendments)
            {
                if ((am.Targets ?? new List<string>()).Contains(id)) Add("amendment", am.Id, "targets", am.Type + " " + am.NewTitle);
                if ((am.Results ?? new List<string>()).Contains(id)) Add("amendment", am.Id, "results", am.Type + " " + am.NewTitle);
                if (am.NewBookId == id) Add("amendment", am.Id, "newBookId", am.Type.ToString());
                if (am.NewChapterId == id) Add("amendment", am.Id, "newChapterId", am.Type.ToString());
            }
            foreach (var a in d.Captures.OutlineAssignments)
            {
                if (a.MediaRef?.Id == id) Add("outlineAssignment", a.Id, "mediaRef.id", a.MediaRef.Kind.ToString());
                if (a.BookId == id) Add("outlineAssignment", a.Id, "bookId", null);
                if (a.ChapterId == id) Add("outlineAssignment", a.Id, "chapterId", null);
                if (a.SectionId == id) Add("outlineAssignment", a.Id, "sectionId", null);
                if (a.NodeId == id) Add("outlineAssignment", a.Id, "nodeId", null);
            }
            foreach (var v in d.ShotList.Videos)
                if ((v.SmeIds ?? new List<string>()).Contains(id)) Add("video", v.Id, "smeIds", v.Title);
            foreach (var b in d.Trip.Books)
                if ((b.Team?.MemberIds ?? new List<string>()).Contains(id)) Add("bookTeam", b.Id, "team.memberIds", b.Name);
            return refs;
        }

        /// <summary>Outline assignments that point at an outline chapter or section of one book.</summary>
        public static List<PlanReference> FindOutlineRefs(TripData d, string bookId, string chapterId = null, string sectionId = null)
        {
            var refs = new List<PlanReference>();
            foreach (var a in d.Captures.OutlineAssignments)
            {
                if (a.BookId != bookId) continue;
                bool hit = sectionId != null ? a.SectionId == sectionId : chapterId != null && a.ChapterId == chapterId;
                if (hit) refs.Add(new PlanReference { Kind = "outlineAssignment", Id = a.Id, Field = sectionId != null ? "sectionId" : "chapterId", Description = a.MediaRef?.Kind + " " + a.MediaRef?.Id });
            }
            return refs;
        }

        public static void ThrowIfReferenced(TripData d, string id, string what, string suggestion)
        {
            var refs = Find(d, id);
            if (refs.Count > 0) throw new PlanEditBlockedException(id, refs, suggestion, what);
        }
    }
}

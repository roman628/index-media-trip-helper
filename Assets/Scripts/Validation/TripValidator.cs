using System.Collections.Generic;
using System.Linq;
using MediaTrip.Model;

namespace MediaTrip.Validation
{
    public enum IssueSeverity { Warning, Error }

    public enum ValidationKind
    {
        Other,
        DuplicateId,
        MissingId,
        DanglingReference,
        /// <summary>A video with no chapter at all (as opposed to an unknown one).</summary>
        VideoWithoutChapter,
        /// <summary>Two planned videos share a number.</summary>
        DuplicateNumber,
    }

    public class ValidationIssue
    {
        public IssueSeverity Severity;
        public ValidationKind Kind;
        public string Message;
        public string EntityId;
        public override string ToString() => $"{Severity}/{Kind}: {Message}";
    }

    /// <summary>
    /// Referential-integrity checks. Nothing here throws; a trip with issues still loads and
    /// works, the UI just gets a list to show. Free-text people and free-text photos are
    /// legitimate and are not reported.
    /// </summary>
    public static class TripValidator
    {
        public static List<ValidationIssue> Validate(TripData d)
        {
            var issues = new List<ValidationIssue>();
            void Err(string msg, string id = null, ValidationKind kind = ValidationKind.DanglingReference) =>
                issues.Add(new ValidationIssue { Severity = IssueSeverity.Error, Kind = kind, Message = msg, EntityId = id });
            void Warn(string msg, string id = null, ValidationKind kind = ValidationKind.Other) =>
                issues.Add(new ValidationIssue { Severity = IssueSeverity.Warning, Kind = kind, Message = msg, EntityId = id });

            var bookIds = new HashSet<string>(d.Trip.Books.Select(b => b.Id));
            var chapterIds = new HashSet<string>(d.ShotList.Chapters.Select(c => c.Id));
            var personIds = new HashSet<string>(d.Trip.People.Select(p => p.Id));
            var dayIds = new HashSet<string>(d.Trip.Days.Select(x => x.Id));
            var photoIds = new HashSet<string>(d.ShotList.Photos.Select(p => p.Id));
            var videoIds = new HashSet<string>(d.ShotList.Videos.Select(v => v.Id));
            var captureIds = new HashSet<string>(d.Captures.Captures.Select(c => c.Id));
            var photoCaptureIds = new HashSet<string>(d.Captures.PhotoCaptures.Select(c => c.Id));

            CheckDuplicates(d.Trip.People.Select(p => p.Id), "person", Err);
            CheckDuplicates(d.Trip.Books.Select(b => b.Id), "book", Err);
            CheckDuplicates(d.Trip.Days.Select(x => x.Id), "day", Err);
            CheckDuplicates(d.ShotList.Chapters.Select(c => c.Id), "chapter", Err);
            CheckDuplicates(d.ShotList.Videos.Select(v => v.Id), "video", Err);
            CheckDuplicates(d.ShotList.Photos.Select(p => p.Id), "photo", Err);
            CheckDuplicates(d.Captures.Amendments.Select(a => a.Id), "amendment", Err);
            CheckDuplicates(d.Captures.Captures.Select(c => c.Id), "capture", Err);
            CheckDuplicates(d.Captures.PhotoCaptures.Select(c => c.Id), "photoCapture", Err);
            CheckDuplicates(d.Captures.OutlineAssignments.Select(a => a.Id), "outlineAssignment", Err);

            var numbers = d.ShotList.Videos.GroupBy(v => v.Number).Where(g => g.Count() > 1);
            foreach (var g in numbers) Warn($"Video number {g.Key} is used by {g.Count()} videos.", null, ValidationKind.DuplicateNumber);

            foreach (var ch in d.ShotList.Chapters)
                if (!bookIds.Contains(ch.BookId ?? "")) Err($"Chapter '{ch.Id}' references unknown book '{ch.BookId}'.", ch.Id);

            foreach (var v in d.ShotList.Videos)
            {
                if (!bookIds.Contains(v.BookId ?? "")) Err($"Video {v.Number} references unknown book '{v.BookId}'.", v.Id);
                if (string.IsNullOrEmpty(v.ChapterId)) Err($"Video {v.Number} ('{v.Title}') has no chapter.", v.Id, ValidationKind.VideoWithoutChapter);
                else if (!chapterIds.Contains(v.ChapterId)) Err($"Video {v.Number} references unknown chapter '{v.ChapterId}'.", v.Id);
                foreach (var pid in v.SmeIds ?? Enumerable.Empty<string>())
                    if (!personIds.Contains(pid)) Warn($"Video {v.Number} SME '{pid}' is not in the people registry.", v.Id, ValidationKind.DanglingReference);
                foreach (var ph in v.PhotoRefs ?? Enumerable.Empty<string>())
                    if (!photoIds.Contains(ph)) Err($"Video {v.Number} photoRef '{ph}' is not in the master photo list.", v.Id);
            }

            foreach (var p in d.ShotList.Photos)
            {
                if (!bookIds.Contains(p.BookId ?? "")) Err($"Photo '{p.Id}' references unknown book '{p.BookId}'.", p.Id);
                if (p.ChapterId != null && !chapterIds.Contains(p.ChapterId)) Err($"Photo '{p.Id}' references unknown chapter '{p.ChapterId}'.", p.Id);
                if (p.HeroType == HeroType.ChapterHero && p.ChapterId == null) Warn($"Photo '{p.Id}' is a chapter hero with no chapterId.", p.Id);
            }

            var planIds = new HashSet<string>(videoIds);
            foreach (var am in d.Captures.Amendments)
            {
                foreach (var t in am.Targets ?? Enumerable.Empty<string>())
                    if (!planIds.Contains(t) && !photoIds.Contains(t)) Err($"Amendment '{am.Id}' ({am.Type}) targets unknown item '{t}'.", am.Id);
                foreach (var r in am.Results ?? Enumerable.Empty<string>())
                    if (am.Type == AmendmentType.Combine || am.Type == AmendmentType.Split || am.Type == AmendmentType.Add) planIds.Add(r);
                if (am.Type == AmendmentType.Add && string.IsNullOrEmpty(am.NewTitle) && (am.NewTitles == null || am.NewTitles.Count == 0))
                    Warn($"Amendment '{am.Id}' (add) has no title.", am.Id);
                if (am.NewBookId != null && !bookIds.Contains(am.NewBookId)) Err($"Amendment '{am.Id}' references unknown book '{am.NewBookId}'.", am.Id);
                if (am.NewChapterId != null && !chapterIds.Contains(am.NewChapterId)) Err($"Amendment '{am.Id}' references unknown chapter '{am.NewChapterId}'.", am.Id);
            }

            foreach (var c in d.Captures.Captures)
            {
                if (!dayIds.Contains(c.DayId ?? "")) Err($"Capture '{c.Id}' references unknown day '{c.DayId}'.", c.Id);
                if (c.PlanVideoId != null && !planIds.Contains(c.PlanVideoId)) Err($"Capture '{c.Id}' references unknown plan item '{c.PlanVideoId}'.", c.Id);
                if (c.BookId != null && !bookIds.Contains(c.BookId)) Err($"Capture '{c.Id}' references unknown book '{c.BookId}'.", c.Id);
                if (c.ChapterId != null && !chapterIds.Contains(c.ChapterId)) Err($"Capture '{c.Id}' references unknown chapter '{c.ChapterId}'.", c.Id);
                foreach (var cp in c.People ?? Enumerable.Empty<CapturePerson>())
                    if (cp.PersonId != null && !personIds.Contains(cp.PersonId)) Warn($"Capture '{c.Id}' person '{cp.PersonId}' is not in the registry.", c.Id, ValidationKind.DanglingReference);
                foreach (var cp in c.Photos ?? Enumerable.Empty<CapturePhoto>())
                {
                    if (cp.PhotoId != null && !photoIds.Contains(cp.PhotoId)) Err($"Capture '{c.Id}' photo '{cp.PhotoId}' is not in the master photo list.", c.Id);
                    if (cp.PhotoId == null && string.IsNullOrWhiteSpace(cp.Text)) Warn($"Capture '{c.Id}' has a photo entry with neither photoId nor text.", c.Id);
                }
            }

            foreach (var pc in d.Captures.PhotoCaptures)
            {
                if (!dayIds.Contains(pc.DayId ?? "")) Err($"Photo capture '{pc.Id}' references unknown day '{pc.DayId}'.", pc.Id);
                if (pc.PhotoId != null && !photoIds.Contains(pc.PhotoId)) Err($"Photo capture '{pc.Id}' photo '{pc.PhotoId}' is not in the master photo list.", pc.Id);
                if (pc.PhotoId == null && string.IsNullOrWhiteSpace(pc.Text)) Warn($"Photo capture '{pc.Id}' has neither photoId nor text.", pc.Id);
                if (pc.AfterCaptureId != null && !captureIds.Contains(pc.AfterCaptureId)) Err($"Photo capture '{pc.Id}' follows unknown capture '{pc.AfterCaptureId}'.", pc.Id);
            }

            foreach (var a in d.Captures.OutlineAssignments)
            {
                if (a.MediaRef == null || a.MediaRef.Id == null) { Err($"Assignment '{a.Id}' has no mediaRef.", a.Id, ValidationKind.Other); continue; }
                bool exists = a.MediaRef.Kind == MediaRefKind.Capture ? captureIds.Contains(a.MediaRef.Id)
                    : a.MediaRef.Kind == MediaRefKind.PhotoCapture ? photoCaptureIds.Contains(a.MediaRef.Id)
                    : photoIds.Contains(a.MediaRef.Id);
                if (!exists) Err($"Assignment '{a.Id}' references unknown {a.MediaRef.Kind} '{a.MediaRef.Id}'.", a.Id);
                if (!bookIds.Contains(a.BookId ?? "")) Err($"Assignment '{a.Id}' references unknown book '{a.BookId}'.", a.Id);
                var outline = d.FindOutline(a.BookId);
                if (outline == null) { Warn($"Assignment '{a.Id}' targets book '{a.BookId}' which has no outline yet.", a.Id); continue; }
                var chapter = outline.Chapters.FirstOrDefault(c => c.Id == a.ChapterId);
                if (a.ChapterId != null && chapter == null) Err($"Assignment '{a.Id}' references unknown outline chapter '{a.ChapterId}'.", a.Id);
                if (a.SectionId != null)
                {
                    var section = (chapter?.Sections ?? outline.Chapters.SelectMany(c => c.Sections)).FirstOrDefault(s => s.Id == a.SectionId);
                    if (section == null) Err($"Assignment '{a.Id}' references unknown outline section '{a.SectionId}'.", a.Id);
                    else if (a.NodeId != null && Node.Find(section.Nodes, a.NodeId) == null) Err($"Assignment '{a.Id}' references unknown node '{a.NodeId}'.", a.Id);
                }
            }

            foreach (var kv in d.Outlines)
            {
                if (!bookIds.Contains(kv.Key)) Warn($"Outline '{kv.Key}' does not match any book in trip.json.", kv.Key, ValidationKind.DanglingReference);
                var ids = kv.Value.Chapters.SelectMany(c => new[] { c.Id }.Concat(c.Sections.SelectMany(s => new[] { s.Id }.Concat(Node.Walk(s.Nodes).Select(n => n.node.Id)))));
                CheckDuplicates(ids, "outline " + kv.Key + " id", Err);
            }

            foreach (var b in d.Trip.Books)
                foreach (var pid in b.Team?.MemberIds ?? Enumerable.Empty<string>())
                    if (!personIds.Contains(pid)) Warn($"Book {b.Number} team member '{pid}' is not in the registry.", b.Id, ValidationKind.DanglingReference);

            return issues;
        }

        private static void CheckDuplicates(IEnumerable<string> ids, string what, System.Action<string, string, ValidationKind> report)
        {
            var seen = new HashSet<string>();
            foreach (var id in ids)
            {
                if (string.IsNullOrEmpty(id)) { report($"A {what} has no id.", null, ValidationKind.MissingId); continue; }
                if (!seen.Add(id)) report($"Duplicate {what} id '{id}'.", id, ValidationKind.DuplicateId);
            }
        }
    }
}

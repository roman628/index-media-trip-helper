using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MediaTrip.Model;
using MediaTrip.Persistence;
using MediaTrip.Session;
using MediaTrip.Status;
using Newtonsoft.Json.Linq;

namespace MediaTrip.Export
{
    /// <summary>What can be exported. The names are the ones the menu shows, so nobody has to guess which shot list a button means.</summary>
    public enum ExportKind
    {
        /// <summary>trip.json: identity, people, books, days.</summary>
        Trip,
        /// <summary>The shot list as printed.</summary>
        OriginalShotList,
        /// <summary>The shot list as it now stands: the original with every recorded change applied.</summary>
        WorkingShotList,
        /// <summary>What changed and why: recorded changes, and corrections to the original under their own heading.</summary>
        Changes,
        /// <summary>What was shot, by day (captures.json).</summary>
        MediaSummary,
        /// <summary>One book's outline.</summary>
        Outline,
    }

    public sealed class ExportRequest
    {
        public ExportKind Kind;
        /// <summary>Outline only.</summary>
        public string BookId;
        public ExportRequest(ExportKind kind, string bookId = null) { Kind = kind; BookId = bookId; }
    }

    public sealed class ExportFile
    {
        public string FileName;
        public byte[] Bytes;
    }

    /// <summary>
    /// One output format. JSON is the first; a Word writer matching the company's templates is
    /// the next, and only needs to implement this: it is handed the open trip and the kind of
    /// document wanted, and returns a file. Nothing else in the app knows what a format is.
    /// </summary>
    public interface ITripExportFormat
    {
        /// <summary>"json", "docx".</summary>
        string Id { get; }
        /// <summary>Shown when more than one format can write a kind: "JSON", "Word".</summary>
        string Label { get; }
        bool Supports(ExportKind kind);
        ExportFile Write(TripSession session, ExportRequest request);
    }

    public static class TripExports
    {
        private static readonly List<ITripExportFormat> Registered = new List<ITripExportFormat> { new JsonExportFormat() };

        public static IReadOnlyList<ITripExportFormat> Formats => Registered;

        /// <summary>Add a format (or replace the one with the same id).</summary>
        public static void Register(ITripExportFormat format)
        {
            Registered.RemoveAll(f => f.Id == format.Id);
            Registered.Add(format);
        }

        public static void Unregister(string formatId) => Registered.RemoveAll(f => f.Id == formatId);

        public static IEnumerable<ITripExportFormat> FormatsFor(ExportKind kind) => Registered.Where(f => f.Supports(kind));

        public static ExportFile Write(TripSession session, ExportRequest request, string formatId = "json")
        {
            var format = Registered.FirstOrDefault(f => f.Id == formatId && f.Supports(request.Kind))
                ?? throw new NotSupportedException("No " + formatId + " export for " + request.Kind);
            return format.Write(session, request);
        }

        /// <summary>The words on the button.</summary>
        public static string Label(ExportKind kind)
        {
            switch (kind)
            {
                case ExportKind.Trip: return "Export trip details";
                case ExportKind.OriginalShotList: return "Export original shot list";
                case ExportKind.WorkingShotList: return "Export working shot list";
                case ExportKind.Changes: return "Export changes";
                case ExportKind.MediaSummary: return "Export media summary";
                case ExportKind.Outline: return "Export this outline";
                default: return "Export";
            }
        }

        /// <summary>"ACME_WDG_P5_working-shot-list" (no extension).</summary>
        public static string BaseName(TripData d, ExportRequest r)
        {
            var id = d.Trip.Identity;
            var parts = new[] { id?.ClientAbbrev, id?.ProgramAbbrev, id?.PhaseNumber != null ? "P" + id.PhaseNumber : null }.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
            var trip = parts.Count > 0 ? string.Join("_", parts) : (d.TripId ?? "trip");
            string what;
            switch (r.Kind)
            {
                case ExportKind.Trip: what = "trip"; break;
                case ExportKind.OriginalShotList: what = "original-shot-list"; break;
                case ExportKind.WorkingShotList: what = "working-shot-list"; break;
                case ExportKind.Changes: what = "changes"; break;
                case ExportKind.MediaSummary: what = "media-summary"; break;
                default: what = "outline-" + (d.FindBook(r.BookId)?.Name ?? r.BookId); break;
            }
            return TripPaths.SanitizeFileName(trip + "_" + what);
        }
    }

    /// <summary>The JSON writer. Original shot list, media summary, trip and outline are the documents themselves, so they import back.</summary>
    public sealed class JsonExportFormat : ITripExportFormat
    {
        public string Id => "json";
        public string Label => "JSON";
        public bool Supports(ExportKind kind) => true;

        public ExportFile Write(TripSession s, ExportRequest r)
        {
            string text;
            switch (r.Kind)
            {
                case ExportKind.Trip: text = TripPackage.ExportDocumentJson(s.Data, DocumentKind.Trip); break;
                case ExportKind.OriginalShotList: text = TripPackage.ExportDocumentJson(s.Data, DocumentKind.ShotList); break;
                case ExportKind.MediaSummary: text = TripPackage.ExportDocumentJson(s.Data, DocumentKind.Captures); break;
                case ExportKind.Outline: text = TripPackage.ExportDocumentJson(s.Data, DocumentKind.Outline, r.BookId); break;
                case ExportKind.WorkingShotList: text = TripSaver.ToJson(WorkingShotList.Build(s)); break;
                case ExportKind.Changes: text = ChangesReport.Build(s).ToString(Newtonsoft.Json.Formatting.Indented); break;
                default: throw new NotSupportedException(r.Kind.ToString());
            }
            return new ExportFile { FileName = TripExports.BaseName(s.Data, r) + ".json", Bytes = new UTF8Encoding(false).GetBytes(text) };
        }
    }

    /// <summary>
    /// The working copy written out in the shot list's own shape: what still stands, with
    /// renames, moves, revisions, combined and added items applied. Each video carries how it
    /// is numbered on screen ("1+2", "5a", "new") and where it came from.
    /// </summary>
    public static class WorkingShotList
    {
        public static ShotListDocument Build(TripSession s)
        {
            var d = s.Data;
            var doc = new ShotListDocument { TripId = d.TripId, Source = "working copy", Chapters = TripJson.Clone(d.ShotList.Chapters) };
            foreach (var item in s.Plan.Items.Where(i => !i.IsDropped && !i.IsSuperseded))
            {
                var v = new Video
                {
                    Id = item.Id, Number = item.Number ?? 0, BookId = item.BookId, ChapterId = item.ChapterId, Title = item.Title,
                    SmeIds = item.SmeIds.ToList(), SmeText = item.SmeText, SceneDescription = item.SceneDescription,
                    Notes = Authoring.NodeTree.Clone(item.Notes ?? new List<Node>(), newIds: false), PhotoRefs = item.PhotoRefs.ToList(),
                    Extra = new Dictionary<string, JToken> { ["displayNumber"] = item.DisplayNumber, ["origin"] = item.Origin.ToString().ToLowerInvariant(), ["status"] = item.Status.ToString() },
                };
                if (item.Origin == PlanItemOrigin.Planned && item.OriginalTitle != item.Title) v.Extra["originalTitle"] = item.OriginalTitle;
                doc.Videos.Add(v);
            }
            foreach (var p in s.Plan.Photos.Where(p => p.Status != PhotoStatus.Dropped))
            {
                var copy = TripJson.Clone(p.Photo);
                copy.Description = p.Description;
                if (copy.Order == int.MaxValue) copy.Order = 0;
                copy.Extra = copy.Extra ?? new Dictionary<string, JToken>();
                if (p.IsNew) copy.Extra["origin"] = "added";
                copy.Extra["status"] = p.Status.ToString();
                doc.Photos.Add(copy);
            }
            return doc;
        }
    }

    /// <summary>Changes as one document: the recorded changes in time order, then the structural changes, then corrections to the original, kept apart.</summary>
    public static class ChangesReport
    {
        public static JObject Build(TripSession s)
        {
            var d = s.Data;
            var ser = TripJson.CreateSerializer();
            return new JObject
            {
                ["schemaVersion"] = Schema.CurrentVersion,
                ["tripId"] = d.TripId,
                ["amendments"] = JArray.FromObject(PlanResolver.OrderedAmendments(d.Captures.Amendments), ser),
                ["changes"] = JArray.FromObject(d.Captures.Edits.Where(e => e.Kind == ChangeRecordKind.Change).ToList(), ser),
                ["corrections"] = JArray.FromObject(d.Captures.Edits.Where(e => e.Kind == ChangeRecordKind.Correction).ToList(), ser),
            };
        }
    }
}

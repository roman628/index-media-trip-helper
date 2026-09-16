using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using MediaTrip.Model;
using MediaTrip.Session;
using MediaTrip.Validation;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MediaTrip.Persistence
{
    public enum ImportIssueKind
    {
        MissingFile, Parse, Schema, DuplicateId, DanglingReference, VideoWithoutChapter, Conflict, Other
    }

    public class ImportIssue
    {
        public IssueSeverity Severity;
        public ImportIssueKind Kind;
        public string File;
        public string EntityId;
        public string Message;
        public override string ToString() => $"{Severity}/{Kind} {File}: {Message}";
    }

    /// <summary>
    /// Result of a dry-run validation. Nothing has been written when you hold one of these.
    /// <see cref="CanImport"/> is false if any error is present; warnings never block.
    /// </summary>
    public class ImportReport
    {
        public string Source;
        public string TripId;
        public string DisplayName;
        public List<ImportIssue> Issues = new List<ImportIssue>();
        public List<string> Files = new List<string>();

        public bool CanImport => !Issues.Any(i => i.Severity == IssueSeverity.Error);
        public IEnumerable<ImportIssue> Errors => Issues.Where(i => i.Severity == IssueSeverity.Error);
        public IEnumerable<ImportIssue> Warnings => Issues.Where(i => i.Severity == IssueSeverity.Warning);

        /// <summary>A trip with the same ID already exists in the library. Importing needs overwrite: true.</summary>
        public bool ExistingTripConflict;
        public string ExistingFolder;

        public int Books, Chapters, Videos, Photos, Outlines, People, Days, Captures, PhotoCaptures, Amendments, Assignments;

        /// <summary>The parsed, in-memory trip (null when parsing failed). Never written by validation.</summary>
        public TripData Staged;
        /// <summary>For a single outline document import: which book it belongs to.</summary>
        public string OutlineBookId;
        /// <summary>Set when the validated text was a single document rather than a whole trip. The caller validates it against the open trip.</summary>
        public DocumentKind? SingleDocumentKind;
        /// <summary>True when the source was a whole trip (zip, folder or JSON bundle).</summary>
        public bool IsWholeTrip;
        /// <summary>Source format that was detected: "zip", "folder", "bundle", "document".</summary>
        public string Format;

        public override string ToString() =>
            $"{Source}: {(CanImport ? "OK" : "BLOCKED")} {Errors.Count()} errors, {Warnings.Count()} warnings; {Videos} videos, {Photos} photos, {Captures} captures";
    }

    public class ImportBlockedException : InvalidOperationException
    {
        public ImportReport Report { get; }
        public ImportBlockedException(ImportReport report)
            : base("Import blocked: " + string.Join("; ", report.Errors.Select(e => e.Message).Take(5)))
        {
            Report = report;
        }
    }

    /// <summary>
    /// Zip and single-document import/export. Every import is a dry run first: the whole
    /// package is parsed and validated in memory, and only a clean package is written, into a
    /// staging folder that is then swapped into place. A failed import leaves no trace.
    /// </summary>
    public static class TripPackage
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        // ------------------------------------------------------------------
        // Export
        // ------------------------------------------------------------------

        /// <summary>Write the whole trip to a zip (trip.json, shotlist.json, captures.json, outlines/*.json at the root).</summary>
        public static void ExportZip(TripData data, string zipPath)
        {
            var dir = Path.GetDirectoryName(zipPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = zipPath + ".tmp";
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                foreach (var (name, doc) in Documents(data))
                    AddEntry(zip, name, TripSaver.ToJson(doc));
            }
            if (File.Exists(zipPath)) File.Delete(zipPath);
            File.Move(tmp, zipPath);
        }

        public static void ExportZip(string tripFolder, string zipPath) => ExportZip(TripLoader.Load(tripFolder), zipPath);

        /// <summary>Zip if the platform's ZipArchive works; returns false with the error otherwise (caller falls back to a bundle).</summary>
        public static bool TryExportZip(TripData data, string zipPath, out string error)
        {
            try
            {
                ExportZip(data, zipPath);
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                try { if (File.Exists(zipPath + ".tmp")) File.Delete(zipPath + ".tmp"); } catch { /* ignore */ }
                return false;
            }
        }

        // ------------------------------------------------------------------
        // Single-file JSON bundle (clipboard transfer, zip fallback)
        // ------------------------------------------------------------------

        public const string BundleMarker = "mediaTripBundle";
        public const int BundleVersion = 1;

        /// <summary>The whole trip as one JSON object: { mediaTripBundle: 1, tripId, files: { "trip.json": {...}, ... } }.</summary>
        public static string ExportBundleJson(TripData data)
        {
            var files = new JObject();
            foreach (var (name, doc) in Documents(data))
                files[name] = TripJson.ParseObject(TripSaver.ToJson(doc));
            var bundle = new JObject
            {
                [BundleMarker] = BundleVersion,
                ["tripId"] = data.TripId,
                ["exportedAt"] = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
                ["files"] = files,
            };
            return bundle.ToString(Formatting.Indented);
        }

        public static void ExportBundle(TripData data, string path) => TripSaver.WriteAtomic(path, ExportBundleJson(data));

        /// <summary>Guess what a JSON object is from its keys: a bundle, or one of the four documents.</summary>
        public static DocumentKind? DetectDocumentKind(JObject obj)
        {
            if (obj == null) return null;
            if (obj[BundleMarker] != null) return null;
            if (obj["videos"] != null || obj["photos"] != null) return DocumentKind.ShotList;
            if (obj["captures"] != null || obj["amendments"] != null || obj["photoCaptures"] != null || obj["outlineAssignments"] != null) return DocumentKind.Captures;
            if (obj["bookId"] != null && obj["chapters"] != null) return DocumentKind.Outline;
            if (obj["people"] != null || obj["books"] != null || obj["identity"] != null || obj["days"] != null) return DocumentKind.Trip;
            if (obj["chapters"] != null) return DocumentKind.ShotList;
            return null;
        }

        public static bool IsBundleJson(string json)
        {
            try { return TripJson.ParseObject(json)[BundleMarker] != null; }
            catch { return false; }
        }

        /// <summary>
        /// Dry-run validate pasted or loaded JSON text. A bundle yields a whole-trip report; a
        /// single document yields a report with <see cref="ImportReport.SingleDocumentKind"/> set
        /// and no staged trip (validate it against the open trip with <see cref="ValidateDocument"/>).
        /// </summary>
        public static ImportReport ValidateJsonText(string json, string libraryRoot = null, string sourceName = null)
        {
            var report = new ImportReport { Source = sourceName ?? "JSON text" };
            JObject obj;
            try
            {
                obj = TripJson.ParseObject(json ?? "");
            }
            catch (Exception ex)
            {
                Add(report, IssueSeverity.Error, ImportIssueKind.Parse, sourceName, null, "Not valid JSON: " + ex.Message);
                return report;
            }

            if (obj[BundleMarker] != null)
            {
                report.Format = "bundle";
                report.IsWholeTrip = true;
                var version = obj[BundleMarker].Type == JTokenType.Integer ? obj[BundleMarker].Value<int>() : 0;
                if (version > BundleVersion)
                {
                    Add(report, IssueSeverity.Error, ImportIssueKind.Schema, sourceName, null, $"Bundle version {version} is newer than this app understands ({BundleVersion}).");
                    return report;
                }
                var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (obj["files"] is JObject fileObj)
                    foreach (var prop in fileObj.Properties())
                        files[prop.Name.Replace('\\', '/')] = prop.Value.ToString(Formatting.None);
                else
                    Add(report, IssueSeverity.Error, ImportIssueKind.MissingFile, sourceName, null, "Bundle has no 'files' object.");
                ValidateFiles(report, files, libraryRoot);
                return report;
            }

            var kind = DetectDocumentKind(obj);
            report.Format = "document";
            if (kind == null)
            {
                Add(report, IssueSeverity.Error, ImportIssueKind.Other, sourceName, null, "This JSON is not a trip bundle and does not look like trip.json, shotlist.json, captures.json or an outline.");
                return report;
            }
            report.SingleDocumentKind = kind;
            try
            {
                SchemaMigrator.Migrate(TripJson.ParseObject(json), kind.Value, sourceName);
            }
            catch (SchemaTooNewException ex)
            {
                Add(report, IssueSeverity.Error, ImportIssueKind.Schema, sourceName, null, ex.Message);
            }
            return report;
        }

        /// <summary>Validate a set of in-memory files (name → JSON text) as a whole trip.</summary>
        public static ImportReport ValidateFileSet(Dictionary<string, string> files, string libraryRoot = null, string sourceName = null)
        {
            var report = new ImportReport { Source = sourceName ?? "files", Format = "files", IsWholeTrip = true };
            ValidateFiles(report, new Dictionary<string, string>(files, StringComparer.OrdinalIgnoreCase), libraryRoot);
            return report;
        }

        /// <summary>Whole-trip import from bundle JSON text. Same all-or-nothing commit as the zip path.</summary>
        public static string ImportBundleJson(string json, bool overwrite = false, string libraryRoot = null, string sourceName = null)
        {
            var report = ValidateJsonText(json, libraryRoot, sourceName);
            if (!report.IsWholeTrip) throw new ImportBlockedException(report);
            return Commit(report, overwrite, libraryRoot);
        }

        /// <summary>JSON text of one document (outline needs the bookId).</summary>
        public static string ExportDocumentJson(TripData data, DocumentKind kind, string bookId = null)
        {
            switch (kind)
            {
                case DocumentKind.Trip: return TripSaver.ToJson(data.Trip);
                case DocumentKind.ShotList: return TripSaver.ToJson(data.ShotList);
                case DocumentKind.Captures: return TripSaver.ToJson(data.Captures);
                case DocumentKind.Outline:
                    var o = data.FindOutline(bookId) ?? throw new KeyNotFoundException("No outline for book " + bookId);
                    return TripSaver.ToJson(o);
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        public static void ExportDocument(TripData data, DocumentKind kind, string path, string bookId = null) =>
            TripSaver.WriteAtomic(path, ExportDocumentJson(data, kind, bookId));

        private static IEnumerable<(string name, DocumentBase doc)> Documents(TripData data)
        {
            yield return (TripLoader.TripFile, data.Trip);
            yield return (TripLoader.ShotListFile, data.ShotList);
            yield return (TripLoader.CapturesFile, data.Captures);
            foreach (var bookId in data.Outlines.Keys.OrderBy(k => k, StringComparer.Ordinal))
                yield return (TripLoader.OutlinesDir + "/" + TripSaver.OutlineFileName(data, bookId), data.Outlines[bookId]);
        }

        private static void AddEntry(ZipArchive zip, string name, string text)
        {
            var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            using (var s = entry.Open())
            using (var w = new StreamWriter(s, Utf8NoBom))
                w.Write(text);
        }

        // ------------------------------------------------------------------
        // Dry-run validation
        // ------------------------------------------------------------------

        /// <summary>Parse and validate a zip without writing anything.</summary>
        public static ImportReport ValidateZip(string zipPath, string libraryRoot = null)
        {
            var report = new ImportReport { Source = zipPath, Format = "zip", IsWholeTrip = true };
            if (!File.Exists(zipPath))
            {
                Add(report, IssueSeverity.Error, ImportIssueKind.MissingFile, null, null, "Zip file not found: " + zipPath);
                return report;
            }
            Dictionary<string, string> files;
            try
            {
                files = ReadZip(zipPath);
            }
            catch (Exception ex)
            {
                Add(report, IssueSeverity.Error, ImportIssueKind.Parse, null, null, "Not a readable zip: " + ex.Message);
                return report;
            }
            ValidateFiles(report, files, libraryRoot);
            return report;
        }

        /// <summary>Parse and validate a plain trip folder (e.g. the StreamingAssets sample) without writing anything.</summary>
        public static ImportReport ValidateFolder(string folder, string libraryRoot = null)
        {
            var report = new ImportReport { Source = folder, Format = "folder", IsWholeTrip = true };
            if (!Directory.Exists(folder))
            {
                Add(report, IssueSeverity.Error, ImportIssueKind.MissingFile, null, null, "Folder not found: " + folder);
                return report;
            }
            var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in new[] { TripLoader.TripFile, TripLoader.ShotListFile, TripLoader.CapturesFile })
            {
                var p = Path.Combine(folder, name);
                if (File.Exists(p)) files[name] = File.ReadAllText(p);
            }
            var outlines = Path.Combine(folder, TripLoader.OutlinesDir);
            if (Directory.Exists(outlines))
                foreach (var p in Directory.GetFiles(outlines, "*.json"))
                    files[TripLoader.OutlinesDir + "/" + Path.GetFileName(p)] = File.ReadAllText(p);
            ValidateFiles(report, files, libraryRoot);
            return report;
        }

        /// <summary>Read all JSON entries, stripping a common root folder if the zip has one.</summary>
        private static Dictionary<string, string> ReadZip(string zipPath)
        {
            var raw = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using (var fs = new FileStream(zipPath, FileMode.Open, FileAccess.Read))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                foreach (var entry in zip.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue; // directory entry
                    if (!entry.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
                    using (var s = entry.Open())
                    using (var r = new StreamReader(s, Encoding.UTF8))
                        raw[entry.FullName.Replace('\\', '/')] = r.ReadToEnd();
                }
            }
            // Locate trip.json; whatever folder it sits in is the package root.
            var tripEntry = raw.Keys
                .Where(k => k.Equals(TripLoader.TripFile, StringComparison.OrdinalIgnoreCase) || k.EndsWith("/" + TripLoader.TripFile, StringComparison.OrdinalIgnoreCase))
                .OrderBy(k => k.Length).FirstOrDefault();
            if (tripEntry == null) return raw;
            var prefix = tripEntry.Substring(0, tripEntry.Length - TripLoader.TripFile.Length);
            if (prefix.Length == 0) return raw;
            var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in raw)
                if (kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    files[kv.Key.Substring(prefix.Length)] = kv.Value;
            return files;
        }

        private static void ValidateFiles(ImportReport report, Dictionary<string, string> files, string libraryRoot)
        {
            report.Files = files.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
            var data = new TripData();
            bool parseFailed = false;

            T Parse<T>(string name, DocumentKind kind) where T : DocumentBase
            {
                if (!files.TryGetValue(name, out var json)) return null;
                try
                {
                    return TripLoader.LoadDocumentFromJson<T>(json, kind, name);
                }
                catch (SchemaTooNewException ex)
                {
                    Add(report, IssueSeverity.Error, ImportIssueKind.Schema, name, null, ex.Message);
                }
                catch (Exception ex)
                {
                    Add(report, IssueSeverity.Error, ImportIssueKind.Parse, name, null, ex.Message);
                }
                parseFailed = true;
                return null;
            }

            if (!files.ContainsKey(TripLoader.TripFile))
                Add(report, IssueSeverity.Error, ImportIssueKind.MissingFile, TripLoader.TripFile, null, "Package has no trip.json.");

            data.Trip = Parse<TripDocument>(TripLoader.TripFile, DocumentKind.Trip) ?? new TripDocument();
            data.ShotList = Parse<ShotListDocument>(TripLoader.ShotListFile, DocumentKind.ShotList) ?? new ShotListDocument { TripId = data.Trip.TripId };
            data.Captures = Parse<CapturesDocument>(TripLoader.CapturesFile, DocumentKind.Captures) ?? new CapturesDocument { TripId = data.Trip.TripId };
            foreach (var name in files.Keys.Where(k => k.StartsWith(TripLoader.OutlinesDir + "/", StringComparison.OrdinalIgnoreCase)).OrderBy(k => k, StringComparer.Ordinal))
            {
                var outline = Parse<OutlineDocument>(name, DocumentKind.Outline);
                if (outline == null) continue;
                var key = string.IsNullOrEmpty(outline.BookId) ? Path.GetFileNameWithoutExtension(name) : outline.BookId;
                if (string.IsNullOrEmpty(outline.BookId))
                    Add(report, IssueSeverity.Warning, ImportIssueKind.Other, name, null, "Outline has no bookId; keyed by file name.");
                if (data.Outlines.ContainsKey(key))
                    Add(report, IssueSeverity.Error, ImportIssueKind.DuplicateId, name, key, "Two outline files for book " + key + ".");
                data.Outlines[key] = outline;
                data.OutlineFileNames[key] = Path.GetFileName(name);
            }

            if (!files.ContainsKey(TripLoader.ShotListFile))
                Add(report, IssueSeverity.Warning, ImportIssueKind.MissingFile, TripLoader.ShotListFile, null, "No shotlist.json; the plan will be empty.");
            if (!files.ContainsKey(TripLoader.CapturesFile))
                Add(report, IssueSeverity.Warning, ImportIssueKind.MissingFile, TripLoader.CapturesFile, null, "No captures.json; no captures or amendments.");

            report.TripId = data.Trip.TripId;
            if (string.IsNullOrEmpty(report.TripId) && files.ContainsKey(TripLoader.TripFile) && !parseFailed)
                Add(report, IssueSeverity.Warning, ImportIssueKind.Other, TripLoader.TripFile, null, "trip.json has no tripId; a new one will be assigned.");

            foreach (var doc in new DocumentBase[] { data.ShotList, data.Captures }.Concat(data.Outlines.Values))
                if (!string.IsNullOrEmpty(doc.TripId) && !string.IsNullOrEmpty(data.Trip.TripId) && doc.TripId != data.Trip.TripId)
                    Add(report, IssueSeverity.Warning, ImportIssueKind.Other, null, doc.TripId, $"{doc.GetType().Name} tripId '{doc.TripId}' does not match trip.json '{data.Trip.TripId}'.");

            foreach (var v in TripValidator.Validate(data))
                Add(report, v.Severity, MapKind(v.Kind), null, v.EntityId, v.Message);

            Count(report, data);
            report.DisplayName = new TripSummary
            {
                TripId = data.Trip.TripId, ClientAbbrev = data.Trip.Identity?.ClientAbbrev, ProgramAbbrev = data.Trip.Identity?.ProgramAbbrev,
                PhaseNumber = data.Trip.Identity?.PhaseNumber, City = data.Trip.Identity?.Location?.City,
            }.DisplayName;

            if (!string.IsNullOrEmpty(report.TripId))
            {
                var dest = Path.Combine(libraryRoot ?? TripPaths.LibraryRoot, TripPaths.SanitizeFileName(report.TripId));
                if (Directory.Exists(dest))
                {
                    report.ExistingTripConflict = true;
                    report.ExistingFolder = dest;
                    Add(report, IssueSeverity.Warning, ImportIssueKind.Conflict, null, report.TripId, "A trip with this ID is already in the library; importing replaces it (overwrite required).");
                }
            }

            report.Staged = parseFailed ? null : data;
        }

        private static ImportIssueKind MapKind(ValidationKind k)
        {
            switch (k)
            {
                case ValidationKind.DuplicateId: return ImportIssueKind.DuplicateId;
                case ValidationKind.MissingId: return ImportIssueKind.DuplicateId;
                case ValidationKind.DanglingReference: return ImportIssueKind.DanglingReference;
                case ValidationKind.VideoWithoutChapter: return ImportIssueKind.VideoWithoutChapter;
                default: return ImportIssueKind.Other;
            }
        }

        private static void Count(ImportReport r, TripData d)
        {
            r.Books = d.Trip.Books.Count; r.People = d.Trip.People.Count; r.Days = d.Trip.Days.Count;
            r.Chapters = d.ShotList.Chapters.Count; r.Videos = d.ShotList.Videos.Count; r.Photos = d.ShotList.Photos.Count;
            r.Outlines = d.Outlines.Count;
            r.Captures = d.Captures.Captures.Count; r.PhotoCaptures = d.Captures.PhotoCaptures.Count;
            r.Amendments = d.Captures.Amendments.Count; r.Assignments = d.Captures.OutlineAssignments.Count;
        }

        private static void Add(ImportReport r, IssueSeverity sev, ImportIssueKind kind, string file, string entityId, string message) =>
            r.Issues.Add(new ImportIssue { Severity = sev, Kind = kind, File = file, EntityId = entityId, Message = message });

        // ------------------------------------------------------------------
        // Import (all or nothing)
        // ------------------------------------------------------------------

        /// <summary>
        /// Validate, then import into the library. Throws <see cref="ImportBlockedException"/>
        /// (with the report) if validation failed or the trip exists and overwrite is false.
        /// Writes to a staging folder first and swaps it in, so a crash never leaves a partial trip.
        /// Returns the destination folder.
        /// </summary>
        public static string ImportZip(string zipPath, bool overwrite = false, string libraryRoot = null)
        {
            var report = ValidateZip(zipPath, libraryRoot);
            return Commit(report, overwrite, libraryRoot);
        }

        public static string ImportFolder(string folder, bool overwrite = false, string libraryRoot = null)
        {
            var report = ValidateFolder(folder, libraryRoot);
            return Commit(report, overwrite, libraryRoot);
        }

        /// <summary>Commit a report produced by ValidateZip/ValidateFolder (so a UI can preview, then confirm).</summary>
        public static string Commit(ImportReport report, bool overwrite = false, string libraryRoot = null)
        {
            if (!report.CanImport || report.Staged == null) throw new ImportBlockedException(report);
            if (report.ExistingTripConflict && !overwrite)
            {
                Add(report, IssueSeverity.Error, ImportIssueKind.Conflict, null, report.TripId, "Trip already exists; pass overwrite: true to replace it.");
                throw new ImportBlockedException(report);
            }

            var data = report.Staged;
            if (string.IsNullOrEmpty(data.Trip.TripId)) data.Trip.TripId = Ids.New("trip");
            foreach (var doc in new DocumentBase[] { data.ShotList, data.Captures }.Concat(data.Outlines.Values))
                doc.TripId = data.Trip.TripId;

            var root = libraryRoot ?? TripPaths.LibraryRoot;
            Directory.CreateDirectory(root);
            var dest = Path.Combine(root, TripPaths.SanitizeFileName(data.Trip.TripId));
            var staging = Path.Combine(root, ".staging-" + Guid.NewGuid().ToString("N"));
            var backup = dest + ".replaced-" + Guid.NewGuid().ToString("N");

            try
            {
                data.FolderPath = staging;
                TripSaver.SaveAll(data);
                if (Directory.Exists(dest)) Directory.Move(dest, backup);
                try
                {
                    Directory.Move(staging, dest);
                }
                catch
                {
                    if (Directory.Exists(backup) && !Directory.Exists(dest)) Directory.Move(backup, dest);
                    throw;
                }
                if (Directory.Exists(backup)) Directory.Delete(backup, true);
            }
            finally
            {
                if (Directory.Exists(staging)) { try { Directory.Delete(staging, true); } catch { /* best effort */ } }
            }
            data.FolderPath = dest;
            return dest;
        }

        // ------------------------------------------------------------------
        // Single documents into an open session
        // ------------------------------------------------------------------

        /// <summary>
        /// Dry run: what would happen if this JSON replaced one document of the open trip.
        /// Validates the merged result (e.g. a new shot list must still contain every video
        /// that existing captures reference).
        /// </summary>
        public static ImportReport ValidateDocument(TripData target, string json, DocumentKind kind, string sourceName = null)
        {
            var report = new ImportReport { Source = sourceName ?? kind.ToString(), Format = "document", SingleDocumentKind = kind };
            var staged = CloneData(target);
            try
            {
                switch (kind)
                {
                    case DocumentKind.Trip: staged.Trip = TripLoader.LoadDocumentFromJson<TripDocument>(json, kind, sourceName); break;
                    case DocumentKind.ShotList: staged.ShotList = TripLoader.LoadDocumentFromJson<ShotListDocument>(json, kind, sourceName); break;
                    case DocumentKind.Captures: staged.Captures = TripLoader.LoadDocumentFromJson<CapturesDocument>(json, kind, sourceName); break;
                    case DocumentKind.Outline:
                    {
                        var o = TripLoader.LoadDocumentFromJson<OutlineDocument>(json, kind, sourceName);
                        if (string.IsNullOrEmpty(o.BookId))
                            Add(report, IssueSeverity.Error, ImportIssueKind.DanglingReference, sourceName, null, "Outline has no bookId.");
                        else if (target.FindBook(o.BookId) == null)
                            Add(report, IssueSeverity.Error, ImportIssueKind.DanglingReference, sourceName, o.BookId, "Outline bookId '" + o.BookId + "' is not a book of this trip.");
                        else { staged.Outlines[o.BookId] = o; report.OutlineBookId = o.BookId; }
                        break;
                    }
                }
            }
            catch (SchemaTooNewException ex)
            {
                Add(report, IssueSeverity.Error, ImportIssueKind.Schema, sourceName, null, ex.Message);
                return report;
            }
            catch (Exception ex)
            {
                Add(report, IssueSeverity.Error, ImportIssueKind.Parse, sourceName, null, ex.Message);
                return report;
            }

            var imported = kind == DocumentKind.Trip ? (DocumentBase)staged.Trip : kind == DocumentKind.ShotList ? staged.ShotList : kind == DocumentKind.Captures ? (DocumentBase)staged.Captures : null;
            if (imported != null && !string.IsNullOrEmpty(imported.TripId) && !string.IsNullOrEmpty(target.TripId) && imported.TripId != target.TripId)
                Add(report, IssueSeverity.Warning, ImportIssueKind.Other, sourceName, imported.TripId, $"Document tripId '{imported.TripId}' differs from the open trip '{target.TripId}'; it will be rewritten.");

            foreach (var v in TripValidator.Validate(staged))
                Add(report, v.Severity, MapKind(v.Kind), sourceName, v.EntityId, v.Message);
            Count(report, staged);
            report.TripId = staged.Trip.TripId;
            report.Staged = staged;
            return report;
        }

        /// <summary>Validate and, if clean, replace the document in the open session (marks it dirty). Throws <see cref="ImportBlockedException"/> otherwise.</summary>
        public static ImportReport ImportDocument(TripSession session, string json, DocumentKind kind, string sourceName = null)
        {
            var report = ValidateDocument(session.Data, json, kind, sourceName);
            if (!report.CanImport) throw new ImportBlockedException(report);
            var staged = report.Staged;
            switch (kind)
            {
                case DocumentKind.Trip:
                    staged.Trip.TripId = session.Data.TripId ?? staged.Trip.TripId;
                    session.Data.Trip = staged.Trip;
                    session.MarkDirty(DocumentKind.Trip);
                    break;
                case DocumentKind.ShotList:
                    staged.ShotList.TripId = session.Data.TripId;
                    session.Data.ShotList = staged.ShotList;
                    session.MarkDirty(DocumentKind.ShotList);
                    break;
                case DocumentKind.Captures:
                    staged.Captures.TripId = session.Data.TripId;
                    session.Data.Captures = staged.Captures;
                    session.MarkDirty(DocumentKind.Captures);
                    break;
                case DocumentKind.Outline:
                    var o = staged.Outlines[report.OutlineBookId];
                    o.TripId = session.Data.TripId;
                    session.Data.Outlines[o.BookId] = o;
                    session.MarkDirty(DocumentKind.Outline, o.BookId);
                    break;
            }
            return report;
        }

        private static TripData CloneData(TripData d)
        {
            var c = new TripData
            {
                FolderPath = d.FolderPath,
                Trip = TripJson.Clone(d.Trip),
                ShotList = TripJson.Clone(d.ShotList),
                Captures = TripJson.Clone(d.Captures),
            };
            foreach (var kv in d.Outlines) c.Outlines[kv.Key] = TripJson.Clone(kv.Value);
            foreach (var kv in d.OutlineFileNames) c.OutlineFileNames[kv.Key] = kv.Value;
            return c;
        }
    }
}

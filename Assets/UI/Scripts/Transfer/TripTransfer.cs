using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MediaTrip.Model;
using MediaTrip.Persistence;
using UnityEngine;

namespace MediaTrip.UI.Transfer
{
    public sealed class ExportResult
    {
        public string Path;
        /// <summary>"zip" or "bundle" (the automatic fallback when zipping is unavailable).</summary>
        public string Format;
        public string Warning;
        public bool Ok => Path != null;
        public string Error;
    }

    /// <summary>
    /// The three transfer routes and the glue around the data layer's packaging:
    /// 1. native picker / OS dialog (primary), 2. the app's own Import/Export folders (visible
    /// in the iOS Files app thanks to the Info.plist flags), 3. the clipboard as JSON.
    /// Every failure is reported in words, never swallowed.
    /// </summary>
    public sealed class TripTransfer
    {
        private readonly AppController _app;
        public IFilePicker Picker { get; }

        public static string ExportDir => Path.Combine(Application.persistentDataPath, "Export");
        public static string ImportDir => Path.Combine(Application.persistentDataPath, "Import");
        public static string InboxDir => Path.Combine(Application.persistentDataPath, "Inbox");

        public TripTransfer(AppController app)
        {
            _app = app;
            Picker = FilePickerFactory.Create();
            try
            {
                Directory.CreateDirectory(ExportDir);
                Directory.CreateDirectory(ImportDir);
            }
            catch (Exception ex) { Debug.LogWarning("Could not create transfer folders: " + ex.Message); }
        }

        // ------------------------------------------------------------------ export

        public static string SafeName(TripData d)
        {
            var id = d.Trip.Identity;
            var parts = new[] { id?.ClientAbbrev, id?.ProgramAbbrev, id?.PhaseNumber != null ? "P" + id.PhaseNumber : null }
                .Where(p => !string.IsNullOrWhiteSpace(p));
            var name = string.Join("_", parts);
            if (name.Length == 0) name = d.TripId ?? "trip";
            return TripPaths.SanitizeFileName(name);
        }

        /// <summary>Write the whole trip as a file in the Export folder: a zip, or a JSON bundle when zipping is unavailable on this platform.</summary>
        public ExportResult ExportTripFile(TripData data, bool preferZip = true)
        {
            var r = new ExportResult();
            var baseName = SafeName(data);
            try
            {
                Directory.CreateDirectory(ExportDir);
                if (preferZip)
                {
                    var zipPath = Path.Combine(ExportDir, baseName + ".zip");
                    if (TripPackage.TryExportZip(data, zipPath, out var err))
                    {
                        r.Path = zipPath; r.Format = "zip";
                        return r;
                    }
                    r.Warning = "Zip is not available on this device (" + err + "); exported a single JSON bundle instead.";
                    Debug.LogWarning(r.Warning);
                }
                var bundlePath = Path.Combine(ExportDir, baseName + ".mediatrip.json");
                TripPackage.ExportBundle(data, bundlePath);
                r.Path = bundlePath; r.Format = "bundle";
            }
            catch (Exception ex)
            {
                r.Error = "Export failed: " + ex.Message;
                Debug.LogException(ex);
            }
            return r;
        }

        public ExportResult ExportDocumentFile(TripData data, DocumentKind kind, string bookId = null)
        {
            var r = new ExportResult { Format = "document" };
            try
            {
                Directory.CreateDirectory(ExportDir);
                var name = SafeName(data) + "_" + DocumentFileName(data, kind, bookId).Replace('/', '_');
                var path = Path.Combine(ExportDir, name);
                TripPackage.ExportDocument(data, kind, path, bookId);
                r.Path = path;
            }
            catch (Exception ex) { r.Error = "Export failed: " + ex.Message; Debug.LogException(ex); }
            return r;
        }

        public static string DocumentFileName(TripData data, DocumentKind kind, string bookId)
        {
            switch (kind)
            {
                case DocumentKind.Trip: return TripLoader.TripFile;
                case DocumentKind.ShotList: return TripLoader.ShotListFile;
                case DocumentKind.Captures: return TripLoader.CapturesFile;
                default: return TripLoader.OutlinesDir + "/" + TripSaver.OutlineFileName(data, bookId);
            }
        }

        // ------------------------------------------------------------------ clipboard

        public bool CopyTripToClipboard(TripData data, out string message)
        {
            try
            {
                var json = TripPackage.ExportBundleJson(data);
                GUIUtility.systemCopyBuffer = json;
                message = "Copied whole trip to the clipboard (" + (json.Length / 1024) + " KB). Paste it on the other device.";
                return true;
            }
            catch (Exception ex) { message = "Copy failed: " + ex.Message; Debug.LogException(ex); return false; }
        }

        public bool CopyDocumentToClipboard(TripData data, DocumentKind kind, string bookId, out string message)
        {
            try
            {
                var json = TripPackage.ExportDocumentJson(data, kind, bookId);
                GUIUtility.systemCopyBuffer = json;
                message = "Copied " + DocumentFileName(data, kind, bookId) + " to the clipboard.";
                return true;
            }
            catch (Exception ex) { message = "Copy failed: " + ex.Message; Debug.LogException(ex); return false; }
        }

        public string ReadClipboard()
        {
            try { return GUIUtility.systemCopyBuffer; }
            catch (Exception ex) { Debug.LogWarning("Clipboard read failed: " + ex.Message); return null; }
        }

        // ------------------------------------------------------------------ validate (dry run)

        /// <summary>Validate a file by extension and content. Never writes.</summary>
        public ImportReport ValidatePath(string path)
        {
            try
            {
                if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    return TripPackage.ValidateZip(path);
                if (Directory.Exists(path))
                    return TripPackage.ValidateFolder(path);
                var text = File.ReadAllText(path);
                return TripPackage.ValidateJsonText(text, null, Path.GetFileName(path));
            }
            catch (Exception ex)
            {
                var r = new ImportReport { Source = path };
                r.Issues.Add(new ImportIssue { Severity = Validation.IssueSeverity.Error, Kind = ImportIssueKind.Parse, File = Path.GetFileName(path), Message = ex.GetType().Name + ": " + ex.Message });
                return r;
            }
        }

        public ImportReport ValidateText(string text, string sourceLabel) => TripPackage.ValidateJsonText(text, null, sourceLabel);

        /// <summary>For a single-document report, validate it against the open trip so the preview shows the merged result.</summary>
        public ImportReport ValidateDocumentAgainstOpenTrip(string json, DocumentKind kind, string sourceLabel) =>
            TripPackage.ValidateDocument(_app.Session.Data, json, kind, sourceLabel);

        // ------------------------------------------------------------------ commit

        /// <summary>Commit a whole-trip report (zip/folder/bundle) into the library, replacing an existing trip with the same id.</summary>
        public string CommitWholeTrip(ImportReport report) => TripPackage.Commit(report, overwrite: true);

        public List<string> FilesInImportFolder()
        {
            var list = new List<string>();
            foreach (var dir in new[] { ImportDir, InboxDir, Application.persistentDataPath })
            {
                if (!Directory.Exists(dir)) continue;
                list.AddRange(Directory.GetFiles(dir).Where(f => f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".json", StringComparison.OrdinalIgnoreCase)));
            }
            return list.Distinct().OrderByDescending(File.GetLastWriteTimeUtc).ToList();
        }
    }
}

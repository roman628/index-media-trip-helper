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
        /// <summary>"zip", "bundle" (the automatic fallback when zipping is unavailable), or "document".</summary>
        public string Format;
        public string Warning;
        public bool Ok => Path != null;
        public string Error;
    }

    /// <summary>
    /// Two calls the UI binds to: <see cref="ShareTrip"/> / <see cref="ShareDocument"/> write a
    /// file and hand it to the platform's share gesture; <see cref="BeginImport"/> asks the
    /// platform for a file and returns its path. Validation and commit stay in the data layer
    /// (<see cref="TripPackage"/>). Every failure is reported in words, never swallowed.
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

        // ------------------------------------------------------------------ share (one call per thing)

        /// <summary>Share the whole trip: a zip, or a single-file JSON bundle where zipping is unavailable on the device.</summary>
        public void ShareTrip(TripData data, Action<string> onDone, Action<string> onFail)
        {
            var r = ExportTripFile(data);
            if (!r.Ok) { onFail?.Invoke(r.Error); return; }
            var note = r.Warning != null ? " " + r.Warning : "";
            Picker.Share(r.Path, m => onDone?.Invoke(m + note), m => { if (m != null) onFail?.Invoke(m); else onDone?.Invoke("Written to " + r.Path + note); });
        }

        /// <summary>Share one document (trip.json, shotlist.json, captures.json, or an outline).</summary>
        public void ShareDocument(TripData data, DocumentKind kind, string bookId, Action<string> onDone, Action<string> onFail)
        {
            var r = ExportDocumentFile(data, kind, bookId);
            if (!r.Ok) { onFail?.Invoke(r.Error); return; }
            Picker.Share(r.Path, m => onDone?.Invoke(m), m => { if (m != null) onFail?.Invoke(m); else onDone?.Invoke("Written to " + r.Path); });
        }

        /// <summary>
        /// Export one named thing (original shot list, working shot list, changes, media summary,
        /// an outline) in a format, and hand the file to the platform's share gesture. The
        /// writing is the export layer's; this only puts the file on disk and shares it, so a
        /// second format needs nothing here.
        /// </summary>
        public void Export(MediaTrip.Session.TripSession session, MediaTrip.Export.ExportRequest request, Action<string> onDone, Action<string> onFail, string formatId = "json")
        {
            string path;
            try
            {
                session.SaveNow();
                var file = MediaTrip.Export.TripExports.Write(session, request, formatId);
                Directory.CreateDirectory(ExportDir);
                path = Path.Combine(ExportDir, file.FileName);
                File.WriteAllBytes(path, file.Bytes);
            }
            catch (Exception ex) { Debug.LogException(ex); onFail?.Invoke("Export failed: " + ex.Message); return; }
            Picker.Share(path, m => onDone?.Invoke(m), m => { if (m != null) onFail?.Invoke(m); else onDone?.Invoke("Written to " + path); });
        }

        // ------------------------------------------------------------------ import (one call)

        /// <summary>
        /// Ask the platform for a file to import. On platforms with no chooser the callback
        /// gets a null path and the UI offers the Import-folder list instead.
        /// </summary>
        public void BeginImport(Action<string> onPath, Action<string> onCancelled, string extensionsCsv = "zip,json")
        {
            if (!Picker.CanPick) { onPath?.Invoke(null); return; }
            Picker.PickImport(onPath, onCancelled, extensionsCsv);
        }

        // ------------------------------------------------------------------ files

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
                    r.Warning = "Zip is not available on this device (" + err + "); shared a single JSON bundle instead.";
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

        // ------------------------------------------------------------------ validate (dry run)

        /// <summary>Validate a file by extension and content, detecting zip / folder / bundle / single document. Never writes.</summary>
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

        /// <summary>For a single-document report, validate it against the open trip so the preview shows the merged result.</summary>
        public ImportReport ValidateDocumentAgainstOpenTrip(string json, DocumentKind kind, string sourceLabel, string outlineBookId = null) =>
            TripPackage.ValidateDocument(_app.Session.Data, json, kind, sourceLabel, outlineBookId);

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

        /// <summary>Clipboard text, used only for pasting notes as bullets.</summary>
        public string ReadClipboard()
        {
            try { return GUIUtility.systemCopyBuffer; }
            catch (Exception ex) { Debug.LogWarning("Clipboard read failed: " + ex.Message); return null; }
        }
    }
}

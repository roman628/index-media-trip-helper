using System;
using System.IO;
using System.Linq;
using MediaTrip.Persistence;
using MediaTrip.Validation;
using UnityEngine.UIElements;

namespace MediaTrip.UI.Transfer
{
    /// <summary>
    /// The one Import gesture: pick a file (or choose from the app's Import folder where no
    /// picker exists), detect whether it is a whole trip or a single document, run the dry-run
    /// validation, show the result in a sheet, and only then commit, all or nothing. Works with
    /// or without an open trip (a single document needs one).
    /// </summary>
    public static class ImportFlow
    {
        public static void Begin(AppController app)
        {
            app.Transfer.BeginImport(path =>
            {
                if (path == null) FolderList(app);
                else Stage(app, path);
            }, m => { if (m != null) Fail(app, m); });
        }

        /// <summary>Validate a file (any route) and show the preview. Nothing is written.</summary>
        public static void Stage(AppController app, string path)
        {
            var io = app.State.IO;
            try
            {
                var report = app.Transfer.ValidatePath(path);
                io.PendingPath = path;
                var label = Path.GetFileName(path);
                if (report.SingleDocumentKind != null && report.CanImport)
                {
                    if (app.Session == null)
                    {
                        Fail(app, label + " is a single document. Open or create a trip first, then import it into that trip.");
                        return;
                    }
                    report = app.Transfer.ValidateDocumentAgainstOpenTrip(File.ReadAllText(path), report.SingleDocumentKind.Value, label);
                }
                io.Report = report;
                io.SourceLabel = label;
                app.ShowSheet(() => PreviewSheet(app));
            }
            catch (Exception ex) { Fail(app, "Could not read " + path + ": " + ex.Message); }
        }

        /// <summary>Apply the staged report. Whole trips go into the library and open; documents replace their counterpart in the open trip.</summary>
        public static void Commit(AppController app)
        {
            var io = app.State.IO; var r = io.Report;
            if (r == null) return;
            try
            {
                if (r.IsWholeTrip)
                {
                    var dest = app.Transfer.CommitWholeTrip(r);
                    var tripId = Path.GetFileName(dest);
                    var label = io.SourceLabel;
                    app.CloseSheet();
                    app.OpenTrip(tripId);
                    app.Toast("Imported " + label);
                }
                else if (r.SingleDocumentKind != null)
                {
                    if (app.Session == null) { Fail(app, "Open a trip first."); return; }
                    var json = io.PendingPath != null ? File.ReadAllText(io.PendingPath) : null;
                    if (json == null) { Fail(app, "The file is gone; choose it again."); return; }
                    TripPackage.ImportDocument(app.Session, json, r.SingleDocumentKind.Value, io.SourceLabel);
                    var label = io.SourceLabel;
                    io.Report = null; io.PendingPath = null;
                    app.CloseSheet();
                    app.Toast("Imported " + label);
                }
            }
            catch (ImportBlockedException ex) { io.Report = ex.Report; app.ShowSheet(() => PreviewSheet(app)); app.Toast(ex.Message); }
            catch (Exception ex) { Fail(app, "Import failed and nothing was changed: " + ex.Message); }
        }

        public static void Discard(AppController app)
        {
            var io = app.State.IO;
            io.Report = null; io.PendingPath = null;
            if (app.HasSheet) app.CloseSheet(); else app.Render();
        }

        /// <summary>The dry-run report: what the file is, what is wrong with it, what importing would change.</summary>
        private static VisualElement PreviewSheet(AppController app)
        {
            var io = app.State.IO; var r = io.Report; var s = app.Session;
            var errs = r.Errors.Count(); var warns = r.Warnings.Count();
            string kind;
            if (r.IsWholeTrip) kind = "Whole trip";
            else if (r.SingleDocumentKind != null) kind = s != null ? TripTransfer.DocumentFileName(s.Data, r.SingleDocumentKind.Value, r.OutlineBookId) : r.SingleDocumentKind.Value.ToString();
            else kind = "Not a trip file";

            var body = U.Scroll();
            body.Add(U.H3(U.Esc(io.SourceLabel)).Mb(4));
            body.Add(U.Sub(kind + " · " + U.Plural(r.Books, "book") + " · " + U.Plural(r.Videos, "video") + " · " + U.Plural(r.Photos, "photo") + " · " + U.Plural(r.Captures, "capture")).Mb(12));

            string change;
            if (r.IsWholeTrip) change = r.ExistingTripConflict ? "Replaces the trip with the same id on this device." : "Adds a new trip and opens it.";
            else if (r.SingleDocumentKind != null) change = "Replaces " + kind + " in the open trip. Everything else is kept.";
            else change = "Nothing can be imported from this file.";
            body.Add(U.Body(change).Mb(12));

            if (r.Issues.Count > 0) body.Add(U.Eyebrow(U.Plural(errs, "error") + " · " + U.Plural(warns, "warning")).Mb(4));
            foreach (var i in r.Issues.OrderByDescending(x => x.Severity).Take(40))
            {
                var row = new VisualElement().Cls("issue");
                row.Add(new VisualElement().Cls("dot" + (i.Severity == IssueSeverity.Warning ? " warn" : "")));
                row.Add(U.Text(U.Esc((i.File != null ? i.File + ": " : "") + i.Message), "body-sm grow"));
                body.Add(row);
            }
            if (r.Issues.Count > 40) body.Add(U.Sub("… and " + (r.Issues.Count - 40) + " more"));

            var import = U.Btn(r.CanImport ? "Import" : "Cannot import", () => Commit(app), "big pri");
            import.SetEnabled(r.CanImport);
            var foot = U.Row(U.Grow(), import).Mt(12);
            foot.style.flexShrink = 0;
            return app.Sheet(720, () => Discard(app), app.SheetHead("Import", () => Discard(app)), body, foot);
        }

        private static void FolderList(AppController app)
        {
            var files = app.Transfer.FilesInImportFolder();
            app.ShowSheet(() =>
            {
                var list = U.Scroll();
                if (files.Count == 0) list.Add(U.Sub("No .zip or .json files found. Put the file in this folder (on the iPad: the Files app, under this app) and try again.\n" + TripTransfer.ImportDir));
                foreach (var f in files)
                {
                    var path = f;
                    list.Add(U.Tap(() => { app.CloseSheet(); Stage(app, path); }, "item outlined mb8",
                        U.Col(U.Text(U.Esc(Path.GetFileName(f)), "bold"), U.Sub(File.GetLastWriteTime(f).ToString("ddd MMM d, HH:mm") + " · " + (new FileInfo(f).Length / 1024) + " KB")).Cls("grow")));
                }
                return app.Sheet(640, app.CloseSheet, app.SheetHead("Choose a file", app.CloseSheet), list);
            });
        }

        /// <summary>Failures stay on screen until dismissed; a toast would be gone before it was read.</summary>
        public static void Fail(AppController app, string message)
        {
            app.ShowSheet(() => app.Sheet(560, app.CloseSheet, app.SheetHead("Import", app.CloseSheet), U.Scroll(U.Body(U.Esc(message)))));
        }
    }
}

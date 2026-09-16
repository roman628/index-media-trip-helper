using System;
using System.IO;
using MediaTrip.Persistence;
using MediaTrip.UI.Author;
using UnityEngine.UIElements;

namespace MediaTrip.UI.Transfer
{
    /// <summary>
    /// The one Import gesture: pick a file (or choose from the app's Import folder where no
    /// picker exists), detect whether it is a whole trip or a single document, run the dry-run
    /// validation, show the preview, and only then commit, all or nothing. Works with or
    /// without an open trip (a single document needs one).
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
                        Fail(app, label + " is a single " + report.SingleDocumentKind + " document; open or create a trip first, then import it into that trip.");
                        return;
                    }
                    report = app.Transfer.ValidateDocumentAgainstOpenTrip(File.ReadAllText(path), report.SingleDocumentKind.Value, label);
                }
                io.Report = report;
                io.SourceLabel = label;
                io.Stage = "preview";
                io.LastMessage = null;
                if (app.Session != null) app.Nav(Screen.IO);
                else app.ShowSheet(() => PreviewSheet(app));
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
                    io.Stage = "idle"; io.Report = null;
                    app.CloseSheet();
                    app.OpenTrip(tripId);
                    app.State.IO.Stage = "done";
                    app.State.IO.SourceLabel = label;
                    app.Nav(Screen.IO);
                    Ok(app, "Imported " + label + " · opened");
                }
                else if (r.SingleDocumentKind != null)
                {
                    if (app.Session == null) { Fail(app, "Open a trip first."); return; }
                    var json = io.PendingPath != null ? File.ReadAllText(io.PendingPath) : null;
                    if (json == null) { Fail(app, "The source is gone; choose it again."); return; }
                    TripPackage.ImportDocument(app.Session, json, r.SingleDocumentKind.Value, io.SourceLabel);
                    app.State.Renumber.Snapshot(app.Session.Data);
                    io.Stage = "done"; io.Report = null;
                    Ok(app, "Imported " + io.SourceLabel);
                }
            }
            catch (ImportBlockedException ex) { io.Report = ex.Report; Fail(app, ex.Message); }
            catch (Exception ex) { Fail(app, "Import failed and nothing was changed: " + ex.Message); }
        }

        public static void Discard(AppController app)
        {
            var io = app.State.IO;
            io.Stage = "idle"; io.Report = null; io.PendingPath = null;
            if (app.HasSheet) app.CloseSheet(); else app.Render();
        }

        private static VisualElement PreviewSheet(AppController app)
        {
            var sheet = new VisualElement().Cls("sheet wide");
            sheet.style.maxHeight = 720;
            sheet.Add(U.Row(U.H2("Import").Cls("grow"), U.Btn("Cancel", () => Discard(app), "sm")).Mb(12));
            sheet.Add(U.Scroll(ImportExportScreen.Preview(app)));
            return app.Overlay(sheet, () => Discard(app));
        }

        private static void FolderList(AppController app)
        {
            var files = app.Transfer.FilesInImportFolder();
            app.ShowSheet(() =>
            {
                var sheet = new VisualElement().Cls("sheet");
                sheet.Add(U.Row(U.Col(U.H2("Files in the app's folder"), U.Sub(TripTransfer.ImportDir)).Cls("grow"), U.Btn("Close", app.CloseSheet, "sm")).Mb(12));
                var list = U.Scroll();
                if (files.Count == 0) list.Add(U.Sub("No .zip or .json files found. Put the file in the Import folder above (on the iPad: the Files app, under this app) and try again."));
                foreach (var f in files)
                {
                    var path = f;
                    list.Add(U.Tap(() => { app.CloseSheet(); Stage(app, path); }, "item outlined mb8",
                        U.Col(U.Text(Path.GetFileName(f), "bold"), U.Sub(File.GetLastWriteTime(f).ToString("ddd MMM d, HH:mm") + " · " + (new FileInfo(f).Length / 1024) + " KB")).Cls("grow")));
                }
                sheet.Add(list);
                return app.Overlay(sheet, app.CloseSheet);
            });
        }

        public static void Fail(AppController app, string message)
        {
            app.State.IO.LastMessage = message;
            app.State.IO.LastMessageIsError = true;
            app.Toast(message);
            app.Render();
        }

        public static void Ok(AppController app, string message)
        {
            app.State.IO.LastMessage = message;
            app.State.IO.LastMessageIsError = false;
            app.Toast(message);
            app.Render();
        }
    }
}

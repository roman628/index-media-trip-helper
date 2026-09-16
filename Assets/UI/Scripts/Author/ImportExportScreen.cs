using System;
using System.IO;
using System.Linq;
using MediaTrip.Persistence;
using MediaTrip.UI.Transfer;
using MediaTrip.Validation;
using UnityEngine.UIElements;

namespace MediaTrip.UI.Author
{
    /// <summary>
    /// Import (with the dry-run report before anything is written) and export (whole trip
    /// or single documents) over three routes: the Files picker, the app's own folder, and
    /// the clipboard. Every route reports what it did or why it could not.
    /// </summary>
    public static class ImportExportScreen
    {
        public static VisualElement Build(AppController app)
        {
            var st = app.State; var io = st.IO; var s = app.Session; var d = s.Data; var tr = app.Transfer;
            var root = U.Row().Cls("grow stretch"); root.style.minHeight = 0;

            // ================= import
            var left = U.Scroll().Pad(20, 24); left.style.width = Length.Percent(60); left.Cls("bordered-right");
            left.Add(U.H1("Import").Mb(4));
            left.Add(U.Sub("A trip zip (trip.json, shotlist.json, captures.json, outlines/), a single-JSON trip bundle, or a single document — including JSON converted from a Word shot list. Nothing is written until you confirm.").Mb(14));

            var routes = U.Row().Cls("wrap").Mb(16);
            var pick = U.Btn(tr.Picker.CanPick ? "Choose file…" : "Choose file (not available here)", () => PickFile(app), "big pri mr10 mb8");
            pick.SetEnabled(tr.Picker.CanPick);
            routes.Add(pick);
            routes.Add(U.Btn("Paste JSON", () => { io.PasteOpen = true; io.PasteText = tr.ReadClipboard() ?? ""; app.Render(); }, "big mr10 mb8"));
            routes.Add(U.Btn("From app folder…", () => FolderList(app), "big mb8"));
            left.Add(routes);
            left.Add(U.Sub("Routes: 1) the Files picker (iPad) or a file dialog (computer) · 2) copy the whole trip on one device with “Copy to clipboard” and use Paste JSON on the other · 3) drop a file into the app's folder (visible in the Files app under this app) and pick it from the list.").Mb(14));

            if (io.LastMessage != null)
            {
                var b = new VisualElement().Cls("banner" + (io.LastMessageIsError ? " bad" : " ok"));
                b.Add(U.Body(U.Esc(io.LastMessage)).Cls("grow"));
                b.Add(U.Btn("", () => { io.LastMessage = null; app.Render(); }, "sm square").Also(x => x.Add(new Glyph(GlyphKind.Cross, 14))));
                left.Add(b);
            }

            if (io.Stage == "preview" && io.Report != null) left.Add(Preview(app));
            else if (io.Stage == "done") left.Add(U.Card(U.H3("Imported " + U.Esc(io.SourceLabel)).Mb(6), U.Sub("The trip is open. Validation issues, if any, are flagged in the top bar and in the JSON inspector.").Mb(12),
                U.Row(U.Btn("Open shot list", () => app.Nav(Screen.ShotList), "pri"), U.Btn("JSON inspector", () => app.Nav(Screen.Json), "ml10"))).Cls("ok"));
            else left.Add(U.Card(U.Sub("No file chosen.")));
            root.Add(left);

            // ================= export
            var right = U.Scroll().Cls("grow").Pad(20, 24);
            right.Add(U.H1("Export").Mb(4));
            right.Add(U.Sub("JSON only in v1. The Word summary comes later and is filled from this data deterministically.").Mb(14));
            right.Add(U.Btn("Copy whole trip to clipboard", () => { if (tr.CopyTripToClipboard(d, out var m)) app.Toast(m); else Fail(app, m); }, "big pri mb10").Cls("fill"));
            right.Add(U.Sub("The most reliable route: paste it into the other device's Import screen.").Mb(14));
            right.Add(U.Btn(tr.Picker.CanPick ? "Export whole trip to Files · .zip" : "Export whole trip · .zip", () => ExportTrip(app, share: false), "big mb10").Cls("fill"));
            right.Add(U.Btn("Share… (AirDrop, Mail)", () => ExportTrip(app, share: true), "big mb16").Cls("fill"));
            right.Add(U.Eyebrow("Single documents").Mb(8));
            foreach (var (kind, bookId, desc) in Documents(d))
            {
                var name = TripTransfer.DocumentFileName(d, kind, bookId);
                var size = TripPackage.ExportDocumentJson(d, kind, bookId).Length / 1024f;
                var row = U.Row().MinH(64).Cls("bordered-bottom");
                row.Add(U.Col(U.Text(name, "bold"), U.Sub(desc + " · " + size.ToString("0.0") + " KB")).Cls("grow"));
                row.Add(U.Btn("Copy", () => { if (tr.CopyDocumentToClipboard(d, kind, bookId, out var m)) app.Toast(m); else Fail(app, m); }, "sm"));
                row.Add(U.Btn("Export", () => ExportDocument(app, kind, bookId), "sm ml8"));
                right.Add(row);
            }
            right.Add(U.Sub("Export folder: " + TripTransfer.ExportDir).Mt(14));
            root.Add(right);
            return root;
        }

        private static System.Collections.Generic.IEnumerable<(DocumentKind, string, string)> Documents(Model.TripData d)
        {
            yield return (DocumentKind.Trip, null, "trip identity, people, books, days");
            yield return (DocumentKind.ShotList, null, "the immutable plan");
            yield return (DocumentKind.Captures, null, "amendments, captures, assignments");
            foreach (var bookId in d.Outlines.Keys.OrderBy(k => d.FindBook(k)?.Number ?? 99))
                yield return (DocumentKind.Outline, bookId, "outline mockup, Book " + (d.FindBook(bookId)?.Number.ToString() ?? "?"));
        }

        private static void Fail(AppController app, string message)
        {
            app.State.IO.LastMessage = message;
            app.State.IO.LastMessageIsError = true;
            app.Toast(message);
            app.Render();
        }

        private static void Ok(AppController app, string message)
        {
            app.State.IO.LastMessage = message;
            app.State.IO.LastMessageIsError = false;
            app.Toast(message);
            app.Render();
        }

        // ------------------------------------------------------------------ export actions

        private static void ExportTrip(AppController app, bool share)
        {
            var tr = app.Transfer;
            var r = tr.ExportTripFile(app.Session.Data);
            if (!r.Ok) { Fail(app, r.Error); return; }
            var note = r.Warning != null ? " " + r.Warning : "";
            if (share) tr.Picker.ShareFile(r.Path, m => Ok(app, m + note), m => { if (m != null) Fail(app, m); else Ok(app, "Written to " + r.Path + note); });
            else tr.Picker.ExportFile(r.Path, m => Ok(app, m + note), m => { if (m != null) Fail(app, m); else Ok(app, "Written to " + r.Path + note); });
        }

        private static void ExportDocument(AppController app, DocumentKind kind, string bookId)
        {
            var tr = app.Transfer;
            var r = tr.ExportDocumentFile(app.Session.Data, kind, bookId);
            if (!r.Ok) { Fail(app, r.Error); return; }
            tr.Picker.ExportFile(r.Path, m => Ok(app, m), m => { if (m != null) Fail(app, m); else Ok(app, "Written to " + r.Path); });
        }

        // ------------------------------------------------------------------ import actions

        private static void PickFile(AppController app)
        {
            app.Transfer.Picker.PickImport(path => Stage(app, path), m => { if (m != null) Fail(app, m); });
        }

        private static void FolderList(AppController app)
        {
            var files = app.Transfer.FilesInImportFolder();
            app.ShowSheet(() =>
            {
                var sheet = new VisualElement().Cls("sheet");
                sheet.Add(U.Row(U.Col(U.H2("Files in the app's folder"), U.Sub(TripTransfer.ImportDir)).Cls("grow"), U.Btn("Close", app.CloseSheet, "sm")).Mb(12));
                var list = U.Scroll();
                if (files.Count == 0) list.Add(U.Sub("No .zip or .json files found. On the iPad, open the Files app, find this app under “On My iPad”, and drop the file into Import."));
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

        /// <summary>Validate a file (any route) and show the preview. Nothing is written.</summary>
        public static void Stage(AppController app, string path)
        {
            var io = app.State.IO;
            try
            {
                var report = app.Transfer.ValidatePath(path);
                io.PendingPath = path; io.PendingJson = null;
                StageReport(app, report, Path.GetFileName(path), path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? null : File.ReadAllText(path));
            }
            catch (Exception ex) { Fail(app, "Could not read " + path + ": " + ex.Message); }
        }

        public static void StageText(AppController app, string text, string label)
        {
            try
            {
                var report = app.Transfer.ValidateText(text, label);
                app.State.IO.PendingPath = null; app.State.IO.PendingJson = text;
                StageReport(app, report, label, text);
            }
            catch (Exception ex) { Fail(app, "Could not read the pasted text: " + ex.Message); }
        }

        private static void StageReport(AppController app, ImportReport report, string label, string jsonIfDocument)
        {
            var io = app.State.IO;
            if (report.SingleDocumentKind != null && jsonIfDocument != null && report.CanImport)
                report = app.Transfer.ValidateDocumentAgainstOpenTrip(jsonIfDocument, report.SingleDocumentKind.Value, label);
            io.Report = report;
            io.SourceLabel = label;
            io.Stage = "preview";
            io.LastMessage = null;
            app.Render();
        }

        private static VisualElement Preview(AppController app)
        {
            var io = app.State.IO; var r = io.Report; var s = app.Session;
            var errs = r.Errors.Count();
            var card = U.Card().Mb(12);
            var kind = r.IsWholeTrip ? "whole trip (" + r.Format + ")" : r.SingleDocumentKind != null ? TripTransfer.DocumentFileName(s.Data, r.SingleDocumentKind.Value, r.OutlineBookId) : "unknown";
            var detected = "Detected " + kind + (r.TripId != null ? " · trip " + r.TripId : "") + " · " + r.Books + " books · " + r.Chapters + " chapters · " + r.Videos + " videos · " + r.Photos + " photos · " + r.Captures + " captures";
            card.Add(U.Row(U.Text("{ }", "num md").W(44), U.Col(U.H3(U.Esc(io.SourceLabel)), U.Sub(detected)).Cls("grow")).Mb(6));
            card.Add(U.Hr().Mt(10).Mb(10));
            card.Add(U.Eyebrow("Validation · " + U.Plural(errs, "error") + " · " + U.Plural(r.Warnings.Count(), "warning")).Mb(6));
            if (r.Issues.Count == 0) card.Add(U.Body("Clean. Every reference resolves."));
            foreach (var i in r.Issues.OrderByDescending(x => x.Severity).Take(40))
            {
                var row = new VisualElement().Cls("issue"); row.style.paddingLeft = 0;
                row.Add(new VisualElement().Cls("dot" + (i.Severity == IssueSeverity.Warning ? " warn" : "")));
                row.Add(U.Body(U.Esc((i.File != null ? i.File + ": " : "") + i.Message)).Cls("grow"));
                card.Add(row);
            }
            if (r.Issues.Count > 40) card.Add(U.Sub("… and " + (r.Issues.Count - 40) + " more"));
            card.Add(U.Hr().Mt(10).Mb(10));
            card.Add(U.Eyebrow("What changes").Mb(6));
            if (r.IsWholeTrip)
                card.Add(U.Body(r.ExistingTripConflict ? "Replaces the trip with the same id already in the library (its current files are swapped out in one step)." : "Adds a new trip to the library and opens it."));
            else if (r.SingleDocumentKind != null)
                card.Add(U.Body("Replaces " + kind + " in the open trip. Captures, amendments and assignments elsewhere are kept; anything they reference must still exist (that is what the errors above check)."));
            else card.Add(U.Body("Nothing can be imported from this file."));
            var host = U.Col(card);
            var actions = U.Row();
            var import = U.Btn(r.CanImport ? (r.IsWholeTrip ? "Import trip" : "Import document") : "Cannot import (" + U.Plural(errs, "error") + ")", () => Commit(app), "big pri");
            import.SetEnabled(r.CanImport);
            actions.Add(import);
            actions.Add(U.Btn("Discard", () => { io.Stage = "idle"; io.Report = null; io.PendingJson = null; io.PendingPath = null; app.Render(); }, "big ml10"));
            host.Add(actions);
            host.Add(U.Sub(r.CanImport ? "Warnings are kept as flags on the imported items, not silently dropped — the badge in the top bar stays until they are fixed." : "Errors block the import. Fix the file, or import the pieces that are clean.").Mt(10));
            return host;
        }

        private static void Commit(AppController app)
        {
            var io = app.State.IO; var r = io.Report;
            try
            {
                if (r.IsWholeTrip)
                {
                    var dest = app.Transfer.CommitWholeTrip(r);
                    var tripId = Path.GetFileName(dest);
                    io.Stage = "done"; io.Report = null;
                    app.OpenTrip(tripId);
                    app.Nav(Screen.IO);
                    io.Stage = "done";
                    Ok(app, "Imported " + io.SourceLabel + " · opened");
                }
                else if (r.SingleDocumentKind != null)
                {
                    var json = io.PendingJson ?? (io.PendingPath != null ? File.ReadAllText(io.PendingPath) : null);
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

        public static VisualElement PasteSheet(AppController app)
        {
            var io = app.State.IO;
            void Close() { io.PasteOpen = false; app.Render(); }
            var sheet = new VisualElement().Cls("sheet wide"); sheet.style.height = 600;
            sheet.Add(U.Row(U.Col(U.H2("Paste JSON"), U.Sub("A whole-trip bundle from “Copy whole trip to clipboard”, or any single document.")).Cls("grow"), U.Btn("Cancel", Close, "sm")).Mb(12));
            // Large texts (a whole trip is tens of KB) exceed what one text field can draw, so
            // anything big is shown as a summary and kept whole in state; small texts stay editable.
            const int editableLimit = 4000;
            var host = U.Col().Cls("grow"); host.style.minHeight = 0;
            sheet.Add(host);
            var info = U.Sub("").Mt(8);
            TextField ta = null;
            void Show()
            {
                host.Clear();
                var text = io.PasteText ?? "";
                if (text.Length > editableLimit)
                {
                    var head = text.Substring(0, 600).Replace("\r", "");
                    host.Add(U.Card(U.H3((text.Length / 1024) + " KB of JSON ready").Mb(6), U.Sub("Too large to edit here; it will be validated as-is. It starts with:").Mb(8), U.Text(U.Esc(head) + "…", "mono")).Cls("grow"));
                    ta = null;
                }
                else
                {
                    ta = U.Input(text, "Paste here…", v => io.PasteText = v, true, "mono").Cls("grow"); ta.name = "io.paste";
                    ta.style.height = Length.Percent(100);
                    host.Add(ta);
                }
                info.text = text.Length == 0 ? "Nothing on the clipboard yet" : (text.Length / 1024) + " KB";
            }
            Show();
            sheet.Add(info);
            sheet.Add(U.Row(U.Btn("Read clipboard again", () => { io.PasteText = app.Transfer.ReadClipboard() ?? ""; Show(); }, "big"), U.Grow(),
                U.Btn("Validate", () => { io.PasteOpen = false; StageText(app, io.PasteText ?? "", "pasted JSON"); }, "big pri")).Mt(12));
            if (ta != null) app.State.Focus = "io.paste";
            return app.Overlay(sheet, Close);
        }
    }
}

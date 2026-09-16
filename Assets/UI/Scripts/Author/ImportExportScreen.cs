using System.Linq;
using MediaTrip.Persistence;
using MediaTrip.UI.Transfer;
using MediaTrip.Validation;
using UnityEngine.UIElements;

namespace MediaTrip.UI.Author
{
    /// <summary>
    /// One Import button (a file of any supported kind, detected automatically, with the
    /// dry-run report before anything is written) and one Share button per thing (the whole
    /// trip, or a single document), each doing the right thing for the platform.
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
            left.Add(U.Sub("A whole trip (a .zip or a trip bundle .json) or a single document (trip.json, shotlist.json, captures.json, an outline), including JSON converted from a Word shot list. The kind is detected from the file. Nothing is written until you confirm.").Mb(14));
            left.Add(U.Btn("Import…", () => ImportFlow.Begin(app), "big pri mb8").W(240));
            left.Add(U.Sub(tr.Picker.CanPick ? "Opens the " + tr.Picker.Name + "." : "No file chooser on this platform: files placed in " + TripTransfer.ImportDir + " are listed instead.").Mb(14));

            if (io.LastMessage != null)
            {
                var b = new VisualElement().Cls("banner" + (io.LastMessageIsError ? " bad" : " ok"));
                b.Add(U.Body(U.Esc(io.LastMessage)).Cls("grow"));
                b.Add(U.Btn("", () => { io.LastMessage = null; app.Render(); }, "sm square").Also(x => x.Add(new Glyph(GlyphKind.Cross, 14))));
                left.Add(b);
            }

            if (io.Stage == "preview" && io.Report != null) left.Add(Preview(app));
            else if (io.Stage == "done") left.Add(U.Card(U.H3("Imported " + U.Esc(io.SourceLabel)).Mb(6), U.Sub("Validation issues, if any, are flagged in the top bar and in the JSON inspector.").Mb(12),
                U.Row(U.Btn("Open shot list", () => app.Nav(Screen.ShotList), "pri"), U.Btn("JSON inspector", () => app.Nav(Screen.Json), "ml10"))).Cls("ok"));
            else left.Add(U.Card(U.Sub("No file chosen.")));
            root.Add(left);

            // ================= share
            var right = U.Scroll().Cls("grow").Pad(20, 24);
            right.Add(U.H1("Share").Mb(4));
            right.Add(U.Sub("JSON only in v1. On the iPad the share sheet offers Files, iCloud Drive, AirDrop and Mail; on a computer a save dialog opens.").Mb(14));
            right.Add(U.Btn("Share whole trip", () => tr.ShareTrip(d, m => ImportFlow.Ok(app, m), m => ImportFlow.Fail(app, m)), "big pri mb16").Cls("fill"));
            right.Add(U.Eyebrow("Single documents").Mb(8));
            foreach (var (kind, bookId, desc) in Documents(d))
            {
                var k = kind; var b = bookId;
                var name = TripTransfer.DocumentFileName(d, kind, bookId);
                var size = TripPackage.ExportDocumentJson(d, kind, bookId).Length / 1024f;
                var row = U.Row().MinH(64).Cls("bordered-bottom");
                row.Add(U.Col(U.Text(name, "bold"), U.Sub(desc + " · " + size.ToString("0.0") + " KB")).Cls("grow"));
                row.Add(U.Btn("Share", () => tr.ShareDocument(d, k, b, m => ImportFlow.Ok(app, m), m => ImportFlow.Fail(app, m)), "sm"));
                right.Add(row);
            }
            right.Add(U.Sub("Files are written to " + TripTransfer.ExportDir + " before sharing; that folder is also visible in the Files app on the iPad.").Mt(14));
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

        /// <summary>The dry-run report as a card with Import / Discard. Also used by the empty-library import sheet.</summary>
        public static VisualElement Preview(AppController app)
        {
            var io = app.State.IO; var r = io.Report; var s = app.Session;
            var errs = r.Errors.Count();
            var card = U.Card().Mb(12);
            string kind;
            if (r.IsWholeTrip) kind = "whole trip (" + r.Format + ")";
            else if (r.SingleDocumentKind != null) kind = s != null ? TripTransfer.DocumentFileName(s.Data, r.SingleDocumentKind.Value, r.OutlineBookId) : r.SingleDocumentKind.Value.ToString();
            else kind = "unknown";
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
            var import = U.Btn(r.CanImport ? (r.IsWholeTrip ? "Import trip" : "Import document") : "Cannot import (" + U.Plural(errs, "error") + ")", () => ImportFlow.Commit(app), "big pri");
            import.SetEnabled(r.CanImport);
            actions.Add(import);
            actions.Add(U.Btn("Discard", () => ImportFlow.Discard(app), "big ml10"));
            host.Add(actions);
            host.Add(U.Sub(r.CanImport ? "Warnings are kept as flags on the imported items, not silently dropped — the badge in the top bar stays until they are fixed." : "Errors block the import. Fix the file, or import the pieces that are clean.").Mt(10));
            return host;
        }
    }
}

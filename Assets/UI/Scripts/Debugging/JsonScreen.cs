using System.Collections.Generic;
using System.Linq;
using MediaTrip.Persistence;
using MediaTrip.UI.ViewModels;
using MediaTrip.Validation;
using UnityEngine.UIElements;

namespace MediaTrip.UI.Debugging
{
    /// <summary>
    /// Read-only JSON of each document with line numbers, the document's validation issues,
    /// and highlight-on-tap. Not part of the navigation: five quick taps on the library title,
    /// or Ctrl/Cmd+Shift+J, open it.
    /// </summary>
    public static class JsonScreen
    {
        public static VisualElement Build(AppController app)
        {
            var st = app.State; var J = st.JS; var s = app.Session; var d = s.Data; var val = app.CurrentValidation;
            var land = app.Layout.Land;

            var docs = new List<(DocumentKind kind, string bookId, string name)>
            {
                (DocumentKind.Trip, null, "trip.json"), (DocumentKind.ShotList, null, "shotlist.json"), (DocumentKind.Captures, null, "captures.json"),
            };
            foreach (var bookId in d.Outlines.Keys.OrderBy(k => d.FindBook(k)?.Number ?? 99))
                docs.Add((DocumentKind.Outline, bookId, "outlines/" + TripSaver.OutlineFileName(d, bookId)));
            if (J.Doc == DocumentKind.Outline && (J.OutlineBook == null || !d.Outlines.ContainsKey(J.OutlineBook))) J.OutlineBook = d.Outlines.Keys.FirstOrDefault();
            if (J.Doc == DocumentKind.Outline && J.OutlineBook == null) J.Doc = DocumentKind.ShotList;
            var current = docs.First(x => x.kind == J.Doc && (x.kind != DocumentKind.Outline || x.bookId == J.OutlineBook));
            var text = TripPackage.ExportDocumentJson(d, current.kind, current.bookId);

            // syntax colors come from the active theme's variables
            var vars = ThemeManager.ReadVariables(app.Theme.Theme, app.Theme.Dark);
            vars.TryGetValue("ac", out var keyColor);
            vars.TryGetValue("ok", out var strColor);
            vars.TryGetValue("warn", out var numColor);
            var lines = JsonHighlighter.Lines(text, J.Highlight, keyColor, strColor, numColor);
            var issues = val.InDocument(current.kind, current.bookId);

            var root = U.Col().Cls("grow fullsize minh0");
            var hdr = new VisualElement().Cls("hdr");
            var back = U.GlyphBtn(GlyphKind.ChevronLeft, "Done", () => app.Nav(Screen.Summary), "sm ghost accent", 16);
            back.style.marginLeft = -10;
            hdr.Add(back);
            hdr.Add(U.Text("JSON", "t grow").Ml(8));
            hdr.Add(U.Btn("Copy", () => { UnityEngine.GUIUtility.systemCopyBuffer = text; app.Toast("Copied " + current.name); }, "sm"));
            root.Add(hdr);

            // ---- documents and issues
            var side = U.Scroll().Named("json-side");
            side.ContentPad(10, 12, 12, 12);
            var chips = U.Row().Cls("wrap");
            foreach (var (kind, bookId, name) in docs)
            {
                var k = kind; var b = bookId;
                chips.Add(U.Chip(name, kind == current.kind && bookId == current.bookId, () => { J.Doc = k; J.OutlineBook = b; J.Highlight = null; app.Render(); }));
            }
            side.Add(chips);
            side.Add(U.Eyebrow(lines.Count + " lines · " + (text.Length / 1024f).ToString("0.0") + " KB · " + U.Plural(issues.Count, "issue")).Mt(8).Mb(6));
            foreach (var i in issues)
            {
                var issue = i;
                side.Add(U.Tap(() => { J.Highlight = issue.EntityId; app.Render(); }, "issue",
                    new VisualElement().Cls("dot" + (i.Severity == IssueSeverity.Warning ? " warn" : "")),
                    U.Col(U.Text(U.Esc(i.Message)).Font(15), U.Sub("fix in " + ValidationSummary.FixLabel(ValidationSummary.FixScreen(i, d)))).Cls("grow")));
            }
            var elsewhere = val.Issues.Count - issues.Count;
            if (elsewhere > 0) side.Add(U.Sub(U.Plural(elsewhere, "issue") + " in other documents").Mt(10));

            // ---- code
            var code = U.Scroll().Named("json-code:" + current.name);
            code.ContentPad(8, 0, 8, 0);
            VisualElement firstHl = null;
            foreach (var line in lines)
            {
                var row = new VisualElement().Cls("jl").On(line.Highlight, "hl");
                row.Add(U.Text(line.Number.ToString(), "ln"));
                var lbl = U.Text(line.Rich, "code"); lbl.style.whiteSpace = WhiteSpace.Pre;
                row.Add(lbl);
                code.Add(row);
                if (line.Highlight && firstHl == null) firstHl = row;
            }
            if (firstHl != null) code.schedule.Execute(() => code.ScrollTo(firstHl)).StartingIn(50);

            if (land)
            {
                side.style.flexGrow = 0; side.W(380); side.Cls("bordered-right");
                root.Add(U.Row(side, code).Cls("grow stretch minh0"));
            }
            else
            {
                side.style.flexGrow = 0; side.style.maxHeight = app.Layout.Height * 0.35f; side.Cls("bordered-bottom");
                root.Add(side);
                root.Add(code);
            }
            return root;
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using MediaTrip.Persistence;
using MediaTrip.UI.ViewModels;
using MediaTrip.Validation;
using UnityEngine.UIElements;

namespace MediaTrip.UI.Author
{
    /// <summary>Read-only JSON of each document with line numbers, the document's issues, and highlight-on-tap.</summary>
    public static class JsonScreen
    {
        public static VisualElement Build(AppController app)
        {
            var st = app.State; var J = st.JS; var s = app.Session; var d = s.Data; var val = app.CurrentValidation;
            var root = U.Row().Cls("grow stretch"); root.style.minHeight = 0;

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

            // theme colors for the syntax coloring come from the resolved style at render time
            var keyColor = "#0b57c2"; var strColor = "#1f7a45"; var numColor = "#8a5500";
            var vars = ThemeManager.ReadVariables(app.Theme.Theme, app.Theme.Dark);
            if (vars.TryGetValue("ac", out var acv)) keyColor = acv;
            if (vars.TryGetValue("ok", out var okv)) strColor = okv;
            if (vars.TryGetValue("warn", out var wv)) numColor = wv;
            var lines = JsonHighlighter.Lines(text, J.Highlight, keyColor, strColor, numColor);
            var issues = val.InDocument(current.kind, current.bookId);

            // ---- nav
            var nav = new VisualElement().Cls("nav").W(380);
            var head = new VisualElement().Cls("head"); head.style.height = StyleKeyword.Auto; head.style.paddingTop = 8; head.style.paddingBottom = 8;
            var chips = U.Row().Cls("wrap");
            foreach (var (kind, bookId, name) in docs)
            {
                var k = kind; var b = bookId;
                var on = kind == current.kind && bookId == current.bookId;
                chips.Add(U.Chip(name, on, () => { J.Doc = k; J.OutlineBook = b; J.Highlight = null; app.Render(); }, "h44").Font(14));
            }
            head.Add(chips);
            nav.Add(head);
            var nb = U.Scroll().Cls("bodyN");
            nb.Add(U.Eyebrow("Read-only · " + lines.Count + " lines · " + (text.Length / 1024f).ToString("0.0") + " KB").Mt(8).Mb(6).Ml(10));
            nb.Add(U.Eyebrow("Issues in this document · " + issues.Count).Mt(14).Mb(6).Ml(10));
            if (issues.Count == 0) nb.Add(U.Sub("Valid against the schema. Every reference resolves.").Pad(0, 10));
            foreach (var i in issues)
            {
                var issue = i;
                var fix = ValidationSummary.FixScreen(i, d);
                var row = U.Tap(() => { J.Highlight = issue.EntityId; app.Render(); }, "issue",
                    new VisualElement().Cls("dot" + (i.Severity == IssueSeverity.Warning ? " warn" : "")),
                    U.Col(U.Text(U.Esc(i.Message)).Font(15), U.Sub("tap to highlight · <b>fix in " + ValidationSummary.FixLabel(fix) + "</b>")).Cls("grow"));
                nb.Add(row);
            }
            if (issues.Count > 0)
                nb.Add(U.Btn("Open editor for the first issue", () => GoFix(app, issues[0]), "sm pri").Mt(10).Ml(10));
            var all = val.Issues.Where(i => !(i.Document == current.kind && (current.kind != DocumentKind.Outline || i.BookId == current.bookId))).ToList();
            if (all.Count > 0) nb.Add(U.Sub(U.Plural(all.Count, "issue") + " in other documents").Mt(14).Pad(0, 10));
            nav.Add(nb);
            root.Add(nav);

            // ---- code
            var edit = new VisualElement().Cls("edit");
            var bar = U.Row().H(64).Pad(0, 20).Cls("bordered-bottom");
            bar.Add(U.H2(current.name).Cls("grow"));
            bar.Add(U.Btn("Copy", () => { UnityEngine.GUIUtility.systemCopyBuffer = text; app.Toast("Copied " + current.name + " to the clipboard"); }, "sm"));
            edit.Add(bar);
            var code = U.Scroll().Cls("bodyE").Pad(8, 0);
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
            edit.Add(code);
            if (firstHl != null) code.schedule.Execute(() => code.ScrollTo(firstHl)).StartingIn(50);
            root.Add(edit);
            return root;
        }

        private static void GoFix(AppController app, ValidationIssue i)
        {
            var st = app.State; var d = app.Session.Data;
            var screen = ValidationSummary.FixScreen(i, d);
            if (screen == Screen.ShotList && i.EntityId != null)
            {
                if (d.FindVideo(i.EntityId) != null) { st.SL.Pane = "videos"; st.SL.SelKind = "video"; st.SL.SelId = i.EntityId; }
                else if (d.FindPhoto(i.EntityId) != null) { st.SL.Pane = "photos"; st.SL.SelKind = "photo"; st.SL.SelId = i.EntityId; }
                else if (d.FindChapter(i.EntityId) != null) { st.SL.Pane = "videos"; st.SL.SelKind = "chapter"; st.SL.SelId = i.EntityId; }
            }
            if (screen == Screen.People && i.EntityId != null && d.FindPerson(i.EntityId) != null) st.PeopleSel = i.EntityId;
            if (screen == Screen.Outline && i.BookId != null) st.OL.Book = i.BookId;
            app.Nav(screen);
        }
    }
}

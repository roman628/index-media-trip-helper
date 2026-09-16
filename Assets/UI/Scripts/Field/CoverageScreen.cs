using System.Linq;
using MediaTrip.Model;
using MediaTrip.Query;
using MediaTrip.Status;
using UnityEngine.UIElements;

namespace MediaTrip.UI.Field
{
    /// <summary>Which outline sections have media assigned, which are empty, and one-tap confirmation of auto suggestions.</summary>
    public static class CoverageScreen
    {
        public static VisualElement Build(AppController app)
        {
            var st = app.State;
            var s = app.Session;
            var d = s.Data;
            if (st.CovBook == null || d.FindBook(st.CovBook) == null) st.CovBook = d.Trip.Books.FirstOrDefault()?.Id;
            var root = U.Scroll().Cls("grow").Pad(20, 24);
            var chips = U.Row().Cls("wrap").Mb(14);
            foreach (var b in d.Trip.Books)
                chips.Add(U.Chip("Book " + b.Number + " · " + b.Name, st.CovBook == b.Id, () => { st.CovBook = b.Id; app.Render(); }));
            root.Add(chips);
            if (st.CovBook == null) { root.Add(U.Card(U.Sub("No books yet."))); return root; }
            root.Add(Body(app, st.CovBook));
            return root;
        }

        public static VisualElement Body(AppController app, string bookId)
        {
            var st = app.State;
            var s = app.Session;
            var d = s.Data;
            var book = d.FindBook(bookId);
            var cov = s.Queries.OutlineCoverage(bookId);
            var host = U.Col();
            if (!cov.HasOutline)
            {
                var card = U.Card(U.H2("No outline mockup for Book " + (book?.Number.ToString() ?? "?") + " yet").Mb(8),
                    U.Sub("Import the book's outline JSON, or create one from the shot list's chapters in authoring mode.").Mb(20),
                    U.Row(U.Btn("Import outline (JSON)", () => app.Nav(Screen.IO), "pri big"), U.Btn("Create from chapters", () => { s.Outline.Ensure(bookId); app.Toast("Outline created from " + d.ChaptersOf(bookId).Count() + " chapters"); }, "big ml10")));
                card.style.alignItems = Align.Center;
                card.Pad(48);
                host.Add(card);
                return host;
            }
            host.Add(U.Row(U.Col(U.H2(U.Esc(cov.Outline.BookTitle ?? book?.Name)), U.Sub(U.Esc(cov.Outline.ProgramName ?? "") + " · " + cov.SectionsCovered + " of " + cov.SectionsTotal + " sections have media")).Cls("grow"),
                U.Toggle("Empty only", st.CovEmptyOnly, () => { st.CovEmptyOnly = !st.CovEmptyOnly; app.Render(); })).Mb(14));
            foreach (var ch in cov.Chapters)
            {
                var secs = ch.Sections.Where(x => !st.CovEmptyOnly || x.IsEmpty).ToList();
                if (secs.Count == 0 && ch.AssignedToChapter.Count == 0) continue;
                host.Add(U.Eyebrow("Chapter " + ch.Chapter.Number + " · " + ch.Chapter.Name).Mt(14).Mb(8));
                foreach (var a in ch.AssignedToChapter) host.Add(AssignmentRow(app, a));
                foreach (var sec in secs)
                {
                    var card = U.Card().Mb(8).Pad(14, 18);
                    if (sec.IsEmpty) card.Cls("dashed");
                    var row = U.Row(); row.style.alignItems = Align.FlexStart;
                    row.Add(U.Num(sec.Section.Number ?? "", "md").W(56));
                    var col = U.Col(U.H3(U.Esc(sec.Section.Name)).Mb(sec.IsEmpty ? 0 : 8)).Cls("grow");
                    foreach (var a in sec.All) col.Add(AssignmentRow(app, a));
                    row.Add(col);
                    if (sec.IsEmpty) row.Add(U.Pill("Empty", "warn lg"));
                    card.Add(row);
                    host.Add(card);
                }
            }
            if (cov.Unplaced.Count > 0)
            {
                host.Add(U.Eyebrow("Assigned to this book but not to a known section").Mt(14).Mb(8));
                foreach (var a in cov.Unplaced) host.Add(AssignmentRow(app, a));
            }
            var (unCaps, unPcs) = s.Queries.UnassignedMedia();
            unCaps = unCaps.Where(c => c.BookId == bookId).ToList();
            if (unCaps.Count > 0)
            {
                host.Add(U.Eyebrow("Captured in this book, not yet in the outline").Mt(18).Mb(8));
                foreach (var c in unCaps)
                {
                    var sugg = s.Search.SuggestOutlineFor(c, 1).FirstOrDefault();
                    var row = U.Row().Cls("card").Mb(8).Pad(10, 14);
                    row.Add(U.Text(U.Esc(c.Title), "bold").Cls("grow"));
                    if (sugg != null)
                    {
                        row.Add(U.Sub("suggest " + U.Esc(sugg.Label) + " · " + (int)(sugg.Score * 100) + "%").Mr(10));
                        row.Add(U.Btn("Assign", () => { s.AssignSuggestion(new MediaRef(MediaRefKind.Capture, c.Id), sugg); s.ConfirmAssignment(s.Data.Captures.OutlineAssignments.Last().Id); app.Toast("Assigned · " + sugg.Label); }, "pri sm"));
                    }
                    else row.Add(U.Sub("no suggestion"));
                }
            }
            return host;
        }

        private static VisualElement AssignmentRow(AppController app, AssignedMedia a)
        {
            var s = app.Session;
            var row = U.Row().Cls("wrap").Mb(6);
            if (a.Confirmed) row.Add(U.St(PlanItemStatus.Captured, 28));
            else { var ring = new VisualElement().Cls("st st-suggest").W(28).H(28); ring.Add(U.Text("?").Font(14).Bold()); row.Add(ring); }
            row.Add(U.Text(U.Esc(a.Title), "bold").Ml(10).Mr(10));
            if (a.Confirmed) row.Add(U.Sub(a.Assignment.Source == AssignmentSource.Auto ? "auto · confirmed" : "manual"));
            else
            {
                row.Add(U.Sub("auto · " + (int)((a.Assignment.Confidence ?? 0) * 100) + "% · unconfirmed").Mr(10));
                row.Add(U.GlyphBtn(GlyphKind.Check, "Confirm", () => { s.ConfirmAssignment(a.Assignment.Id); app.Toast("Confirmed · " + a.Title); }, "pri sm", 14));
                row.Add(U.Btn("Wrong", () => { s.RemoveAssignment(a.Assignment.Id); app.Toast("Suggestion removed"); }, "sm ml6"));
            }
            return row;
        }
    }
}

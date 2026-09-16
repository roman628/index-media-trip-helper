using System.Collections.Generic;
using System.Linq;
using MediaTrip.Model;
using MediaTrip.Status;
using MediaTrip.UI.ViewModels;
using UnityEngine.UIElements;

namespace MediaTrip.UI.Author
{
    /// <summary>Review the changes recorded on top of the plan, in order; edit their wording, or undo them.</summary>
    public static class AmendmentsScreen
    {
        private static string TypeLabel(AmendmentType t)
        {
            switch (t)
            {
                case AmendmentType.Combine: return "Combine";
                case AmendmentType.Rename: return "Rename";
                case AmendmentType.Add: return "Add (unplanned)";
                case AmendmentType.Drop: return "Drop";
                case AmendmentType.Split: return "Split";
                case AmendmentType.Move: return "Move";
                default: return t.ToString();
            }
        }

        public static VisualElement Build(AppController app)
        {
            var st = app.State; var s = app.Session; var d = s.Data;
            var root = U.Scroll().Cls("grow").Pad(20, 24);
            root.Add(U.Col(U.H1("Amendments"), U.Sub("The plan stays as printed. These are the changes recorded on top of it, in order. Editing here changes what the summary says; undoing restores the planned item.")).Mb(6));
            var rows = PlanResolver.OrderedAmendments(d.Captures.Amendments);
            if (rows.Count == 0) root.Add(U.Card(U.Sub("No amendments recorded.")).Mt(12));
            foreach (var a in rows)
            {
                var aid = a.Id;
                var card = U.Card().Cls("row").Mt(12); card.style.alignItems = Align.FlexStart;
                var pillCls = a.Type == AmendmentType.Drop ? "bad" : a.Type == AmendmentType.Add ? "ok" : "ac";
                var pill = U.Pill(TypeLabel(a.Type), pillCls + " md").Mr(14); pill.style.minWidth = 120; pill.style.justifyContent = Justify.Center;
                card.Add(pill);
                card.Add(U.Col(U.Text(Describe(app, a), "body").Font(18).Mb(4), U.Sub(U.FmtTimestamp(a.At) + " · " + U.Esc(a.Reason ?? "no reason recorded"))).Cls("grow"));
                card.Add(U.Btn("Edit", () => { st.AmendEdit = aid; app.Render(); }, "sm ml10"));
                card.Add(U.Btn("Undo", () => { s.UndoAmendment(aid); app.Toast("Amendment undone"); }, "sm danger ml8"));
                root.Add(card);
            }
            return root;
        }

        private static string Describe(AppController app, Amendment a)
        {
            var s = app.Session; var d = s.Data;
            string Tg(string t) { var v = d.FindVideo(t); var it = s.Plan.FindItem(t); return v != null ? "#" + v.Number + " " + U.Esc(v.Title) : it != null ? it.DisplayNumber + " " + U.Esc(it.Title) : t; }
            var targets = (a.Targets ?? new List<string>()).Select(Tg).ToList();
            switch (a.Type)
            {
                case AmendmentType.Combine: return string.Join(" + ", targets) + " → <b>" + U.Esc(a.NewTitle) + "</b> (" + (s.Plan.FindItem(a.Results.FirstOrDefault())?.DisplayNumber ?? "?") + ")";
                case AmendmentType.Rename: return targets.FirstOrDefault() + " → <b>" + U.Esc(a.NewTitle) + "</b>";
                case AmendmentType.Add: return "<b>" + U.Esc(a.NewTitle) + "</b> in " + Fmt.BookChapter(d, a.NewBookId, a.NewChapterId);
                case AmendmentType.Drop: return string.Join(", ", targets) + " not shot";
                case AmendmentType.Split: return targets.FirstOrDefault() + " → " + string.Join(" / ", (a.NewTitles ?? new List<string>()).Select(U.Esc));
                case AmendmentType.Move: return targets.FirstOrDefault() + " → " + Fmt.BookChapter(d, a.NewBookId, a.NewChapterId);
                default: return string.Join(", ", targets);
            }
        }

        public static VisualElement EditSheet(AppController app)
        {
            var st = app.State; var s = app.Session; var d = s.Data;
            var a = d.FindAmendment(st.AmendEdit);
            void Close() { st.AmendEdit = null; app.Render(); }
            if (a == null) { st.AmendEdit = null; return new VisualElement(); }
            var sheet = new VisualElement().Cls("sheet");
            sheet.Add(U.Row(U.H2("Edit " + TypeLabel(a.Type).ToLowerInvariant()).Cls("grow"), U.Btn("Cancel", Close, "sm")).Mb(14));
            if (a.Type != AmendmentType.Drop && a.Type != AmendmentType.Move)
            {
                var title = U.Input(a.NewTitle, null, v => app.Edit(() => s.UpdateAmendment(a.Id, x => x.NewTitle = v))); title.name = "amend:" + a.Id + ":title";
                sheet.Add(U.Field("Title as shot", title));
            }
            var reason = U.Input(a.Reason, null, v => app.Edit(() => s.UpdateAmendment(a.Id, x => x.Reason = v)), true); reason.name = "amend:" + a.Id + ":reason";
            sheet.Add(U.Field("Reason", reason));
            if (a.Type == AmendmentType.Add || a.Type == AmendmentType.Move)
            {
                sheet.Add(U.Lbl("Chapter"));
                var chips = U.Row().Cls("wrap").Mb(12);
                foreach (var c in d.ShotList.Chapters)
                {
                    var cid = c.Id; var b = d.FindBook(c.BookId);
                    chips.Add(U.Chip("B" + (b?.Number.ToString() ?? "?") + " Ch." + c.Number + " " + U.Esc(c.Name), a.NewChapterId == c.Id, () => { s.UpdateAmendment(a.Id, x => { x.NewChapterId = cid; x.NewBookId = c.BookId; }); app.Render(); }));
                }
                sheet.Add(chips);
            }
            sheet.Add(U.Row(U.Grow(), U.Btn("Done", Close, "big pri")));
            return app.Overlay(sheet, Close);
        }
    }

}

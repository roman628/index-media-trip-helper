using System.Collections.Generic;
using System.Linq;
using MediaTrip.Authoring;
using MediaTrip.Model;
using UnityEngine.UIElements;

namespace MediaTrip.UI.Author
{
    /// <summary>Outline mockup editor: one book at a time; chapters on the left, sections with bullets and chapter notes on the right.</summary>
    public static class OutlineScreen
    {
        public static VisualElement Build(AppController app)
        {
            var st = app.State; var O = st.OL; var s = app.Session; var d = s.Data; var oe = s.Outline;
            if (O.Book == null || d.FindBook(O.Book) == null) O.Book = d.Trip.Books.FirstOrDefault()?.Id;
            var ol = d.FindOutline(O.Book);
            var chapters = ol?.Chapters ?? new List<OutlineChapter>();
            var c = chapters.FirstOrDefault(x => x.Id == O.Chapter) ?? chapters.FirstOrDefault();
            if (c != null) O.Chapter = c.Id;

            var root = U.Row().Cls("grow stretch"); root.style.minHeight = 0;

            // ---- nav
            var nav = new VisualElement().Cls("nav").W(320);
            var head = new VisualElement().Cls("head");
            if (d.Trip.Books.Count > 0)
                head.Add(U.Segmented(d.Trip.Books.Select(b => (b.Id, "Book " + b.Number)).ToList(), O.Book, id => { O.Book = id; O.Chapter = d.FindOutline(id)?.Chapters.FirstOrDefault()?.Id; O.Sel = null; app.Render(); }, 44).Cls("grow"));
            else head.Add(U.Sub("No books yet"));
            nav.Add(head);
            var nb = U.Scroll().Cls("bodyN");
            if (ol != null)
            {
                var t = U.Input(ol.BookTitle, null, v => app.Edit(() => oe.UpdateHeader(O.Book, o => o.BookTitle = v)), false, "h48"); t.name = "ol:title";
                nb.Add(U.Field("Book title", t).Pad(4, 6));
                var p = U.Input(ol.ProgramName, null, v => app.Edit(() => oe.UpdateHeader(O.Book, o => o.ProgramName = v)), false, "h48"); p.name = "ol:program";
                nb.Add(U.Field("Program", p).Pad(0, 6));
                nb.Add(U.Eyebrow("Chapters").Mt(8).Mb(4).Ml(10));
                foreach (var cc in chapters)
                {
                    var row = U.Tap(() => { O.Chapter = cc.Id; O.Sel = null; app.Render(); }, "trow",
                        new Glyph(GlyphKind.Lines, 18).Cls("handle"), U.Num(cc.Number.ToString(), "sm"),
                        U.Col(U.Text(U.Esc(string.IsNullOrEmpty(cc.Name) ? "Untitled" : cc.Name), "bold").Font(15).Cls("ellipsis"), U.Sub(U.Plural(cc.Sections.Count, "section"))).Cls("grow"));
                    row.MinH(56).On(c != null && c.Id == cc.Id);
                    nb.Add(row);
                }
                var add = U.Tap(() => { var nc = oe.AddChapter(O.Book, ""); O.Chapter = nc.Id; O.Sel = null; app.State.Focus = "oc:" + nc.Id + ":name"; app.Render(); }, "trow", U.Text("+ Chapter", "bold accent"));
                add.MinH(48);
                nb.Add(add);
            }
            else if (O.Book != null)
            {
                var b = d.FindBook(O.Book);
                var card = U.Card(U.H3("No outline for Book " + b.Number).Mb(8), U.Sub("Start from the shot list's chapters, or import the mockup JSON.").Mb(12),
                    U.Btn("Create from chapters", () => { oe.Ensure(O.Book); O.Chapter = d.FindOutline(O.Book)?.Chapters.FirstOrDefault()?.Id; app.Toast("Outline created from " + d.ChaptersOf(O.Book).Count() + " chapters"); }, "pri").Mb(8),
                    U.Btn("Import JSON", () => app.Nav(Screen.IO)));
                card.style.marginLeft = 8; card.style.marginRight = 8;
                nb.Add(card);
            }
            nav.Add(nb);
            root.Add(nav);

            // ---- edit
            var edit = new VisualElement().Cls("edit");
            var body = U.Scroll().Cls("bodyE");
            edit.Add(body);
            NodeListEditor active = null;
            string activeNode = null;
            if (ol != null && c != null)
            {
                var name = U.Input(c.Name, "Chapter name", v => app.Edit(() => oe.UpdateChapter(O.Book, c.Id, x => x.Name = v)), false, "h52 tall"); name.name = "oc:" + c.Id + ":name";
                body.Add(U.Row(U.Num(c.Number.ToString(), "lg").W(56), U.Field(null, name).Cls("grow").Mb(0)).Mb(8));
                foreach (var sec in c.Sections)
                {
                    var selected = O.Sel == "sec:" + sec.Id;
                    var card = U.Card().Mt(12).Mb(12).Pad(14, 16);
                    if (selected) card.Cls("ac");
                    var head2 = U.Row().Mb(8);
                    head2.Add(U.Tap(() => { O.Sel = "sec:" + sec.Id; app.Render(); }, "ohandle", new Glyph(GlyphKind.Lines, 20)).W(36).H(52));
                    head2.Add(U.Num(sec.Number ?? "", "md").W(56));
                    var sn = U.Input(sec.Name, "Section name", v => app.Edit(() => oe.UpdateSection(O.Book, sec.Id, x => x.Name = v)), false, "h52"); sn.name = "os:" + sec.Id + ":name";
                    sn.RegisterCallback<FocusInEvent>(_ => { O.Sel = "sec:" + sec.Id; });
                    head2.Add(sn.Cls("grow"));
                    var media = d.Captures.OutlineAssignments.Count(a => a.BookId == O.Book && a.SectionId == sec.Id);
                    head2.Add(U.Sub(U.Plural(media, "media item", "media items")).Ml(12).Cls("nowrap"));
                    card.Add(head2);
                    var bullets = oe.Bullets(O.Book, sec.Id);
                    var nodeSel = O.Sel != null && O.Sel.StartsWith("node:") && NodeTree.Locate(bullets.Roots, O.Sel.Substring(5)) != null ? O.Sel.Substring(5) : null;
                    if (nodeSel != null) { active = bullets; activeNode = nodeSel; }
                    var ov = OutlinerView.Build(app, bullets, nodeSel, id => { O.Sel = "node:" + id; }, "section:" + sec.Id);
                    ov.style.marginLeft = 36;
                    card.Add(ov);
                    body.Add(card);
                }
                var addSec = U.BtnKbd("+ Section", "⇧Enter", () => { var ns = oe.AddSection(O.Book, c.Id, ""); O.Sel = "sec:" + ns.Id; app.State.Focus = "os:" + ns.Id + ":name"; app.Render(); }, "");
                addSec.style.alignSelf = Align.FlexStart;
                body.Add(addSec.Mt(4).Mb(18));
                var notes = U.Input(c.Notes, "Per-chapter notes area", v => app.Edit(() => oe.SetChapterNotes(O.Book, c.Id, v)), true); notes.name = "oc:" + c.Id + ":notes";
                body.Add(U.Field("Chapter notes", notes));
            }
            else body.Add(U.Sub("Pick a book."));
            if (ol != null)
            {
                var kind = activeNode != null ? "node" : O.Sel != null && O.Sel.StartsWith("sec:") ? "section" : "chapter";
                edit.Add(AccessoryBar.Build(app, kind, canChild: true, canIndent: kind == "node", showPaste: kind != "chapter"));
            }
            root.Add(edit);

            app.AccessoryAction = act => Accessory(app, act, active, activeNode);
            return root;
        }

        /// <summary>Which section of the selected book's outline contains a node id.</summary>
        public static string SectionOfNode(AppController app, string nodeId)
        {
            var ol = app.Session.Data.FindOutline(app.State.OL.Book);
            if (ol == null) return null;
            foreach (var c in ol.Chapters) foreach (var s in c.Sections) if (NodeTree.Locate(s.Nodes, nodeId) != null) return s.Id;
            return null;
        }

        private static bool Accessory(AppController app, string act, NodeListEditor active, string activeNode)
        {
            var st = app.State; var O = st.OL; var s = app.Session; var d = s.Data; var oe = s.Outline;
            var ol = d.FindOutline(O.Book);
            if (ol == null) return false;
            var c = ol.Chapters.FirstOrDefault(x => x.Id == O.Chapter);
            if (c == null) return false;

            if (act == "paste")
            {
                if (O.Sel == null) { app.Toast("Select a section to paste bullets into"); return true; }
                O.Paste = ""; app.Render(); return true;
            }

            if (active != null && activeNode != null)
            {
                var next = OutlinerView.Apply(active, activeNode, act, out var handled);
                if (!handled) return false;
                O.Sel = next != null ? "node:" + next : "sec:" + SectionOfNode(app, activeNode);
                app.State.Focus = next != null ? "node:" + next : null;
                app.Render();
                return true;
            }

            if (O.Sel != null && O.Sel.StartsWith("sec:"))
            {
                var id = O.Sel.Substring(4);
                var i = c.Sections.FindIndex(x => x.Id == id);
                if (i < 0) return false;
                switch (act)
                {
                    case "sib": { var ns = oe.AddSection(O.Book, c.Id, "", i + 1); O.Sel = "sec:" + ns.Id; app.State.Focus = "os:" + ns.Id + ":name"; app.Render(); return true; }
                    case "child": { var n = oe.Bullets(O.Book, id).AddRoot(""); O.Sel = "node:" + n.Id; app.State.Focus = "node:" + n.Id; app.Render(); return true; }
                    case "up": if (i > 0) { oe.ReorderSection(O.Book, id, i - 1); app.Render(); } return true;
                    case "down": if (i < c.Sections.Count - 1) { oe.ReorderSection(O.Book, id, i + 1); app.Render(); } return true;
                    case "dup":
                    {
                        var src = c.Sections[i];
                        var ns = oe.AddSection(O.Book, c.Id, src.Name, i + 1);
                        oe.Bullets(O.Book, ns.Id).Replace(NodeTree.Clone(src.Nodes, newIds: true));
                        O.Sel = "sec:" + ns.Id; app.Render(); return true;
                    }
                    case "del": ShotListScreen.Guarded(app, () => { oe.RemoveSection(O.Book, id); O.Sel = null; }); app.Render(); return true;
                    case "prev": case "next": { var j = act == "prev" ? i - 1 : i + 1; if (j >= 0 && j < c.Sections.Count) { O.Sel = "sec:" + c.Sections[j].Id; app.Render(); } return true; }
                }
                return false;
            }

            // chapter level
            var ci = ol.Chapters.IndexOf(c);
            switch (act)
            {
                case "sib": { var nc = oe.AddChapter(O.Book, "", ci + 1); O.Chapter = nc.Id; O.Sel = null; app.State.Focus = "oc:" + nc.Id + ":name"; app.Render(); return true; }
                case "child": { var ns = oe.AddSection(O.Book, c.Id, ""); O.Sel = "sec:" + ns.Id; app.State.Focus = "os:" + ns.Id + ":name"; app.Render(); return true; }
                case "up": if (ci > 0) { oe.ReorderChapter(O.Book, c.Id, ci - 1); app.Render(); } return true;
                case "down": if (ci < ol.Chapters.Count - 1) { oe.ReorderChapter(O.Book, c.Id, ci + 1); app.Render(); } return true;
                case "del":
                    if (ol.Chapters.Count <= 1) { app.Toast("An outline keeps at least one chapter"); return true; }
                    ShotListScreen.Guarded(app, () => { oe.RemoveChapter(O.Book, c.Id); O.Chapter = ol.Chapters.FirstOrDefault()?.Id; O.Sel = null; });
                    app.Render(); return true;
                case "dup": app.Toast("Chapters are not duplicated; add a sibling instead"); return true;
                case "prev": case "next": { var j = act == "prev" ? ci - 1 : ci + 1; if (j >= 0 && j < ol.Chapters.Count) { O.Chapter = ol.Chapters[j].Id; O.Sel = null; app.Render(); } return true; }
            }
            return false;
        }
    }
}

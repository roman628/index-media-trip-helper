using System.Collections.Generic;
using MediaTrip.Authoring;
using MediaTrip.Model;
using MediaTrip.UI.Field;
using UnityEngine.UIElements;

namespace MediaTrip.UI.Author
{
    /// <summary>Paste a block of text and see it as nested bullets before inserting it.</summary>
    public static class PasteSheet
    {
        public static VisualElement Build(AppController app, string target)
        {
            var st = app.State;
            string text = target == "outline" ? st.OL.Paste : st.SL.Paste;
            void Close() { if (target == "outline") st.OL.Paste = null; else st.SL.Paste = null; app.Render(); }

            var sheet = new VisualElement().Cls("sheet wide");
            sheet.style.height = 640;
            sheet.Add(U.Row(U.Col(U.H2("Paste notes"), U.Sub("Indentation and list markers (1. a) • -) become nesting. Edit on the left, check the right, then insert.")).Cls("grow"),
                U.Btn("Cancel", Close, "sm")).Mb(12));

            var body = U.Row().Cls("grow stretch"); body.style.minHeight = 0;
            var scheme = target == "outline" ? LabelScheme.Outline : LabelScheme.ShotListNotes;
            var preview = U.Scroll().Cls("grow card").Pad(12);
            var count = U.Sub("");
            void Refresh()
            {
                preview.Clear();
                var tree = IndentedTextParser.Parse(text ?? "", new PasteOptions { Relabel = scheme });
                if (tree.Count == 0) preview.Add(U.Sub("Nothing parsed yet."));
                else preview.Add(NotesView.Build(tree));
                count.text = U.Plural(NodeTree.Count(tree), "bullet");
            }
            var ta = U.Input(text ?? "", "Paste here…", v => { text = v; if (target == "outline") st.OL.Paste = v; else st.SL.Paste = v; Refresh(); }, true, "mono");
            ta.name = "paste.text";
            ta.style.width = Length.Percent(50);
            ta.style.height = Length.Percent(100);
            ta.style.marginRight = 16;
            body.Add(ta);
            body.Add(preview);
            sheet.Add(body);
            Refresh();

            var buttons = U.Row(count.Cls("grow")).Mt(14);
            buttons.Add(U.Btn("Replace", () => Insert(app, target, text, scheme, replace: true, close: Close), "big"));
            buttons.Add(U.Btn("Append", () => Insert(app, target, text, scheme, replace: false, close: Close), "big pri ml10"));
            sheet.Add(buttons);
            if (string.IsNullOrEmpty(text))
            {
                var clip = app.Transfer.ReadClipboard();
                if (!string.IsNullOrEmpty(clip) && clip.Length < 20000) { ta.SetValueWithoutNotify(clip); text = clip; if (target == "outline") st.OL.Paste = clip; else st.SL.Paste = clip; Refresh(); }
            }
            st.Focus = "paste.text";
            return app.Overlay(sheet, Close);
        }

        private static void Insert(AppController app, string target, string text, LabelScheme scheme, bool replace, System.Action close)
        {
            var tree = IndentedTextParser.Parse(text ?? "", new PasteOptions());
            NodeListEditor ed = null;
            var st = app.State;
            if (target == "outline")
            {
                if (st.OL.Sel != null && st.OL.Sel.StartsWith("sec:")) ed = app.Session.Outline.Bullets(st.OL.Book, st.OL.Sel.Substring(4));
                else if (st.OL.Sel != null && st.OL.Sel.StartsWith("node:"))
                {
                    var secId = OutlineScreen.SectionOfNode(app, st.OL.Sel.Substring(5));
                    if (secId != null) ed = app.Session.Outline.Bullets(st.OL.Book, secId);
                }
            }
            else if (st.SL.SelKind == "video" && st.SL.SelId != null) ed = app.Session.PlanEditor.Notes(st.SL.SelId);
            if (ed == null) { app.Toast("Select a video or a section first"); return; }
            if (replace) ed.Replace(tree); else ed.Append(tree);
            ed.Relabel(scheme);
            app.Toast(U.Plural(NodeTree.Count(tree), "bullet") + " inserted");
            close();
        }
    }
}

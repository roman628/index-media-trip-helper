using System.Collections.Generic;
using MediaTrip.Authoring;
using MediaTrip.Model;
using UnityEngine.UIElements;

namespace MediaTrip.UI.Docs
{
    /// <summary>Read-only nested bullets, arbitrary depth.</summary>
    public static class NotesView
    {
        public static VisualElement Build(List<Node> nodes, float fontSize = 17, float indent = 22)
        {
            var host = U.Col();
            Add(host, nodes, 0, indent, fontSize);
            return host;
        }

        private static void Add(VisualElement host, List<Node> nodes, int depth, float indent, float fontSize)
        {
            if (nodes == null) return;
            foreach (var n in nodes)
            {
                var row = new VisualElement().Cls("note");
                row.style.marginLeft = depth * indent;
                row.Add(U.Text(string.IsNullOrEmpty(n.Label) ? "•" : U.Esc(n.Label), "lb").Font(fontSize));
                row.Add(U.Text(U.Esc(n.Text), "txt").Font(fontSize));
                host.Add(row);
                Add(host, n.Children, depth + 1, indent, fontSize);
            }
        }
    }

    /// <summary>
    /// Editable nested bullets: one row per node with its label, an in-place text field named
    /// "node:<id>", and outdent / indent / delete. The keyboard does the same through
    /// <see cref="AppController.AccessoryAction"/>: Enter adds a bullet, Tab and Shift-Tab
    /// indent and outdent, Backspace on an empty bullet deletes it, Alt-arrows move it.
    /// </summary>
    public static class NodeEditor
    {
        public static VisualElement Build(AppController app, NodeListEditor ed, AppState.PasteState paste)
        {
            var host = U.Col();
            var step = app.Layout.Phone ? 14 : 26;
            foreach (var (node, depth) in Node.Walk(ed.Roots))
            {
                var id = node.Id;
                var row = new VisualElement().Cls("orow");
                row.style.marginLeft = System.Math.Min(depth, 4) * step;
                row.Add(U.Text(U.Esc(node.Label ?? ""), "olb"));
                var tf = U.Input(node.Text, "…", v => app.Edit(() => ed.SetText(id, v)));
                tf.name = "node:" + id;
                tf.RegisterCallback<FocusInEvent>(_ => app.State.Focus = tf.name);
                row.Add(tf);
                row.Add(U.Tap(() => Do(app, ed, id, "out"), "ob", new Glyph(GlyphKind.Outdent, 20)));
                row.Add(U.Tap(() => Do(app, ed, id, "in"), "ob", new Glyph(GlyphKind.Indent, 20)));
                row.Add(U.Tap(() => Do(app, ed, id, "del"), "ob", new Glyph(GlyphKind.Cross, 16)));
                host.Add(row);
            }
            var add = U.Row(U.AddBtn("Bullet", () => Do(app, ed, null, "sib")));
            if (paste != null) add.Add(U.Btn("Paste…", () => { app.State.Paste = paste; app.Render(); }, "sm ghost accent"));
            host.Add(add);

            // Several outliners can be open at once (expanded videos, expanded sections): each
            // answers for its own bullets and passes the rest on.
            var next = app.AccessoryAction;
            app.AccessoryAction = act =>
            {
                var name = app.FocusedFieldName;
                if (!name.StartsWith("node:")) return false;
                var nodeId = name.Substring(5);
                if (NodeTree.Locate(ed.Roots, nodeId) == null) return next != null && next(act);
                return Do(app, ed, nodeId, act);
            };
            return host;
        }

        /// <summary>Apply a structural action and keep the caret on the bullet it concerns.</summary>
        private static bool Do(AppController app, NodeListEditor ed, string id, string act)
        {
            string focus = id;
            switch (act)
            {
                case "sib": focus = (id == null ? ed.AddRoot("") : ed.AddSibling(id, "")).Id; break;
                case "child": focus = ed.AddChild(id, "", 0).Id; break;
                case "in": ed.Indent(id); break;
                case "out": ed.Outdent(id); break;
                case "up": ed.MoveUp(id); break;
                case "down": ed.MoveDown(id); break;
                case "del":
                {
                    var loc = NodeTree.Locate(ed.Roots, id);
                    if (loc == null) return false;
                    focus = loc.Index > 0 ? loc.Siblings[loc.Index - 1].Id : loc.Parent?.Id;
                    ed.Remove(id);
                    break;
                }
                case "prev":
                case "next":
                {
                    var flat = new List<string>();
                    foreach (var (n, _) in Node.Walk(ed.Roots)) flat.Add(n.Id);
                    var j = flat.IndexOf(id) + (act == "prev" ? -1 : 1);
                    if (j < 0 || j >= flat.Count) return false;
                    focus = flat[j];
                    break;
                }
                default: return false;
            }
            app.RenderKeepFocus(focus != null ? "node:" + focus : null);
            return true;
        }
    }

    /// <summary>Paste a block of text and see it as nested bullets before inserting it.</summary>
    public static class PasteSheet
    {
        public static VisualElement Build(AppController app)
        {
            var st = app.State; var p = st.Paste;
            void Close() { st.Paste = null; st.Focus = null; app.Render(); }
            var scheme = p.Kind == "section" ? LabelScheme.Outline : LabelScheme.ShotListNotes;

            var preview = U.Scroll().Cls("card");
            var count = U.Sub("").Cls("grow");
            void Refresh()
            {
                preview.Clear();
                var tree = IndentedTextParser.Parse(p.Text ?? "", new PasteOptions { Relabel = scheme });
                if (tree.Count == 0) preview.Add(U.Sub("Indentation and list markers become nesting."));
                else preview.Add(NotesView.Build(tree, 16));
                count.text = U.Plural(NodeTree.Count(tree), "bullet");
            }
            if (string.IsNullOrEmpty(p.Text))
            {
                var clip = app.Transfer.ReadClipboard();
                if (!string.IsNullOrEmpty(clip) && clip.Length < 20000) p.Text = clip;
            }
            var ta = U.Input(p.Text, "Paste here…", v => { p.Text = v; Refresh(); }, true, "mono");
            ta.name = "paste.text";
            ta.style.height = app.Layout.Phone ? 160 : 220;
            ta.style.marginBottom = 12;
            Refresh();

            var buttons = U.Row(count, U.Btn("Replace", () => Insert(app, scheme, true), "big"), U.Btn("Append", () => Insert(app, scheme, false), "big pri ml10")).Mt(12);
            buttons.style.flexShrink = 0;
            return app.Sheet(720, Close, app.SheetHead("Paste bullets", Close), ta, preview, buttons);
        }

        private static void Insert(AppController app, LabelScheme scheme, bool replace)
        {
            var p = app.State.Paste;
            var tree = IndentedTextParser.Parse(p.Text ?? "", new PasteOptions());
            var ed = p.Kind == "section"
                ? app.Session.Outline.Bullets(p.BookId, p.Id)
                : new MediaTrip.Authoring.PlanEdits(app.Session, p.Working ? MediaTrip.Authoring.PlanEditMode.Working : MediaTrip.Authoring.PlanEditMode.Original).Notes(p.Id);
            if (ed == null) { app.Toast("Nothing to paste into"); return; }
            if (replace) ed.Replace(tree); else ed.Append(tree);
            ed.Relabel(scheme);
            app.State.Paste = null;
            app.Render();
            app.Toast(U.Plural(NodeTree.Count(tree), "bullet") + " added");
        }
    }
}

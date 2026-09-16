using System;
using System.Collections.Generic;
using MediaTrip.Authoring;
using MediaTrip.Model;
using UnityEngine.UIElements;

namespace MediaTrip.UI.Author
{
    /// <summary>
    /// Editable nested bullets: one 52 px row per node with a handle (selects), the label,
    /// and an in-place text field named "node:<id>". Structural operations come from the
    /// accessory bar or the keyboard, both routed through <see cref="AppController.AccessoryAction"/>.
    /// </summary>
    public static class OutlinerView
    {
        public static VisualElement Build(AppController app, NodeListEditor editor, string selectedId, Action<string> onSelect, string emptyKey)
        {
            var host = U.Col();
            var roots = editor.Roots;
            if (roots.Count == 0)
            {
                var first = U.Tap(() =>
                {
                    var n = editor.AddRoot("");
                    onSelect(n.Id);
                    app.State.Focus = "node:" + n.Id;
                    app.Render();
                }, "item outlined", U.Sub("+ First bullet"), Shortcuts.HardwareKeyboardPresent ? U.Kbd("Enter") : null);
                first.MinH(52);
                host.Add(first);
                return host;
            }
            foreach (var (node, depth) in Node.Walk(roots))
            {
                var id = node.Id;
                var row = new VisualElement().Cls("orow").On(selectedId == id);
                row.style.marginLeft = depth * 32;
                var handle = U.Tap(() => { onSelect(id); app.State.Focus = "node:" + id; app.Render(); }, "ohandle", new Glyph(GlyphKind.Lines, 20));
                handle.tooltip = "Select this bullet (the bar below acts on it)";
                row.Add(handle);
                row.Add(U.Text(U.Esc(node.Label ?? ""), "olb"));
                var tf = U.Input(node.Text, "…", v => app.Edit(() => editor.SetText(id, v)));
                tf.name = "node:" + id;
                tf.RegisterCallback<FocusInEvent>(_ => { if (app.State.Focus != tf.name) { onSelect(id); app.State.Focus = tf.name; MarkSelected(host, tf.name); } });
                row.Add(tf);
                host.Add(row);
            }
            return host;
        }

        private static void MarkSelected(VisualElement host, string fieldName)
        {
            foreach (var r in host.Children())
            {
                var tf = r.Q<TextField>();
                r.EnableInClassList("on", tf != null && tf.name == fieldName);
            }
        }

        /// <summary>Apply an accessory action to a node list. Returns the id to select afterwards (or null when nothing changed).</summary>
        public static string Apply(NodeListEditor ed, string sel, string act, out bool handled)
        {
            handled = true;
            if (sel == null && act != "sib" && act != "child") { handled = false; return null; }
            switch (act)
            {
                case "sib": return sel == null ? ed.AddRoot("").Id : ed.AddSibling(sel, "").Id;
                case "child": return sel == null ? ed.AddRoot("").Id : ed.AddChild(sel, "", 0).Id;
                case "in": ed.Indent(sel); return sel;
                case "out": ed.Outdent(sel); return sel;
                case "up": ed.MoveUp(sel); return sel;
                case "down": ed.MoveDown(sel); return sel;
                case "dup":
                {
                    var loc = NodeTree.Locate(ed.Roots, sel);
                    if (loc == null) return sel;
                    var copy = NodeTree.Clone(new List<Node> { loc.Node }, newIds: true)[0];
                    loc.Siblings.Insert(loc.Index + 1, copy);
                    ed.Relabel();
                    return copy.Id;
                }
                case "del":
                {
                    var loc = NodeTree.Locate(ed.Roots, sel);
                    if (loc == null) return null;
                    var next = loc.Index > 0 ? loc.Siblings[loc.Index - 1].Id : loc.Siblings.Count > loc.Index + 1 ? loc.Siblings[loc.Index + 1].Id : loc.Parent?.Id;
                    ed.Remove(sel);
                    return next;
                }
                case "prev":
                case "next":
                {
                    var flat = new List<string>();
                    foreach (var (n, _) in Node.Walk(ed.Roots)) flat.Add(n.Id);
                    var i = flat.IndexOf(sel);
                    var j = i + (act == "prev" ? -1 : 1);
                    if (j < 0 || j >= flat.Count) return sel;
                    return flat[j];
                }
                default: handled = false; return null;
            }
        }
    }

    /// <summary>The touch equivalent of the keyboard: Sibling, Child, Outdent, Indent, Up, Down, Duplicate, Delete, Paste.</summary>
    public static class AccessoryBar
    {
        public static VisualElement Build(AppController app, string kind, bool canChild, bool canIndent, bool showPaste)
        {
            var bar = new VisualElement().Cls("acc");
            var label = kind == "node" ? "Bullet" : kind == "video" ? "Video" : kind == "chapter" ? "Chapter" : kind == "section" ? "Section" : kind == "photo" ? "Photo" : "";
            bar.Add(U.Eyebrow(label).W(64).Mr(6).Font(11));
            bool kb = Shortcuts.HardwareKeyboardPresent;
            var m = Shortcuts.Meta;
            void B(string act, GlyphKind g, string text, string key, bool enabled = true)
            {
                var b = U.Tap(() => { if (app.AccessoryAction != null && !app.AccessoryAction(act)) app.Toast("Nothing to " + text.ToLowerInvariant() + " here"); }, "accb", new Glyph(g, 22), U.Text(text));
                if (kb && key != null) b.Add(U.Kbd(key));
                b.SetEnabled(enabled);
                bar.Add(b);
            }
            B("sib", GlyphKind.Plus, "Sibling", "Enter");
            B("child", GlyphKind.ChildArrow, "Child", "⇧Enter", canChild);
            B("out", GlyphKind.Outdent, "Outdent", "⇧Tab", canIndent);
            B("in", GlyphKind.Indent, "Indent", "Tab", canIndent);
            B("up", GlyphKind.ArrowUp, "Up", "⌥↑");
            B("down", GlyphKind.ArrowDown, "Down", "⌥↓");
            B("dup", GlyphKind.Copy, "Duplicate", m + "D");
            B("del", GlyphKind.Cross, "Delete", m + "⌫");
            bar.Add(U.Grow());
            if (showPaste) bar.Add(U.BtnKbd("Paste…", m + "⇧V", () => app.AccessoryAction?.Invoke("paste"), "sm"));
            return bar;
        }
    }
}

using System.Collections.Generic;
using MediaTrip.Model;
using UnityEngine.UIElements;

namespace MediaTrip.UI.Field
{
    /// <summary>Read-only nested bullets, arbitrary depth, sized for reading on the iPad.</summary>
    public static class NotesView
    {
        public static VisualElement Build(List<Node> nodes, float indent = 24, float fontSize = 16)
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
                var lb = U.Text(string.IsNullOrEmpty(n.Label) ? "•" : U.Esc(n.Label), "lb").Font(fontSize);
                var tx = U.Text(U.Esc(n.Text), "txt").Font(fontSize);
                tx.style.flexGrow = 1;
                row.Add(lb);
                row.Add(tx);
                host.Add(row);
                Add(host, n.Children, depth + 1, indent, fontSize);
            }
        }
    }
}

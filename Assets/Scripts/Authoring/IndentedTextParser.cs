using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using MediaTrip.Model;

namespace MediaTrip.Authoring
{
    public class PasteOptions
    {
        /// <summary>How many spaces one tab counts for when tabs and spaces are mixed.</summary>
        public int TabWidth = 4;
        /// <summary>Strip leading bullet markers ("- ", "• ", "a.", "1)", "iv.") from the text.</summary>
        public bool StripMarkers = true;
        /// <summary>Keep a stripped alphanumeric marker as the node's label. Plain bullets never become labels.</summary>
        public bool KeepMarkersAsLabels = true;
        /// <summary>If set, relabel the whole tree with this scheme after parsing.</summary>
        public LabelScheme Relabel = null;
        public static readonly PasteOptions Default = new PasteOptions();
    }

    /// <summary>
    /// Turns pasted plain text (one item per line, nesting by leading tabs or spaces) into a
    /// node tree. Indent levels are inferred from the distinct widths that actually appear, so
    /// two-space, four-space and tab indentation all work, including mixed. A line that dedents
    /// to a width between two known levels snaps to the nearest shallower level.
    /// </summary>
    public static class IndentedTextParser
    {
        // "- ", "* ", "• ", "· ", "o ", "▪ " plain bullets; "a." "A)" "1." "(3)" "iv." "IV)" markers.
        private static readonly Regex MarkerRx = new Regex(
            @"^(?:(?<bullet>[-*•·▪‣o])|\(?(?<label>\d{1,3}|[a-zA-Z]|[ivxlcIVXLC]{1,6})[.)])(?=\s|$)\s*",
            RegexOptions.Compiled);

        public static List<Node> Parse(string text, PasteOptions options = null)
        {
            options = options ?? PasteOptions.Default;
            var roots = new List<Node>();
            if (string.IsNullOrEmpty(text)) return roots;

            var levels = new List<int>();          // indent width per depth
            var parents = new List<Node>();        // last node per depth

            foreach (var raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;

                int width = 0, i = 0;
                for (; i < raw.Length; i++)
                {
                    if (raw[i] == '\t') width += options.TabWidth;
                    else if (raw[i] == ' ') width += 1;
                    else if (raw[i] == ' ') width += 1;
                    else break;
                }
                var body = raw.Substring(i).TrimEnd();

                string label = null;
                if (options.StripMarkers)
                {
                    var m = MarkerRx.Match(body);
                    if (m.Success)
                    {
                        if (options.KeepMarkersAsLabels && m.Groups["label"].Success) label = m.Groups["label"].Value;
                        body = body.Substring(m.Length);
                    }
                }

                int depth = DepthFor(levels, width);
                var node = new Node { Id = Ids.New("n"), Label = label, Text = body, Children = new List<Node>() };

                if (depth == 0) roots.Add(node);
                else
                {
                    var parent = parents[depth - 1];
                    if (parent.Children == null) parent.Children = new List<Node>();
                    parent.Children.Add(node);
                }

                if (parents.Count > depth) parents.RemoveRange(depth, parents.Count - depth);
                parents.Add(node);
            }

            if (options.Relabel != null) NodeTree.Relabel(roots, options.Relabel);
            return roots;
        }

        /// <summary>Map an indent width onto a depth, growing or trimming the level stack.</summary>
        private static int DepthFor(List<int> levels, int width)
        {
            if (levels.Count == 0)
            {
                levels.Add(width); // whatever the first line uses is depth 0
                return 0;
            }
            int top = levels[levels.Count - 1];
            if (width > top)
            {
                levels.Add(width);
                return levels.Count - 1;
            }
            // Dedent (or same): pop deeper levels, snap to the nearest level not deeper than width.
            while (levels.Count > 1 && levels[levels.Count - 1] > width) levels.RemoveAt(levels.Count - 1);
            if (width > levels[levels.Count - 1] && levels.Count > 0)
            {
                // Between two known levels: treat as a new deeper level under the shallower one.
                levels.Add(width);
            }
            return levels.Count - 1;
        }
    }
}

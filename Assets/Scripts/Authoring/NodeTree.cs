using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MediaTrip.Model;
using MediaTrip.Persistence;

namespace MediaTrip.Authoring
{
    public enum LabelStyle { None, Numeric, LowerAlpha, UpperAlpha, LowerRoman, UpperRoman }

    /// <summary>Which label style each depth uses. Cycles when the tree is deeper than the list.</summary>
    public class LabelScheme
    {
        public List<LabelStyle> Styles { get; }

        public LabelScheme(params LabelStyle[] styles)
        {
            Styles = styles == null || styles.Length == 0 ? new List<LabelStyle> { LabelStyle.None } : styles.ToList();
        }

        /// <summary>Shot-list video notes as printed: 1 / a / 1 / a ...</summary>
        public static LabelScheme ShotListNotes => new LabelScheme(LabelStyle.Numeric, LabelStyle.LowerAlpha);
        /// <summary>Outline bullets as printed: a / 1 / i, then cycling.</summary>
        public static LabelScheme Outline => new LabelScheme(LabelStyle.LowerAlpha, LabelStyle.Numeric, LabelStyle.LowerRoman);
        public static LabelScheme Unlabeled => new LabelScheme(LabelStyle.None);

        public LabelStyle StyleAt(int depth) => Styles[depth % Styles.Count];

        /// <summary>
        /// Read the style used at each depth from the first labelled node found there, falling
        /// back to <paramref name="fallback"/> for depths with no usable label. Lets a relabel
        /// after an edit keep whatever convention the document already used.
        /// </summary>
        public static LabelScheme Infer(IEnumerable<Node> roots, LabelScheme fallback)
        {
            var byDepth = new Dictionary<int, LabelStyle>();
            int maxDepth = -1;
            foreach (var (node, depth) in Node.Walk(roots))
            {
                if (depth > maxDepth) maxDepth = depth;
                if (byDepth.ContainsKey(depth)) continue;
                var style = Detect(node.Label, depth, byDepth);
                if (style != LabelStyle.None) byDepth[depth] = style;
            }
            if (maxDepth < 0) return fallback;
            // Cover the observed depths, rounded up to a whole number of fallback cycles so that
            // depths deeper than anything seen so far keep following the fallback's pattern.
            int cycle = Math.Max(1, fallback.Styles.Count);
            int length = ((maxDepth + 1 + cycle - 1) / cycle) * cycle;
            var styles = new LabelStyle[length];
            for (int d = 0; d < length; d++)
                styles[d] = byDepth.TryGetValue(d, out var s) ? s : fallback.StyleAt(d);
            return new LabelScheme(styles);
        }

        /// <summary>Best guess for a label's style. "i" is ambiguous (alpha or roman); it is read as roman only when the parent depth is not already roman-free alpha.</summary>
        public static LabelStyle Detect(string label, int depth = 0, IReadOnlyDictionary<int, LabelStyle> context = null)
        {
            if (string.IsNullOrWhiteSpace(label)) return LabelStyle.None;
            var t = label.Trim().TrimEnd('.', ')', ':');
            if (t.Length == 0) return LabelStyle.None;
            if (t.All(char.IsDigit)) return LabelStyle.Numeric;
            bool lower = t.All(char.IsLower), upper = t.All(char.IsUpper);
            if (!lower && !upper) return LabelStyle.None;
            bool romanChars = t.All(c => "ivxlcdmIVXLCDM".IndexOf(c) >= 0);
            if (romanChars && (t.Length > 1 || t.Equals("i", StringComparison.OrdinalIgnoreCase)))
            {
                // Single "i" at a depth whose previous depth is alpha reads as roman (a / 1 / i).
                return lower ? LabelStyle.LowerRoman : LabelStyle.UpperRoman;
            }
            if (t.Length == 1) return lower ? LabelStyle.LowerAlpha : LabelStyle.UpperAlpha;
            return LabelStyle.None;
        }

        public static string Format(LabelStyle style, int index1)
        {
            if (index1 < 1) index1 = 1;
            switch (style)
            {
                case LabelStyle.Numeric: return index1.ToString();
                case LabelStyle.LowerAlpha: return Alpha(index1, 'a');
                case LabelStyle.UpperAlpha: return Alpha(index1, 'A');
                case LabelStyle.LowerRoman: return Roman(index1).ToLowerInvariant();
                case LabelStyle.UpperRoman: return Roman(index1);
                default: return null;
            }
        }

        /// <summary>a..z, then aa, ab, ... (spreadsheet style).</summary>
        private static string Alpha(int n, char baseChar)
        {
            var sb = new StringBuilder();
            while (n > 0)
            {
                n--;
                sb.Insert(0, (char)(baseChar + n % 26));
                n /= 26;
            }
            return sb.ToString();
        }

        public static string Roman(int n)
        {
            if (n <= 0 || n >= 4000) return n.ToString();
            var values = new[] { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
            var symbols = new[] { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };
            var sb = new StringBuilder();
            for (int i = 0; i < values.Length; i++)
                while (n >= values[i]) { sb.Append(symbols[i]); n -= values[i]; }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Pure structural operations on a recursive node list (video notes, outline bullets).
    /// No dirty tracking here; <see cref="NodeListEditor"/> wraps these for a session.
    /// Every operation is by node ID so the UI never has to hold indices.
    /// </summary>
    public static class NodeTree
    {
        public class Location
        {
            public Node Node;
            public Node Parent;          // null at root level
            public List<Node> Siblings;  // the list that contains Node
            public int Index;
            public int Depth;
        }

        public static Node NewNode(string text, string label = null) =>
            new Node { Id = Ids.New("n"), Text = text ?? "", Label = label, Children = new List<Node>() };

        public static Location Locate(List<Node> roots, string id)
        {
            if (roots == null || id == null) return null;
            return LocateIn(roots, null, id, 0);
        }

        private static Location LocateIn(List<Node> siblings, Node parent, string id, int depth)
        {
            for (int i = 0; i < siblings.Count; i++)
            {
                var n = siblings[i];
                if (n.Id == id) return new Location { Node = n, Parent = parent, Siblings = siblings, Index = i, Depth = depth };
                if (n.Children == null) n.Children = new List<Node>();
                var found = LocateIn(n.Children, n, id, depth + 1);
                if (found != null) return found;
            }
            return null;
        }

        public static Node AddRoot(List<Node> roots, string text, int? index = null)
        {
            var node = NewNode(text);
            roots.Insert(Clamp(index ?? roots.Count, roots.Count), node);
            return node;
        }

        public static Node AddSibling(List<Node> roots, string id, string text, bool after = true)
        {
            var loc = Require(roots, id);
            var node = NewNode(text);
            loc.Siblings.Insert(after ? loc.Index + 1 : loc.Index, node);
            return node;
        }

        public static Node AddChild(List<Node> roots, string id, string text, int? index = null)
        {
            var loc = Require(roots, id);
            if (loc.Node.Children == null) loc.Node.Children = new List<Node>();
            var node = NewNode(text);
            loc.Node.Children.Insert(Clamp(index ?? loc.Node.Children.Count, loc.Node.Children.Count), node);
            return node;
        }

        /// <summary>Make the node the last child of its previous sibling. False if it has none.</summary>
        public static bool Indent(List<Node> roots, string id)
        {
            var loc = Require(roots, id);
            if (loc.Index == 0) return false;
            var newParent = loc.Siblings[loc.Index - 1];
            loc.Siblings.RemoveAt(loc.Index);
            if (newParent.Children == null) newParent.Children = new List<Node>();
            newParent.Children.Add(loc.Node);
            return true;
        }

        /// <summary>
        /// Move the node up one level to directly after its parent. The siblings that followed
        /// it become its children (outliner convention: what was visually below it stays below
        /// it). False if the node is already at root level.
        /// </summary>
        public static bool Outdent(List<Node> roots, string id)
        {
            var loc = Require(roots, id);
            if (loc.Parent == null) return false;
            var parentLoc = Require(roots, loc.Parent.Id);
            var following = loc.Siblings.Skip(loc.Index + 1).ToList();
            loc.Siblings.RemoveRange(loc.Index, loc.Siblings.Count - loc.Index);
            if (loc.Node.Children == null) loc.Node.Children = new List<Node>();
            loc.Node.Children.AddRange(following);
            parentLoc.Siblings.Insert(parentLoc.Index + 1, loc.Node);
            return true;
        }

        public static bool MoveUp(List<Node> roots, string id)
        {
            var loc = Require(roots, id);
            if (loc.Index == 0) return false;
            loc.Siblings.RemoveAt(loc.Index);
            loc.Siblings.Insert(loc.Index - 1, loc.Node);
            return true;
        }

        public static bool MoveDown(List<Node> roots, string id)
        {
            var loc = Require(roots, id);
            if (loc.Index >= loc.Siblings.Count - 1) return false;
            loc.Siblings.RemoveAt(loc.Index);
            loc.Siblings.Insert(loc.Index + 1, loc.Node);
            return true;
        }

        /// <summary>Move a node (with its subtree) under a new parent (null = root level) at an index. Refuses to move into its own subtree.</summary>
        public static bool Move(List<Node> roots, string id, string newParentId, int? index = null)
        {
            var loc = Require(roots, id);
            List<Node> target;
            if (newParentId == null) target = roots;
            else
            {
                if (newParentId == id || loc.Node.Walk().Any(n => n.Id == newParentId)) return false;
                var p = Require(roots, newParentId);
                if (p.Node.Children == null) p.Node.Children = new List<Node>();
                target = p.Node.Children;
            }
            loc.Siblings.RemoveAt(loc.Index);
            target.Insert(Clamp(index ?? target.Count, target.Count), loc.Node);
            return true;
        }

        /// <summary>Remove the node and its whole subtree. Returns it, or null if not found.</summary>
        public static Node Remove(List<Node> roots, string id)
        {
            var loc = Locate(roots, id);
            if (loc == null) return null;
            loc.Siblings.RemoveAt(loc.Index);
            return loc.Node;
        }

        /// <summary>Rewrite every label from sibling position and depth using the scheme.</summary>
        public static void Relabel(List<Node> roots, LabelScheme scheme)
        {
            RelabelLevel(roots, scheme, 0);
        }

        private static void RelabelLevel(List<Node> siblings, LabelScheme scheme, int depth)
        {
            if (siblings == null) return;
            var style = scheme.StyleAt(depth);
            for (int i = 0; i < siblings.Count; i++)
            {
                siblings[i].Label = LabelScheme.Format(style, i + 1);
                RelabelLevel(siblings[i].Children, scheme, depth + 1);
            }
        }

        /// <summary>Deep copy. With <paramref name="newIds"/> every node gets a fresh GUID (for duplicating a video).</summary>
        public static List<Node> Clone(IEnumerable<Node> roots, bool newIds)
        {
            var copy = TripJson.Clone(roots?.ToList() ?? new List<Node>());
            if (newIds) foreach (var (n, _) in Node.Walk(copy)) n.Id = Ids.New("n");
            return copy;
        }

        public static int Count(IEnumerable<Node> roots) => Node.Walk(roots).Count();

        private static Location Require(List<Node> roots, string id) =>
            Locate(roots, id) ?? throw new KeyNotFoundException("No node with id " + id);

        private static int Clamp(int index, int count) => index < 0 ? 0 : (index > count ? count : index);
    }

    /// <summary>
    /// <see cref="NodeTree"/> bound to one list (a video's notes or a section's bullets):
    /// relabels after each structural change and notifies the owner so it can mark dirty.
    /// </summary>
    public sealed class NodeListEditor
    {
        private readonly Func<List<Node>> _roots;
        private readonly Action<List<Node>> _replace;
        private readonly Action _changed;

        public LabelScheme Scheme { get; set; }
        public List<Node> Roots => _roots();

        public NodeListEditor(Func<List<Node>> roots, Action<List<Node>> replace, LabelScheme scheme, Action changed)
        {
            _roots = roots;
            _replace = replace;
            Scheme = scheme;
            _changed = changed;
        }

        private T Done<T>(T result)
        {
            if (Scheme != null) NodeTree.Relabel(Roots, Scheme);
            _changed?.Invoke();
            return result;
        }

        public Node AddRoot(string text, int? index = null) => Done(NodeTree.AddRoot(Roots, text, index));
        public Node AddSibling(string id, string text, bool after = true) => Done(NodeTree.AddSibling(Roots, id, text, after));
        public Node AddChild(string id, string text, int? index = null) => Done(NodeTree.AddChild(Roots, id, text, index));
        public bool Indent(string id) => Done(NodeTree.Indent(Roots, id));
        public bool Outdent(string id) => Done(NodeTree.Outdent(Roots, id));
        public bool MoveUp(string id) => Done(NodeTree.MoveUp(Roots, id));
        public bool MoveDown(string id) => Done(NodeTree.MoveDown(Roots, id));
        public bool Move(string id, string newParentId, int? index = null) => Done(NodeTree.Move(Roots, id, newParentId, index));
        public Node Remove(string id) => Done(NodeTree.Remove(Roots, id));

        public void SetText(string id, string text)
        {
            var loc = NodeTree.Locate(Roots, id) ?? throw new KeyNotFoundException("No node with id " + id);
            loc.Node.Text = text ?? "";
            _changed?.Invoke();
        }

        /// <summary>Set one label by hand (e.g. a printed marker that does not follow the scheme). Not touched again until the next structural change.</summary>
        public void SetLabel(string id, string label)
        {
            var loc = NodeTree.Locate(Roots, id) ?? throw new KeyNotFoundException("No node with id " + id);
            loc.Node.Label = label;
            _changed?.Invoke();
        }

        public void Relabel(LabelScheme scheme = null)
        {
            if (scheme != null) Scheme = scheme;
            Done(0);
        }

        /// <summary>Replace the whole list (e.g. with a pasted tree).</summary>
        public void Replace(List<Node> nodes)
        {
            _replace(nodes ?? new List<Node>());
            Done(0);
        }

        /// <summary>Append a pasted tree at root level (or under a node).</summary>
        public void Append(IEnumerable<Node> nodes, string parentId = null)
        {
            var list = nodes?.ToList() ?? new List<Node>();
            if (parentId == null) Roots.AddRange(list);
            else
            {
                var loc = NodeTree.Locate(Roots, parentId) ?? throw new KeyNotFoundException("No node with id " + parentId);
                if (loc.Node.Children == null) loc.Node.Children = new List<Node>();
                loc.Node.Children.AddRange(list);
            }
            Done(0);
        }
    }
}

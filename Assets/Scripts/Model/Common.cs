using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MediaTrip.Model
{
    /// <summary>
    /// Every entity gets a stable string ID. New entities use a GUID; sample/imported data may
    /// use short hand-written IDs ("v-001"). Never compare IDs by shape, only by equality.
    /// </summary>
    public static class Ids
    {
        public static string New() => Guid.NewGuid().ToString("D");

        /// <summary>GUID with a short readable prefix, e.g. "cap-3f2a...". Purely cosmetic.</summary>
        public static string New(string prefix) =>
            string.IsNullOrEmpty(prefix) ? New() : prefix + "-" + Guid.NewGuid().ToString("N");
    }

    /// <summary>Schema version written into every document and checked by the migrator.</summary>
    public static class Schema
    {
        public const int CurrentVersion = 1;
    }

    /// <summary>
    /// Base for the four top-level documents. Unknown JSON properties are kept in
    /// <see cref="Extra"/> so a file authored by a newer app (or by hand) survives a load/save.
    /// </summary>
    public abstract class DocumentBase
    {
        public int SchemaVersion { get; set; } = Schema.CurrentVersion;
        public string TripId { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; }
    }

    /// <summary>
    /// Recursive bullet used for shot-list video notes and outline section bullets.
    /// Depth is unbounded. Never flatten this to fixed levels.
    /// </summary>
    public class Node
    {
        public string Id { get; set; }
        /// <summary>Printed marker ("a", "b", "1", "1.1") if there is one.</summary>
        public string Label { get; set; }
        public string Text { get; set; }
        public List<Node> Children { get; set; } = new List<Node>();

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; }

        /// <summary>Depth-first enumeration of this node and every descendant.</summary>
        public IEnumerable<Node> Walk()
        {
            yield return this;
            if (Children == null) yield break;
            foreach (var child in Children)
                foreach (var n in child.Walk())
                    yield return n;
        }

        /// <summary>Depth-first enumeration of a list of roots, with depth (0 = root).</summary>
        public static IEnumerable<(Node node, int depth)> Walk(IEnumerable<Node> roots, int depth = 0)
        {
            if (roots == null) yield break;
            foreach (var root in roots)
            {
                yield return (root, depth);
                foreach (var pair in Walk(root.Children, depth + 1))
                    yield return pair;
            }
        }

        public static Node Find(IEnumerable<Node> roots, string id)
        {
            foreach (var (node, _) in Walk(roots))
                if (node.Id == id) return node;
            return null;
        }

        public static int MaxDepth(IEnumerable<Node> roots)
        {
            int max = 0;
            foreach (var (_, depth) in Walk(roots))
                if (depth + 1 > max) max = depth + 1;
            return max;
        }
    }
}

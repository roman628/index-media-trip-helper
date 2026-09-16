using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MediaTrip.Model
{
    /// <summary>outlines/book-N.json: the outline mockup for one book. Editable.</summary>
    public class OutlineDocument : DocumentBase
    {
        public string BookId { get; set; }
        public string BookTitle { get; set; }
        public string ProgramName { get; set; }
        public List<OutlineChapter> Chapters { get; set; } = new List<OutlineChapter>();
    }

    public class OutlineChapter
    {
        public string Id { get; set; }
        public int Number { get; set; }
        public string Name { get; set; }
        public List<OutlineSection> Sections { get; set; } = new List<OutlineSection>();
        /// <summary>Per-chapter notes area.</summary>
        public string Notes { get; set; }
        [JsonExtensionData] public IDictionary<string, JToken> Extra { get; set; }
    }

    public class OutlineSection
    {
        public string Id { get; set; }
        /// <summary>"1.1", "1.2". A string because it is printed, never computed with.</summary>
        public string Number { get; set; }
        public string Name { get; set; }
        /// <summary>Bullets a/b/c, sub-points 1/2/3, arbitrarily deep.</summary>
        public List<Node> Nodes { get; set; } = new List<Node>();
        [JsonExtensionData] public IDictionary<string, JToken> Extra { get; set; }
    }
}

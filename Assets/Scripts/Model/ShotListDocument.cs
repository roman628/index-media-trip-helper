using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MediaTrip.Model
{
    public enum HeroType { None, BookCover, ChapterHero }

    /// <summary>
    /// shotlist.json: the immutable plan. Nothing in here is edited to record what happened;
    /// changes go into <see cref="Amendment"/>s and captures into <see cref="Capture"/>s.
    /// </summary>
    public class ShotListDocument : DocumentBase
    {
        public string ImportedAt { get; set; }
        /// <summary>"typed in app" | "converted from Word" | ...</summary>
        public string Source { get; set; }
        public List<Chapter> Chapters { get; set; } = new List<Chapter>();
        public List<Video> Videos { get; set; } = new List<Video>();
        /// <summary>The master photo list, grouped by book. Video photoRefs point in here.</summary>
        public List<Photo> Photos { get; set; } = new List<Photo>();
    }

    public class Chapter
    {
        public string Id { get; set; }
        public string BookId { get; set; }
        public int Number { get; set; }
        public string Name { get; set; }
        [JsonExtensionData] public IDictionary<string, JToken> Extra { get; set; }
    }

    public class Video
    {
        public string Id { get; set; }
        /// <summary>Continuous across books and chapters; belongs to the plan as a whole.</summary>
        public int Number { get; set; }
        public string BookId { get; set; }
        public string ChapterId { get; set; }
        public string Title { get; set; }
        public List<string> SmeIds { get; set; } = new List<string>();
        public string SmeText { get; set; }
        public string SceneDescription { get; set; }
        /// <summary>Nested bullets, arbitrary depth.</summary>
        public List<Node> Notes { get; set; } = new List<Node>();
        /// <summary>Photos shootable with this video's setup; IDs into the master photo list.</summary>
        public List<string> PhotoRefs { get; set; } = new List<string>();
        [JsonExtensionData] public IDictionary<string, JToken> Extra { get; set; }
    }

    public class Photo
    {
        public string Id { get; set; }
        /// <summary>PRIMARY book: where the photo sits in the master list.</summary>
        public string BookId { get; set; }
        /// <summary>Set for chapter hero shots; null otherwise.</summary>
        public string ChapterId { get; set; }
        /// <summary>Extra books this photo also belongs to. Rare, but allowed.</summary>
        public List<string> AlsoBookIds { get; set; } = new List<string>();
        /// <summary>Extra chapters this photo also belongs to. Rare, but allowed.</summary>
        public List<string> AlsoChapterIds { get; set; } = new List<string>();
        public HeroType HeroType { get; set; } = HeroType.None;
        /// <summary>Order within its primary book; heroes come first (cover, then ch.1..N).</summary>
        public int Order { get; set; }
        public string Description { get; set; }
        [JsonExtensionData] public IDictionary<string, JToken> Extra { get; set; }

        [JsonIgnore] public bool IsHero => HeroType != HeroType.None;
        [JsonIgnore] public bool IsShared => (AlsoBookIds?.Count ?? 0) > 0 || (AlsoChapterIds?.Count ?? 0) > 0;

        public bool BelongsToBook(string bookId) =>
            bookId != null && (BookId == bookId || (AlsoBookIds != null && AlsoBookIds.Contains(bookId)));

        public bool BelongsToChapter(string chapterId) =>
            chapterId != null && (ChapterId == chapterId || (AlsoChapterIds != null && AlsoChapterIds.Contains(chapterId)));
    }
}

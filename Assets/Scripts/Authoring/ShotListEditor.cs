using System;
using System.Collections.Generic;
using System.Linq;
using MediaTrip.Model;
using MediaTrip.Persistence;
using MediaTrip.Session;

namespace MediaTrip.Authoring
{
    /// <summary>
    /// Structural editing of the plan documents (books in trip.json; chapters, videos and the
    /// master photo list in shotlist.json). This is authoring, not capture: it edits the plan
    /// itself, which is fine before a trip. Once a capture or amendment references an item,
    /// anything that would orphan that reference (delete, move, retitle) is refused with a
    /// <see cref="PlanEditBlockedException"/> pointing at the amendment to use instead.
    /// Working content (notes, scene description, SMEs, photo refs) stays editable.
    ///
    /// Numbering: books, chapters-within-book and photos-within-book are 1..N; video numbers
    /// run continuously across books and chapters in printed order. Every operation renumbers.
    /// List order is the printed order. Captures keep the plannedNumber they were shot against.
    /// </summary>
    public sealed class ShotListEditor
    {
        private readonly TripSession _s;
        private TripData D => _s.Data;

        internal ShotListEditor(TripSession session) { _s = session; }

        // ------------------------------------------------------------------
        // Reads
        // ------------------------------------------------------------------

        public List<Chapter> ChaptersOf(string bookId) => D.ShotList.Chapters.Where(c => c.BookId == bookId).ToList();
        public List<Video> VideosOfChapter(string chapterId) => D.ShotList.Videos.Where(v => v.ChapterId == chapterId).ToList();
        public List<Video> VideosOfBook(string bookId) => D.ShotList.Videos.Where(v => v.BookId == bookId).ToList();
        public List<Photo> PhotosOfBook(string bookId) => D.ShotList.Photos.Where(p => p.BookId == bookId).ToList();
        public List<PlanReference> ReferencesTo(string id) => ReferenceFinder.Find(D, id);
        public bool IsReferenced(string id) => ReferenceFinder.Find(D, id).Count > 0;

        // ------------------------------------------------------------------
        // Books (trip.json)
        // ------------------------------------------------------------------

        public Book AddBook(string name, int? index = null, BookTeam team = null)
        {
            var book = new Book { Id = Ids.New("b"), Name = name, Team = team ?? new BookTeam() };
            Insert(D.Trip.Books, b => true, book, index);
            Finish(booksChanged: true);
            return book;
        }

        /// <summary>Edit name/team. Id and number are restored after the edit.</summary>
        public void UpdateBook(string id, Action<Book> edit)
        {
            var b = RequireBook(id);
            var keepId = b.Id; var keepNumber = b.Number;
            edit(b);
            b.Id = keepId; b.Number = keepNumber;
            Finish(booksChanged: true);
        }

        /// <summary>
        /// Remove a book. Blocked if captures, amendments or assignments reference it. Its
        /// chapters, videos, photos and outline are removed only with <paramref name="cascade"/>;
        /// each of those must itself be unreferenced.
        /// </summary>
        public void RemoveBook(string id, bool cascade = false)
        {
            var b = RequireBook(id);
            ReferenceFinder.ThrowIfReferenced(D, id, "Removing book", "Books with recorded captures cannot be removed.");
            var chapters = ChaptersOf(id);
            var photos = PhotosOfBook(id);
            var hasOutline = D.Outlines.ContainsKey(id);
            if (!cascade && (chapters.Count > 0 || photos.Count > 0 || hasOutline))
                throw new InvalidOperationException($"Book '{b.Name}' is not empty ({chapters.Count} chapters, {photos.Count} photos{(hasOutline ? ", outline" : "")}). Pass cascade: true to remove its contents.");

            var blocked = new List<PlanReference>();
            foreach (var ch in chapters) blocked.AddRange(ReferenceFinder.Find(D, ch.Id));
            foreach (var v in VideosOfBook(id)) blocked.AddRange(ReferenceFinder.Find(D, v.Id));
            foreach (var p in photos) blocked.AddRange(ReferenceFinder.Find(D, p.Id));
            if (blocked.Count > 0) throw new PlanEditBlockedException(id, blocked, "Use drop amendments for items already shot.", "Removing book contents");

            foreach (var v in VideosOfBook(id).ToList()) D.ShotList.Videos.Remove(v);
            foreach (var p in photos) RemovePhotoInternal(p);
            foreach (var ch in chapters) { D.ShotList.Chapters.Remove(ch); StripAssociations(null, ch.Id); }
            StripAssociations(id, null);
            D.Trip.Books.Remove(b);
            if (hasOutline) _s.RemoveOutline(id);
            Finish(booksChanged: true);
        }

        public void ReorderBook(string id, int newIndex)
        {
            var b = RequireBook(id);
            Reorder(D.Trip.Books, x => true, b, newIndex);
            Finish(booksChanged: true);
        }

        // ------------------------------------------------------------------
        // Chapters
        // ------------------------------------------------------------------

        public Chapter AddChapter(string bookId, string name, int? index = null)
        {
            RequireBook(bookId);
            var ch = new Chapter { Id = Ids.New("c"), BookId = bookId, Name = name };
            Insert(D.ShotList.Chapters, c => c.BookId == bookId, ch, index);
            Finish();
            return ch;
        }

        /// <summary>Edit the name (and any extra data). Id, bookId and number are restored after the edit.</summary>
        public void UpdateChapter(string id, Action<Chapter> edit)
        {
            var c = RequireChapter(id);
            var keepId = c.Id; var keepBook = c.BookId; var keepNumber = c.Number;
            edit(c);
            c.Id = keepId; c.BookId = keepBook; c.Number = keepNumber;
            Finish();
        }

        /// <summary>Remove a chapter. Blocked if referenced. Contents need <paramref name="cascade"/> and must be unreferenced.</summary>
        public void RemoveChapter(string id, bool cascade = false)
        {
            var c = RequireChapter(id);
            ReferenceFinder.ThrowIfReferenced(D, id, "Removing chapter", "Chapters with recorded captures cannot be removed.");
            var videos = VideosOfChapter(id);
            var photos = D.ShotList.Photos.Where(p => p.ChapterId == id).ToList();
            if (!cascade && (videos.Count > 0 || photos.Count > 0))
                throw new InvalidOperationException($"Chapter '{c.Name}' is not empty ({videos.Count} videos, {photos.Count} photos). Pass cascade: true to remove its contents.");

            var blocked = new List<PlanReference>();
            foreach (var v in videos) blocked.AddRange(ReferenceFinder.Find(D, v.Id));
            foreach (var p in photos) blocked.AddRange(ReferenceFinder.Find(D, p.Id));
            if (blocked.Count > 0) throw new PlanEditBlockedException(id, blocked, "Use drop amendments for items already shot.", "Removing chapter contents");

            foreach (var v in videos) D.ShotList.Videos.Remove(v);
            foreach (var p in photos) RemovePhotoInternal(p);
            D.ShotList.Chapters.Remove(c);
            StripAssociations(null, c.Id);
            Finish();
        }

        public void ReorderChapter(string id, int newIndex)
        {
            var c = RequireChapter(id);
            Reorder(D.ShotList.Chapters, x => x.BookId == c.BookId, c, newIndex);
            Finish();
        }

        // ------------------------------------------------------------------
        // Videos
        // ------------------------------------------------------------------

        /// <summary>Add a planned video to a chapter at an index among that chapter's videos (default: end). Numbers shift accordingly.</summary>
        public Video AddVideo(string chapterId, string title, int? index = null, Action<Video> init = null)
        {
            var ch = RequireChapter(chapterId);
            var v = new Video { Id = Ids.New("v"), Title = title, BookId = ch.BookId, ChapterId = chapterId };
            init?.Invoke(v);
            v.Id = v.Id ?? Ids.New("v"); v.BookId = ch.BookId; v.ChapterId = chapterId;
            Insert(D.ShotList.Videos, x => x.ChapterId == chapterId, v, index);
            Finish();
            return v;
        }

        /// <summary>
        /// Edit working content (SMEs, scene description, notes, photo refs, title). Id, number,
        /// book and chapter are restored after the edit. A title change on a referenced video is
        /// refused: use <see cref="TripSession.Rename"/> (a rename amendment) instead.
        /// </summary>
        public void UpdateVideo(string id, Action<Video> edit)
        {
            var v = RequireVideo(id);
            var keepId = v.Id; var keepNumber = v.Number; var keepBook = v.BookId; var keepChapter = v.ChapterId; var keepTitle = v.Title;
            edit(v);
            v.Id = keepId; v.Number = keepNumber; v.BookId = keepBook; v.ChapterId = keepChapter;
            if (v.Title != keepTitle)
            {
                var refs = ReferenceFinder.Find(D, id);
                if (refs.Count > 0)
                {
                    v.Title = keepTitle;
                    throw new PlanEditBlockedException(id, refs, "Use a rename amendment (TripSession.Rename) so the change is recorded.", "Retitling a video");
                }
            }
            Finish();
        }

        public void SetVideoTitle(string id, string title) => UpdateVideo(id, v => v.Title = title);

        /// <summary>Remove a planned video. Blocked if any capture or amendment references it (use a drop amendment).</summary>
        public void RemoveVideo(string id)
        {
            var v = RequireVideo(id);
            ReferenceFinder.ThrowIfReferenced(D, id, "Removing video", "Use a drop amendment (TripSession.Drop) for a video that was planned on paper.");
            D.ShotList.Videos.Remove(v);
            Finish();
        }

        /// <summary>Move a video to another chapter (any book). Blocked if referenced (use a move amendment).</summary>
        public void MoveVideo(string id, string targetChapterId, int? index = null)
        {
            var v = RequireVideo(id);
            var ch = RequireChapter(targetChapterId);
            ReferenceFinder.ThrowIfReferenced(D, id, "Moving video", "Use a move amendment (TripSession.Move) so the change is recorded.");
            D.ShotList.Videos.Remove(v);
            v.BookId = ch.BookId;
            v.ChapterId = ch.Id;
            Insert(D.ShotList.Videos, x => x.ChapterId == ch.Id, v, index);
            Finish();
        }

        /// <summary>Reorder a video among its chapter's videos. Allowed even when referenced: only numbers shift, and captures keep theirs.</summary>
        public void ReorderVideo(string id, int newIndex)
        {
            var v = RequireVideo(id);
            Reorder(D.ShotList.Videos, x => x.ChapterId == v.ChapterId, v, newIndex);
            Finish();
        }

        /// <summary>Copy a video (new ID, new note IDs) directly after the original.</summary>
        public Video DuplicateVideo(string id, string newTitle = null)
        {
            var v = RequireVideo(id);
            var copy = TripJson.Clone(v);
            copy.Id = Ids.New("v");
            copy.Title = newTitle ?? (v.Title + " (copy)");
            copy.Notes = NodeTree.Clone(v.Notes, newIds: true);
            D.ShotList.Videos.Insert(D.ShotList.Videos.IndexOf(v) + 1, copy);
            Finish();
            return copy;
        }

        /// <summary>Editor for a video's nested notes. Relabels with the scheme the notes already use (default 1 / a / 1).</summary>
        public NodeListEditor Notes(string videoId)
        {
            var v = RequireVideo(videoId);
            if (v.Notes == null) v.Notes = new List<Node>();
            return new NodeListEditor(() => v.Notes, n => v.Notes = n, LabelScheme.Infer(v.Notes, LabelScheme.ShotListNotes), () => Finish());
        }

        public void AddPhotoRef(string videoId, string photoId, int? index = null)
        {
            var v = RequireVideo(videoId);
            RequirePhoto(photoId);
            if (v.PhotoRefs == null) v.PhotoRefs = new List<string>();
            if (v.PhotoRefs.Contains(photoId)) return;
            v.PhotoRefs.Insert(Clamp(index ?? v.PhotoRefs.Count, v.PhotoRefs.Count), photoId);
            Finish();
        }

        public void RemovePhotoRef(string videoId, string photoId)
        {
            var v = RequireVideo(videoId);
            if (v.PhotoRefs != null && v.PhotoRefs.Remove(photoId)) Finish();
        }

        public void SetPhotoRefs(string videoId, IEnumerable<string> photoIds)
        {
            var v = RequireVideo(videoId);
            var list = (photoIds ?? Enumerable.Empty<string>()).Distinct().ToList();
            foreach (var id in list) RequirePhoto(id);
            v.PhotoRefs = list;
            Finish();
        }

        // ------------------------------------------------------------------
        // Master photo list
        // ------------------------------------------------------------------

        /// <summary>
        /// Add a photo to a book's master list. A book has one cover and one hero per chapter;
        /// adding a second is refused (edit the existing one instead). Heroes are always ordered
        /// first (cover, then chapter heroes by chapter); <paramref name="index"/> positions a
        /// non-hero photo among the non-hero photos.
        /// </summary>
        public Photo AddPhoto(string bookId, string description, HeroType heroType = HeroType.None, string chapterId = null, int? index = null)
        {
            RequireBook(bookId);
            if (chapterId != null)
            {
                var ch = RequireChapter(chapterId);
                if (ch.BookId != bookId) throw new ArgumentException("Chapter " + chapterId + " is not in book " + bookId);
            }
            if (heroType == HeroType.BookCover && D.ShotList.Photos.Any(p => p.BookId == bookId && p.HeroType == HeroType.BookCover))
                throw new InvalidOperationException("Book already has a cover hero shot.");
            if (heroType == HeroType.ChapterHero)
            {
                if (chapterId == null) throw new ArgumentException("A chapter hero needs a chapterId.");
                if (D.ShotList.Photos.Any(p => p.ChapterId == chapterId && p.HeroType == HeroType.ChapterHero))
                    throw new InvalidOperationException("Chapter already has a hero shot.");
            }
            var photo = new Photo { Id = Ids.New("ph"), BookId = bookId, ChapterId = chapterId, HeroType = heroType, Description = description };
            Insert(D.ShotList.Photos, p => p.BookId == bookId && p.HeroType == HeroType.None, photo, heroType == HeroType.None ? index : null);
            Finish();
            return photo;
        }

        /// <summary>Edit description/hero type/chapter. Id, bookId and order are restored; hero uniqueness is enforced.</summary>
        public void UpdatePhoto(string id, Action<Photo> edit)
        {
            var p = RequirePhoto(id);
            var keepId = p.Id; var keepBook = p.BookId; var keepOrder = p.Order;
            var before = TripJson.Clone(p);
            edit(p);
            p.Id = keepId; p.BookId = keepBook; p.Order = keepOrder;
            if (p.HeroType == HeroType.BookCover && D.ShotList.Photos.Any(o => o != p && o.BookId == p.BookId && o.HeroType == HeroType.BookCover))
            { Restore(p, before); throw new InvalidOperationException("Book already has a cover hero shot."); }
            if (p.HeroType == HeroType.ChapterHero && (p.ChapterId == null || D.ShotList.Photos.Any(o => o != p && o.ChapterId == p.ChapterId && o.HeroType == HeroType.ChapterHero)))
            { Restore(p, before); throw new InvalidOperationException("Chapter hero needs a chapter that has no hero yet."); }
            if (p.ChapterId != null && RequireChapter(p.ChapterId).BookId != p.BookId)
            { Restore(p, before); throw new ArgumentException("Chapter is not in this photo's book."); }
            Finish();
        }

        /// <summary>Remove a master-list photo. Blocked if a capture, photo capture, amendment or assignment references it. Video photoRefs to it are cleaned up.</summary>
        public void RemovePhoto(string id)
        {
            var p = RequirePhoto(id);
            ReferenceFinder.ThrowIfReferenced(D, id, "Removing photo", "Use a drop amendment for a photo that was on the printed list.");
            RemovePhotoInternal(p);
            Finish();
        }

        /// <summary>Reorder a non-hero photo among its book's non-hero photos. Hero positions are fixed by the rule.</summary>
        public void ReorderPhoto(string id, int newIndex)
        {
            var p = RequirePhoto(id);
            if (p.HeroType != HeroType.None) throw new InvalidOperationException("Hero shots are ordered automatically (cover, then chapter heroes).");
            Reorder(D.ShotList.Photos, x => x.BookId == p.BookId && x.HeroType == HeroType.None, p, newIndex);
            Finish();
        }

        /// <summary>Copy a photo as a non-hero photo directly after the original in its book.</summary>
        public Photo DuplicatePhoto(string id)
        {
            var p = RequirePhoto(id);
            var copy = TripJson.Clone(p);
            copy.Id = Ids.New("ph");
            copy.HeroType = HeroType.None;
            copy.ChapterId = null;
            D.ShotList.Photos.Insert(D.ShotList.Photos.IndexOf(p) + 1, copy);
            Finish();
            return copy;
        }

        /// <summary>Extra book/chapter associations for a photo that belongs in more than one place.</summary>
        public void SetPhotoAssociations(string photoId, IEnumerable<string> alsoBookIds, IEnumerable<string> alsoChapterIds)
        {
            var p = RequirePhoto(photoId);
            var books = (alsoBookIds ?? Enumerable.Empty<string>()).Where(b => b != null && b != p.BookId).Distinct().ToList();
            var chapters = (alsoChapterIds ?? Enumerable.Empty<string>()).Where(c => c != null && c != p.ChapterId).Distinct().ToList();
            foreach (var b in books) RequireBook(b);
            foreach (var c in chapters) RequireChapter(c);
            p.AlsoBookIds = books;
            p.AlsoChapterIds = chapters;
            Finish();
        }

        private void RemovePhotoInternal(Photo p)
        {
            D.ShotList.Photos.Remove(p);
            foreach (var v in D.ShotList.Videos) v.PhotoRefs?.RemoveAll(r => r == p.Id);
        }

        private void StripAssociations(string bookId, string chapterId)
        {
            foreach (var p in D.ShotList.Photos)
            {
                if (bookId != null) p.AlsoBookIds?.Remove(bookId);
                if (chapterId != null) p.AlsoChapterIds?.Remove(chapterId);
            }
        }

        private static void Restore(Photo target, Photo from)
        {
            target.ChapterId = from.ChapterId; target.HeroType = from.HeroType; target.Description = from.Description; target.Extra = from.Extra;
        }

        // ------------------------------------------------------------------
        // Numbering
        // ------------------------------------------------------------------

        /// <summary>
        /// Rebuild all numbering from list order: books 1..N; chapters 1..N within each book;
        /// videos continuous across books and chapters in printed order (book, chapter, list
        /// position); photos 1..N within each book with heroes first. Lists are re-sorted into
        /// that order. Captures are untouched: plannedNumber is their record of the paper.
        /// </summary>
        public void Renumber()
        {
            var books = D.Trip.Books;
            for (int i = 0; i < books.Count; i++) books[i].Number = i + 1;
            var bookIndex = books.Select((b, i) => (b.Id, i)).ToDictionary(t => t.Id, t => t.i);
            int BookRank(string id) => id != null && bookIndex.TryGetValue(id, out var i) ? i : int.MaxValue;

            // Chapters: stable by book rank, then list order.
            var chapters = D.ShotList.Chapters.Select((c, i) => (c, i)).OrderBy(t => BookRank(t.c.BookId)).ThenBy(t => t.i).Select(t => t.c).ToList();
            foreach (var group in chapters.GroupBy(c => c.BookId))
            {
                int n = 1;
                foreach (var c in group) c.Number = n++;
            }
            D.ShotList.Chapters = chapters;
            var chapterIndex = chapters.Select((c, i) => (c.Id, i)).ToDictionary(t => t.Id, t => t.i);
            int ChapterRank(string id) => id != null && chapterIndex.TryGetValue(id, out var i) ? i : int.MaxValue;

            // Videos: by chapter rank (which already encodes book), then list order.
            var videos = D.ShotList.Videos.Select((v, i) => (v, i)).OrderBy(t => ChapterRank(t.v.ChapterId)).ThenBy(t => t.i).Select(t => t.v).ToList();
            for (int i = 0; i < videos.Count; i++)
            {
                videos[i].Number = i + 1;
                var ch = D.FindChapter(videos[i].ChapterId);
                if (ch != null) videos[i].BookId = ch.BookId;
            }
            D.ShotList.Videos = videos;

            // Photos: by book rank; within a book cover, chapter heroes by chapter rank, then the rest in list order.
            int HeroRank(Photo p) => p.HeroType == HeroType.BookCover ? 0 : p.HeroType == HeroType.ChapterHero ? 1 : 2;
            var photos = D.ShotList.Photos.Select((p, i) => (p, i))
                .OrderBy(t => BookRank(t.p.BookId))
                .ThenBy(t => HeroRank(t.p))
                .ThenBy(t => t.p.HeroType == HeroType.ChapterHero ? ChapterRank(t.p.ChapterId) : 0)
                .ThenBy(t => t.i)
                .Select(t => t.p).ToList();
            foreach (var group in photos.GroupBy(p => p.BookId))
            {
                int n = 1;
                foreach (var p in group) p.Order = n++;
            }
            D.ShotList.Photos = photos;
        }

        private void Finish(bool booksChanged = false)
        {
            Renumber();
            _s.MarkDirty(DocumentKind.ShotList);
            if (booksChanged) _s.MarkDirty(DocumentKind.Trip);
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private Book RequireBook(string id) => D.FindBook(id) ?? throw new KeyNotFoundException("No book " + id);
        private Chapter RequireChapter(string id) => D.FindChapter(id) ?? throw new KeyNotFoundException("No chapter " + id);
        private Video RequireVideo(string id) => D.FindVideo(id) ?? throw new KeyNotFoundException("No video " + id);
        private Photo RequirePhoto(string id) => D.FindPhoto(id) ?? throw new KeyNotFoundException("No photo " + id);

        private static int Clamp(int index, int count) => index < 0 ? 0 : (index > count ? count : index);

        /// <summary>Insert an item at an index among the members of its group, keeping the group contiguous in the flat list.</summary>
        internal static void Insert<T>(List<T> all, Func<T, bool> inGroup, T item, int? index) where T : class
        {
            var siblings = all.Where(inGroup).ToList();
            int idx = Clamp(index ?? siblings.Count, siblings.Count);
            if (siblings.Count == 0) { all.Add(item); return; }
            if (idx >= siblings.Count) all.Insert(all.IndexOf(siblings[siblings.Count - 1]) + 1, item);
            else all.Insert(all.IndexOf(siblings[idx]), item);
        }

        /// <summary>Move an item to a new index among its group members.</summary>
        internal static void Reorder<T>(List<T> all, Func<T, bool> inGroup, T item, int newIndex) where T : class
        {
            var siblings = all.Where(inGroup).ToList();
            if (!siblings.Remove(item)) return;
            siblings.Insert(Clamp(newIndex, siblings.Count), item);
            int first = all.IndexOf(item);
            foreach (var s in siblings) { var i = all.IndexOf(s); if (i >= 0 && i < first) first = i; }
            all.RemoveAll(x => siblings.Contains(x));
            all.InsertRange(Math.Min(first, all.Count), siblings);
        }
    }
}

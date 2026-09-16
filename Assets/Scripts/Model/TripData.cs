using System.Collections.Generic;
using System.Linq;

namespace MediaTrip.Model
{
    /// <summary>
    /// A whole trip folder in memory: trip.json, shotlist.json, outlines/*.json, captures.json.
    /// Plain data. Mutation tracking and autosave live in <c>MediaTrip.Session.TripSession</c>.
    /// </summary>
    public class TripData
    {
        /// <summary>Folder the data was loaded from (null for a fresh in-memory trip).</summary>
        public string FolderPath { get; set; }

        public TripDocument Trip { get; set; } = new TripDocument();
        public ShotListDocument ShotList { get; set; } = new ShotListDocument();
        public CapturesDocument Captures { get; set; } = new CapturesDocument();

        /// <summary>Outlines keyed by bookId. A book with no outline file simply has no entry.</summary>
        public Dictionary<string, OutlineDocument> Outlines { get; } = new Dictionary<string, OutlineDocument>();

        /// <summary>Outline file name (relative to outlines/) per bookId, so saves go back to the same file.</summary>
        public Dictionary<string, string> OutlineFileNames { get; } = new Dictionary<string, string>();

        public string TripId => Trip?.TripId;

        // ---- Convenience lookups (linear scans; the data is tiny) ----

        public Book FindBook(string id) => Trip.Books.FirstOrDefault(b => b.Id == id);
        public Chapter FindChapter(string id) => ShotList.Chapters.FirstOrDefault(c => c.Id == id);
        public Video FindVideo(string id) => ShotList.Videos.FirstOrDefault(v => v.Id == id);
        public Photo FindPhoto(string id) => ShotList.Photos.FirstOrDefault(p => p.Id == id);
        public Person FindPerson(string id) => Trip.People.FirstOrDefault(p => p.Id == id);
        public Day FindDay(string id) => Trip.Days.FirstOrDefault(d => d.Id == id);
        public Capture FindCapture(string id) => Captures.Captures.FirstOrDefault(c => c.Id == id);
        public PhotoCapture FindPhotoCapture(string id) => Captures.PhotoCaptures.FirstOrDefault(c => c.Id == id);
        public Amendment FindAmendment(string id) => Captures.Amendments.FirstOrDefault(a => a.Id == id);

        public OutlineDocument FindOutline(string bookId) =>
            bookId != null && Outlines.TryGetValue(bookId, out var o) ? o : null;

        public OutlineSection FindOutlineSection(string bookId, string sectionId)
        {
            var outline = FindOutline(bookId);
            if (outline == null || sectionId == null) return null;
            return outline.Chapters.SelectMany(c => c.Sections).FirstOrDefault(s => s.Id == sectionId);
        }

        public IEnumerable<Chapter> ChaptersOf(string bookId) =>
            ShotList.Chapters.Where(c => c.BookId == bookId).OrderBy(c => c.Number);

        /// <summary>Creates an empty, valid trip with a fresh tripId.</summary>
        public static TripData CreateNew(string clientAbbrev = null, string programAbbrev = null)
        {
            var data = new TripData();
            var id = Ids.New("trip");
            data.Trip.TripId = id;
            data.Trip.Identity.ClientAbbrev = clientAbbrev;
            data.Trip.Identity.ProgramAbbrev = programAbbrev;
            data.ShotList.TripId = id;
            data.ShotList.Source = "typed in app";
            data.Captures.TripId = id;
            return data;
        }
    }
}

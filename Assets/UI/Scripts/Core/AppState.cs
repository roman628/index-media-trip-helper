using System.Collections.Generic;
using MediaTrip.Persistence;
using MediaTrip.UI.ViewModels;

namespace MediaTrip.UI
{
    /// <summary>The trip library, the five documents, the filming screen, and the hidden JSON inspector.</summary>
    public enum Screen { Library, Trip, ShotList, Outlines, Covers, Summary, Film, Json }

    public static class Screens
    {
        /// <summary>The five documents, in rail / bottom-bar order.</summary>
        public static readonly Screen[] Tabs = { Screen.Trip, Screen.ShotList, Screen.Outlines, Screen.Covers, Screen.Summary };

        public static string Label(Screen s)
        {
            switch (s)
            {
                case Screen.Library: return "Trips";
                case Screen.Trip: return "Trip";
                case Screen.ShotList: return "Shot list";
                case Screen.Outlines: return "Outlines";
                case Screen.Covers: return "Covers";
                case Screen.Summary: return "Summary";
                case Screen.Film: return "Filming";
                case Screen.Json: return "JSON";
                default: return s.ToString();
            }
        }

        public static GlyphKind Glyph(Screen s)
        {
            switch (s)
            {
                case Screen.Trip: return GlyphKind.Target;
                case Screen.ShotList: return GlyphKind.List;
                case Screen.Outlines: return GlyphKind.Lines;
                case Screen.Covers: return GlyphKind.Star;
                case Screen.Summary: return GlyphKind.Square;
                default: return GlyphKind.None;
            }
        }

        /// <summary>
        /// Documents with an edit state. Edit changes the document; the normal state annotates
        /// it. Covers and Summary are annotation only, so they have no Edit.
        /// </summary>
        public static bool HasEdit(Screen s) => s == Screen.Trip || s == Screen.ShotList || s == Screen.Outlines || s == Screen.Library;
    }

    /// <summary>Everything about what is on screen that is not trip data.</summary>
    public sealed class AppState
    {
        public Screen Screen = Screen.Library;
        /// <summary>The header's Edit toggle. Cleared whenever the screen changes.</summary>
        public bool Edit;
        public bool Menu;
        public bool Shortcuts;
        /// <summary>Search text; null when the search sheet is closed.</summary>
        public string Search;
        /// <summary>Name of the TextField to refocus after a re-render (null = none).</summary>
        public string Focus;

        public TripState Trip = new TripState();
        public ShotListState SL = new ShotListState();
        public OutlineState OL = new OutlineState();
        public CoversState CV = new CoversState();
        public SummaryState SM = new SummaryState();
        public FilmState Film = new FilmState();
        public PasteState Paste;
        public IoState IO = new IoState();
        public JsonState JS = new JsonState();

        public sealed class TripState
        {
            public string Section = "identity";
            /// <summary>Section to scroll to after the next render.</summary>
            public string ScrollTo;
            /// <summary>The person being typed in before they have a name; null when no row is open.</summary>
            public PersonDraft NewPerson;
            /// <summary>The book whose "add team member" row is open.</summary>
            public string MemberBook;
            public string MemberFirst = "", MemberLast = "";
        }

        public sealed class PersonDraft
        {
            public string First = "", Last = "", Title = "";
            public Model.Org Org = Model.Org.Client;
        }

        public sealed class ShotListState
        {
            public ShotListMode View = ShotListMode.Working;
            public bool HideDone;
            /// <summary>Videos (and, in edit, photos) whose detail is expanded in place.</summary>
            public HashSet<string> Expanded = new HashSet<string>();
            /// <summary>Row to scroll to after the next render (opened from search, Summary, Changes).</summary>
            public string ScrollTo;
            public AmendDraft Amend;
            /// <summary>True once "Fix the original" was chosen for this visit, so the question is asked once.</summary>
            public bool OriginalGatePassed;
            /// <summary>The SME being typed for a video in Edit, by video id.</summary>
            public Dictionary<string, SmeDraft> SmeDrafts = new Dictionary<string, SmeDraft>();
            public SmeDraft SmeDraftFor(string videoId)
            {
                if (!SmeDrafts.TryGetValue(videoId, out var d)) SmeDrafts[videoId] = d = new SmeDraft();
                return d;
            }
        }

        public sealed class OutlineState
        {
            public string Book;
            public HashSet<string> OpenChapters = new HashSet<string>();
            public HashSet<string> OpenSections = new HashSet<string>();
            public string ScrollTo;
            /// <summary>The "place media" input that is open: "chapterId|sectionId" (sectionId empty for the chapter as a whole).</summary>
            public string PlaceAt;
            public string PlaceQuery = "";
            /// <summary>When a typed name matched nothing: is the new thing a photo or a video?</summary>
            public bool PlaceNewIsVideo;
        }

        public sealed class CoversState
        {
            public string SlotBook;
            /// <summary>"cover" or a chapter id; null when no slot sheet is open.</summary>
            public string SlotKey;
            public string Query = "";
        }

        public sealed class SummaryState
        {
            public string Day;
            public string OpenCapture;
            public string OpenPhoto;
            public PhotoBatchDraft Batch;
        }

        public sealed class FilmState
        {
            public CaptureDraft Draft;
            public string DayId;
            public bool PlanOpen;
            public Screen Return = Screen.Summary;
        }

        public sealed class PasteState
        {
            /// <summary>"video" or "section".</summary>
            public string Kind;
            public string BookId;
            public string Id;
            /// <summary>Video notes only: paste into the working copy (recorded as a revision) rather than the original.</summary>
            public bool Working;
            public string Text = "";
        }

        public sealed class IoState
        {
            public ImportReport Report;
            public string SourceLabel;
            public string PendingPath;
            /// <summary>Set when the import was started for one kind of document; anything else is refused.</summary>
            public DocumentKind? Expect;
            /// <summary>The book an outline import goes into, whatever bookId the file carries.</summary>
            public string OutlineBookId;
        }

        public sealed class JsonState
        {
            public DocumentKind Doc = DocumentKind.ShotList;
            public string OutlineBook;
            public string Highlight;
        }
    }
}

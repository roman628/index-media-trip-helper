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

        /// <summary>Documents with an edit state. Covers and Summary are always live.</summary>
        public static bool HasEdit(Screen s) => s == Screen.Trip || s == Screen.ShotList || s == Screen.Outlines || s == Screen.Library;
    }

    /// <summary>Everything about what is on screen that is not trip data.</summary>
    public sealed class AppState
    {
        public Screen Screen = Screen.Library;
        /// <summary>The header's Edit toggle. Cleared whenever the screen changes.</summary>
        public bool Edit;
        public bool Menu;
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
        }

        public sealed class ShotListState
        {
            public ShotListMode View = ShotListMode.Working;
            public bool HideDone;
            /// <summary>Plan item whose detail is open (side panel in landscape, sheet otherwise).</summary>
            public string VideoId;
            /// <summary>Master-list photo being edited (edit state only).</summary>
            public string PhotoId;
            public AmendDraft Amend;
            public string SmeQ = "";
        }

        public sealed class OutlineState
        {
            public string Book;
            public string Section;
            /// <summary>Portrait and phone show either the chapter list or one section.</summary>
            public bool ListOpen = true;
            public bool Place;
        }

        public sealed class CoversState
        {
            public string SlotBook;
            /// <summary>"cover" or a chapter id; null when no slot sheet is open.</summary>
            public string SlotKey;
            public string NewText = "";
            public bool Picking;
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
            public string Text = "";
        }

        public sealed class IoState
        {
            public ImportReport Report;
            public string SourceLabel;
            public string PendingPath;
        }

        public sealed class JsonState
        {
            public DocumentKind Doc = DocumentKind.ShotList;
            public string OutlineBook;
            public string Highlight;
        }
    }
}

using System.Collections.Generic;
using MediaTrip.Persistence;
using MediaTrip.UI.ViewModels;

namespace MediaTrip.UI
{
    public enum Mode { Field, Author }

    public enum Screen
    {
        // field
        Today, Plan, Heroes, Capture, Coverage,
        // author
        Trip, People, ShotList, Outline, Amend, IO, Json,
    }

    public static class Screens
    {
        public static readonly Screen[] FieldTabs = { Screen.Today, Screen.Plan, Screen.Heroes, Screen.Coverage };
        public static readonly Screen[] AuthorTabs = { Screen.Trip, Screen.People, Screen.ShotList, Screen.Outline, Screen.Amend, Screen.IO, Screen.Json };

        public static bool IsField(Screen s) => s == Screen.Today || s == Screen.Plan || s == Screen.Heroes || s == Screen.Capture || s == Screen.Coverage;

        public static string Label(Screen s)
        {
            switch (s)
            {
                case Screen.Today: return "Today";
                case Screen.Plan: return "Plan";
                case Screen.Heroes: return "Heroes";
                case Screen.Coverage: return "Coverage";
                case Screen.Capture: return "Log capture";
                case Screen.Trip: return "Trip";
                case Screen.People: return "People";
                case Screen.ShotList: return "Shot list";
                case Screen.Outline: return "Outlines";
                case Screen.Amend: return "Amendments";
                case Screen.IO: return "Import / export";
                case Screen.Json: return "JSON";
                default: return s.ToString();
            }
        }
    }

    /// <summary>Everything about what is on screen that is not trip data.</summary>
    public sealed class AppState
    {
        public Mode Mode = Mode.Field;
        public Screen Screen = Screen.Today;
        public string Query = "";
        public bool Help;
        /// <summary>Name of the TextField to refocus after a re-render (null = none).</summary>
        public string Focus;

        // ---- field
        public string DayId;
        public bool RemainingOnly;
        public HashSet<string> OpenBooks = new HashSet<string>();
        /// <summary>Plan item shown in the detail sheet on Today (null = closed).</summary>
        public string DetailItemId;
        public string PlanBook;
        public string PlanVideo;
        public string CovBook;
        public bool CovEmptyOnly;
        public CaptureDraft Capture;
        public AmendDraft Amend;

        // ---- author
        public string TripSection = "identity";
        public string PeopleSel;
        public string PeopleQ = "";
        public ShotListState SL = new ShotListState();
        public OutlineState OL = new OutlineState();
        public string AmendEdit;
        public IoState IO = new IoState();
        public JsonState JS = new JsonState();
        public RenumberTracker Renumber = new RenumberTracker();

        public sealed class ShotListState
        {
            public string Pane = "videos";        // videos | photos
            public string SelKind = "video";      // video | chapter | photo | none
            public string SelId;
            public string Book;
            public string NoteSel;
            /// <summary>Paste sheet text; null when the sheet is closed.</summary>
            public string Paste;
            public string SmeQ = "";
            public HashSet<string> Collapsed = new HashSet<string>();
        }

        public sealed class OutlineState
        {
            public string Book;
            public string Chapter;
            /// <summary>"sec:<id>" | "node:<id>" | null.</summary>
            public string Sel;
            public string Paste;
        }

        public sealed class IoState
        {
            /// <summary>idle | preview | done</summary>
            public string Stage = "idle";
            public ImportReport Report;
            public string SourceLabel;
            public string PendingPath;
            public string LastMessage;
            public bool LastMessageIsError;
        }

        public sealed class JsonState
        {
            public DocumentKind Doc = DocumentKind.ShotList;
            public string OutlineBook;
            public string Highlight;
        }
    }
}

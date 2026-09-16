using System;
using System.Linq;
using MediaTrip.Model;
using MediaTrip.Persistence;
using MediaTrip.Session;
using MediaTrip.UI.Author;
using MediaTrip.UI.Field;
using MediaTrip.UI.Shell;
using MediaTrip.UI.Transfer;
using MediaTrip.UI.ViewModels;
using UnityEngine;
using UnityEngine.UIElements;

namespace MediaTrip.UI
{
    /// <summary>
    /// The app: owns the open TripSession, the UI state, the theme, and the render loop.
    /// Screens are rebuilt from state on every render (the data is small); text fields that
    /// edit the trip use <see cref="Edit"/> so typing does not trigger a rebuild, and the
    /// focused field is restored by name after a rebuild.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class AppController : MonoBehaviour
    {
        private const string PrefLastTrip = "mt.lastTrip";

        public TripSession Session { get; private set; }
        public AppState State { get; private set; } = new AppState();
        public ThemeManager Theme { get; private set; }
        public TripTransfer Transfer { get; private set; }
        public ValidationSummary Validation { get; private set; }
        public DateTime? LastSaved { get; private set; }
        public VisualElement Root { get; private set; }

        /// <summary>Set by the active editor screen: handles accessory-bar actions ("sib", "child", "in", "out", "up", "down", "dup", "del", "paste"). Returns true if handled.</summary>
        public Func<string, bool> AccessoryAction;
        /// <summary>Set by the active screen for list navigation keys. Returns true if handled.</summary>
        public Func<KeyDownEvent, bool> ScreenKeys;

        private UIDocument _doc;
        private VisualElement _topHost, _bodyHost, _overlayHost, _toastHost;
        private TripAutosaveBehaviour _autosave;
        private int _suppress;
        private bool _renderScheduled;
        private Label _toast;
        private IVisualElementScheduledItem _toastTimer;

        private void OnEnable()
        {
            _doc = GetComponent<UIDocument>();
            var root = _doc.rootVisualElement;
            Root = root.Q("app") ?? root;
            _topHost = Root.Q("top-host");
            _bodyHost = Root.Q("body-host");
            _overlayHost = Root.Q("overlay-host");
            _toastHost = Root.Q("toast-host");
            if (_topHost == null || _bodyHost == null)
            {
                Debug.LogError("App.uxml is missing its hosts; run Media Trip > Set Up Scene.");
                return;
            }
            var app = Resources.Load<StyleSheet>("Styles/App");
            if (app != null && !Root.styleSheets.Contains(app)) Root.styleSheets.Add(app);
            Theme = new ThemeManager(Root);
            Theme.Changed += ScheduleRender;
            Transfer = new TripTransfer(this);
            NativeBridge.Ensure();
            Shortcuts.Attach(this, Root);
            Boot();
        }

        private void OnDisable()
        {
            CloseTrip();
        }

        private void Boot()
        {
            try
            {
                TripLibrary.EnsureSampleIfEmpty();
                var last = PlayerPrefs.GetString(PrefLastTrip, null);
                var trips = TripLibrary.ListTrips();
                var pick = trips.FirstOrDefault(t => t.TripId == last && t.LoadError == null) ?? trips.FirstOrDefault(t => t.LoadError == null);
                if (pick != null) OpenTrip(pick.TripId);
                else
                {
                    var created = TripLibrary.CreateTrip("NEW", "TRIP");
                    OpenTrip(created.TripId);
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                _bodyHost.Clear();
                _bodyHost.Add(U.Text("Could not open a trip: " + ex.Message, "h3 bad-text").Pad(24));
            }
        }

        // ------------------------------------------------------------------ trip lifecycle

        public void OpenTrip(string tripId)
        {
            CloseTrip();
            Session = TripSession.OpenFromLibrary(tripId);
            _autosave = TripAutosaveBehaviour.Attach(Session);
            Session.Autosaver.Saved += () => { LastSaved = DateTime.Now; ScheduleRender(); };
            Session.Autosaver.SaveFailed += ex => Toast("Save failed: " + ex.Message);
            Session.Changed += OnSessionChanged;
            PlayerPrefs.SetString(PrefLastTrip, tripId);
            PlayerPrefs.Save();

            var keepMode = State?.Mode ?? Mode.Field;
            State = new AppState { Mode = keepMode, Screen = keepMode == Mode.Field ? Screen.Today : Screen.Trip };
            var t = Session.Data.Trip;
            State.DayId = Fmt.TodayOrLast(t, DateTime.Now)?.Id;
            State.PlanBook = t.Books.FirstOrDefault()?.Id;
            State.CovBook = State.PlanBook;
            State.SL.Book = State.PlanBook;
            State.OL.Book = State.PlanBook;
            State.PeopleSel = t.People.FirstOrDefault()?.Id;
            var firstVideo = Session.Data.ShotList.Videos.FirstOrDefault();
            State.SL.SelId = firstVideo?.Id;
            State.SL.SelKind = firstVideo != null ? "video" : "none";
            State.OL.Chapter = Session.Data.FindOutline(State.OL.Book)?.Chapters.FirstOrDefault()?.Id;
            State.Renumber.Snapshot(Session.Data);
            foreach (var b in t.Books.Skip(Math.Max(0, t.Books.Count - 1))) State.OpenBooks.Add(b.Id);
            Render();
        }

        public void CloseTrip()
        {
            if (Session == null) return;
            Session.Changed -= OnSessionChanged;
            if (_autosave != null) { _autosave.Detach(); _autosave = null; }
            Session.Dispose();
            Session = null;
        }

        public void ReloadCurrentTrip()
        {
            var id = Session?.Data.TripId;
            if (id != null) OpenTrip(id);
        }

        private void OnSessionChanged()
        {
            Validation = null;
            if (_suppress > 0) return;
            ScheduleRender();
        }

        public ValidationSummary CurrentValidation => Validation ?? (Validation = ValidationSummary.Of(Session.Data));

        // ------------------------------------------------------------------ rendering

        /// <summary>Run a mutation without a rebuild (typing into a bound field).</summary>
        public void Edit(Action a)
        {
            _suppress++;
            try { a(); }
            finally { _suppress--; }
        }

        public void ScheduleRender()
        {
            if (_renderScheduled || Root == null) return;
            _renderScheduled = true;
            Root.schedule.Execute(() => { _renderScheduled = false; Render(); });
        }

        public void Render()
        {
            if (Session == null || _topHost == null) return;
            AccessoryAction = null;
            ScreenKeys = null;
            try
            {
                _topHost.Clear();
                _topHost.Add(TopBar.Build(this));
                _bodyHost.Clear();
                _bodyHost.Add(BuildScreen());
                _overlayHost.Clear();
                var overlay = BuildOverlay();
                if (overlay != null) { _overlayHost.Add(overlay); _overlayHost.pickingMode = PickingMode.Position; }
                else _overlayHost.pickingMode = PickingMode.Ignore;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                _bodyHost.Clear();
                _bodyHost.Add(U.Col(U.H2("Something went wrong drawing this screen"), U.Sub(ex.Message)).Pad(24));
            }
            RestoreFocus();
        }

        private VisualElement BuildScreen()
        {
            switch (State.Screen)
            {
                case Screen.Today: return TodayScreen.Build(this);
                case Screen.Plan: return PlanScreen.Build(this);
                case Screen.Heroes: return HeroesScreen.Build(this);
                case Screen.Capture: return CaptureScreen.Build(this);
                case Screen.Coverage: return CoverageScreen.Build(this);
                case Screen.Trip: return TripSetupScreen.Build(this);
                case Screen.People: return PeopleScreen.Build(this);
                case Screen.ShotList: return ShotListScreen.Build(this);
                case Screen.Outline: return OutlineScreen.Build(this);
                case Screen.Amend: return AmendmentsScreen.Build(this);
                case Screen.IO: return ImportExportScreen.Build(this);
                case Screen.Json: return JsonScreen.Build(this);
                default: return U.Text("?");
            }
        }

        private VisualElement BuildOverlay()
        {
            if (State.Help) return HelpSheet.Build(this);
            if (State.Amend != null) return AmendSheet.Build(this);
            if (State.DetailItemId != null && (State.Screen == Screen.Today || State.Screen == Screen.Heroes || State.Screen == Screen.Coverage)) return VideoDetail.Sheet(this, State.DetailItemId);
            if (State.SL.Paste != null && State.Screen == Screen.ShotList) return PasteSheet.Build(this, "shotlist");
            if (State.OL.Paste != null && State.Screen == Screen.Outline) return PasteSheet.Build(this, "outline");
            if (State.AmendEdit != null && State.Screen == Screen.Amend) return AmendmentsScreen.EditSheet(this);
            if (State.IO.PasteOpen && State.Screen == Screen.IO) return ImportExportScreen.PasteSheet(this);
            if (_sheet != null) return _sheet;
            return null;
        }

        private VisualElement _sheet;

        /// <summary>Show an ad-hoc sheet (theme picker, trip library, confirmations). Survives re-renders until closed.</summary>
        public void ShowSheet(Func<VisualElement> build)
        {
            _sheetBuilder = build;
            _sheet = build();
            Render();
        }

        private Func<VisualElement> _sheetBuilder;

        public void CloseSheet()
        {
            _sheet = null;
            _sheetBuilder = null;
            Render();
        }

        public bool HasSheet => _sheet != null;

        public VisualElement Overlay(VisualElement sheet, Action onBackdrop)
        {
            var overlay = new VisualElement().Cls("overlay");
            overlay.RegisterCallback<ClickEvent>(e => { if (e.target == overlay) onBackdrop?.Invoke(); });
            overlay.Add(sheet);
            return overlay;
        }

        private void RestoreFocus()
        {
            var name = State.Focus;
            if (string.IsNullOrEmpty(name)) return;
            var tf = Root.Q<TextField>(name);
            if (tf == null) return;
            tf.schedule.Execute(() =>
            {
                tf.Focus();
                var n = tf.value?.Length ?? 0;
                tf.SelectRange(n, n);
            });
        }

        /// <summary>Re-render and put the caret back into the named field.</summary>
        public void RenderKeepFocus(string fieldName)
        {
            State.Focus = fieldName;
            Render();
        }

        // ------------------------------------------------------------------ navigation

        public void Nav(Screen s)
        {
            State.Screen = s;
            State.Mode = Screens.IsField(s) ? Mode.Field : Mode.Author;
            State.Help = false;
            State.Query = "";
            State.Focus = null;
            _sheet = null;
            if (s == Screen.Capture && State.Capture == null) State.Capture = new CaptureDraft();
            if (s == Screen.ShotList && !State.Renumber.HasSnapshot) State.Renumber.Snapshot(Session.Data);
            Render();
        }

        public void SetMode(Mode m) => Nav(m == Mode.Field ? Screen.Today : Screen.ShotList);

        public void OpenCapture(string planItemId = null)
        {
            State.Capture = planItemId != null ? CaptureDraft.ForItem(Session, planItemId) : new CaptureDraft();
            State.DetailItemId = null;
            Nav(Screen.Capture);
            State.Focus = "cap.title";
            Render();
        }

        public void OpenDetail(string planItemId)
        {
            State.DetailItemId = planItemId;
            Render();
        }

        public void OpenAmend(string planItemId)
        {
            State.Amend = AmendDraft.For(Session, planItemId);
            Render();
        }

        // ------------------------------------------------------------------ toast

        public void Toast(string message)
        {
            if (_toastHost == null) return;
            _toastTimer?.Pause();
            _toast?.RemoveFromHierarchy();
            _toast = U.Text(message, "toast");
            _toastHost.Add(_toast);
            _toastTimer = _toastHost.schedule.Execute(() => { _toast?.RemoveFromHierarchy(); _toast = null; }).StartingIn(2400);
        }

        // ------------------------------------------------------------------ helpers used by screens

        public string TodayId => State.DayId ?? Session.Data.Trip.Days.LastOrDefault()?.Id;

        public string EnsureDay()
        {
            if (Session.Data.Trip.Days.Count == 0)
            {
                var d = Session.AddDay(DateTime.Now.ToString("yyyy-MM-dd"), "Day 1");
                State.DayId = d.Id;
            }
            return TodayId;
        }

        public void ToggleItem(string planItemId)
        {
            var item = Session.Plan.FindItem(planItemId);
            if (item == null || item.IsDropped || item.IsSuperseded) return;
            if (item.VideoCaptured)
            {
                Session.Uncheck(planItemId);
                Toast("Unchecked · " + item.Title);
            }
            else
            {
                var day = EnsureDay();
                var cap = Session.CheckOff(planItemId, day);
                var dayLabel = Session.Data.FindDay(day)?.Label ?? "today";
                Toast("Captured · " + dayLabel + " #" + cap.CapturedOrder + " · details can be added later");
            }
        }

        public void TogglePhoto(string photoId)
        {
            var day = EnsureDay();
            var on = Session.TogglePhotoCaptured(photoId, day);
            var p = Session.Data.FindPhoto(photoId);
            Toast((on ? "Captured · " : "Unchecked · ") + (p?.Description ?? photoId));
        }
    }
}

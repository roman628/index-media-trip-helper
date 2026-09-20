using System;
using System.Collections.Generic;
using System.Linq;
using MediaTrip.Model;
using MediaTrip.Persistence;
using MediaTrip.Session;
using MediaTrip.UI.Debugging;
using MediaTrip.UI.Docs;
using MediaTrip.UI.Shell;
using MediaTrip.UI.Transfer;
using MediaTrip.UI.ViewModels;
using UnityEngine;
using UnityEngine.UIElements;

namespace MediaTrip.UI
{
    /// <summary>
    /// The app: owns the open TripSession (null while only the library is showing), the UI
    /// state, the theme, the overlay layer and the render loop. Screens are rebuilt from state
    /// on every render and pick their layout from <see cref="Layout"/>. Text fields that edit
    /// the trip use <see cref="Edit"/> so typing does not trigger a rebuild; the focused field
    /// and every named scroll position are restored after one.
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
        public VisualElement Root { get; private set; }
        public LayoutInfo Layout { get; private set; }
        public event Action<LayoutInfo> LayoutChanged;
        public bool HasTrip => Session != null;
        /// <summary>Fixes the points-per-pixel factor instead of inferring it (used when checking layouts at a given size).</summary>
        public float? ScaleOverride;

        /// <summary>Set by the visible outliner: handles the keyboard's structural keys. Returns true if handled.</summary>
        public Func<string, bool> AccessoryAction;
        /// <summary>Set by the outline editor: handles keys typed in a chapter ("oc:id") or section ("os:id") name field. Arguments are the field name and the action.</summary>
        public Func<string, string, bool> NameKeyAction;

        private UIDocument _doc;
        private VisualElement _bodyHost, _popupLayer, _modalLayer, _toastHost;
        private TripAutosaveBehaviour _autosave;
        private int _suppress;
        private bool _renderScheduled;
        private Label _toast;
        private IVisualElementScheduledItem _toastTimer;
        private VisualElement _sheet;
        private VisualElement _popup;
        private VisualElement _popupAnchor;
        private IVisualElementScheduledItem _popupCloser;
        private bool _popupDismiss;
        private readonly Dictionary<string, Vector2> _scroll = new Dictionary<string, Vector2>();
        private PanelSettings _sharedPanel;
        private int _debugTaps;
        private float _debugTapAt;

        private void OnEnable()
        {
            _doc = GetComponent<UIDocument>();
            // The scale is set at run time from the screen. Work on a copy so the asset in the
            // project is never rewritten with whatever size the Game view happened to have.
            if (_doc.panelSettings != null && _sharedPanel == null)
            {
                _sharedPanel = _doc.panelSettings;
                var copy = Instantiate(_sharedPanel);
                copy.name = _sharedPanel.name + " (runtime)";
                _doc.panelSettings = copy;
            }
            ApplyPanelScale();
            var root = _doc.rootVisualElement;
            Root = root.Q("app") ?? root;
            _bodyHost = Root.Q("body-host");
            _popupLayer = Root.Q("popup-layer");
            _modalLayer = Root.Q("modal-layer");
            _toastHost = Root.Q("toast-host");
            if (_bodyHost == null || _popupLayer == null || _modalLayer == null)
            {
                Debug.LogError("App.uxml is missing its hosts; reimport Assets/UI and run Media Trip > Set Up Scene.");
                return;
            }
            var app = Resources.Load<StyleSheet>("Styles/App");
            if (app != null && !Root.styleSheets.Contains(app)) Root.styleSheets.Add(app);
            Theme = new ThemeManager(Root);
            Theme.Changed += ScheduleRender;
            Transfer = new TripTransfer(this);
            NativeBridge.Ensure();
            Shortcuts.Attach(this, Root);
            Root.RegisterCallback<GeometryChangedEvent>(_ => ApplyLayout());
            Root.RegisterCallback<PointerDownEvent>(OnRootPointerDown, TrickleDown.TrickleDown);
            ApplyLayout();
            Boot();
        }

        private void OnDisable()
        {
            CloseTrip();
        }

        private void Update()
        {
            ApplyPanelScale();
        }

        private void Boot()
        {
            try
            {
                var last = PlayerPrefs.GetString(PrefLastTrip, null);
                var pick = TripLibrary.ListTrips().FirstOrDefault(t => t.TripId == last && t.LoadError == null);
                if (pick != null) OpenTrip(pick.TripId);
                else Render();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                _bodyHost.Clear();
                _bodyHost.Add(U.Text("Could not open the trip library: " + ex.Message, "h3 bad-text").Pad(24));
            }
        }

        // ------------------------------------------------------------------ points, orientation, layout kind

        /// <summary>The panel is laid out in points. Pixels per point follow the screen (or the render target) size.</summary>
        private void ApplyPanelScale()
        {
            var ps = _doc != null ? _doc.panelSettings : null;
            if (ps == null) return;
            int w = ps.targetTexture != null ? ps.targetTexture.width : UnityEngine.Screen.width;
            int h = ps.targetTexture != null ? ps.targetTexture.height : UnityEngine.Screen.height;
            var scale = ScaleOverride ?? PanelScale.For(w, h, UnityEngine.Screen.dpi, Application.isMobilePlatform);
            if (ps.scaleMode != PanelScaleMode.ConstantPixelSize) ps.scaleMode = PanelScaleMode.ConstantPixelSize;
            if (!Mathf.Approximately(ps.scale, scale)) ps.scale = scale;
        }

        private void ApplyLayout()
        {
            var w = Root.resolvedStyle.width;
            var h = Root.resolvedStyle.height;
            if (float.IsNaN(w) || float.IsNaN(h) || w <= 0 || h <= 0) return;
            var info = new LayoutInfo(w, h);
            var changed = !info.SameAs(Layout);
            var resized = !Mathf.Approximately(info.Width, Layout.Width) || !Mathf.Approximately(info.Height, Layout.Height);
            Layout = info;
            foreach (var c in LayoutInfo.AllClasses) Root.RemoveFromClassList(c);
            foreach (var c in info.Classes()) Root.AddToClassList(c);
            if (changed) LayoutChanged?.Invoke(info);
            if (changed || resized) ScheduleRender();
        }

        // ------------------------------------------------------------------ trip lifecycle

        public void OpenTrip(string tripId, Screen start = Screen.Summary)
        {
            CloseTrip();
            Session = TripSession.OpenFromLibrary(tripId);
            _autosave = TripAutosaveBehaviour.Attach(Session);
            Session.Autosaver.SaveFailed += ex => Toast("Save failed: " + ex.Message);
            Session.Changed += OnSessionChanged;
            PlayerPrefs.SetString(PrefLastTrip, tripId);
            PlayerPrefs.Save();

            State = new AppState { Screen = start };
            var t = Session.Data.Trip;
            State.SM.Day = Fmt.TodayOrLast(t, DateTime.Now)?.Id;
            State.OL.Book = t.Books.FirstOrDefault()?.Id;
            _sheet = null;
            _scroll.Clear();
            Render();
        }

        /// <summary>Close the open trip; the library shows until another is opened.</summary>
        public void CloseTrip()
        {
            if (Session == null) return;
            Session.Changed -= OnSessionChanged;
            if (_autosave != null) { _autosave.Detach(); _autosave = null; }
            Session.Dispose();
            Session = null;
            Validation = null;
            State = new AppState();
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
            if (_bodyHost == null) return;
            AccessoryAction = null;
            NameKeyAction = null;
            ClosePopup();
            SaveScroll();
            try
            {
                _bodyHost.Clear();
                _modalLayer.Clear();
                if (Session == null) State.Screen = Screen.Library;
                _bodyHost.Add(BuildScreen());
                var overlay = BuildOverlay();
                if (overlay != null) { _modalLayer.Add(overlay); _modalLayer.pickingMode = PickingMode.Position; }
                else _modalLayer.pickingMode = PickingMode.Ignore;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                _bodyHost.Clear();
                _bodyHost.Add(U.Col(U.H2("Something went wrong drawing this screen"), U.Sub(ex.Message),
                    U.Btn("Trips", () => { State = new AppState { Screen = Screen.Library }; Render(); }, "mt16")).Pad(24));
            }
            RestoreScroll();
            RestoreFocus();
        }

        private VisualElement BuildScreen()
        {
            switch (State.Screen)
            {
                case Screen.Library: return LibraryScreen.Build(this);
                case Screen.Trip: return TripScreen.Build(this);
                case Screen.ShotList: return ShotListScreen.Build(this);
                case Screen.Outlines: return OutlinesScreen.Build(this);
                case Screen.Covers: return CoversScreen.Build(this);
                case Screen.Summary: return SummaryScreen.Build(this);
                case Screen.Film: return FilmScreen.Build(this);
                case Screen.Json: return JsonScreen.Build(this);
                default: return U.Text("?");
            }
        }

        private VisualElement BuildOverlay()
        {
            if (_sheet != null) return _sheet;
            if (Session == null) return null;
            if (State.Search != null) return SearchSheet.Build(this);
            if (State.Shortcuts) return ShortcutsSheet.Build(this);
            if (State.Menu) return AppShell.Menu(this);
            if (State.Paste != null) return PasteSheet.Build(this);
            switch (State.Screen)
            {
                case Screen.ShotList: return ShotListScreen.Overlay(this);
                case Screen.Outlines: return OutlinesScreen.Overlay(this);
                case Screen.Covers: return CoversScreen.Overlay(this);
                case Screen.Summary: return SummaryScreen.Overlay(this);
                case Screen.Film: return FilmScreen.Overlay(this);
                default: return null;
            }
        }

        /// <summary>Close whatever is on top: popup, sheet, search, menu, a screen's own sheet. Returns false when nothing was open.</summary>
        public bool CloseTop()
        {
            if (HasPopup) { ClosePopup(); return true; }
            if (_sheet != null) { CloseSheet(); return true; }
            var st = State;
            bool closed = true;
            if (st.Search != null) st.Search = null;
            else if (st.Shortcuts) st.Shortcuts = false;
            else if (st.Menu) st.Menu = false;
            else if (st.Paste != null) st.Paste = null;
            else if (st.Screen == Screen.ShotList && st.SL.Amend != null) st.SL.Amend = null;
            else if (st.Screen == Screen.Film && st.Film.PlanOpen) st.Film.PlanOpen = false;
            else if (st.Screen == Screen.Covers && st.CV.SlotKey != null) st.CV.SlotKey = null;
            else if (st.Screen == Screen.Summary && st.SM.Batch != null) st.SM.Batch = null;
            else if (st.Screen == Screen.Summary && (st.SM.OpenCapture != null || st.SM.OpenPhoto != null)) { st.SM.OpenCapture = null; st.SM.OpenPhoto = null; }
            else if (st.Screen == Screen.Outlines && st.OL.PlaceAt != null) { st.OL.PlaceAt = null; st.OL.PlaceQuery = ""; }
            else closed = false;
            if (closed) { st.Focus = null; Render(); }
            return closed;
        }

        // ------------------------------------------------------------------ overlay layer: sheets

        /// <summary>Show an ad-hoc sheet (theme picker, confirmations, import preview) in the modal layer. Survives re-renders until closed.</summary>
        public void ShowSheet(Func<VisualElement> build)
        {
            _sheet = build();
            Render();
        }

        public void CloseSheet()
        {
            _sheet = null;
            Render();
        }

        public bool HasSheet => _sheet != null;

        public VisualElement Overlay(VisualElement sheet, Action onBackdrop, string classes = "")
        {
            var overlay = new VisualElement().Cls("overlay " + classes);
            overlay.RegisterCallback<ClickEvent>(e => { if (e.target == overlay) onBackdrop?.Invoke(); });
            overlay.Add(sheet);
            return overlay;
        }

        /// <summary>
        /// A sheet sized for the screen: full-screen on the phone, a centered card elsewhere,
        /// never wider or taller than the panel. <paramref name="top"/> pins it under the header.
        /// </summary>
        public VisualElement Sheet(float width, Action onClose, bool top, params VisualElement[] children)
        {
            var sheet = new VisualElement().Cls("sheet");
            if (Layout.Phone) sheet.Cls("full");
            else
            {
                sheet.style.width = Layout.SheetWidth(width);
                sheet.style.maxHeight = Mathf.Max(200, Layout.Height - (top ? 60 : 40));
                if (top) sheet.style.marginTop = 20;
            }
            foreach (var c in children) if (c != null) sheet.Add(c);
            return Overlay(sheet, onClose, top && !Layout.Phone ? "top" : "");
        }

        public VisualElement Sheet(float width, Action onClose, params VisualElement[] children) => Sheet(width, onClose, false, children);

        /// <summary>The title row of a sheet: title, optional extra, Close.</summary>
        public VisualElement SheetHead(string title, Action onClose, VisualElement extra = null)
        {
            var t = U.H2(title).Cls("grow");
            var row = U.Row(t, extra, U.Btn("Close", onClose, "sm ml8")).Mb(14);
            row.style.flexShrink = 0;
            return row;
        }

        // ------------------------------------------------------------------ overlay layer: anchored popups

        /// <summary>
        /// Show a popup (suggestion list, small menu) in the top-level popup layer, positioned
        /// under <paramref name="anchor"/>, or above it when there is no room below. One at a
        /// time; it closes on the next render, on <see cref="ClosePopup"/>, or shortly after the
        /// anchored field loses focus.
        /// </summary>
        public VisualElement ShowPopup(VisualElement anchor, VisualElement content, float width, float maxHeight = 420, float offsetX = 0, bool dismissOnOutsideTap = false)
        {
            ClosePopup();
            _popupDismiss = dismissOnOutsideTap;
            if (anchor == null || content == null) return null;
            content.style.position = Position.Absolute;
            content.style.width = Mathf.Min(width, Layout.Width - 16);
            content.style.maxHeight = maxHeight;
            _popup = content;
            _popupAnchor = anchor;
            _popupLayer.Add(content);
            _popupLayer.pickingMode = PickingMode.Ignore;
            PositionPopup(offsetX);
            content.RegisterCallback<GeometryChangedEvent>(_ => PositionPopup(offsetX));
            anchor.RegisterCallback<GeometryChangedEvent>(_ => PositionPopup(offsetX));
            return content;
        }

        private void PositionPopup(float offsetX)
        {
            if (_popup == null || _popupAnchor == null) return;
            var a = _popupAnchor.worldBound;
            var l = _popupLayer.worldBound;
            var pw = _popup.resolvedStyle.width > 0 ? _popup.resolvedStyle.width : _popup.style.width.value.value;
            var ph = _popup.resolvedStyle.height;
            var left = a.xMin - l.xMin + offsetX;
            _popup.style.left = Mathf.Clamp(left, 8, Mathf.Max(8, l.width - pw - 8));
            var below = a.yMax - l.yMin + 4;
            var above = a.yMin - l.yMin - 4 - ph;
            bool fitsBelow = float.IsNaN(ph) || below + ph <= l.height - 8;
            _popup.style.top = fitsBelow || above < 8 ? below : above;
        }

        /// <summary>A menu-style popup closes when anything outside it is pressed.</summary>
        private void OnRootPointerDown(PointerDownEvent e)
        {
            if (_popup == null || !_popupDismiss) return;
            for (var v = e.target as VisualElement; v != null; v = v.parent) if (v == _popup) return;
            ClosePopup();
        }

        public void ClosePopup()
        {
            _popupDismiss = false;
            _popupCloser?.Pause();
            _popupCloser = null;
            if (_popup != null) _popup.RemoveFromHierarchy();
            _popup = null;
            _popupAnchor = null;
        }

        public bool HasPopup => _popup != null;

        /// <summary>Close the popup a moment after the field loses focus (long enough for a tap on a popup row to land first).</summary>
        public void ClosePopupWhenBlurred(VisualElement field)
        {
            field.RegisterCallback<FocusOutEvent>(_ =>
            {
                _popupCloser?.Pause();
                _popupCloser = Root.schedule.Execute(() => { if (_popupAnchor == field || _popupAnchor?.parent == field) ClosePopup(); }).StartingIn(220);
            });
        }

        // ------------------------------------------------------------------ focus and scroll survive a rebuild

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
            // A field that was just added sits below what was on screen; bring it into view
            // after the old scroll position has been put back, or the list jumps away from it.
            tf.schedule.Execute(() => ScrollIntoView(tf)).StartingIn(60);
        }

        /// <summary>Scroll the nearest ScrollView so the element is visible, with a little room around it.</summary>
        public static void ScrollIntoView(VisualElement e)
        {
            if (e?.panel == null) return;
            var sv = e.GetFirstAncestorOfType<ScrollView>();
            if (sv == null) return;
            var view = sv.contentViewport.worldBound;
            var box = e.worldBound;
            float dy = 0;
            if (box.yMin < view.yMin + 12) dy = box.yMin - view.yMin - 12;
            else if (box.yMax > view.yMax - 12) dy = box.yMax - view.yMax + 12;
            if (Mathf.Abs(dy) < 1) return;
            var max = Mathf.Max(0, sv.contentContainer.layout.height - view.height);
            sv.scrollOffset = new Vector2(sv.scrollOffset.x, Mathf.Clamp(sv.scrollOffset.y + dy, 0, max));
        }

        /// <summary>After the next layout, scroll to the element with this name (a row opened from search or from another screen).</summary>
        public void ScrollToNamed(string elementName)
        {
            if (string.IsNullOrEmpty(elementName)) return;
            Root.schedule.Execute(() => { var e = Root.Q(elementName); if (e != null) ScrollIntoView(e); }).StartingIn(80);
        }

        /// <summary>Re-render and put the caret back into the named field.</summary>
        public void RenderKeepFocus(string fieldName)
        {
            State.Focus = fieldName;
            Render();
        }

        /// <summary>The name of the text field that has the caret, or "".</summary>
        public string FocusedFieldName
        {
            get
            {
                var f = Root?.panel?.focusController?.focusedElement as VisualElement;
                while (f != null && !(f is TextField)) f = f.parent;
                return f?.name ?? "";
            }
        }

        private void SaveScroll()
        {
            if (Root == null) return;
            Root.Query<ScrollView>().ForEach(sv => { if (!string.IsNullOrEmpty(sv.name)) _scroll[sv.name] = sv.scrollOffset; });
        }

        private void RestoreScroll()
        {
            Root.Query<ScrollView>().ForEach(sv =>
            {
                if (string.IsNullOrEmpty(sv.name) || !_scroll.TryGetValue(sv.name, out var offset) || offset == Vector2.zero) return;
                EventCallback<GeometryChangedEvent> once = null;
                once = _ => { sv.contentContainer.UnregisterCallback(once); sv.scrollOffset = offset; };
                sv.contentContainer.RegisterCallback(once);
                sv.schedule.Execute(() => sv.scrollOffset = offset);
            });
        }

        /// <summary>Forget a scroll position so the list opens at the top next time.</summary>
        public void ResetScroll(string name) => _scroll.Remove(name);

        // ------------------------------------------------------------------ navigation

        public void Nav(Screen s)
        {
            if (Session == null) s = Screen.Library;
            var st = State;
            st.Screen = s;
            st.Edit = false;
            st.Menu = false;
            st.Search = null;
            st.Focus = null;
            st.Paste = null;
            st.Shortcuts = false;
            st.OL.PlaceAt = null;
            st.SL.OriginalGatePassed = false;
            _sheet = null;
            if (s != Screen.ShotList) st.SL.Amend = null;
            Session?.EndEditSession();
            Render();
        }

        /// <summary>
        /// The header's Edit / Done. Edit changes the document; the normal state annotates it.
        /// Entering Edit on the original shot list once media exists asks first whether this is
        /// a fix to what was typed or a change of plan (see <see cref="ShotListScreen.OriginalGate"/>).
        /// </summary>
        public void ToggleEdit()
        {
            var st = State;
            st.Focus = null;
            if (st.Edit)
            {
                st.Edit = false;
                st.SL.OriginalGatePassed = false;
                st.Trip.NewPerson = null; st.Trip.MemberBook = null;
                Session?.EndEditSession();
                Render();
                return;
            }
            if (st.Screen == Screen.ShotList && st.SL.View == ShotListMode.Original && Session != null && Session.HasFieldMedia && !st.SL.OriginalGatePassed)
            {
                ShotListScreen.OriginalGate(this);
                return;
            }
            st.Edit = true;
            st.OL.PlaceAt = null;
            Render();
        }

        /// <summary>Open the filming screen, for a plan item or blank. It returns to where it was opened from.</summary>
        public void StartFilm(string planItemId, string dayId = null)
        {
            var f = State.Film;
            f.Draft = planItemId != null ? CaptureDraft.ForItem(Session, planItemId) : new CaptureDraft();
            f.DayId = dayId;
            f.PlanOpen = false;
            f.Return = State.Screen == Screen.Film ? Screen.Summary : State.Screen;
            Nav(Screen.Film);
            if (planItemId == null) RenderKeepFocus("film.title");
        }

        public void EditCapture(string captureId)
        {
            var c = Session.Data.FindCapture(captureId);
            if (c == null) return;
            var f = State.Film;
            f.Draft = CaptureDraft.ForCapture(Session, c);
            f.DayId = c.DayId;
            f.PlanOpen = false;
            f.Return = Screen.Summary;
            State.SM.OpenCapture = null;
            Nav(Screen.Film);
        }

        public void OpenVideo(string planItemId)
        {
            Nav(Screen.ShotList);
            State.SL.View = ShotListMode.Working;
            var item = Session.Plan.FindItem(planItemId);
            if (item != null && (item.IsDropped || item.IsSuperseded)) State.SL.View = ShotListMode.Original;
            State.SL.HideDone = false;
            State.SL.Expanded.Add(planItemId);
            Render();
            ScrollToNamed("row:" + planItemId);
        }

        /// <summary>Open Outlines on a book with a chapter (and optionally one of its sections) expanded.</summary>
        public void OpenChapter(string bookId, string chapterId, string sectionId = null)
        {
            Nav(Screen.Outlines);
            State.OL.Book = bookId;
            if (chapterId != null) State.OL.OpenChapters.Add(chapterId);
            if (sectionId != null) State.OL.OpenSections.Add(sectionId);
            Render();
            ScrollToNamed(sectionId != null ? "sec:" + sectionId : "ch:" + chapterId);
        }

        public void OpenSection(string bookId, string sectionId)
        {
            var ch = Session.Outline.ChapterOfSection(bookId, sectionId);
            OpenChapter(bookId, ch?.Id, sectionId);
        }

        public void OpenSummary(string captureId)
        {
            var c = Session.Data.FindCapture(captureId);
            Nav(Screen.Summary);
            if (c != null) { State.SM.Day = c.DayId; State.SM.OpenCapture = c.Id; }
            Render();
        }

        /// <summary>Five quick taps on the library title open the JSON inspector.</summary>
        public void DebugTap()
        {
            var now = Time.realtimeSinceStartup;
            _debugTaps = now - _debugTapAt < 1.5f ? _debugTaps + 1 : 1;
            _debugTapAt = now;
            if (_debugTaps < 5) return;
            _debugTaps = 0;
            OpenDebug();
        }

        public void OpenDebug()
        {
            if (Session == null) { Toast("Open a trip first"); return; }
            Nav(Screen.Json);
        }

        // ------------------------------------------------------------------ toast

        public void Toast(string message)
        {
            if (_toastHost == null) return;
            _toastTimer?.Pause();
            _toast?.RemoveFromHierarchy();
            _toast = U.Text(message, "toast");
            _toast.style.maxWidth = Mathf.Max(200, Layout.Width - 32);
            _toastHost.Add(_toast);
            _toastTimer = _toastHost.schedule.Execute(() => { _toast?.RemoveFromHierarchy(); _toast = null; }).StartingIn(2400);
        }

        // ------------------------------------------------------------------ days

        /// <summary>The day something logged right now belongs to; created when the trip is under way and has no day for today.</summary>
        public string DayForNow()
        {
            var r = DayPicker.For(Session.Data.Trip, DateTime.Now);
            if (r.Day != null) return r.Day.Id;
            var d = Session.AddDay(r.CreateDate, r.CreateLabel);
            return d.Id;
        }

        public string DayLabel(string dayId)
        {
            var d = Session?.Data.FindDay(dayId);
            return d == null ? "today" : (string.IsNullOrEmpty(d.Label) ? U.FmtDate(d.Date) : d.Label);
        }
    }
}

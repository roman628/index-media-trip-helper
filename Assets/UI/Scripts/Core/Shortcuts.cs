using UnityEngine;
using UnityEngine.UIElements;

namespace MediaTrip.UI
{
    /// <summary>
    /// Keyboard accelerators. Every one of these is a second way to reach an action that also
    /// has a tap target; nothing here is keyboard-only except moving a bullet up or down.
    /// Cmd on the iPad and Ctrl on the laptop are treated the same.
    /// </summary>
    public static class Shortcuts
    {
        /// <summary>True when a physical keyboard is present (desktop always; iPad with a Bluetooth keyboard).</summary>
        public static bool HardwareKeyboardPresent
        {
            get
            {
#if UNITY_EDITOR || UNITY_STANDALONE
                return true;
#elif ENABLE_INPUT_SYSTEM
                return UnityEngine.InputSystem.Keyboard.current != null;
#else
                return false;
#endif
            }
        }

        public static string Meta => Application.platform == RuntimePlatform.OSXEditor || Application.platform == RuntimePlatform.OSXPlayer || Application.platform == RuntimePlatform.IPhonePlayer ? "⌘" : "Ctrl";

        public static void Attach(AppController app, VisualElement root)
        {
            root.RegisterCallback<KeyDownEvent>(e => OnKey(app, e), TrickleDown.TrickleDown);
            root.RegisterCallback<NavigationMoveEvent>(e => OnNavMove(app, e), TrickleDown.TrickleDown);
        }

        private static bool IsMeta(KeyDownEvent e) => e.ctrlKey || e.commandKey;

        private static TextField FocusedField(VisualElement root)
        {
            var f = root.panel?.focusController?.focusedElement as VisualElement;
            while (f != null && !(f is TextField)) f = f.parent;
            return f as TextField;
        }

        private static void Consume(EventBase e)
        {
            e.StopImmediatePropagation();
        }

        private static void OnNavMove(AppController app, NavigationMoveEvent e)
        {
            // Tab inside an outliner row is indent/outdent, not focus navigation.
            var tf = FocusedField(app.Root);
            var n = tf?.name ?? "";
            if (n.StartsWith("os:") && e.direction == NavigationMoveEvent.Direction.Next && app.NameKeyAction != null)
            {
                // Tab in a section name goes into its bullets rather than to the next control.
                if (app.NameKeyAction(n, "tab")) Consume(e);
                return;
            }
            if (tf == null || !n.StartsWith("node:")) return;
            if (e.direction == NavigationMoveEvent.Direction.Next || e.direction == NavigationMoveEvent.Direction.Previous)
            {
                if (app.AccessoryAction != null && app.AccessoryAction(e.direction == NavigationMoveEvent.Direction.Next ? "in" : "out"))
                    Consume(e);
            }
        }

        private static void OnKey(AppController app, KeyDownEvent e)
        {
            if (e.keyCode == KeyCode.Escape)
            {
                if (app.CloseTop()) { Consume(e); return; }
                var focused = FocusedField(app.Root);
                if (focused != null) { focused.Blur(); app.State.Focus = null; Consume(e); }
                return;
            }
            if (app.Session == null) return;
            var meta = IsMeta(e);
            var tf = FocusedField(app.Root);
            var inNode = (tf?.name ?? "").StartsWith("node:");

            if (meta && e.keyCode == KeyCode.F) { app.State.Search = app.State.Search ?? ""; app.RenderKeepFocus("search"); Consume(e); return; }
            if (meta && e.keyCode == KeyCode.S) { app.Session.SaveNow(); app.Toast("Saved"); Consume(e); return; }
            if (meta && e.shiftKey && e.keyCode == KeyCode.J) { app.OpenDebug(); Consume(e); return; }
            if (meta && e.keyCode == KeyCode.E && Screens.HasEdit(app.State.Screen)) { app.ToggleEdit(); Consume(e); return; }
            if (meta && !e.shiftKey && e.keyCode >= KeyCode.Alpha1 && e.keyCode <= KeyCode.Alpha5)
            {
                app.Nav(Screens.Tabs[e.keyCode - KeyCode.Alpha1]);
                Consume(e);
                return;
            }

            if (meta && (e.keyCode == KeyCode.RightBracket || e.keyCode == KeyCode.LeftBracket))
            {
                var open = e.keyCode == KeyCode.RightBracket;
                if (app.State.Screen == Screen.ShotList) { MediaTrip.UI.Docs.ShotListScreen.ExpandAll(app, open); Consume(e); return; }
                if (app.State.Screen == Screen.Outlines) { MediaTrip.UI.Docs.OutlinesScreen.ExpandAll(app, open); Consume(e); return; }
            }

            // ---- chapter and section names in the outline editor: the whole outline can be typed from the keyboard
            var fieldName = tf?.name ?? "";
            if (app.NameKeyAction != null && (fieldName.StartsWith("oc:") || fieldName.StartsWith("os:")))
            {
                string nameAct = null;
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) nameAct = meta ? "chapter" : "enter";
                else if (e.keyCode == KeyCode.Tab && !e.shiftKey) nameAct = "tab";
                else if (e.altKey && e.keyCode == KeyCode.UpArrow) nameAct = "moveup";
                else if (e.altKey && e.keyCode == KeyCode.DownArrow) nameAct = "movedown";
                else if (e.keyCode == KeyCode.UpArrow) nameAct = "prev";
                else if (e.keyCode == KeyCode.DownArrow) nameAct = "next";
                else if (e.keyCode == KeyCode.Backspace && string.IsNullOrEmpty(tf.value)) nameAct = "del";
                if (nameAct != null && app.NameKeyAction(fieldName, nameAct)) { Consume(e); return; }
            }

            // ---- the outliner's structural keys
            if (app.AccessoryAction != null && inNode)
            {
                string act = null;
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) act = e.shiftKey ? "child" : "sib";
                else if (e.keyCode == KeyCode.Tab) act = e.shiftKey ? "out" : "in";
                else if (e.altKey && e.keyCode == KeyCode.UpArrow) act = "up";
                else if (e.altKey && e.keyCode == KeyCode.DownArrow) act = "down";
                else if (meta && e.keyCode == KeyCode.Backspace) act = "del";
                else if (e.keyCode == KeyCode.Backspace && string.IsNullOrEmpty(tf.value)) act = "del";
                else if (e.keyCode == KeyCode.UpArrow && !e.altKey) act = "prev";
                else if (e.keyCode == KeyCode.DownArrow && !e.altKey) act = "next";
                if (act != null && app.AccessoryAction(act)) { Consume(e); return; }
            }
        }
    }
}

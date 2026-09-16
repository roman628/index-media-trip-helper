using UnityEngine;
using UnityEngine.UIElements;

namespace MediaTrip.UI
{
    /// <summary>
    /// Keyboard accelerators. Every one of these is a second way to reach an action that also
    /// has a tap target; nothing here is keyboard-only. Cmd on the iPad and Ctrl on the laptop
    /// are treated the same.
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
            if (tf == null || !(tf.name ?? "").StartsWith("node:")) return;
            if (e.direction == NavigationMoveEvent.Direction.Next || e.direction == NavigationMoveEvent.Direction.Previous)
            {
                if (app.AccessoryAction != null && app.AccessoryAction(e.direction == NavigationMoveEvent.Direction.Next ? "in" : "out"))
                    Consume(e);
            }
        }

        private static void OnKey(AppController app, KeyDownEvent e)
        {
            if (app.Session == null) return;
            var meta = IsMeta(e);
            var state = app.State;
            var tf = FocusedField(app.Root);
            var fieldName = tf?.name ?? "";
            var inNode = fieldName.StartsWith("node:");

            // ---- everywhere
            if (meta && e.keyCode == KeyCode.Slash) { state.Help = !state.Help; app.Render(); Consume(e); return; }
            if (meta && e.keyCode == KeyCode.F) { state.Focus = "q"; app.Render(); Consume(e); return; }
            if (meta && e.keyCode == KeyCode.S) { app.Session.SaveNow(); app.Toast("Saved · autosave is on anyway"); Consume(e); return; }
            if (meta && state.Mode == Mode.Author && e.keyCode >= KeyCode.Alpha1 && e.keyCode <= KeyCode.Alpha7)
            {
                int idx = e.keyCode - KeyCode.Alpha1;
                app.Nav(Screens.AuthorTabs[idx]);
                Consume(e);
                return;
            }
            if (e.keyCode == KeyCode.Escape)
            {
                if (state.Help) { state.Help = false; app.Render(); Consume(e); return; }
                if (app.HasSheet) { app.CloseSheet(); Consume(e); return; }
                if (state.Amend != null) { state.Amend = null; app.Render(); Consume(e); return; }
                if (state.DetailItemId != null) { state.DetailItemId = null; app.Render(); Consume(e); return; }
                if (state.SL.Paste != null) { state.SL.Paste = null; app.Render(); Consume(e); return; }
                if (state.OL.Paste != null) { state.OL.Paste = null; app.Render(); Consume(e); return; }
                if (state.IO.PasteOpen) { state.IO.PasteOpen = false; app.Render(); Consume(e); return; }
                if (state.AmendEdit != null) { state.AmendEdit = null; app.Render(); Consume(e); return; }
                if (tf != null) { tf.Blur(); state.Focus = null; Consume(e); return; }
                if (!string.IsNullOrEmpty(state.Query)) { state.Query = ""; app.Render(); Consume(e); return; }
                return;
            }

            // ---- outliner and list accelerators (the accessory bar's keyboard twins)
            if (app.AccessoryAction != null)
            {
                string act = null;
                bool inTextOfOutliner = inNode;
                bool listContext = tf == null || inNode || fieldName.StartsWith("video:") || fieldName.StartsWith("chapter:") || fieldName.StartsWith("photo:") || fieldName.StartsWith("os:") || fieldName.StartsWith("oc:");
                if (!listContext) return;
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) act = e.shiftKey ? "child" : "sib";
                else if (e.keyCode == KeyCode.Tab && inTextOfOutliner) act = e.shiftKey ? "out" : "in";
                else if (e.altKey && e.keyCode == KeyCode.UpArrow) act = "up";
                else if (e.altKey && e.keyCode == KeyCode.DownArrow) act = "down";
                else if (meta && e.keyCode == KeyCode.D) act = "dup";
                else if (meta && e.keyCode == KeyCode.Backspace) act = "del";
                else if (e.keyCode == KeyCode.Backspace && inTextOfOutliner && string.IsNullOrEmpty(tf.value)) act = "del";
                else if (meta && e.shiftKey && e.keyCode == KeyCode.V) act = "paste";
                else if ((e.keyCode == KeyCode.UpArrow || e.keyCode == KeyCode.DownArrow) && !e.altKey && (inNode || tf == null)) act = e.keyCode == KeyCode.UpArrow ? "prev" : "next";
                if (act != null && app.AccessoryAction(act)) { Consume(e); return; }
            }

            if (app.ScreenKeys != null && tf == null && app.ScreenKeys(e)) { Consume(e); }
        }
    }
}

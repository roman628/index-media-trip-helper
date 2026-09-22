using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace MediaTrip.UI.Shell
{
    /// <summary>
    /// One input for picking an existing thing or making a new one. Typing fuzzy-searches; the
    /// matches drop down under the field, each saying what it is; when nothing matches
    /// outright, the last row creates what was typed. Enter takes the first row. Used wherever
    /// the app used to show a long list: photos in Covers, media in Outlines, SMEs in Filming.
    /// </summary>
    public static class PickerField
    {
        public sealed class Row
        {
            public VisualElement Lead;
            public string Title;
            public string Sub;
            /// <summary>Drawn in the accent color: the "create new" row.</summary>
            public bool IsCreate;
            public Action Pick;
        }

        /// <param name="rows">Matches for the text, best first, create rows last. An empty list closes the dropdown.</param>
        public static TextField Build(AppController app, string name, string value, string placeholder, Action<string> onText, Func<string, List<Row>> rows, string classes = "grow")
        {
            var tf = U.Input(value, placeholder, null, false, classes);
            tf.name = name;
            app.ClosePopupWhenBlurred(tf);

            List<Row> current = new List<Row>();
            void Show(string text)
            {
                current = string.IsNullOrWhiteSpace(text) ? new List<Row>() : rows(text) ?? new List<Row>();
                if (current.Count == 0) { app.ClosePopup(); return; }
                var box = new VisualElement().Cls("sugg");
                var list = U.Scroll();
                foreach (var r in current)
                {
                    var row = r;
                    var text2 = U.Col(U.Text(U.Esc(r.Title), "bold" + (r.IsCreate ? " accent" : "")).Cls("wraptext"), string.IsNullOrEmpty(r.Sub) ? null : U.Sub(U.Esc(r.Sub))).Cls("grow");
                    list.Add(U.Tap(() => { app.ClosePopup(); row.Pick?.Invoke(); }, "sugg-row", r.Lead, text2));
                }
                box.Add(list);
                var w = tf.resolvedStyle.width;
                // tall enough that the "new" rows at the end are seen without scrolling; it flips above the field when there is no room below
                app.ShowPopup(tf, box, float.IsNaN(w) || w < 260 ? Mathf.Min(420, app.Layout.Width - 24) : w, 470);
            }

            tf.RegisterValueChangedCallback(e => { onText?.Invoke(e.newValue); Show(e.newValue); });
            tf.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode != KeyCode.Return && e.keyCode != KeyCode.KeypadEnter) return;
                if (current.Count == 0) current = string.IsNullOrWhiteSpace(tf.value) ? current : rows(tf.value) ?? current;
                if (current.Count == 0) return;
                e.StopPropagation();
                app.ClosePopup();
                current[0].Pick?.Invoke();
            }, TrickleDown.TrickleDown);
            return tf;
        }
    }
}

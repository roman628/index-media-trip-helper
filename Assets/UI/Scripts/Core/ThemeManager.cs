using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace MediaTrip.UI
{
    /// <summary>
    /// One stylesheet per theme variant under Resources/Themes, containing only the sixteen
    /// variables. Switching a theme = swapping that one stylesheet on the root element.
    /// Beacon is the "sun" mode: a one-tap toggle that remembers the theme it replaced.
    /// </summary>
    public sealed class ThemeManager
    {
        public static readonly string[] Names = { "Ledger", "Graphite", "Harbor", "Moss", "Beacon" };
        public static readonly Dictionary<string, string> Notes = new Dictionary<string, string>
        {
            ["Ledger"] = "baseline · warm paper, rust",
            ["Graphite"] = "neutral greys, violet accent",
            ["Harbor"] = "cool slate, blue",
            ["Moss"] = "olive neutrals, teal accent",
            ["Beacon"] = "sun mode · black/white, 3 px borders, yellow",
        };

        private const string PrefTheme = "mt.theme";
        private const string PrefDark = "mt.dark";
        private const string PrefSunPrev = "mt.sunPrev";

        private readonly VisualElement _root;
        private StyleSheet _current;

        public string Theme { get; private set; } = "Ledger";
        public bool Dark { get; private set; }
        public bool IsSun => Theme == "Beacon";
        public event Action Changed;

        public ThemeManager(VisualElement root)
        {
            _root = root;
            Theme = PlayerPrefs.GetString(PrefTheme, "Ledger");
            if (Array.IndexOf(Names, Theme) < 0) Theme = "Ledger";
            Dark = PlayerPrefs.GetInt(PrefDark, 0) == 1;
            Apply();
        }

        public void Set(string theme, bool dark)
        {
            if (Array.IndexOf(Names, theme) < 0) theme = "Ledger";
            Theme = theme;
            Dark = dark;
            PlayerPrefs.SetString(PrefTheme, Theme);
            PlayerPrefs.SetInt(PrefDark, Dark ? 1 : 0);
            PlayerPrefs.Save();
            Apply();
            Changed?.Invoke();
        }

        public void ToggleDark() => Set(Theme, !Dark);

        /// <summary>One tap: Beacon on (remembering the previous theme) or back to it.</summary>
        public void ToggleSun()
        {
            if (IsSun)
            {
                var prev = PlayerPrefs.GetString(PrefSunPrev, "Ledger");
                Set(prev == "Beacon" ? "Ledger" : prev, Dark);
            }
            else
            {
                PlayerPrefs.SetString(PrefSunPrev, Theme);
                Set("Beacon", Dark);
            }
        }

        private void Apply()
        {
            var sheet = Resources.Load<StyleSheet>("Themes/" + Theme + "-" + (Dark ? "Dark" : "Light"));
            if (sheet == null)
            {
                Debug.LogError("Theme stylesheet missing: Themes/" + Theme + "-" + (Dark ? "Dark" : "Light"));
                return;
            }
            if (_current != null && _root.styleSheets.Contains(_current)) _root.styleSheets.Remove(_current);
            _current = sheet;
            _root.styleSheets.Add(sheet);
            _root.AddToClassList("theme");
        }

        /// <summary>Read the variable values of a variant (for the theme picker's swatches). Parses the USS text, no UI needed.</summary>
        public static Dictionary<string, string> ReadVariables(string theme, bool dark)
        {
            var result = new Dictionary<string, string>();
            var asset = Resources.Load<TextAsset>("Themes/" + theme + "-" + (dark ? "Dark" : "Light"));
            string text = asset != null ? asset.text : null;
            if (text == null)
            {
#if UNITY_EDITOR
                var path = "Assets/UI/Resources/Themes/" + theme + "-" + (dark ? "Dark" : "Light") + ".uss";
                if (System.IO.File.Exists(path)) text = System.IO.File.ReadAllText(path);
#endif
            }
            if (text == null) return result;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (!line.StartsWith("--")) continue;
                var colon = line.IndexOf(':');
                if (colon < 0) continue;
                var key = line.Substring(2, colon - 2).Trim();
                var val = line.Substring(colon + 1).Trim().TrimEnd(';').Trim();
                result[key] = val;
            }
            return result;
        }

        public static Color ParseColor(string hex, Color fallback)
        {
            return ColorUtility.TryParseHtmlString(hex, out var c) ? c : fallback;
        }

        /// <summary>WCAG contrast ratio between two colors.</summary>
        public static float Contrast(Color a, Color b)
        {
            float L(Color c)
            {
                float f(float x) => x <= 0.03928f ? x / 12.92f : Mathf.Pow((x + 0.055f) / 1.055f, 2.4f);
                return 0.2126f * f(c.r) + 0.7152f * f(c.g) + 0.0722f * f(c.b);
            }
            float x = L(a), y = L(b);
            return (Mathf.Max(x, y) + 0.05f) / (Mathf.Min(x, y) + 0.05f);
        }
    }
}

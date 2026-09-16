using System;
using System.Collections.Generic;
using System.Globalization;
using MediaTrip.Model;
using MediaTrip.Status;
using UnityEngine;
using UnityEngine.UIElements;

namespace MediaTrip.UI
{
    /// <summary>Element factory. Every visual comes from here so classes and sizes stay consistent.</summary>
    public static class U
    {
        // ------------------------------------------------------------------ layout
        public static VisualElement Row(params VisualElement[] children) => With(new VisualElement(), "row", children);
        public static VisualElement Col(params VisualElement[] children) => With(new VisualElement(), "col", children);
        public static VisualElement Grow() { var v = new VisualElement(); v.AddToClassList("grow"); return v; }
        public static VisualElement Hr() { var v = new VisualElement(); v.AddToClassList("hr"); return v; }

        public static VisualElement With(VisualElement v, string classes, params VisualElement[] children)
        {
            foreach (var c in (classes ?? "").Split(' ')) if (c.Length > 0) v.AddToClassList(c);
            foreach (var ch in children) if (ch != null) v.Add(ch);
            return v;
        }

        public static T Cls<T>(this T v, string classes) where T : VisualElement
        {
            foreach (var c in (classes ?? "").Split(' ')) if (c.Length > 0) v.AddToClassList(c);
            return v;
        }

        public static T W<T>(this T v, float width) where T : VisualElement { v.style.width = width; v.style.flexShrink = 0; v.style.flexGrow = 0; return v; }
        public static T H<T>(this T v, float height) where T : VisualElement { v.style.height = height; return v; }
        public static T MinH<T>(this T v, float height) where T : VisualElement { v.style.minHeight = height; return v; }
        public static T Ml<T>(this T v, float px) where T : VisualElement { v.style.marginLeft = px; return v; }
        public static T Mr<T>(this T v, float px) where T : VisualElement { v.style.marginRight = px; return v; }
        public static T Mt<T>(this T v, float px) where T : VisualElement { v.style.marginTop = px; return v; }
        public static T Mb<T>(this T v, float px) where T : VisualElement { v.style.marginBottom = px; return v; }
        public static T Pad<T>(this T v, float px) where T : VisualElement { v.style.paddingLeft = px; v.style.paddingRight = px; v.style.paddingTop = px; v.style.paddingBottom = px; return v; }
        public static T Pad<T>(this T v, float vert, float horiz) where T : VisualElement { v.style.paddingLeft = horiz; v.style.paddingRight = horiz; v.style.paddingTop = vert; v.style.paddingBottom = vert; return v; }
        public static T Font<T>(this T v, float size) where T : VisualElement { v.style.fontSize = size; return v; }
        public static T Bold<T>(this T v) where T : VisualElement { v.style.unityFontStyleAndWeight = FontStyle.Bold; return v; }
        public static T Hide<T>(this T v, bool hidden = true) where T : VisualElement { v.style.display = hidden ? DisplayStyle.None : DisplayStyle.Flex; return v; }
        public static T On<T>(this T v, bool on, string cls = "on") where T : VisualElement { v.EnableInClassList(cls, on); return v; }

        public static ScrollView Scroll(params VisualElement[] children)
        {
            var sv = new ScrollView(ScrollViewMode.Vertical);
            sv.AddToClassList("scroll");
            sv.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            sv.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            sv.touchScrollBehavior = ScrollView.TouchScrollBehavior.Elastic;
            sv.mouseWheelScrollSize = 40;
            foreach (var c in children) if (c != null) sv.Add(c);
            return sv;
        }

        // ------------------------------------------------------------------ text
        public static Label Text(string text, string classes = null)
        {
            var l = new Label(text ?? "");
            l.enableRichText = true;
            if (classes != null) l.Cls(classes);
            return l;
        }

        public static Label H1(string t) => Text(t, "h1");
        public static Label H2(string t) => Text(t, "h2");
        public static Label H3(string t) => Text(t, "h3");
        public static Label Sub(string t) => Text(t, "sub");
        public static Label Eyebrow(string t) => Text((t ?? "").ToUpperInvariant(), "eyebrow");
        public static Label Lbl(string t) => Text((t ?? "").ToUpperInvariant(), "lbl");
        public static Label Body(string t) => Text(t, "body");

        public static string Esc(string s) => (s ?? "").Replace("<", "‹").Replace(">", "›");
        public static string Strike(string s) => "<s>" + Esc(s) + "</s>";

        // ------------------------------------------------------------------ buttons
        public static Button Btn(string text, Action onClick, string classes = "")
        {
            var b = new Button(onClick) { text = text ?? "" };
            b.Cls("btn " + classes);
            return b;
        }

        /// <summary>A button whose content is arbitrary elements (glyph + label, columns...).</summary>
        public static Button Tap(Action onClick, string classes, params VisualElement[] children)
        {
            var b = new Button(onClick) { text = "" };
            b.Cls("plain " + classes);
            foreach (var c in children) if (c != null) b.Add(c);
            return b;
        }

        public static Button GlyphBtn(GlyphKind kind, string text, Action onClick, string classes = "", float glyphSize = 18)
        {
            var b = new Button(onClick) { text = "" };
            b.Cls("btn " + classes);
            b.Add(new Glyph(kind, glyphSize));
            if (!string.IsNullOrEmpty(text)) { var l = Text(text).Ml(8); l.style.unityFontStyleAndWeight = FontStyle.Bold; b.Add(l); }
            return b;
        }

        public static VisualElement Kbd(string keys)
        {
            var v = new VisualElement().Cls("kbd");
            v.Add(Text(keys));
            return v;
        }

        /// <summary>A button with a text label and a keyboard hint (shown only when a hardware keyboard is present). Built from children, since a Button's own text and its children would overlap.</summary>
        public static Button BtnKbd(string text, string key, Action onClick, string classes = "")
        {
            var b = new Button(onClick) { text = "" };
            b.Cls("btn " + classes);
            b.Add(Text(text).Bold());
            if (key != null && Shortcuts.HardwareKeyboardPresent) b.Add(Kbd(key));
            return b;
        }

        /// <summary>A chip with a remove (x) tap target on its right.</summary>
        public static VisualElement ChipX(string text, Action onRemove, string classes = "")
        {
            var v = new VisualElement().Cls("chip " + classes);
            v.Add(Text(text).Bold());
            var x = Tap(onRemove, "", new Glyph(GlyphKind.Cross, 14));
            x.style.marginLeft = 10; x.style.width = 32; x.style.height = 32; x.style.alignItems = Align.Center; x.style.justifyContent = Justify.Center;
            v.Add(x);
            return v;
        }

        // ------------------------------------------------------------------ status
        public static string StatusClass(PlanItemStatus s)
        {
            switch (s)
            {
                case PlanItemStatus.Captured: return "st-captured";
                case PlanItemStatus.PartiallyCaptured: return "st-partial";
                case PlanItemStatus.Dropped: return "st-dropped";
                case PlanItemStatus.Superseded: return "st-superseded";
                default: return "";
            }
        }

        public static GlyphKind StatusGlyph(PlanItemStatus s)
        {
            switch (s)
            {
                case PlanItemStatus.Captured: return GlyphKind.Check;
                case PlanItemStatus.PartiallyCaptured: return GlyphKind.Half;
                case PlanItemStatus.Dropped: return GlyphKind.Cross;
                case PlanItemStatus.Superseded: return GlyphKind.Arrow;
                default: return GlyphKind.None;
            }
        }

        /// <summary>The status ring: empty ring, filled disc with check, half circle, X, grey disc with arrow.</summary>
        public static VisualElement St(PlanItemStatus s, float size)
        {
            var v = new VisualElement().Cls("st " + StatusClass(s)).W(size).H(size);
            v.Add(new Glyph(StatusGlyph(s), size * 0.62f, 3f));
            return v;
        }

        public static VisualElement StPhoto(PhotoStatus s, float size, bool hero = false)
        {
            var status = s == PhotoStatus.Captured ? PlanItemStatus.Captured : s == PhotoStatus.Dropped ? PlanItemStatus.Dropped : PlanItemStatus.NotCaptured;
            var v = new VisualElement().Cls("st " + StatusClass(status)).W(size).H(size);
            if (s == PhotoStatus.NotCaptured && hero) { v.Cls("st-hero-missing"); v.Add(new Glyph(GlyphKind.Star, size * 0.6f)); }
            else v.Add(new Glyph(StatusGlyph(status), size * 0.62f, 3f));
            return v;
        }

        public static Button StBtn(PlanItemStatus s, float size, Action onClick)
        {
            var b = new Button(onClick) { text = "" };
            b.Cls("st " + StatusClass(s)).W(size).H(size);
            b.Add(new Glyph(StatusGlyph(s), size * 0.62f, 3f));
            var disabled = s == PlanItemStatus.Dropped || s == PlanItemStatus.Superseded;
            b.SetEnabled(!disabled);
            return b;
        }

        public static VisualElement StatusPill(PlanItemStatus s)
        {
            switch (s)
            {
                case PlanItemStatus.Captured: return Pill("Captured", "fill", GlyphKind.Check);
                case PlanItemStatus.PartiallyCaptured: return Pill("Partial", "warn", GlyphKind.Half);
                case PlanItemStatus.Dropped: return Pill("Dropped", "bad", GlyphKind.Cross);
                case PlanItemStatus.Superseded: return Pill("Superseded", "", GlyphKind.Arrow);
                default: return Pill("Not captured", "", GlyphKind.Ring);
            }
        }

        public static string StatusWord(PlanItemStatus s)
        {
            switch (s)
            {
                case PlanItemStatus.Captured: return "captured";
                case PlanItemStatus.PartiallyCaptured: return "partial";
                case PlanItemStatus.Dropped: return "dropped";
                case PlanItemStatus.Superseded: return "superseded";
                default: return "not captured";
            }
        }

        // ------------------------------------------------------------------ pills / chips / toggles / checks
        public static VisualElement Pill(string text, string classes = "", GlyphKind glyph = GlyphKind.None)
        {
            var v = new VisualElement().Cls("pill " + classes);
            if (glyph != GlyphKind.None) { var g = new Glyph(glyph, 12, 2f); g.style.marginRight = 5; v.Add(g); }
            var l = Text(text);
            l.style.color = StyleKeyword.Null;
            v.Add(l);
            return v;
        }

        public static Button Chip(string text, bool on, Action onClick, string classes = "")
        {
            var b = new Button(onClick) { text = text ?? "" };
            b.Cls("chip " + classes).On(on);
            return b;
        }

        public static Button Toggle(string text, bool on, Action onClick)
        {
            var b = new Button(onClick) { text = "" };
            b.Cls("tog plain").On(on);
            b.Add(Text(text));
            var knob = new VisualElement().Cls("knob");
            knob.Add(new VisualElement().Cls("dot"));
            b.Add(knob);
            return b;
        }

        public static Button Check(bool on, Action onClick, string classes = "", GlyphKind emptyGlyph = GlyphKind.None)
        {
            var b = new Button(onClick) { text = "" };
            b.Cls("check " + classes).On(on);
            if (on) b.Add(new Glyph(GlyphKind.Check, 30, 3.5f));
            else if (emptyGlyph != GlyphKind.None) b.Add(new Glyph(emptyGlyph, 26));
            return b;
        }

        public static Label Num(string text, string classes = "") => Text(text, "num " + classes);

        public static VisualElement Card(params VisualElement[] children) => With(new VisualElement(), "card", children);

        public static VisualElement Bar(float fraction)
        {
            var bar = new VisualElement().Cls("bar");
            var fill = new VisualElement().Cls("fillbar");
            fill.style.width = Length.Percent(Mathf.Clamp01(fraction) * 100f);
            bar.Add(fill);
            return bar;
        }

        // ------------------------------------------------------------------ inputs
        public static TextField Input(string value, string placeholder = null, Action<string> onChange = null, bool multiline = false, string classes = "")
        {
            var tf = new TextField();
            tf.multiline = multiline;
            tf.SetValueWithoutNotify(value ?? "");
            tf.Cls("inp " + classes + (multiline ? " multi" : ""));
            if (!string.IsNullOrEmpty(placeholder))
            {
                tf.textEdition.placeholder = placeholder;
                tf.textEdition.hidePlaceholderOnFocus = false;
            }
            if (onChange != null) tf.RegisterValueChangedCallback(e => onChange(e.newValue));
            return tf;
        }

        public static VisualElement Field(string label, VisualElement input, string classes = "")
        {
            var f = new VisualElement().Cls("fld " + classes);
            if (label != null) f.Add(Lbl(label));
            f.Add(input);
            return f;
        }

        public static VisualElement Stepper(int value, Action<int> onDelta)
        {
            var v = new VisualElement().Cls("stepper");
            var minus = new Button(() => onDelta(-1)) { text = "" }.Cls("stepbtn"); minus.Add(new Glyph(GlyphKind.Minus, 24, 3));
            var plus = new Button(() => onDelta(1)) { text = "" }.Cls("stepbtn"); plus.Add(new Glyph(GlyphKind.Plus, 24, 3));
            v.Add(minus);
            v.Add(Text(value.ToString(), "val"));
            v.Add(plus);
            return v;
        }

        public static VisualElement Segmented(IList<(string id, string label)> options, string current, Action<string> onPick, float height = 48)
        {
            var seg = new VisualElement().Cls("segc").H(height);
            for (int i = 0; i < options.Count; i++)
            {
                var (id, label) = options[i];
                var b = new Button(() => onPick(id)) { text = label }.Cls("segb grow").On(id == current);
                if (i == options.Count - 1) b.Cls("last");
                seg.Add(b);
            }
            return seg;
        }

        // ------------------------------------------------------------------ formatting
        public static string FmtDate(string iso)
        {
            if (string.IsNullOrEmpty(iso)) return "—";
            if (DateTime.TryParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                return d.ToString("ddd, MMM d", CultureInfo.InvariantCulture);
            return iso;
        }

        public static string FmtTimestamp(string iso)
        {
            var t = PlanResolver.ParseTimestamp(iso);
            if (t == null) return iso ?? "";
            return t.Value.ToLocalTime().ToString("ddd h:mm tt", CultureInfo.InvariantCulture);
        }

        public static string Plural(int n, string one, string many = null) => n + " " + (n == 1 ? one : (many ?? one + "s"));

        public static string HeroLabel(Photo p, Chapter ch) =>
            p.HeroType == HeroType.BookCover ? "Cover" : "Ch." + (ch?.Number.ToString() ?? "?") + " hero";

        /// <summary>"Book cover: ..." / "Ch.1 hero: ..." prefixes are printed labels, not content; strip them for tiles.</summary>
        public static string ShortDescription(string description)
        {
            if (string.IsNullOrEmpty(description)) return "";
            var idx = description.IndexOf(": ", StringComparison.Ordinal);
            if (idx > 0 && idx < 16 && (description.StartsWith("Book cover", StringComparison.OrdinalIgnoreCase) || description.StartsWith("Ch.", StringComparison.OrdinalIgnoreCase) || description.StartsWith("Detail", StringComparison.OrdinalIgnoreCase)))
                return description.Substring(idx + 2);
            return description;
        }
    }
}

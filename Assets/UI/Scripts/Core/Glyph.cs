using UnityEngine;
using UnityEngine.UIElements;

namespace MediaTrip.UI
{
    public enum GlyphKind
    {
        None, Check, Half, Cross, Arrow, Star, Plus, Minus, ChevronDown, ChevronUp, ChevronRight,
        Lines, Search, Keyboard, Sun, Moon, Question, Person, Copy, ChildArrow, Outdent, Indent,
        ArrowUp, ArrowDown, Braces, Dot, Ring, Pencil, Circle,
    }

    /// <summary>
    /// Vector glyphs drawn with Painter2D so the status system never depends on a font having
    /// the right symbol. Stroke and fill use the element's resolved text color, so the theme
    /// variables drive them like any other text.
    /// </summary>
    public class Glyph : VisualElement
    {
        private GlyphKind _kind;
        private float _stroke = 2.5f;

        public GlyphKind Kind { get => _kind; set { _kind = value; MarkDirtyRepaint(); } }
        public float StrokeWidth { get => _stroke; set { _stroke = value; MarkDirtyRepaint(); } }

        public Glyph(GlyphKind kind, float size = 20, float stroke = 2.5f)
        {
            _kind = kind;
            _stroke = stroke;
            style.width = size;
            style.height = size;
            style.flexShrink = 0;
            pickingMode = PickingMode.Ignore;
            AddToClassList("glyph");
            generateVisualContent += Draw;
        }

        private void Draw(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D;
            var c = resolvedStyle.color;
            float w = contentRect.width, h = contentRect.height;
            float s = Mathf.Min(w, h);
            float cx = w / 2f, cy = h / 2f;
            p.strokeColor = c;
            p.fillColor = c;
            p.lineWidth = Mathf.Max(1.5f, _stroke * s / 20f);
            p.lineCap = LineCap.Round;
            p.lineJoin = LineJoin.Round;

            switch (_kind)
            {
                case GlyphKind.Check:
                    p.BeginPath();
                    p.MoveTo(new Vector2(cx - s * 0.30f, cy + s * 0.02f));
                    p.LineTo(new Vector2(cx - s * 0.08f, cy + s * 0.24f));
                    p.LineTo(new Vector2(cx + s * 0.32f, cy - s * 0.22f));
                    p.Stroke();
                    break;
                case GlyphKind.Half:
                    p.BeginPath();
                    p.Arc(new Vector2(cx, cy), s * 0.34f, 90, 270);
                    p.ClosePath();
                    p.Fill();
                    break;
                case GlyphKind.Cross:
                    p.BeginPath();
                    p.MoveTo(new Vector2(cx - s * 0.26f, cy - s * 0.26f)); p.LineTo(new Vector2(cx + s * 0.26f, cy + s * 0.26f));
                    p.MoveTo(new Vector2(cx + s * 0.26f, cy - s * 0.26f)); p.LineTo(new Vector2(cx - s * 0.26f, cy + s * 0.26f));
                    p.Stroke();
                    break;
                case GlyphKind.Arrow:
                    p.BeginPath();
                    p.MoveTo(new Vector2(cx - s * 0.32f, cy)); p.LineTo(new Vector2(cx + s * 0.30f, cy));
                    p.MoveTo(new Vector2(cx + s * 0.06f, cy - s * 0.24f)); p.LineTo(new Vector2(cx + s * 0.30f, cy)); p.LineTo(new Vector2(cx + s * 0.06f, cy + s * 0.24f));
                    p.Stroke();
                    break;
                case GlyphKind.ArrowUp:
                    p.BeginPath();
                    p.MoveTo(new Vector2(cx, cy + s * 0.32f)); p.LineTo(new Vector2(cx, cy - s * 0.30f));
                    p.MoveTo(new Vector2(cx - s * 0.24f, cy - s * 0.06f)); p.LineTo(new Vector2(cx, cy - s * 0.30f)); p.LineTo(new Vector2(cx + s * 0.24f, cy - s * 0.06f));
                    p.Stroke();
                    break;
                case GlyphKind.ArrowDown:
                    p.BeginPath();
                    p.MoveTo(new Vector2(cx, cy - s * 0.32f)); p.LineTo(new Vector2(cx, cy + s * 0.30f));
                    p.MoveTo(new Vector2(cx - s * 0.24f, cy + s * 0.06f)); p.LineTo(new Vector2(cx, cy + s * 0.30f)); p.LineTo(new Vector2(cx + s * 0.24f, cy + s * 0.06f));
                    p.Stroke();
                    break;
                case GlyphKind.Star:
                {
                    p.BeginPath();
                    float ro = s * 0.42f, ri = s * 0.18f;
                    for (int i = 0; i < 10; i++)
                    {
                        float a = -90f + i * 36f;
                        float r = i % 2 == 0 ? ro : ri;
                        var pt = new Vector2(cx + r * Mathf.Cos(a * Mathf.Deg2Rad), cy + r * Mathf.Sin(a * Mathf.Deg2Rad));
                        if (i == 0) p.MoveTo(pt); else p.LineTo(pt);
                    }
                    p.ClosePath();
                    p.Fill();
                    break;
                }
                case GlyphKind.Plus:
                    p.BeginPath();
                    p.MoveTo(new Vector2(cx - s * 0.3f, cy)); p.LineTo(new Vector2(cx + s * 0.3f, cy));
                    p.MoveTo(new Vector2(cx, cy - s * 0.3f)); p.LineTo(new Vector2(cx, cy + s * 0.3f));
                    p.Stroke();
                    break;
                case GlyphKind.Minus:
                    p.BeginPath();
                    p.MoveTo(new Vector2(cx - s * 0.3f, cy)); p.LineTo(new Vector2(cx + s * 0.3f, cy));
                    p.Stroke();
                    break;
                case GlyphKind.ChevronDown:
                    p.BeginPath();
                    p.MoveTo(new Vector2(cx - s * 0.28f, cy - s * 0.14f)); p.LineTo(new Vector2(cx, cy + s * 0.14f)); p.LineTo(new Vector2(cx + s * 0.28f, cy - s * 0.14f));
                    p.Stroke();
                    break;
                case GlyphKind.ChevronUp:
                    p.BeginPath();
                    p.MoveTo(new Vector2(cx - s * 0.28f, cy + s * 0.14f)); p.LineTo(new Vector2(cx, cy - s * 0.14f)); p.LineTo(new Vector2(cx + s * 0.28f, cy + s * 0.14f));
                    p.Stroke();
                    break;
                case GlyphKind.ChevronRight:
                    p.BeginPath();
                    p.MoveTo(new Vector2(cx - s * 0.14f, cy - s * 0.28f)); p.LineTo(new Vector2(cx + s * 0.14f, cy)); p.LineTo(new Vector2(cx - s * 0.14f, cy + s * 0.28f));
                    p.Stroke();
                    break;
                case GlyphKind.Lines:
                    p.BeginPath();
                    for (int i = -1; i <= 1; i++)
                    {
                        p.MoveTo(new Vector2(cx - s * 0.3f, cy + i * s * 0.22f));
                        p.LineTo(new Vector2(cx + s * 0.3f, cy + i * s * 0.22f));
                    }
                    p.Stroke();
                    break;
                case GlyphKind.Search:
                    p.BeginPath();
                    p.Arc(new Vector2(cx - s * 0.08f, cy - s * 0.08f), s * 0.24f, 0, 360);
                    p.Stroke();
                    p.BeginPath();
                    p.MoveTo(new Vector2(cx + s * 0.10f, cy + s * 0.10f)); p.LineTo(new Vector2(cx + s * 0.34f, cy + s * 0.34f));
                    p.Stroke();
                    break;
                case GlyphKind.Keyboard:
                {
                    p.BeginPath();
                    float x0 = cx - s * 0.4f, y0 = cy - s * 0.22f, x1 = cx + s * 0.4f, y1 = cy + s * 0.22f;
                    p.MoveTo(new Vector2(x0, y0)); p.LineTo(new Vector2(x1, y0)); p.LineTo(new Vector2(x1, y1)); p.LineTo(new Vector2(x0, y1)); p.ClosePath();
                    p.Stroke();
                    p.lineWidth = Mathf.Max(1f, p.lineWidth * 0.8f);
                    p.BeginPath();
                    for (int r = 0; r < 2; r++)
                        for (int k = 0; k < 4; k++)
                        {
                            var pt = new Vector2(x0 + s * 0.14f + k * s * 0.17f, y0 + s * 0.12f + r * s * 0.12f);
                            p.MoveTo(pt); p.LineTo(pt + new Vector2(0.01f, 0));
                        }
                    p.MoveTo(new Vector2(cx - s * 0.2f, y1 - s * 0.1f)); p.LineTo(new Vector2(cx + s * 0.2f, y1 - s * 0.1f));
                    p.Stroke();
                    break;
                }
                case GlyphKind.Sun:
                    p.BeginPath();
                    p.Arc(new Vector2(cx, cy), s * 0.16f, 0, 360);
                    p.Fill();
                    p.BeginPath();
                    for (int i = 0; i < 8; i++)
                    {
                        float a = i * 45f * Mathf.Deg2Rad;
                        p.MoveTo(new Vector2(cx + Mathf.Cos(a) * s * 0.26f, cy + Mathf.Sin(a) * s * 0.26f));
                        p.LineTo(new Vector2(cx + Mathf.Cos(a) * s * 0.4f, cy + Mathf.Sin(a) * s * 0.4f));
                    }
                    p.Stroke();
                    break;
                case GlyphKind.Moon:
                    p.BeginPath();
                    p.Arc(new Vector2(cx, cy), s * 0.34f, 40, 320);
                    p.Arc(new Vector2(cx + s * 0.14f, cy), s * 0.30f, 300, 60, ArcDirection.CounterClockwise);
                    p.ClosePath();
                    p.Fill();
                    break;
                case GlyphKind.Person:
                    p.BeginPath();
                    p.Arc(new Vector2(cx, cy - s * 0.14f), s * 0.16f, 0, 360);
                    p.Stroke();
                    p.BeginPath();
                    p.Arc(new Vector2(cx, cy + s * 0.36f), s * 0.32f, 200, 340);
                    p.Stroke();
                    break;
                case GlyphKind.Copy:
                {
                    p.BeginPath();
                    float a = s * 0.22f;
                    p.MoveTo(new Vector2(cx - a - s * 0.08f, cy - a + s * 0.08f)); p.LineTo(new Vector2(cx + a - s * 0.08f - s * 0.16f, cy - a + s * 0.08f));
                    p.LineTo(new Vector2(cx + a - s * 0.08f - s * 0.16f, cy + a + s * 0.08f)); p.LineTo(new Vector2(cx - a - s * 0.08f, cy + a + s * 0.08f)); p.ClosePath();
                    p.Stroke();
                    p.BeginPath();
                    p.MoveTo(new Vector2(cx - a + s * 0.08f, cy - a - s * 0.08f)); p.LineTo(new Vector2(cx + a + s * 0.08f, cy - a - s * 0.08f));
                    p.LineTo(new Vector2(cx + a + s * 0.08f, cy + a - s * 0.08f)); p.LineTo(new Vector2(cx - a + s * 0.08f + s * 0.16f, cy + a - s * 0.08f));
                    p.Stroke();
                    break;
                }
                case GlyphKind.ChildArrow:
                    p.BeginPath();
                    p.MoveTo(new Vector2(cx - s * 0.26f, cy - s * 0.3f)); p.LineTo(new Vector2(cx - s * 0.26f, cy + s * 0.1f)); p.LineTo(new Vector2(cx + s * 0.28f, cy + s * 0.1f));
                    p.MoveTo(new Vector2(cx + s * 0.08f, cy - s * 0.1f)); p.LineTo(new Vector2(cx + s * 0.28f, cy + s * 0.1f)); p.LineTo(new Vector2(cx + s * 0.08f, cy + s * 0.3f));
                    p.Stroke();
                    break;
                case GlyphKind.Outdent:
                    p.BeginPath();
                    p.MoveTo(new Vector2(cx - s * 0.36f, cy - s * 0.28f)); p.LineTo(new Vector2(cx - s * 0.36f, cy + s * 0.28f));
                    p.MoveTo(new Vector2(cx + s * 0.34f, cy)); p.LineTo(new Vector2(cx - s * 0.26f, cy));
                    p.MoveTo(new Vector2(cx - s * 0.04f, cy - s * 0.22f)); p.LineTo(new Vector2(cx - s * 0.26f, cy)); p.LineTo(new Vector2(cx - s * 0.04f, cy + s * 0.22f));
                    p.Stroke();
                    break;
                case GlyphKind.Indent:
                    p.BeginPath();
                    p.MoveTo(new Vector2(cx + s * 0.36f, cy - s * 0.28f)); p.LineTo(new Vector2(cx + s * 0.36f, cy + s * 0.28f));
                    p.MoveTo(new Vector2(cx - s * 0.34f, cy)); p.LineTo(new Vector2(cx + s * 0.26f, cy));
                    p.MoveTo(new Vector2(cx + s * 0.04f, cy - s * 0.22f)); p.LineTo(new Vector2(cx + s * 0.26f, cy)); p.LineTo(new Vector2(cx + s * 0.04f, cy + s * 0.22f));
                    p.Stroke();
                    break;
                case GlyphKind.Dot:
                    p.BeginPath();
                    p.Arc(new Vector2(cx, cy), s * 0.16f, 0, 360);
                    p.Fill();
                    break;
                case GlyphKind.Ring:
                    p.BeginPath();
                    p.Arc(new Vector2(cx, cy), s * 0.3f, 0, 360);
                    p.Stroke();
                    break;
                case GlyphKind.Circle:
                    p.BeginPath();
                    p.Arc(new Vector2(cx, cy), s * 0.34f, 0, 360);
                    p.Fill();
                    break;
                case GlyphKind.Pencil:
                    p.BeginPath();
                    p.MoveTo(new Vector2(cx - s * 0.3f, cy + s * 0.3f)); p.LineTo(new Vector2(cx - s * 0.22f, cy + s * 0.06f));
                    p.LineTo(new Vector2(cx + s * 0.18f, cy - s * 0.34f)); p.LineTo(new Vector2(cx + s * 0.34f, cy - s * 0.18f));
                    p.LineTo(new Vector2(cx - s * 0.06f, cy + s * 0.22f)); p.ClosePath();
                    p.Stroke();
                    break;
                case GlyphKind.Braces:
                case GlyphKind.Question:
                default:
                    break;
            }
        }
    }
}

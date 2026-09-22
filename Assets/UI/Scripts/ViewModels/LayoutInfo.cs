using System;
using System.Collections.Generic;

namespace MediaTrip.UI.ViewModels
{
    /// <summary>Which of the three layouts a screen builds: phone (one column, bottom bar), portrait tablet (bottom bar), landscape tablet (rail, side panels).</summary>
    public enum LayoutKind { Phone, Port, Land }

    /// <summary>
    /// Orientation, width class and layout kind derived from the panel size in points. USS has
    /// no media queries, so these become classes on the root element and screens switch on
    /// <see cref="Kind"/> when they build. Pure logic; the AppController applies it on resize.
    /// </summary>
    public readonly struct LayoutInfo
    {
        public const float CompactMaxWidth = 700f;
        public const float RegularMaxWidth = 1100f;
        /// <summary>A short side under this is a phone in either orientation.</summary>
        public const float PhoneMaxShortSide = 500f;
        /// <summary>Above this width landscape layouts get a third column.</summary>
        public const float XWideMinWidth = 1200f;

        public readonly float Width;
        public readonly float Height;
        public readonly bool Portrait;
        /// <summary>"compact" | "regular" | "wide"</summary>
        public readonly string WidthClass;
        public readonly LayoutKind Kind;
        public readonly bool XWide;

        public LayoutInfo(float width, float height)
        {
            Width = width;
            Height = height;
            Portrait = height > width;
            WidthClass = width < CompactMaxWidth ? "compact" : width < RegularMaxWidth ? "regular" : "wide";
            var phone = width < CompactMaxWidth || Math.Min(width, height) < PhoneMaxShortSide;
            Kind = phone ? LayoutKind.Phone : Portrait ? LayoutKind.Port : LayoutKind.Land;
            XWide = Kind == LayoutKind.Land && width > XWideMinWidth;
        }

        public bool Landscape => !Portrait;
        public bool Compact => WidthClass == "compact";
        public bool Phone => Kind == LayoutKind.Phone;
        public bool Land => Kind == LayoutKind.Land;
        public bool Port => Kind == LayoutKind.Port;

        public string KindClass => Kind == LayoutKind.Phone ? "k-phone" : Kind == LayoutKind.Port ? "k-port" : "k-land";

        public static readonly string[] AllClasses = { "portrait", "landscape", "compact", "regular", "wide", "k-phone", "k-port", "k-land", "xwide" };

        /// <summary>The classes that should be on the root for this size.</summary>
        public IEnumerable<string> Classes()
        {
            yield return Portrait ? "portrait" : "landscape";
            yield return WidthClass;
            yield return KindClass;
            if (XWide) yield return "xwide";
        }

        /// <summary>True when nothing a layout switches on differs, so no rebuild is needed.</summary>
        public bool SameAs(LayoutInfo other) => Portrait == other.Portrait && WidthClass == other.WidthClass && Kind == other.Kind && XWide == other.XWide;

        /// <summary>Sheets are full-screen on the phone and a centered card elsewhere, never wider than the screen.</summary>
        public float SheetWidth(float wanted) => Math.Min(wanted, Width - 40f);

        /// <summary>How many columns a board of cards gets.</summary>
        public int BoardColumns => Kind == LayoutKind.Phone ? 1 : Kind == LayoutKind.Port ? 2 : XWide ? 3 : 2;

        public override string ToString() => Kind + " " + WidthClass + " " + (int)Width + "x" + (int)Height;
    }

    /// <summary>
    /// The panel works in points, as the design does. On the device a point is 2 or 3 pixels;
    /// in the Editor the Game view is set to a device's pixel size, so the factor is inferred
    /// from that size. Pure so it can be tested against the device list.
    /// </summary>
    public static class PanelScale
    {
        /// <param name="mobile">True on the device itself, where <paramref name="dpi"/> is trustworthy.</param>
        public static float For(int pixelWidth, int pixelHeight, float dpi, bool mobile)
        {
            if (pixelWidth <= 0 || pixelHeight <= 0) return 1f;
            int shortSide = Math.Min(pixelWidth, pixelHeight);
            int longSide = Math.Max(pixelWidth, pixelHeight);
            float aspect = (float)longSide / shortSide;
            if (mobile && dpi > 0)
            {
                // Every 3x iPhone is over 400 ppi; every iPad and 2x iPhone is under.
                return dpi >= 400f ? 3f : 2f;
            }
            // Editor / desktop: recognise device pixel sizes.
            if (aspect > 1.9f && shortSide >= 1100) return 3f;      // 3x phones (1179, 1206, 1290, 1320 wide)
            if (aspect > 1.7f && (shortSide == 750 || shortSide == 828)) return 2f; // 2x phones (750 x 1334, 828 x 1792)
            if (shortSide >= 1400 && aspect < 1.65f) return 2f;       // retina iPads (1488 and up on the short side)
            return 1f;                                                // already in points (1180 x 820, a desktop window)
        }
    }
}

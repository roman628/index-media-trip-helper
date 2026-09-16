using System.Collections.Generic;

namespace MediaTrip.UI.ViewModels
{
    /// <summary>
    /// Orientation and width class derived from the panel size. USS has no media queries, so
    /// these become classes on the root element (.portrait/.landscape and .compact/.regular/.wide)
    /// and layouts switch on them. Pure logic; the AppController applies it on resize.
    /// </summary>
    public readonly struct LayoutInfo
    {
        public const float CompactMaxWidth = 700f;
        public const float RegularMaxWidth = 1100f;

        public readonly float Width;
        public readonly float Height;
        public readonly bool Portrait;
        /// <summary>"compact" | "regular" | "wide"</summary>
        public readonly string WidthClass;

        public LayoutInfo(float width, float height)
        {
            Width = width;
            Height = height;
            Portrait = height > width;
            WidthClass = width < CompactMaxWidth ? "compact" : width < RegularMaxWidth ? "regular" : "wide";
        }

        public bool Landscape => !Portrait;
        public bool Compact => WidthClass == "compact";

        public static readonly string[] AllClasses = { "portrait", "landscape", "compact", "regular", "wide" };

        /// <summary>The classes that should be on the root for this size.</summary>
        public IEnumerable<string> Classes()
        {
            yield return Portrait ? "portrait" : "landscape";
            yield return WidthClass;
        }

        public bool SameAs(LayoutInfo other) => Portrait == other.Portrait && WidthClass == other.WidthClass;

        public override string ToString() => (Portrait ? "portrait" : "landscape") + " " + WidthClass + " " + (int)Width + "x" + (int)Height;
    }
}

using System.Collections.Generic;
using System.Linq;
using MediaTrip.UI.ViewModels;
using NUnit.Framework;

namespace MediaTrip.UI.Tests
{
    public class LayoutInfoTests
    {
        [Test]
        public void Orientation_AndWidthClasses()
        {
            var ipadLandscape = new LayoutInfo(1180, 820);
            Assert.IsTrue(ipadLandscape.Landscape);
            Assert.AreEqual("wide", ipadLandscape.WidthClass);
            CollectionAssert.AreEqual(new[] { "landscape", "wide", "k-land" }, ipadLandscape.Classes().ToList());

            var ipadPortrait = new LayoutInfo(820, 1180);
            Assert.IsTrue(ipadPortrait.Portrait);
            Assert.AreEqual("regular", ipadPortrait.WidthClass);
            CollectionAssert.AreEqual(new[] { "portrait", "regular", "k-port" }, ipadPortrait.Classes().ToList());

            var splitView = new LayoutInfo(500, 1100);
            Assert.AreEqual("compact", splitView.WidthClass);
            Assert.IsTrue(splitView.Compact);
            Assert.IsTrue(splitView.Phone, "a narrow split view gets the one-column layout");

            Assert.IsFalse(ipadLandscape.SameAs(ipadPortrait));
            Assert.IsTrue(ipadLandscape.SameAs(new LayoutInfo(1190, 830)), "same classes, no change event");
            foreach (var c in ipadLandscape.Classes().Concat(ipadPortrait.Classes())) CollectionAssert.Contains(LayoutInfo.AllClasses, c);
        }

        [Test]
        public void TheFiveDesignSizes_GetTheirLayoutKind()
        {
            var big = new LayoutInfo(1376, 1032);
            Assert.AreEqual(LayoutKind.Land, big.Kind);
            Assert.IsTrue(big.XWide);
            Assert.AreEqual(3, big.BoardColumns);
            CollectionAssert.Contains(big.Classes().ToList(), "xwide");

            Assert.AreEqual(LayoutKind.Port, new LayoutInfo(1032, 1376).Kind);
            Assert.AreEqual(2, new LayoutInfo(1032, 1376).BoardColumns);

            var eleven = new LayoutInfo(1180, 820);
            Assert.AreEqual(LayoutKind.Land, eleven.Kind);
            Assert.IsFalse(eleven.XWide);
            Assert.AreEqual(2, eleven.BoardColumns);
            Assert.IsFalse(eleven.SameAs(big), "the third column is a layout change");

            Assert.AreEqual(LayoutKind.Port, new LayoutInfo(820, 1180).Kind);

            var phone = new LayoutInfo(402, 874);
            Assert.AreEqual(LayoutKind.Phone, phone.Kind);
            Assert.AreEqual(1, phone.BoardColumns);
            Assert.AreEqual(LayoutKind.Phone, new LayoutInfo(874, 402).Kind, "a phone on its side is still a phone: no rail");
            Assert.AreEqual(362, phone.SheetWidth(720));
            Assert.AreEqual(720, eleven.SheetWidth(720));
        }

        [Test]
        public void PanelScale_TurnsDevicePixelsIntoPoints()
        {
            // Editor: the Game view is set to a device's pixel size.
            Assert.AreEqual(2f, PanelScale.For(2752, 2064, 96, false), "iPad Pro 13");
            Assert.AreEqual(2f, PanelScale.For(1640, 2360, 96, false), "iPad Air 11 portrait");
            Assert.AreEqual(2f, PanelScale.For(2266, 1488, 96, false), "iPad mini");
            Assert.AreEqual(3f, PanelScale.For(1206, 2622, 96, false), "iPhone 16 Pro");
            Assert.AreEqual(3f, PanelScale.For(2868, 1320, 96, false), "iPhone 16 Pro Max landscape");
            Assert.AreEqual(2f, PanelScale.For(750, 1334, 96, false), "iPhone SE");
            Assert.AreEqual(1f, PanelScale.For(1180, 820, 96, false), "already points");
            Assert.AreEqual(1f, PanelScale.For(402, 874, 96, false));
            Assert.AreEqual(1f, PanelScale.For(1920, 1080, 96, false), "a desktop window");
            Assert.AreEqual(1f, PanelScale.For(0, 0, 96, false));

            // On the device the pixel density decides.
            Assert.AreEqual(2f, PanelScale.For(2360, 1640, 264, true));
            Assert.AreEqual(3f, PanelScale.For(1206, 2622, 460, true));
            Assert.AreEqual(2f, PanelScale.For(750, 1334, 326, true));

            // and the result lands on the design sizes
            var s = PanelScale.For(1206, 2622, 460, true);
            Assert.AreEqual(LayoutKind.Phone, new LayoutInfo(1206 / s, 2622 / s).Kind);
            s = PanelScale.For(2360, 1640, 264, true);
            Assert.AreEqual(LayoutKind.Land, new LayoutInfo(2360 / s, 1640 / s).Kind);
        }
    }

    public class ListMathTests
    {
        private static List<(float, float)> Rows(int n, float h = 80) => Enumerable.Range(0, n).Select(i => (i * h, h)).ToList();

        [Test]
        public void DropIndex_IsWhereTheRowEndsUp()
        {
            var rows = Rows(4);
            Assert.AreEqual(0, ListMath.DropIndex(rows, 2, 10), "above the first row's middle");
            Assert.AreEqual(1, ListMath.DropIndex(rows, 2, 100), "between the first and second");
            Assert.AreEqual(2, ListMath.DropIndex(rows, 2, 200), "its own place");
            Assert.AreEqual(3, ListMath.DropIndex(rows, 2, 1000), "past the end");
            Assert.AreEqual(0, ListMath.DropIndex(rows, 0, 0));
            Assert.AreEqual(3, ListMath.DropIndex(rows, 0, 290));
            Assert.AreEqual(0, ListMath.DropIndex(Rows(1), 0, 500));
        }

        [Test]
        public void Move_ReordersInPlace()
        {
            var list = new List<string> { "a", "b", "c", "d" };
            ListMath.Move(list, 0, 2);
            CollectionAssert.AreEqual(new[] { "b", "c", "a", "d" }, list);
            ListMath.Move(list, 3, 0);
            CollectionAssert.AreEqual(new[] { "d", "b", "c", "a" }, list);
            ListMath.Move(list, 1, 99);
            CollectionAssert.AreEqual(new[] { "d", "c", "a", "b" }, list);
            ListMath.Move(list, 7, 0);
            CollectionAssert.AreEqual(new[] { "d", "c", "a", "b" }, list);
        }

        [Test]
        public void ActiveSection_FollowsTheScroll()
        {
            var tops = new List<float> { 0, 120, 300, 900, 960 };
            Assert.AreEqual(0, ListMath.ActiveSection(tops, 0, 80, 1000));
            Assert.AreEqual(1, ListMath.ActiveSection(tops, 40, 80, 1000), "a section becomes current as its top nears the top edge");
            Assert.AreEqual(2, ListMath.ActiveSection(tops, 250, 80, 1000));
            Assert.AreEqual(4, ListMath.ActiveSection(tops, 1000, 80, 1000), "the bottom of the document belongs to the last section");
            Assert.AreEqual(-1, ListMath.ActiveSection(new List<float>(), 0));
        }
    }
}

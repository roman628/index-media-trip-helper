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
            CollectionAssert.AreEqual(new[] { "landscape", "wide" }, ipadLandscape.Classes().ToList());

            var ipadPortrait = new LayoutInfo(820, 1180);
            Assert.IsTrue(ipadPortrait.Portrait);
            Assert.AreEqual("regular", ipadPortrait.WidthClass);
            CollectionAssert.AreEqual(new[] { "portrait", "regular" }, ipadPortrait.Classes().ToList());

            var splitView = new LayoutInfo(500, 1100);
            Assert.AreEqual("compact", splitView.WidthClass);
            Assert.IsTrue(splitView.Compact);

            Assert.IsFalse(ipadLandscape.SameAs(ipadPortrait));
            Assert.IsTrue(ipadLandscape.SameAs(new LayoutInfo(1300, 900)), "same classes, no change event");
            Assert.AreEqual(5, LayoutInfo.AllClasses.Length);
        }
    }
}

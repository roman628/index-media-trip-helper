using System.Linq;
using MediaTrip.Authoring;
using MediaTrip.Model;
using NUnit.Framework;

namespace MediaTrip.Tests
{
    public class PasteParserTests
    {
        private static string Flat(System.Collections.Generic.List<Node> roots) =>
            string.Join(" | ", Node.Walk(roots).Select(p => new string('>', p.depth) + (p.node.Label != null ? "[" + p.node.Label + "]" : "") + p.node.Text));

        [Test]
        public void Tabs()
        {
            var t = IndentedTextParser.Parse("Open wide\n\tPan left\n\t\tHold on sign\n\tPan right\nClose up");
            Assert.AreEqual("Open wide | >Pan left | >>Hold on sign | >Pan right | Close up", Flat(t));
            Assert.AreEqual(2, t.Count);
            Assert.AreEqual(3, Node.MaxDepth(t));
        }

        [Test]
        public void Spaces_TwoAndFour_AndMixedWithTabs()
        {
            var two = IndentedTextParser.Parse("A\n  B\n    C\n  D");
            Assert.AreEqual("A | >B | >>C | >D", Flat(two));
            var four = IndentedTextParser.Parse("A\n    B\n        C\n    D");
            Assert.AreEqual("A | >B | >>C | >D", Flat(four));
            var mixed = IndentedTextParser.Parse("A\n\tB\n        C\n    D", new PasteOptions { TabWidth = 4 });
            Assert.AreEqual("A | >B | >>C | >D", Flat(mixed));
        }

        [Test]
        public void Markers_StrippedAndKeptAsLabels()
        {
            var t = IndentedTextParser.Parse("1. First\n   a. Sub a\n   b) Sub b\n      i. Deep\n- Bullet\n• Dot\n2. Second");
            Assert.AreEqual("[1]First | >[a]Sub a | >[b]Sub b | >>[i]Deep | Bullet | Dot | [2]Second", Flat(t));

            var noLabels = IndentedTextParser.Parse("1. First\n- Bullet", new PasteOptions { KeepMarkersAsLabels = false });
            Assert.AreEqual("First | Bullet", Flat(noLabels));

            var raw = IndentedTextParser.Parse("1. First", new PasteOptions { StripMarkers = false });
            Assert.AreEqual("1. First", raw[0].Text);
        }

        [Test]
        public void MarkerNeedsSpaceOrEnd_SoWordsAreNotEaten()
        {
            var t = IndentedTextParser.Parse("a.m. call time\no-ring check\n1.5 mm gap\n3) three");
            Assert.AreEqual("a.m. call time", t[0].Text);
            Assert.AreEqual("o-ring check", t[1].Text);
            Assert.AreEqual("1.5 mm gap", t[2].Text);
            Assert.AreEqual("three", t[3].Text);
            Assert.AreEqual("3", t[3].Label);
        }

        [Test]
        public void BlankLines_CrLf_TrailingSpaces_FirstLineIndented()
        {
            var t = IndentedTextParser.Parse("\r\n  Top  \r\n\r\n    Child\r\n  Top2\r\n");
            Assert.AreEqual("Top | >Child | Top2", Flat(t));
        }

        [Test]
        public void Dedent_ToUnknownWidth_SnapsToNearestShallowerLevel()
        {
            // depth widths: 0, 4, 8; then a line at 6 -> shallower level is 4, so it becomes a new level under it (depth 2)
            var t = IndentedTextParser.Parse("A\n    B\n        C\n      D\nE");
            Assert.AreEqual("A | >B | >>C | >>D | E", Flat(t));
            // a line at 2 after the stack was 0,4: 2 is between -> new level under depth 0
            var u = IndentedTextParser.Parse("A\n    B\n  C\nD");
            Assert.AreEqual("A | >B | >C | D", Flat(u));
        }

        [Test]
        public void JumpOfTwoLevels_IsOneDeeper()
        {
            var t = IndentedTextParser.Parse("A\n\t\t\tB\n\tC");
            Assert.AreEqual("A | >B | >C", Flat(t));
        }

        [Test]
        public void RelabelOption_AndEmptyInput()
        {
            var t = IndentedTextParser.Parse("x\n\ty\n\tz\nw", new PasteOptions { Relabel = LabelScheme.Outline });
            Assert.AreEqual("[a]x | >[1]y | >[2]z | [b]w", Flat(t));
            Assert.IsEmpty(IndentedTextParser.Parse(""));
            Assert.IsEmpty(IndentedTextParser.Parse(null));
            Assert.IsEmpty(IndentedTextParser.Parse("   \n\t\n"));
        }

        [Test]
        public void ParsedTree_DropsIntoAnOutlineSection()
        {
            var data = Fixtures.LoadSample();
            var s = new MediaTrip.Session.TripSession(data, () => 0);
            var bullets = s.Outline.Bullets("b-001", "s-2-2");
            bullets.Append(IndentedTextParser.Parse("Calibrate meter\n\tBump test\n\tZero in fresh air"));
            var nodes = data.Outlines["b-001"].Chapters[1].Sections[1].Nodes;
            Assert.AreEqual(2, nodes.Count);
            Assert.AreEqual("b", nodes[1].Label);
            Assert.AreEqual("2", nodes[1].Children[1].Label);
            Assert.AreEqual("Zero in fresh air", nodes[1].Children[1].Text);
        }
    }
}

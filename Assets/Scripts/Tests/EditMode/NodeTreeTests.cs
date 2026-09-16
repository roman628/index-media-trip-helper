using System.Collections.Generic;
using System.Linq;
using MediaTrip.Authoring;
using MediaTrip.Model;
using NUnit.Framework;

namespace MediaTrip.Tests
{
    public class NodeTreeTests
    {
        private static List<Node> Tree()
        {
            // A
            //   A1
            //   A2
            //     A2i
            // B
            // C
            return new List<Node>
            {
                new Node { Id = "A", Text = "A", Children = new List<Node>
                {
                    new Node { Id = "A1", Text = "A1" },
                    new Node { Id = "A2", Text = "A2", Children = new List<Node> { new Node { Id = "A2i", Text = "A2i" } } },
                } },
                new Node { Id = "B", Text = "B" },
                new Node { Id = "C", Text = "C" },
            };
        }

        private static string Flat(List<Node> roots) =>
            string.Join(" ", Node.Walk(roots).Select(p => new string('.', p.depth) + p.node.Id));

        [Test]
        public void Locate_FindsDepthParentAndIndex()
        {
            var t = Tree();
            var loc = NodeTree.Locate(t, "A2i");
            Assert.AreEqual(2, loc.Depth);
            Assert.AreEqual("A2", loc.Parent.Id);
            Assert.AreEqual(0, loc.Index);
            Assert.IsNull(NodeTree.Locate(t, "nope"));
            Assert.IsNull(NodeTree.Locate(t, "B").Parent);
        }

        [Test]
        public void AddSibling_Child_Root()
        {
            var t = Tree();
            var x = NodeTree.AddSibling(t, "A1", "X");
            var y = NodeTree.AddSibling(t, "A1", "Y", after: false);
            var z = NodeTree.AddChild(t, "B", "Z");
            var r = NodeTree.AddRoot(t, "R", 0);
            Assert.AreEqual($"{r.Id} A .{y.Id} .A1 .{x.Id} .A2 ..A2i B .{z.Id} C", Flat(t));
            Assert.IsTrue(System.Guid.TryParse(x.Id.Substring(2), out _), "new nodes get GUIDs");
        }

        [Test]
        public void Indent_MakesLastChildOfPreviousSibling_FirstCannot()
        {
            var t = Tree();
            Assert.IsFalse(NodeTree.Indent(t, "A"));
            Assert.IsFalse(NodeTree.Indent(t, "A1"));
            Assert.IsTrue(NodeTree.Indent(t, "C"));
            Assert.AreEqual("A .A1 .A2 ..A2i B .C", Flat(t));
            Assert.IsTrue(NodeTree.Indent(t, "A2"));
            Assert.AreEqual("A .A1 ..A2 ...A2i B .C", Flat(t));
        }

        [Test]
        public void Outdent_MovesAfterParent_FollowingSiblingsBecomeChildren()
        {
            var t = Tree();
            Assert.IsFalse(NodeTree.Outdent(t, "B"));
            Assert.IsTrue(NodeTree.Outdent(t, "A1"));
            Assert.AreEqual("A A1 .A2 ..A2i B C", Flat(t), "A2 followed A1, so it becomes A1's child");
            Assert.IsTrue(NodeTree.Outdent(t, "A2i"));
            Assert.AreEqual("A A1 .A2 .A2i B C", Flat(t));
        }

        [Test]
        public void MoveUpDown_AtEdges()
        {
            var t = Tree();
            Assert.IsFalse(NodeTree.MoveUp(t, "A"));
            Assert.IsTrue(NodeTree.MoveDown(t, "A"));
            Assert.AreEqual("B A .A1 .A2 ..A2i C", Flat(t));
            Assert.IsTrue(NodeTree.MoveDown(t, "A"));
            Assert.IsFalse(NodeTree.MoveDown(t, "A"));
            Assert.IsTrue(NodeTree.MoveUp(t, "A2"));
            Assert.AreEqual("B C A .A2 ..A2i .A1", Flat(t));
        }

        [Test]
        public void Move_UnderNewParent_RefusesOwnSubtree()
        {
            var t = Tree();
            Assert.IsFalse(NodeTree.Move(t, "A", "A2i"));
            Assert.IsTrue(NodeTree.Move(t, "C", "A2", 0));
            Assert.AreEqual("A .A1 .A2 ..C ..A2i B", Flat(t));
            Assert.IsTrue(NodeTree.Move(t, "A2i", null, 0));
            Assert.AreEqual("A2i A .A1 .A2 ..C B", Flat(t));
        }

        [Test]
        public void Remove_TakesSubtree()
        {
            var t = Tree();
            var removed = NodeTree.Remove(t, "A2");
            Assert.AreEqual("A2i", removed.Children[0].Id);
            Assert.AreEqual("A .A1 B C", Flat(t));
            Assert.IsNull(NodeTree.Remove(t, "nope"));
            Assert.AreEqual(4, NodeTree.Count(t));
        }

        [Test]
        public void Relabel_Schemes()
        {
            var t = Tree();
            NodeTree.Relabel(t, LabelScheme.ShotListNotes);
            Assert.AreEqual("1", t[0].Label);
            Assert.AreEqual("b", t[0].Children[1].Label);
            Assert.AreEqual("1", t[0].Children[1].Children[0].Label, "cycles back to numeric");
            Assert.AreEqual("3", t[2].Label);

            NodeTree.Relabel(t, LabelScheme.Outline);
            Assert.AreEqual("a", t[0].Label);
            Assert.AreEqual("2", t[0].Children[1].Label);
            Assert.AreEqual("i", t[0].Children[1].Children[0].Label);

            NodeTree.Relabel(t, LabelScheme.Unlabeled);
            Assert.IsNull(t[0].Label);
        }

        [Test]
        public void LabelFormatting()
        {
            Assert.AreEqual("iv", LabelScheme.Format(LabelStyle.LowerRoman, 4));
            Assert.AreEqual("IX", LabelScheme.Format(LabelStyle.UpperRoman, 9));
            Assert.AreEqual("xiv", LabelScheme.Format(LabelStyle.LowerRoman, 14));
            Assert.AreEqual("z", LabelScheme.Format(LabelStyle.LowerAlpha, 26));
            Assert.AreEqual("aa", LabelScheme.Format(LabelStyle.LowerAlpha, 27));
            Assert.AreEqual("AB", LabelScheme.Format(LabelStyle.UpperAlpha, 28));
            Assert.AreEqual("12", LabelScheme.Format(LabelStyle.Numeric, 12));
        }

        [Test]
        public void LabelDetection_AndInfer()
        {
            Assert.AreEqual(LabelStyle.Numeric, LabelScheme.Detect("3"));
            Assert.AreEqual(LabelStyle.Numeric, LabelScheme.Detect("3."));
            Assert.AreEqual(LabelStyle.LowerAlpha, LabelScheme.Detect("b"));
            Assert.AreEqual(LabelStyle.UpperAlpha, LabelScheme.Detect("B)"));
            Assert.AreEqual(LabelStyle.LowerRoman, LabelScheme.Detect("iv"));
            Assert.AreEqual(LabelStyle.LowerRoman, LabelScheme.Detect("i"));
            Assert.AreEqual(LabelStyle.None, LabelScheme.Detect("1.1"));
            Assert.AreEqual(LabelStyle.None, LabelScheme.Detect(null));

            var sample = Fixtures.LoadSample();
            var notes = LabelScheme.Infer(sample.FindVideo("v-001").Notes, LabelScheme.Unlabeled);
            CollectionAssert.AreEqual(new[] { LabelStyle.Numeric, LabelStyle.LowerAlpha, LabelStyle.Numeric }, notes.Styles);
            var outline = LabelScheme.Infer(sample.Outlines["b-001"].Chapters[0].Sections[0].Nodes, LabelScheme.Unlabeled);
            CollectionAssert.AreEqual(new[] { LabelStyle.LowerAlpha, LabelStyle.Numeric, LabelStyle.LowerRoman }, outline.Styles);

            var unlabeled = new List<Node> { new Node { Id = "x", Text = "x", Children = new List<Node> { new Node { Id = "y", Text = "y" } } } };
            var inferred = LabelScheme.Infer(unlabeled, LabelScheme.Outline);
            CollectionAssert.AreEqual(new[] { LabelStyle.LowerAlpha, LabelStyle.Numeric, LabelStyle.LowerRoman }, inferred.Styles, "falls back per depth, whole cycles");

            // Only one labelled depth observed: deeper levels follow the fallback, and the cycle length stays sane.
            var shallow = new List<Node> { new Node { Id = "x", Label = "a", Text = "x" } };
            var s2 = LabelScheme.Infer(shallow, LabelScheme.Outline);
            Assert.AreEqual(LabelStyle.Numeric, s2.StyleAt(1));
            Assert.AreEqual(LabelStyle.LowerRoman, s2.StyleAt(2));
            Assert.AreEqual(LabelStyle.LowerAlpha, s2.StyleAt(3));
            var threeDeep = LabelScheme.Infer(sample.FindVideo("v-001").Notes, LabelScheme.ShotListNotes);
            Assert.AreEqual(LabelStyle.LowerAlpha, threeDeep.StyleAt(3), "1 / a / 1 / a continues");
        }

        [Test]
        public void Clone_NewIds_KeepsShape()
        {
            var t = Tree();
            var c = NodeTree.Clone(t, newIds: true);
            Assert.AreEqual(NodeTree.Count(t), NodeTree.Count(c));
            Assert.AreEqual("A2i", Node.Walk(c).Select(p => p.node).First(n => n.Text == "A2i").Text);
            CollectionAssert.IsEmpty(Node.Walk(c).Select(p => p.node.Id).Intersect(Node.Walk(t).Select(p => p.node.Id)));
            var same = NodeTree.Clone(t, newIds: false);
            CollectionAssert.AreEqual(Node.Walk(t).Select(p => p.node.Id).ToList(), Node.Walk(same).Select(p => p.node.Id).ToList());
        }

        [Test]
        public void NodeListEditor_RelabelsAndNotifies()
        {
            var t = Tree();
            int changes = 0;
            var ed = new NodeListEditor(() => t, n => t = n, LabelScheme.Outline, () => changes++);
            ed.AddChild("B", "new");
            Assert.AreEqual("1", t[1].Children[0].Label, "outline scheme: depth 1 is numeric");
            Assert.AreEqual("b", t[1].Label);
            ed.SetText("C", "See");
            ed.SetLabel("C", "*");
            Assert.AreEqual("*", t[2].Label);
            ed.MoveUp("C");
            Assert.AreEqual("b", t[1].Label, "structural change relabels the hand-set label too");
            ed.Replace(new List<Node> { NodeTree.NewNode("only") });
            Assert.AreEqual(1, t.Count);
            Assert.AreEqual("a", t[0].Label);
            ed.Append(new[] { NodeTree.NewNode("child") }, t[0].Id);
            Assert.AreEqual("1", t[0].Children[0].Label);
            Assert.AreEqual(6, changes);
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using MediaTrip.Model;
using MediaTrip.Status;
using NUnit.Framework;

namespace MediaTrip.Tests
{
    public class ResolverTests
    {
        // ---------------- Sample trip ----------------

        [Test]
        public void Sample_Combine_SupersedesSources_AndResolvesToResult()
        {
            var plan = PlanResolver.Resolve(Fixtures.LoadSample());
            Assert.IsEmpty(plan.Issues, string.Join("\n", plan.Issues));

            var v1 = plan.FindItem("v-001");
            var v2 = plan.FindItem("v-002");
            var combined = plan.FindItem("v-c001");
            Assert.IsNotNull(combined, "combine result exists as a plan item");

            Assert.AreEqual(PlanItemStatus.Superseded, v1.Status);
            Assert.AreEqual(PlanItemStatus.Superseded, v2.Status);
            CollectionAssert.AreEqual(new[] { "v-c001" }, v1.ResultIds);
            CollectionAssert.AreEqual(new[] { "v-c001" }, v2.ResultIds);

            // Looking up ANY source lands on what was actually shot.
            Assert.AreSame(combined, plan.Resolve("v-001").Single());
            Assert.AreSame(combined, plan.Resolve("v-002").Single());
            Assert.AreEqual("cap-001", plan.CapturesFor("v-002").Single().Id);
            Assert.AreEqual(PlanItemStatus.Captured, v1.EffectiveStatus);
            Assert.AreEqual(PlanItemStatus.Captured, v2.EffectiveStatus);

            Assert.AreEqual(PlanItemOrigin.Combined, combined.Origin);
            Assert.AreEqual("Walkthrough and Entry Point Hazards", combined.Title);
            Assert.AreEqual("1+2", combined.DisplayNumber);
            CollectionAssert.AreEqual(new[] { 1, 2 }, combined.SourceNumbers);
            CollectionAssert.AreEqual(new[] { "v-001", "v-002" }, combined.SourceIds);
            Assert.AreEqual("b-001", combined.BookId);
            Assert.AreEqual("c-101", combined.ChapterId);
            CollectionAssert.AreEquivalent(new[] { "ph-001", "ph-003", "ph-004" }, combined.PhotoRefs);
            CollectionAssert.AreEqual(new[] { "p-006" }, combined.SmeIds);
            Assert.AreEqual(4, combined.Notes.Count, "notes of both sources are carried");
            Assert.AreEqual(PlanItemStatus.Captured, combined.Status);
            Assert.IsTrue(combined.VideoCaptured);
        }

        [Test]
        public void Sample_Rename_KeepsIdAndChangesTitle()
        {
            var plan = PlanResolver.Resolve(Fixtures.LoadSample());
            var v3 = plan.FindItem("v-003");
            Assert.AreEqual("Completing the Confined Space Entry Permit", v3.Title);
            Assert.AreEqual("Filling Out the Entry Permit", v3.OriginalTitle);
            Assert.AreEqual(PlanItemStatus.Captured, v3.Status);
            Assert.AreEqual("Filling Out the Entry Permit", v3.PlannedVideo.Title, "the shot list itself is untouched");
        }

        [Test]
        public void Sample_Drop_Add_AndPhotoRule()
        {
            var plan = PlanResolver.Resolve(Fixtures.LoadSample());

            Assert.AreEqual(PlanItemStatus.Dropped, plan.FindItem("v-005").Status);
            Assert.AreEqual(PlanItemStatus.Dropped, plan.FindItem("v-005").EffectiveStatus);

            var added = plan.FindItem("v-a001");
            Assert.AreEqual(PlanItemOrigin.Added, added.Origin);
            Assert.AreEqual("Group Lock Box Walkthrough", added.Title);
            Assert.AreEqual("b-002", added.BookId);
            Assert.AreEqual("c-201", added.ChapterId);
            Assert.IsNull(added.Number);
            Assert.AreEqual(PlanItemStatus.Captured, added.Status);

            // Video 4 was shot; its ch.2 hero (ph-007) is still missing. Photos are candidates,
            // not requirements, so the video is Captured and the count is exposed separately.
            var v4 = plan.FindItem("v-004");
            Assert.AreEqual(2, v4.PhotoRefsTotal);
            Assert.AreEqual(1, v4.PhotoRefsCaptured);
            Assert.AreEqual("1 of 2", v4.PhotoProgress);
            Assert.AreEqual(PlanItemStatus.Captured, v4.Status);
            Assert.IsNull(plan.FindItem("v-a001").PhotoProgress);

            var planPhotoRule = PlanResolver.Resolve(Fixtures.LoadSample(), new ResolveOptions { PhotoRefsAffectVideoStatus = true });
            Assert.AreEqual(PlanItemStatus.PartiallyCaptured, planPhotoRule.FindItem("v-004").Status, "opt-in switch still works");
        }

        [Test]
        public void Amendments_ApplyInTimestampOrder_NotListOrder()
        {
            // Hand-edited file: the combine that uses split result "a" is listed BEFORE the split.
            var d = Fixtures.SyntheticTrip();
            // "earlier" is 09:00 at UTC-4 = 13:00Z; "later" is 15:00Z. Offsets must be honoured, not just string order.
            d.Captures.Amendments.Add(new Amendment { Id = "later", Type = AmendmentType.Combine, At = "2026-09-15T15:00:00Z", Targets = new List<string> { "a", "v2" }, Results = new List<string> { "c" }, NewTitle = "C" });
            d.Captures.Amendments.Add(new Amendment { Id = "earlier", Type = AmendmentType.Split, At = "2026-09-15T09:00:00-04:00", Targets = new List<string> { "v1" }, Results = new List<string> { "a", "b" }, NewTitles = new List<string> { "A", "B" } });
            var plan = PlanResolver.Resolve(d);
            Assert.IsEmpty(plan.Issues, string.Join("\n", plan.Issues));
            Assert.AreEqual("c", plan.Resolve("v2").Single().Id);
            CollectionAssert.AreEqual(new[] { "c", "b" }, plan.Resolve("v1").Select(i => i.Id).ToList());
            CollectionAssert.AreEqual(new[] { "earlier", "later" }, PlanResolver.OrderedAmendments(d.Captures.Amendments).Select(a => a.Id).ToList());
        }

        [Test]
        public void Amendments_MissingTimestamp_GoLast_TiesKeepListOrder()
        {
            var list = new List<Amendment>
            {
                new Amendment { Id = "x", At = null },
                new Amendment { Id = "y", At = "2026-09-15T10:00:00Z" },
                new Amendment { Id = "z", At = "2026-09-15T10:00:00Z" },
                new Amendment { Id = "w", At = "not a date" },
                new Amendment { Id = "v", At = "2026-09-15T09:00:00Z" },
            };
            CollectionAssert.AreEqual(new[] { "v", "y", "z", "x", "w" }, PlanResolver.OrderedAmendments(list).Select(a => a.Id).ToList());
        }

        [Test]
        public void Sample_PhotoStatuses()
        {
            var plan = PlanResolver.Resolve(Fixtures.LoadSample());
            Assert.AreEqual(PhotoStatus.Captured, plan.FindPhoto("ph-001").Status);
            Assert.AreEqual(PhotoStatus.Captured, plan.FindPhoto("ph-002").Status, "captured via a stand-alone photo capture");
            Assert.AreEqual(1, plan.FindPhoto("ph-002").CapturedInPhotoCaptures.Count);
            Assert.AreEqual(PhotoStatus.Captured, plan.FindPhoto("ph-003").Status);
            Assert.AreEqual(PhotoStatus.Captured, plan.FindPhoto("ph-004").Status);
            Assert.AreEqual(PhotoStatus.Captured, plan.FindPhoto("ph-005").Status);
            Assert.AreEqual(PhotoStatus.Captured, plan.FindPhoto("ph-006").Status);
            Assert.AreEqual(PhotoStatus.NotCaptured, plan.FindPhoto("ph-007").Status);
            Assert.AreEqual(PhotoStatus.Captured, plan.FindPhoto("ph-008").Status);
        }

        [Test]
        public void Sample_OrderIsAsPrinted_WithVirtualItemsAfterTheirSource()
        {
            var plan = PlanResolver.Resolve(Fixtures.LoadSample());
            CollectionAssert.AreEqual(
                new[] { "v-001", "v-c001", "v-002", "v-003", "v-004", "v-005", "v-a001" },
                plan.Items.Select(i => i.Id).ToList());
        }

        // ---------------- Synthetic: split ----------------

        private static Amendment SplitV1(params string[] results) => new Amendment
        {
            Id = "am-split", Type = AmendmentType.Split, At = "2026-09-15T10:00:00Z",
            Targets = new List<string> { "v1" }, Results = results.ToList(),
            NewTitles = results.Select((r, i) => "Part " + (i + 1)).ToList(),
        };

        private static Capture Cap(string id, string planId, string day = "d1", int order = 1) =>
            new Capture { Id = id, DayId = day, CapturedOrder = order, PlanVideoId = planId, Title = planId };

        [Test]
        public void Split_SourceSuperseded_ResolvesToAllParts()
        {
            var d = Fixtures.SyntheticTrip();
            d.Captures.Amendments.Add(SplitV1("v1a", "v1b"));
            var plan = PlanResolver.Resolve(d);
            Assert.IsEmpty(plan.Issues);

            var src = plan.FindItem("v1");
            Assert.AreEqual(PlanItemStatus.Superseded, src.Status);
            CollectionAssert.AreEqual(new[] { "v1a", "v1b" }, plan.Resolve("v1").Select(i => i.Id).ToList());
            Assert.AreEqual(PlanItemStatus.NotCaptured, src.EffectiveStatus);

            var a = plan.FindItem("v1a");
            Assert.AreEqual(PlanItemOrigin.Split, a.Origin);
            Assert.AreEqual(1, a.Number);
            Assert.AreEqual("1a", a.DisplayNumber);
            Assert.AreEqual("1b", plan.FindItem("v1b").DisplayNumber);
            Assert.AreEqual("Part 1", a.Title);
            Assert.AreEqual("c1", a.ChapterId);
            CollectionAssert.AreEqual(new[] { "p1", "p2" }, a.PhotoRefs);
            CollectionAssert.AreEqual(new[] { "v1", "v1a", "v1b", "v2", "v3", "v4", "v5" }, plan.Items.Select(i => i.Id).ToList());
        }

        [Test]
        public void Split_OnePartCaptured_IsPartial_BothCaptured_IsCaptured()
        {
            var d = Fixtures.SyntheticTrip();
            d.Captures.Amendments.Add(SplitV1("v1a", "v1b"));
            d.Captures.Captures.Add(Cap("c1", "v1a"));
            var opts = new ResolveOptions { PhotoRefsAffectVideoStatus = false };

            var plan = PlanResolver.Resolve(d, opts);
            Assert.AreEqual(PlanItemStatus.PartiallyCaptured, plan.FindItem("v1").EffectiveStatus);
            Assert.AreEqual(PlanItemStatus.Captured, plan.FindItem("v1a").Status);
            Assert.AreEqual(PlanItemStatus.NotCaptured, plan.FindItem("v1b").Status);
            Assert.AreEqual("c1", plan.CapturesFor("v1").Single().Id);

            d.Captures.Captures.Add(Cap("c2", "v1b", "d2", 1));
            plan = PlanResolver.Resolve(d, opts);
            Assert.AreEqual(PlanItemStatus.Captured, plan.FindItem("v1").EffectiveStatus);
            CollectionAssert.AreEqual(new[] { "c1", "c2" }, plan.CapturesFor("v1").Select(c => c.Id).ToList());
        }

        [Test]
        public void Split_OnePartDropped_OtherCaptured_IsPartial_BothDropped_IsDropped()
        {
            var d = Fixtures.SyntheticTrip();
            d.Captures.Amendments.Add(SplitV1("v1a", "v1b"));
            d.Captures.Amendments.Add(new Amendment { Id = "am-d", Type = AmendmentType.Drop, Targets = new List<string> { "v1b" } });
            d.Captures.Captures.Add(Cap("c1", "v1a"));
            var opts = new ResolveOptions { PhotoRefsAffectVideoStatus = false };
            var plan = PlanResolver.Resolve(d, opts);
            Assert.AreEqual(PlanItemStatus.PartiallyCaptured, plan.FindItem("v1").EffectiveStatus);

            d.Captures.Captures.Clear();
            d.Captures.Amendments.Add(new Amendment { Id = "am-d2", Type = AmendmentType.Drop, Targets = new List<string> { "v1a" } });
            plan = PlanResolver.Resolve(d, opts);
            Assert.AreEqual(PlanItemStatus.Dropped, plan.FindItem("v1").EffectiveStatus);
        }

        // ---------------- Synthetic: combine chains and mirrors ----------------

        [Test]
        public void Combine_ThenCombineAgain_ResolvesThroughTheChain()
        {
            var d = Fixtures.SyntheticTrip();
            d.Captures.Amendments.Add(new Amendment { Id = "a1", Type = AmendmentType.Combine, Targets = new List<string> { "v1", "v2" }, Results = new List<string> { "x" }, NewTitle = "X" });
            d.Captures.Amendments.Add(new Amendment { Id = "a2", Type = AmendmentType.Combine, Targets = new List<string> { "x", "v3" }, Results = new List<string> { "y" }, NewTitle = "Y" });
            d.Captures.Captures.Add(Cap("c", "y"));
            var plan = PlanResolver.Resolve(d, new ResolveOptions { PhotoRefsAffectVideoStatus = false });
            Assert.IsEmpty(plan.Issues);

            foreach (var id in new[] { "v1", "v2", "v3", "x" })
            {
                Assert.AreEqual(PlanItemStatus.Superseded, plan.FindItem(id).Status, id);
                Assert.AreEqual("y", plan.Resolve(id).Single().Id, id);
                Assert.AreEqual(PlanItemStatus.Captured, plan.FindItem(id).EffectiveStatus, id);
            }
            var y = plan.FindItem("y");
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, y.SourceNumbers);
            Assert.AreEqual("1+2+3", y.DisplayNumber);
            CollectionAssert.AreEqual(new[] { "v1", "x", "y", "v2", "v3", "v4", "v5" }, plan.Items.Select(i => i.Id).ToList());
        }

        [Test]
        public void Combine_ThenSplitTheResult_IsTheMirrorCase()
        {
            var d = Fixtures.SyntheticTrip();
            d.Captures.Amendments.Add(new Amendment { Id = "a1", Type = AmendmentType.Combine, Targets = new List<string> { "v1", "v2" }, Results = new List<string> { "x" }, NewTitle = "X" });
            d.Captures.Amendments.Add(new Amendment { Id = "a2", Type = AmendmentType.Split, Targets = new List<string> { "x" }, Results = new List<string> { "xa", "xb" }, NewTitles = new List<string> { "XA", "XB" } });
            d.Captures.Captures.Add(Cap("c", "xb"));
            var plan = PlanResolver.Resolve(d, new ResolveOptions { PhotoRefsAffectVideoStatus = false });

            CollectionAssert.AreEqual(new[] { "xa", "xb" }, plan.Resolve("v1").Select(i => i.Id).ToList());
            CollectionAssert.AreEqual(new[] { "xa", "xb" }, plan.Resolve("v2").Select(i => i.Id).ToList());
            Assert.AreEqual(PlanItemStatus.PartiallyCaptured, plan.FindItem("v1").EffectiveStatus);
            Assert.AreEqual(PlanItemStatus.PartiallyCaptured, plan.FindItem("v2").EffectiveStatus);
            Assert.AreEqual(PlanItemStatus.PartiallyCaptured, plan.FindItem("x").EffectiveStatus);
            Assert.AreEqual("XB", plan.FindItem("xb").Title);
        }

        [Test]
        public void Combine_MissingTitle_JoinsSourceTitles_AndUnknownTargetIsReported()
        {
            var d = Fixtures.SyntheticTrip();
            d.Captures.Amendments.Add(new Amendment { Id = "a1", Type = AmendmentType.Combine, Targets = new List<string> { "v1", "v2", "ghost" }, Results = new List<string> { "x" } });
            var plan = PlanResolver.Resolve(d);
            Assert.AreEqual("Video 1 + Video 2", plan.FindItem("x").Title);
            Assert.AreEqual(1, plan.Issues.Count);
            StringAssert.Contains("ghost", plan.Issues[0]);
        }

        [Test]
        public void Move_ChangesChapterAndBook_ShotListUntouched()
        {
            var d = Fixtures.SyntheticTrip();
            d.Captures.Amendments.Add(new Amendment { Id = "a1", Type = AmendmentType.Move, Targets = new List<string> { "v1" }, Results = new List<string> { "v1" }, NewChapterId = "c2" });
            var plan = PlanResolver.Resolve(d);
            Assert.AreEqual("c2", plan.FindItem("v1").ChapterId);
            Assert.AreEqual("b1", plan.FindItem("v1").BookId);
            Assert.AreEqual("c1", d.FindVideo("v1").ChapterId);
        }

        [Test]
        public void Cycle_DoesNotHang()
        {
            var d = Fixtures.SyntheticTrip();
            // Nonsense data: x supersedes v1, and v1 supersedes x. Must terminate.
            d.Captures.Amendments.Add(new Amendment { Id = "a1", Type = AmendmentType.Combine, Targets = new List<string> { "v1", "v2" }, Results = new List<string> { "x" } });
            d.Captures.Amendments.Add(new Amendment { Id = "a2", Type = AmendmentType.Split, Targets = new List<string> { "x" }, Results = new List<string> { "v1" } });
            var plan = PlanResolver.Resolve(d);
            Assert.IsNotNull(plan.Resolve("v1"));
            Assert.IsNotEmpty(plan.Issues, "the bogus split result id already exists and is reported");
        }

        [Test]
        public void UnmatchedAndUnplannedCaptures_AreSurfaced()
        {
            var d = Fixtures.SyntheticTrip();
            d.Captures.Captures.Add(Cap("c1", "nope"));
            d.Captures.Captures.Add(Cap("c2", null, "d1", 2));
            var plan = PlanResolver.Resolve(d);
            Assert.AreEqual("c1", plan.UnmatchedCaptures.Single().Id);
            Assert.AreEqual("c2", plan.UnplannedCaptures.Single().Id);
            Assert.AreEqual(1, plan.Issues.Count);
        }

        [Test]
        public void Aggregate_Table()
        {
            var C = PlanItemStatus.Captured; var N = PlanItemStatus.NotCaptured; var P = PlanItemStatus.PartiallyCaptured; var D = PlanItemStatus.Dropped;
            Assert.AreEqual(C, PlanResolver.Aggregate(new[] { C, C }, N));
            Assert.AreEqual(D, PlanResolver.Aggregate(new[] { D, D }, N));
            Assert.AreEqual(N, PlanResolver.Aggregate(new[] { N, D }, C));
            Assert.AreEqual(N, PlanResolver.Aggregate(new[] { N, N }, C));
            Assert.AreEqual(P, PlanResolver.Aggregate(new[] { C, N }, N));
            Assert.AreEqual(P, PlanResolver.Aggregate(new[] { C, D }, N));
            Assert.AreEqual(P, PlanResolver.Aggregate(new[] { P }, N));
            Assert.AreEqual(N, PlanResolver.Aggregate(new PlanItemStatus[0], N));
        }
    }
}

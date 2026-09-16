using System;
using System.Linq;
using MediaTrip.Authoring;
using MediaTrip.Model;
using MediaTrip.Persistence;
using MediaTrip.Session;
using NUnit.Framework;

namespace MediaTrip.Tests
{
    public class ShotListEditorTests
    {
        private string _temp;

        [SetUp] public void SetUp() => _temp = Fixtures.NewTempFolder("planedit");
        [TearDown] public void TearDown() => Fixtures.DeleteFolder(_temp);

        private TripSession Sample()
        {
            var data = Fixtures.LoadSample();
            data.FolderPath = _temp;
            return new TripSession(data, () => 0);
        }

        private static TripSession Synthetic() => new TripSession(Fixtures.SyntheticTrip(), () => 0);

        private static int[] Numbers(TripSession s) => s.Data.ShotList.Videos.Select(v => v.Number).ToArray();
        private static string[] Ids(TripSession s) => s.Data.ShotList.Videos.Select(v => v.Id).ToArray();

        // ---------------- Numbering ----------------

        [Test]
        public void InsertVideo_RenumbersContinuously_CapturesKeepPlannedNumber()
        {
            var s = Sample();
            var before = s.Data.FindCapture("cap-002").PlannedNumber;   // 3, shot against v-003
            var v = s.PlanEditor.AddVideo("c-101", "Inserted first", index: 0);

            CollectionAssert.AreEqual(new[] { v.Id, "v-001", "v-002", "v-003", "v-004", "v-005" }, Ids(s));
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5, 6 }, Numbers(s));
            Assert.AreEqual(4, s.Data.FindVideo("v-003").Number, "video 3 is now printed as 4");
            Assert.AreEqual(before, s.Data.FindCapture("cap-002").PlannedNumber, "the capture still says 3");
            Assert.AreEqual(3, s.Data.FindCapture("cap-002").PlannedNumber);
            Assert.AreEqual("b-001", v.BookId);
            Assert.IsTrue(s.IsDirty);

            // The resolved plan reflects the new number, and the capture still resolves.
            Assert.AreEqual(4, s.Plan.FindItem("v-003").Number);
            Assert.AreEqual("cap-002", s.Plan.CapturesFor("v-003").Single().Id);
        }

        [Test]
        public void AddVideo_ToLaterBook_ContinuesNumbering_AcrossBooks()
        {
            var s = Sample();
            var v = s.PlanEditor.AddVideo("c-202", "After try step");
            Assert.AreEqual(6, v.Number);
            var w = s.PlanEditor.AddVideo("c-102", "End of chapter 2 book 1");
            Assert.AreEqual(4, w.Number);
            Assert.AreEqual(7, v.Number, "book 2 shifted by one");
            Assert.AreEqual(5, s.Data.FindVideo("v-004").Number);
        }

        [Test]
        public void RemoveVideo_Unreferenced_Renumbers()
        {
            var s = Synthetic();
            s.PlanEditor.RemoveVideo("v2");
            CollectionAssert.AreEqual(new[] { "v1", "v3", "v4", "v5" }, Ids(s));
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, Numbers(s));
        }

        [Test]
        public void MoveVideo_BetweenChapters_Renumbers()
        {
            var s = Synthetic(); // v1..v3 in c1, v4..v5 in c2
            s.PlanEditor.MoveVideo("v1", "c2", index: 1);
            CollectionAssert.AreEqual(new[] { "v2", "v3", "v4", "v1", "v5" }, Ids(s));
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, Numbers(s));
            Assert.AreEqual("c2", s.Data.FindVideo("v1").ChapterId);
            Assert.AreEqual(4, s.Data.FindVideo("v1").Number);
        }

        [Test]
        public void ReorderVideo_WithinChapter()
        {
            var s = Synthetic();
            s.PlanEditor.ReorderVideo("v3", 0);
            CollectionAssert.AreEqual(new[] { "v3", "v1", "v2", "v4", "v5" }, Ids(s));
            s.PlanEditor.ReorderVideo("v3", 99);
            CollectionAssert.AreEqual(new[] { "v1", "v2", "v3", "v4", "v5" }, Ids(s));
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, Numbers(s));
        }

        [Test]
        public void ReorderVideo_IsAllowedEvenWhenCaptured_ButNumbersShiftOnlyInThePlan()
        {
            var s = Sample();
            s.PlanEditor.ReorderVideo("v-005", 0); // v-005 (dropped, referenced) moves before v-004 in c-202? they are in different chapters
            // v-005 is alone in c-202, so nothing changes.
            Assert.AreEqual(5, s.Data.FindVideo("v-005").Number);
            s.PlanEditor.ReorderVideo("v-002", 0); // v-002 is captured via combine; reorder in c-101
            Assert.AreEqual(1, s.Data.FindVideo("v-002").Number);
            Assert.AreEqual(2, s.Data.FindVideo("v-001").Number);
            Assert.AreEqual(1, s.Data.FindCapture("cap-001").PlannedNumber);
        }

        [Test]
        public void DuplicateVideo_NewIdsForVideoAndNotes_InsertedAfter()
        {
            var s = Sample();
            var copy = s.PlanEditor.DuplicateVideo("v-001");
            Assert.AreNotEqual("v-001", copy.Id);
            Assert.AreEqual(2, copy.Number);
            Assert.AreEqual(3, s.Data.FindVideo("v-002").Number);
            Assert.AreEqual("Walkthrough: Identifying a Double Dead End Space (copy)", copy.Title);
            CollectionAssert.AreEqual(new[] { "ph-001", "ph-003" }, copy.PhotoRefs);
            var originalIds = Node.Walk(s.Data.FindVideo("v-001").Notes).Select(n => n.node.Id).ToList();
            var copyIds = Node.Walk(copy.Notes).Select(n => n.node.Id).ToList();
            Assert.AreEqual(originalIds.Count, copyIds.Count);
            CollectionAssert.IsEmpty(originalIds.Intersect(copyIds));
            Assert.AreEqual("Note ventilation", Node.Walk(copy.Notes).Select(n => n.node).First(n => n.Label == "2" && n.Text.StartsWith("Note")).Text);
        }

        // ---------------- Guard rule ----------------

        [Test]
        public void RemoveVideo_WithCapture_IsBlocked_WithReferenceList()
        {
            var s = Sample();
            var ex = Assert.Throws<PlanEditBlockedException>(() => s.PlanEditor.RemoveVideo("v-004"));
            Assert.AreEqual("v-004", ex.EntityId);
            Assert.IsTrue(ex.References.Any(r => r.Kind == "capture" && r.Id == "cap-003"));
            StringAssert.Contains("drop amendment", ex.Suggestion);
            Assert.IsNotNull(s.Data.FindVideo("v-004"), "nothing changed");
            Assert.IsFalse(s.IsDirty);
        }

        [Test]
        public void RemoveVideo_ReferencedOnlyByAmendment_IsBlocked()
        {
            var s = Sample();
            var ex = Assert.Throws<PlanEditBlockedException>(() => s.PlanEditor.RemoveVideo("v-005")); // dropped, no capture
            Assert.IsTrue(ex.References.All(r => r.Kind == "amendment"));
        }

        [Test]
        public void MoveVideo_WithCapture_IsBlocked_ButMoveAmendmentWorks()
        {
            var s = Sample();
            Assert.Throws<PlanEditBlockedException>(() => s.PlanEditor.MoveVideo("v-004", "c-202"));
            Assert.AreEqual("c-201", s.Data.FindVideo("v-004").ChapterId);
            s.Move("v-004", "b-002", "c-202", "moved in the field");
            Assert.AreEqual("c-202", s.Plan.FindItem("v-004").ChapterId);
            Assert.AreEqual("c-201", s.Data.FindVideo("v-004").ChapterId, "plan document untouched");
        }

        [Test]
        public void Retitle_WithCapture_IsBlocked_ContentEditsAllowed()
        {
            var s = Sample();
            Assert.Throws<PlanEditBlockedException>(() => s.PlanEditor.SetVideoTitle("v-004", "New title"));
            Assert.AreEqual("Applying the First Lock", s.Data.FindVideo("v-004").Title);

            s.PlanEditor.UpdateVideo("v-004", v => { v.SceneDescription = "Changed"; v.Id = "hacked"; v.Number = 99; v.ChapterId = "c-202"; });
            var v4 = s.Data.FindVideo("v-004");
            Assert.AreEqual("Changed", v4.SceneDescription);
            Assert.AreEqual(4, v4.Number);
            Assert.AreEqual("c-201", v4.ChapterId, "identity fields are restored");

            // Amendment results are not plan-document videos.
            Assert.Throws<System.Collections.Generic.KeyNotFoundException>(() => s.PlanEditor.SetVideoTitle("v-a001", "x"));
        }

        [Test]
        public void Retitle_Unreferenced_IsAllowed()
        {
            var s = Synthetic();
            s.PlanEditor.SetVideoTitle("v2", "Renamed in the plan");
            Assert.AreEqual("Renamed in the plan", s.Data.FindVideo("v2").Title);
        }

        // ---------------- Photos ----------------

        [Test]
        public void RemovePhoto_Referenced_Blocked_Unreferenced_StripsPhotoRefs()
        {
            var s = Sample();
            Assert.Throws<PlanEditBlockedException>(() => s.PlanEditor.RemovePhoto("ph-001")); // in cap-001.photos
            Assert.Throws<PlanEditBlockedException>(() => s.PlanEditor.RemovePhoto("ph-002")); // in pc-001

            // ph-007 is the un-captured ch.2 hero of book 2, referenced only by v-004.photoRefs (plan-internal).
            s.PlanEditor.RemovePhoto("ph-007");
            Assert.IsNull(s.Data.FindPhoto("ph-007"));
            CollectionAssert.AreEqual(new[] { "ph-005" }, s.Data.FindVideo("v-004").PhotoRefs);
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, s.PlanEditor.PhotosOfBook("b-002").Select(p => p.Order).ToArray());
        }

        [Test]
        public void AddPhoto_HeroesFirst_OneCoverPerBook_OneHeroPerChapter()
        {
            var s = Synthetic();
            var d1 = s.PlanEditor.AddPhoto("b1", "Detail 1");
            var h2 = s.PlanEditor.AddPhoto("b1", "Ch2 hero", HeroType.ChapterHero, "c2");
            var d0 = s.PlanEditor.AddPhoto("b1", "Detail 0", index: 0);
            CollectionAssert.AreEqual(new[] { "p1", "p2", h2.Id, d0.Id, d1.Id }, s.PlanEditor.PhotosOfBook("b1").Select(p => p.Id).ToArray());
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, s.PlanEditor.PhotosOfBook("b1").Select(p => p.Order).ToArray());

            Assert.Throws<InvalidOperationException>(() => s.PlanEditor.AddPhoto("b1", "Second cover", HeroType.BookCover));
            Assert.Throws<InvalidOperationException>(() => s.PlanEditor.AddPhoto("b1", "Second ch1 hero", HeroType.ChapterHero, "c1"));
            Assert.Throws<ArgumentException>(() => s.PlanEditor.AddPhoto("b1", "Hero without chapter", HeroType.ChapterHero));

            s.PlanEditor.ReorderPhoto(d1.Id, 0);
            CollectionAssert.AreEqual(new[] { "p1", "p2", h2.Id, d1.Id, d0.Id }, s.PlanEditor.PhotosOfBook("b1").Select(p => p.Id).ToArray());
            Assert.Throws<InvalidOperationException>(() => s.PlanEditor.ReorderPhoto("p1", 3));

            s.PlanEditor.UpdatePhoto(d0.Id, p => p.Description = "Detail zero");
            Assert.Throws<InvalidOperationException>(() => s.PlanEditor.UpdatePhoto(d0.Id, p => p.HeroType = HeroType.BookCover));
            Assert.AreEqual(HeroType.None, s.Data.FindPhoto(d0.Id).HeroType, "rolled back");
        }

        [Test]
        public void PhotoRefs_AddRemoveSet()
        {
            var s = Synthetic();
            s.PlanEditor.AddPhotoRef("v2", "p1");
            s.PlanEditor.AddPhotoRef("v2", "p1");
            s.PlanEditor.AddPhotoRef("v2", "p2", 0);
            CollectionAssert.AreEqual(new[] { "p2", "p1" }, s.Data.FindVideo("v2").PhotoRefs);
            Assert.Throws<System.Collections.Generic.KeyNotFoundException>(() => s.PlanEditor.AddPhotoRef("v2", "nope"));
            s.PlanEditor.RemovePhotoRef("v2", "p2");
            s.PlanEditor.SetPhotoRefs("v3", new[] { "p1", "p1", "p2" });
            CollectionAssert.AreEqual(new[] { "p1", "p2" }, s.Data.FindVideo("v3").PhotoRefs);
        }

        // ---------------- Chapters and books ----------------

        [Test]
        public void Chapters_AddReorderRemove_RenumberWithinBook_AndVideosFollow()
        {
            var s = Synthetic();
            var c0 = s.PlanEditor.AddChapter("b1", "Chapter Zero", index: 0);
            Assert.AreEqual(1, c0.Number);
            Assert.AreEqual(2, s.Data.FindChapter("c1").Number);
            var v = s.PlanEditor.AddVideo(c0.Id, "Intro");
            Assert.AreEqual(1, v.Number);
            Assert.AreEqual(2, s.Data.FindVideo("v1").Number);

            s.PlanEditor.ReorderChapter(c0.Id, 2);
            CollectionAssert.AreEqual(new[] { "c1", "c2", c0.Id }, s.Data.ShotList.Chapters.Select(c => c.Id).ToArray());
            Assert.AreEqual(6, v.Number, "its videos moved to the end");
            Assert.AreEqual(1, s.Data.FindVideo("v1").Number);

            Assert.Throws<InvalidOperationException>(() => s.PlanEditor.RemoveChapter(c0.Id), "not empty without cascade");
            s.PlanEditor.RemoveChapter(c0.Id, cascade: true);
            Assert.IsNull(s.Data.FindVideo(v.Id));
            Assert.AreEqual(5, s.Data.ShotList.Videos.Count);
        }

        [Test]
        public void RemoveChapter_CascadeBlockedByCapturedVideo()
        {
            var s = Sample();
            var ex = Assert.Throws<PlanEditBlockedException>(() => s.PlanEditor.RemoveChapter("c-102", cascade: true));
            Assert.IsTrue(ex.References.Any(r => r.Id == "cap-002"));
            Assert.IsNotNull(s.Data.FindChapter("c-102"));
            Assert.IsNotNull(s.Data.FindVideo("v-003"));
        }

        [Test]
        public void Books_AddReorderRemove()
        {
            var s = Synthetic();
            var b2 = s.PlanEditor.AddBook("Book Two");
            Assert.AreEqual(2, b2.Number);
            var c = s.PlanEditor.AddChapter(b2.Id, "B2 Ch1");
            var v = s.PlanEditor.AddVideo(c.Id, "B2 video");
            Assert.AreEqual(6, v.Number);

            s.PlanEditor.ReorderBook(b2.Id, 0);
            Assert.AreEqual(1, b2.Number);
            Assert.AreEqual(1, v.Number, "book two now prints first, so its video is number 1");
            Assert.AreEqual(2, s.Data.FindVideo("v1").Number);

            Assert.Throws<InvalidOperationException>(() => s.PlanEditor.RemoveBook(b2.Id));
            s.PlanEditor.RemoveBook(b2.Id, cascade: true);
            Assert.IsNull(s.Data.FindBook(b2.Id));
            Assert.IsNull(s.Data.FindChapter(c.Id));
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, Numbers(s));
            Assert.AreEqual(1, s.Data.FindBook("b1").Number);
        }

        [Test]
        public void RemoveBook_WithOutline_CascadeDeletesOutlineFile()
        {
            var s = Sample();
            s.SaveAll();
            Assert.IsTrue(System.IO.File.Exists(System.IO.Path.Combine(_temp, "outlines", "book-1.json")));
            // Book 1 has captures -> blocked.
            Assert.Throws<PlanEditBlockedException>(() => s.PlanEditor.RemoveBook("b-001", cascade: true));

            // Make a fresh book with an outline and no references, then remove it.
            var b = s.PlanEditor.AddBook("Temp");
            s.Outline.Ensure(b.Id);
            s.SaveNow();
            Assert.IsTrue(System.IO.File.Exists(System.IO.Path.Combine(_temp, "outlines", "book-3.json")));
            s.PlanEditor.RemoveBook(b.Id, cascade: true);
            s.SaveNow();
            Assert.IsFalse(System.IO.File.Exists(System.IO.Path.Combine(_temp, "outlines", "book-3.json")));
            Assert.IsFalse(s.Data.Outlines.ContainsKey(b.Id));
        }

        [Test]
        public void RemoveBook_ReferencedByCapture_Blocked()
        {
            var s = Sample();
            Assert.Throws<PlanEditBlockedException>(() => s.PlanEditor.RemoveBook("b-002", cascade: true));
        }

        // ---------------- Notes ----------------

        [Test]
        public void Notes_Editor_RelabelsWithExistingScheme_AndMarksDirty()
        {
            var s = Sample();
            var notes = s.PlanEditor.Notes("v-001");
            var added = notes.AddSibling("n-003", "Wrap up on the exit sign");
            Assert.AreEqual("4", added.Label);
            notes.Indent(added.Id);
            Assert.AreEqual("d", added.Label, "now a child of note 3, labelled after a/b/c");
            Assert.AreEqual("1", Node.Find(s.Data.FindVideo("v-001").Notes, "n-003b1").Label, "third level keeps numeric");
            Assert.IsTrue(s.IsDirty);
        }

        [Test]
        public void Sample_RenumberIsIdempotent()
        {
            var s = Sample();
            var before = TripJson.Serialize(s.Data.ShotList);
            s.PlanEditor.Renumber();
            Assert.AreEqual(before, TripJson.Serialize(s.Data.ShotList));
        }

        [Test]
        public void EditedPlan_SavesAndReloads()
        {
            var s = Sample();
            s.SaveAll();
            var v = s.PlanEditor.AddVideo("c-201", "Persisted");
            s.SaveNow();
            var again = TripLoader.Load(_temp);
            Assert.AreEqual(5, again.FindVideo(v.Id).Number);
            Assert.AreEqual(6, again.FindVideo("v-005").Number);
            Assert.AreEqual(3, again.FindCapture("cap-002").PlannedNumber);
        }
    }
}

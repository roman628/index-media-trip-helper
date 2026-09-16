using System;
using System.Linq;
using MediaTrip.Model;
using MediaTrip.Query;
using MediaTrip.Status;
using NUnit.Framework;

namespace MediaTrip.Tests
{
    public class QueryTests
    {
        private TripQueries Q() => new TripQueries(Fixtures.LoadSample());

        [Test]
        public void FullShotList_AndCurrent()
        {
            var q = Q();
            Assert.AreEqual(7, q.FullShotList().Count);
            CollectionAssert.AreEqual(new[] { "v-c001", "v-003", "v-004", "v-005", "v-a001" }, q.CurrentShotList().Select(i => i.Id).ToList());
        }

        [Test]
        public void Remaining_And_Captured_Filters()
        {
            var q = Q();
            CollectionAssert.AreEqual(new[] { "v-004" }, q.Remaining().Select(i => i.Id).ToList());
            CollectionAssert.AreEqual(new[] { "v-c001", "v-003", "v-a001" }, q.Captured().Select(i => i.Id).ToList());
            CollectionAssert.AreEqual(new[] { "v-005" }, q.Dropped().Select(i => i.Id).ToList());
            CollectionAssert.AreEqual(new[] { "v-001", "v-002" }, q.ByStatus(PlanItemStatus.Superseded).Select(i => i.Id).ToList());

            var noPhotoRule = new TripQueries(Fixtures.LoadSample(), new ResolveOptions { PhotoRefsAffectVideoStatus = false });
            Assert.IsEmpty(noPhotoRule.Remaining());
            Assert.AreEqual(4, noPhotoRule.Captured().Count);
        }

        [Test]
        public void ShotListByBook_GroupsInPrintedOrder()
        {
            var grouped = Q().ShotListByBook();
            Assert.AreEqual(2, grouped.Count);
            Assert.AreEqual("b-001", grouped[0].book.Id);
            Assert.AreEqual("c-101", grouped[0].chapters[0].chapter.Id);
            CollectionAssert.AreEqual(new[] { "v-c001" }, grouped[0].chapters[0].items.Select(i => i.Id).ToList());
            CollectionAssert.AreEqual(new[] { "v-003" }, grouped[0].chapters[1].items.Select(i => i.Id).ToList());
            CollectionAssert.AreEqual(new[] { "v-004", "v-a001" }, grouped[1].chapters[0].items.Select(i => i.Id).ToList());
            CollectionAssert.AreEqual(new[] { "v-005" }, grouped[1].chapters[1].items.Select(i => i.Id).ToList());
        }

        [Test]
        public void Heroes_AcrossBooks_WithStatus()
        {
            var heroes = Q().Heroes();
            CollectionAssert.AreEqual(new[] { "ph-001", "ph-002", "ph-003", "ph-005", "ph-006", "ph-007" }, heroes.Select(h => h.Photo.Id).ToList());
            Assert.AreEqual("Book cover", heroes[0].Label);
            Assert.AreEqual("Ch.1 hero", heroes[1].Label);
            Assert.AreEqual("Ch.2 hero", heroes[5].Label);
            Assert.AreEqual("Lockout / Tagout", heroes[5].Book.Name);
            Assert.IsFalse(heroes[5].IsCaptured);
            Assert.AreEqual(5, heroes.Count(h => h.IsCaptured));
            Assert.AreEqual((5, 6), Q().HeroProgress());
            CollectionAssert.AreEqual(new[] { "ph-007" }, Q().RemainingHeroes().Select(h => h.Photo.Id).ToList());
        }

        [Test]
        public void Day_CapturesInOrder_WithPhotosUnderAndAdditional()
        {
            var q = Q();
            CollectionAssert.AreEqual(new[] { "cap-001", "cap-002" }, q.DayCaptures("d-001").Select(c => c.Id).ToList());
            CollectionAssert.AreEqual(new[] { "cap-003", "cap-004" }, q.DayCaptures("d-002").Select(c => c.Id).ToList());

            var day1 = q.DaySummary("d-001");
            Assert.AreEqual("Day 1", day1.Day.Label);
            Assert.AreEqual(2, day1.Entries.Count);
            Assert.AreSame(q.Item("v-c001"), day1.Entries[0].Item);
            Assert.IsEmpty(day1.Entries[0].PhotosUnder);
            CollectionAssert.AreEqual(new[] { "pc-001" }, day1.Entries[1].PhotosUnder.Select(p => p.Id).ToList());
            Assert.IsEmpty(day1.AdditionalPhotography);

            var day2 = q.DaySummary("d-002");
            CollectionAssert.AreEqual(new[] { "pc-002" }, day2.AdditionalPhotography.Select(p => p.Id).ToList());
            Assert.AreEqual("v-a001", day2.Entries[1].Item.Id);

            Assert.AreEqual(2, q.AllDays().Count);
            Assert.AreEqual("d-002", q.Today(new DateTime(2026, 9, 16)).Day.Id);
            Assert.IsNull(q.Today(new DateTime(2026, 9, 20)));
            Assert.AreEqual(4, q.NextCapturedOrder("d-001"));
            Assert.AreEqual(1, q.NextCapturedOrder("d-none"));
        }

        [Test]
        public void Day_UnderVideoPhotoWithoutAfterCaptureId_FilesUnderPrecedingCapture()
        {
            var data = Fixtures.LoadSample();
            data.Captures.PhotoCaptures.Add(new PhotoCapture { Id = "pc-x", DayId = "d-001", CapturedOrder = 2, PhotoId = "ph-004", Section = PhotoCaptureSection.UnderVideo });
            var day1 = new TripQueries(data).DaySummary("d-001");
            CollectionAssert.AreEqual(new[] { "pc-x", "pc-001" }, day1.Entries[1].PhotosUnder.Select(p => p.Id).ToList());
        }

        [Test]
        public void Day_ReorderedCaptureList_StillSortsByCapturedOrder()
        {
            var data = Fixtures.LoadSample();
            data.Captures.Captures.Reverse();
            var q = new TripQueries(data);
            CollectionAssert.AreEqual(new[] { "cap-001", "cap-002" }, q.DayCaptures("d-001").Select(c => c.Id).ToList());
        }

        [Test]
        public void OutlineCoverage_Book1()
        {
            var cov = Q().OutlineCoverage("b-001");
            Assert.IsTrue(cov.HasOutline);
            Assert.AreEqual(2, cov.Chapters.Count);
            Assert.AreEqual(3, cov.SectionsCovered);
            Assert.AreEqual(4, cov.SectionsTotal);

            var s11 = cov.Chapters[0].Sections[0];
            Assert.AreEqual("1.1", s11.Section.Number);
            Assert.AreEqual("cap-001", s11.Assigned.Single().Capture.Id);
            Assert.AreEqual("Walkthrough and Entry Point Hazards", s11.Assigned.Single().Title);
            Assert.IsTrue(s11.HasConfirmed);

            var s12 = cov.Chapters[0].Sections[1];
            Assert.AreEqual(MediaRefKind.PhotoCapture, s12.Assigned.Single().Kind);
            Assert.AreEqual("Ch.1 hero: gallery of confined spaces", s12.Assigned.Single().Title, "photo capture title falls back to the planned photo description");
            Assert.IsTrue(s12.OnlyUnconfirmed);
            Assert.IsTrue(s12.Assigned.Single().IsAutoUnconfirmed);

            Assert.IsFalse(cov.Chapters[1].Sections[0].IsEmpty);
            Assert.IsTrue(cov.Chapters[1].Sections[1].IsEmpty);
            Assert.IsEmpty(cov.Unplaced);
        }

        [Test]
        public void OutlineCoverage_BookWithoutOutline()
        {
            var all = Q().OutlineCoverageAll();
            Assert.AreEqual(2, all.Count);
            Assert.IsFalse(all[1].HasOutline);
            Assert.AreEqual(0, all[1].SectionsTotal);
        }

        [Test]
        public void UnassignedMedia()
        {
            var (caps, pcs) = Q().UnassignedMedia();
            CollectionAssert.AreEqual(new[] { "cap-003", "cap-004" }, caps.Select(c => c.Id).ToList());
            CollectionAssert.AreEqual(new[] { "pc-002" }, pcs.Select(c => c.Id).ToList());
        }

        [Test]
        public void ResolveLookups_FromTheQueryApi()
        {
            var q = Q();
            Assert.AreEqual("v-c001", q.Resolve("v-002").Single().Id);
            Assert.AreEqual("cap-001", q.CapturesFor("v-001").Single().Id);
            Assert.IsEmpty(q.Resolve("nope"));
            Assert.AreEqual(2, q.PeopleOfOrg(Org.Client).Count);
            Assert.AreEqual(1, q.PeopleWithRole(PersonRole.Writer).Count);
        }
    }
}

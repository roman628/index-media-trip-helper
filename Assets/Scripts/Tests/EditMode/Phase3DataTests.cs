using System.IO;
using System.Linq;
using MediaTrip.Model;
using MediaTrip.Persistence;
using MediaTrip.Session;
using MediaTrip.Status;
using MediaTrip.Validation;
using NUnit.Framework;

namespace MediaTrip.Tests
{
    public class PhotoAssociationTests
    {
        [Test]
        public void SharedPhoto_AppearsUnderBothBooks_AndHeroesListsBothChapters()
        {
            var s = new TripSession(Fixtures.LoadSample(), () => 0);
            // ph-006 is book 2's ch.1 hero; say it also serves as book 1 ch.2's hero.
            s.PlanEditor.SetPhotoAssociations("ph-006", new[] { "b-001" }, new[] { "c-102" });
            var p = s.Data.FindPhoto("ph-006");
            Assert.IsTrue(p.IsShared);
            Assert.IsTrue(p.BelongsToBook("b-001"));
            Assert.IsTrue(p.BelongsToChapter("c-102"));
            Assert.AreEqual("b-002", p.BookId, "primary book unchanged");

            var q = s.Queries;
            var book1 = q.PhotosOfBook("b-001");
            Assert.AreEqual("ph-006", book1.Last().Id, "shared photo appended after the book's own photos");
            Assert.AreEqual(4, q.PhotosOfBook("b-001", includeShared: false).Count);
            CollectionAssert.Contains(q.PhotosOfChapter("c-102").Select(x => x.Id).ToList(), "ph-006");

            var heroes = q.Heroes();
            var shared = heroes.Where(h => h.Photo.Id == "ph-006").ToList();
            Assert.AreEqual(2, shared.Count);
            Assert.IsTrue(shared.Any(h => h.IsShared && h.Chapter.Id == "c-102" && h.Book.Id == "b-001"));
            Assert.IsTrue(shared.Any(h => !h.IsShared && h.Chapter.Id == "c-201"));
            Assert.AreEqual((6, 7), q.HeroProgress());

            Assert.AreEqual(5, q.BookStats("b-001").PhotosTotal);
            Assert.IsEmpty(TripValidator.Validate(s.Data));

            // Coverage hints the associated photos per outline chapter.
            var cov = q.OutlineCoverage("b-001");
            CollectionAssert.Contains(cov.Chapters[1].AssociatedPhotos.Select(x => x.Id).ToList(), "ph-006");
        }

        [Test]
        public void Associations_RoundTrip_AndUnknownIdsAreFlagged()
        {
            var temp = Fixtures.NewTempFolder("assoc");
            try
            {
                var d = Fixtures.LoadSample();
                d.FindPhoto("ph-004").AlsoBookIds.Add("b-002");
                TripSaver.SaveAll(d, temp);
                var again = TripLoader.Load(temp);
                CollectionAssert.AreEqual(new[] { "b-002" }, again.FindPhoto("ph-004").AlsoBookIds);
                Assert.IsEmpty(again.FindPhoto("ph-001").AlsoBookIds);

                again.FindPhoto("ph-004").AlsoChapterIds.Add("c-999");
                var issues = TripValidator.Validate(again);
                Assert.IsTrue(issues.Any(i => i.EntityId == "ph-004" && i.Message.Contains("c-999") && i.Document == DocumentKind.ShotList));
            }
            finally { Fixtures.DeleteFolder(temp); }
        }

        [Test]
        public void RemovingChapter_StripsAssociations()
        {
            var s = new TripSession(Fixtures.SyntheticTrip(), () => 0);
            var extra = s.PlanEditor.AddChapter("b1", "Three");
            s.PlanEditor.SetPhotoAssociations("p1", null, new[] { extra.Id });
            s.PlanEditor.RemoveChapter(extra.Id);
            Assert.IsEmpty(s.Data.FindPhoto("p1").AlsoChapterIds);
            Assert.IsEmpty(TripValidator.Validate(s.Data));
        }

        [Test]
        public void SampleRoundTrip_StillSemanticallyIdentical_WithNewFields()
        {
            var temp = Fixtures.NewTempFolder("rt3");
            try
            {
                TripSaver.SaveAll(Fixtures.LoadSample(), temp);
                var a = File.ReadAllText(Path.Combine(Fixtures.SampleFolder, "shotlist.json"));
                var b = File.ReadAllText(Path.Combine(temp, "shotlist.json"));
                Assert.IsTrue(TripJson.SemanticallyEqual(a, b, out var diff), diff);
            }
            finally { Fixtures.DeleteFolder(temp); }
        }
    }

    public class SessionFieldOpsTests
    {
        private static TripSession S() => new TripSession(Fixtures.LoadSample(), () => 0);

        [Test]
        public void Uncheck_RemovesCapturesOfResolvedItem()
        {
            var s = S();
            Assert.AreEqual(1, s.Uncheck("v-001"), "v-001 resolves to the combined item, whose capture goes");
            Assert.IsNull(s.Data.FindCapture("cap-001"));
            Assert.AreEqual(PlanItemStatus.NotCaptured, s.Plan.FindItem("v-c001").Status);
            Assert.IsFalse(s.Data.Captures.OutlineAssignments.Any(a => a.MediaRef.Id == "cap-001"), "its assignment went too");
        }

        [Test]
        public void TogglePhoto_ThreeCases()
        {
            var s = S();
            Assert.IsFalse(s.TogglePhotoCaptured("ph-001", "d-002"), "listed under cap-001 -> flipped off");
            Assert.AreEqual(PhotoStatus.NotCaptured, s.Plan.FindPhoto("ph-001").Status);
            Assert.IsTrue(s.TogglePhotoCaptured("ph-001", "d-002"));

            Assert.IsFalse(s.TogglePhotoCaptured("ph-002", "d-002"), "stand-alone photo capture removed");
            Assert.IsNull(s.Data.FindPhotoCapture("pc-001"));
            Assert.IsFalse(s.Data.Captures.OutlineAssignments.Any(a => a.Id == "oa-003"));

            Assert.IsTrue(s.TogglePhotoCaptured("ph-007", "d-002"), "nothing yet -> new photo capture on the day");
            var pc = s.Data.Captures.PhotoCaptures.Single(p => p.PhotoId == "ph-007");
            Assert.AreEqual("d-002", pc.DayId);
            Assert.AreEqual(PhotoCaptureSection.AdditionalPhotography, pc.Section);
            Assert.AreEqual(PhotoStatus.Captured, s.Plan.FindPhoto("ph-007").Status);
        }

        [Test]
        public void UndoAmendment_Combine_RepointsCaptureToFirstSource()
        {
            var s = S();
            s.UndoAmendment("am-001");
            Assert.IsNull(s.Data.FindAmendment("am-001"));
            var cap = s.Data.FindCapture("cap-001");
            Assert.AreEqual("v-001", cap.PlanVideoId);
            Assert.AreEqual("Walkthrough: Identifying a Double Dead End Space", cap.Title);
            Assert.AreEqual(1, cap.PlannedNumber);
            Assert.AreEqual(PlanItemStatus.Captured, s.Plan.FindItem("v-001").Status);
            Assert.AreEqual(PlanItemStatus.NotCaptured, s.Plan.FindItem("v-002").Status);
            Assert.IsEmpty(TripValidator.Validate(s.Data));
        }

        [Test]
        public void UndoAmendment_Rename_RestoresTitle_Add_RemovesCaptureWhenAsked_Drop_JustGoes()
        {
            var s = S();
            s.UndoAmendment("am-002");
            Assert.AreEqual("Filling Out the Entry Permit", s.Data.FindCapture("cap-002").Title);
            s.UndoAmendment("am-003", deleteCaptures: true);
            Assert.IsNull(s.Data.FindCapture("cap-004"));
            Assert.IsNull(s.Plan.FindItem("v-a001"));
            s.UndoAmendment("am-004");
            Assert.AreEqual(PlanItemStatus.NotCaptured, s.Plan.FindItem("v-005").Status);
            Assert.AreEqual(1, s.Data.Captures.Amendments.Count);
            Assert.IsEmpty(TripValidator.Validate(s.Data));
        }

        [Test]
        public void UpdateAmendment_TitleFollowsIntoCaptures()
        {
            var s = S();
            s.UpdateAmendment("am-001", a => { a.NewTitle = "Walkthrough (final)"; a.Reason = "edited"; });
            Assert.AreEqual("Walkthrough (final)", s.Data.FindCapture("cap-001").Title);
            Assert.AreEqual("Walkthrough (final)", s.Plan.FindItem("v-c001").Title);
        }

        [Test]
        public void AdoptTypedPerson_LinksCaptures()
        {
            var s = S();
            var typed = s.TypedButUnregisteredPeople();
            Assert.AreEqual(1, typed.Count);
            Assert.AreEqual("Owen Pratt", typed[0].name);
            var p = s.AdoptTypedPerson("Owen Pratt", "Electrician");
            Assert.AreEqual(p.Id, s.Data.FindCapture("cap-003").People[1].PersonId);
            Assert.IsEmpty(s.TypedButUnregisteredPeople());
        }

        [Test]
        public void RemoveDay_BlockedWhileCapturesExist()
        {
            var s = S();
            Assert.Throws<MediaTrip.Authoring.PlanEditBlockedException>(() => s.RemoveDay("d-001"));
            var d = s.AddDay("2026-09-17", "Day 3");
            s.RemoveDay(d.Id);
            Assert.AreEqual(2, s.Data.Trip.Days.Count);
        }
    }

    public class BundleTests
    {
        private string _temp;
        [SetUp] public void SetUp() => _temp = Fixtures.NewTempFolder("bundle");
        [TearDown] public void TearDown() => Fixtures.DeleteFolder(_temp);

        [Test]
        public void Bundle_RoundTrips_ThroughText()
        {
            var lib = Path.Combine(_temp, "Trips");
            var json = TripPackage.ExportBundleJson(Fixtures.LoadSample());
            Assert.IsTrue(TripPackage.IsBundleJson(json));
            var report = TripPackage.ValidateJsonText(json, lib, "clipboard");
            Assert.IsTrue(report.IsWholeTrip);
            Assert.AreEqual("bundle", report.Format);
            Assert.IsTrue(report.CanImport, string.Join("\n", report.Issues));
            Assert.AreEqual(4, report.Files.Count);
            Assert.IsFalse(Directory.Exists(lib));

            var dest = TripPackage.ImportBundleJson(json, libraryRoot: lib);
            foreach (var f in new[] { "trip.json", "shotlist.json", "captures.json", Path.Combine("outlines", "book-1.json") })
                Assert.IsTrue(TripJson.SemanticallyEqual(File.ReadAllText(Path.Combine(Fixtures.SampleFolder, f)), File.ReadAllText(Path.Combine(dest, f)), out var diff), f + ": " + diff);
        }

        [Test]
        public void SingleDocument_Detection()
        {
            var d = Fixtures.LoadSample();
            Assert.AreEqual(DocumentKind.ShotList, TripPackage.ValidateJsonText(TripPackage.ExportDocumentJson(d, DocumentKind.ShotList)).SingleDocumentKind);
            Assert.AreEqual(DocumentKind.Captures, TripPackage.ValidateJsonText(TripPackage.ExportDocumentJson(d, DocumentKind.Captures)).SingleDocumentKind);
            Assert.AreEqual(DocumentKind.Trip, TripPackage.ValidateJsonText(TripPackage.ExportDocumentJson(d, DocumentKind.Trip)).SingleDocumentKind);
            Assert.AreEqual(DocumentKind.Outline, TripPackage.ValidateJsonText(TripPackage.ExportDocumentJson(d, DocumentKind.Outline, "b-001")).SingleDocumentKind);
            var r = TripPackage.ValidateJsonText("{ \"hello\": 1 }");
            Assert.IsFalse(r.CanImport);
            Assert.IsNull(r.SingleDocumentKind);
            Assert.IsFalse(TripPackage.ValidateJsonText("not json").CanImport);
            Assert.IsFalse(TripPackage.ValidateJsonText("{ \"mediaTripBundle\": 99, \"files\": {} }").CanImport);
        }

        [Test]
        public void TryExportZip_ReportsInsteadOfThrowing()
        {
            var ok = TripPackage.TryExportZip(Fixtures.LoadSample(), Path.Combine(_temp, "sub", "t.zip"), out var err);
            Assert.IsTrue(ok, err);
            Assert.IsNull(err);
            var bad = TripPackage.TryExportZip(Fixtures.LoadSample(), _temp + Path.DirectorySeparatorChar + "nope\0bad.zip", out var err2);
            Assert.IsFalse(bad);
            Assert.IsNotNull(err2);
        }

        [Test]
        public void EnsureSampleIfEmpty_ImportsOnce()
        {
            var lib = Path.Combine(_temp, "Trips");
            Assert.IsNotNull(TripLibrary.EnsureSampleIfEmpty(lib));
            Assert.IsNull(TripLibrary.EnsureSampleIfEmpty(lib));
            Assert.AreEqual(1, TripLibrary.ListTrips(lib).Count);
        }
    }
}

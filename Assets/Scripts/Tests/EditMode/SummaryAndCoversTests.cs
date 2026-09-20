using System.IO;
using System.Linq;
using MediaTrip.Authoring;
using MediaTrip.Model;
using MediaTrip.Persistence;
using MediaTrip.Session;
using MediaTrip.Validation;
using NUnit.Framework;

namespace MediaTrip.Tests
{
    public class DayTimelineTests
    {
        [Test]
        public void Timeline_IsFlat_InCaptureOrder()
        {
            var s = new TripSession(Fixtures.LoadSample(), () => 0);
            var day1 = s.Queries.DayTimeline("d-001");
            CollectionAssert.AreEqual(new[] { "cap-001", "cap-002", "pc-001" }, day1.Select(e => e.Id).ToList());
            Assert.IsTrue(day1[0].IsVideo);
            Assert.IsFalse(day1[2].IsVideo);
            Assert.AreEqual("v-c001", day1[0].Item.Id);
            Assert.IsEmpty(s.Queries.DayTimeline("no-such-day"));
        }

        [Test]
        public void ReorderDay_RewritesOrders_AcrossVideosAndPhotos()
        {
            var s = new TripSession(Fixtures.LoadSample(), () => 0);
            s.ReorderDay("d-001", new[] { "pc-001", "cap-001", "cap-002" });
            CollectionAssert.AreEqual(new[] { "pc-001", "cap-001", "cap-002" }, s.Queries.DayTimeline("d-001").Select(e => e.Id).ToList());
            Assert.AreEqual(1, s.Data.FindPhotoCapture("pc-001").CapturedOrder);
            Assert.AreEqual(2, s.Data.FindCapture("cap-001").CapturedOrder);
            Assert.AreEqual(3, s.Data.FindCapture("cap-002").CapturedOrder);

            // ids that are not listed keep their relative order after the listed ones
            s.ReorderDay("d-001", new[] { "cap-002" });
            CollectionAssert.AreEqual(new[] { "cap-002", "pc-001", "cap-001" }, s.Queries.DayTimeline("d-001").Select(e => e.Id).ToList());

            // the other day is untouched
            CollectionAssert.AreEqual(new[] { "cap-003", "cap-004", "pc-002" }, s.Queries.DayTimeline("d-002").Select(e => e.Id).ToList());
        }

        [Test]
        public void MoveDayEntry_StepsOne_AndStopsAtTheEnds()
        {
            var s = new TripSession(Fixtures.LoadSample(), () => 0);
            Assert.IsFalse(s.MoveDayEntry("d-001", "cap-001", -1));
            Assert.IsTrue(s.MoveDayEntry("d-001", "cap-001", 1));
            CollectionAssert.AreEqual(new[] { "cap-002", "cap-001", "pc-001" }, s.Queries.DayTimeline("d-001").Select(e => e.Id).ToList());
            Assert.IsTrue(s.MoveDayEntry("d-001", "cap-001", 1));
            Assert.IsFalse(s.MoveDayEntry("d-001", "cap-001", 1));
            Assert.IsFalse(s.MoveDayEntry("d-001", "nope", 1));
        }

        [Test]
        public void NewCaptures_AreTimestamped_AndAppendAtTheBottom()
        {
            var s = new TripSession(Fixtures.LoadSample(), () => 0);
            var cap = s.AddCapture(new Capture { DayId = "d-002", Title = "Extra" });
            var pc = s.AddPhotoCapture(new PhotoCapture { DayId = "d-002", Text = "Sign" });
            Assert.IsNotNull(cap.At);
            Assert.IsNotNull(pc.At);
            var ids = s.Queries.DayTimeline("d-002").Select(e => e.Id).ToList();
            Assert.AreEqual(cap.Id, ids[ids.Count - 2]);
            Assert.AreEqual(pc.Id, ids[ids.Count - 1]);
        }
    }

    public class CoverBoardTests
    {
        [Test]
        public void Board_HasCoverAndChapterSlots_PlannedHeroFillsWhenCaptured()
        {
            var s = new TripSession(Fixtures.LoadSample(), () => 0);
            var board = s.Queries.CoverBoard();
            Assert.AreEqual(2, board.Count);
            Assert.AreEqual(3, board[0].Slots.Count, "cover + two chapters");
            Assert.IsTrue(board[0].Slots[0].IsCover);
            Assert.AreEqual("ph-001", board[0].Slots[0].Planned.Id);
            Assert.IsFalse(board[0].Slots[0].IsEmpty);
            Assert.IsTrue(board[0].Slots[0].Assigned[0].IsPlanned);

            var empty = board.SelectMany(b => b.Slots).Where(x => x.IsEmpty).ToList();
            Assert.AreEqual(1, empty.Count);
            Assert.AreEqual("c-202", empty[0].Chapter.Id);
            Assert.AreEqual("ph-007", empty[0].Planned.Id, "planned stays visible on an empty slot");
        }

        [Test]
        public void Assign_ExistingPhoto_OrNewText_KeepsPlannedVisible()
        {
            var s = new TripSession(Fixtures.LoadSample(), () => 0);
            var a = s.AssignHero("b-002", "c-202", "ph-008", null);
            var again = s.AssignHero("b-002", "c-202", "ph-008", null);
            Assert.AreSame(a, again, "assigning the same photo twice is one assignment");
            var b = s.AssignHero("b-002", "c-202", null, "  Owen's lock on the breaker ");

            var slot = s.Queries.CoverSlot("b-002", "c-202");
            Assert.AreEqual("ph-007", slot.Planned.Id);
            Assert.AreEqual(2, slot.Assigned.Count);
            Assert.IsFalse(slot.Assigned[0].IsNew);
            Assert.AreEqual("ph-008", slot.Assigned[0].Photo.Id);
            Assert.IsTrue(slot.Assigned[1].IsNew);
            Assert.AreEqual("Owen's lock on the breaker", slot.Assigned[1].Text);
            Assert.AreEqual(HeroType.ChapterHero, s.Data.FindPhoto("ph-007").HeroType, "the plan is not edited");

            s.RemoveHeroAssignment(a.Id);
            s.RemoveHeroAssignment(b.Id);
            Assert.IsTrue(s.Queries.CoverSlot("b-002", "c-202").IsEmpty);
            Assert.Throws<System.ArgumentException>(() => s.AssignHero("b-002", "c-202", null, " "));
        }

        [Test]
        public void CoverSlot_UsesNullChapterForTheCover()
        {
            var s = new TripSession(Fixtures.LoadSample(), () => 0);
            s.AssignHero("b-001", null, null, "Wide of the plant at dusk");
            var cover = s.Queries.CoverSlot("b-001", null);
            Assert.IsTrue(cover.IsCover);
            Assert.AreEqual(2, cover.Assigned.Count, "captured planned cover plus the new one");
            Assert.AreEqual(1, s.Queries.CoverSlot("b-001", "c-101").Assigned.Count, "chapter slots do not pick up the cover's assignment");
            Assert.IsNull(s.Queries.CoverSlot("b-001", "no-such-chapter"));
        }

        [Test]
        public void AssignedPhoto_CannotBeDeletedFromThePlan()
        {
            var s = new TripSession(Fixtures.LoadSample(), () => 0);
            s.AssignHero("b-002", "c-202", "ph-007", null);
            Assert.Throws<PlanEditBlockedException>(() => s.PlanEditor.RemovePhoto("ph-007"));
        }

        [Test]
        public void RemovingThePhotoCapture_UnlinksTheAssignment()
        {
            var s = new TripSession(Fixtures.LoadSample(), () => 0);
            var pc = s.AddPhotoCapture(new PhotoCapture { DayId = "d-002", Text = "New hero" });
            var a = s.AssignHero("b-002", "c-202", null, "New hero", pc.Id);
            Assert.AreEqual(pc.Id, a.PhotoCaptureId);
            s.RemovePhotoCapture(pc.Id);
            Assert.IsNull(a.PhotoCaptureId);
            Assert.IsFalse(s.Queries.CoverSlot("b-002", "c-202").IsEmpty, "the assignment stays");
        }
    }

    public class SectionNoteTests
    {
        [Test]
        public void Set_Get_Clear()
        {
            var s = new TripSession(Fixtures.LoadSample(), () => 0);
            Assert.AreEqual("", s.NoteText("c-101", "s-1-1"));
            s.SetNote("b-001", "c-101", "s-1-1", "Gallery wide first.");
            s.SetNote("b-001", "c-101", "s-1-1", "Gallery wide first, then the hatch.");
            Assert.AreEqual(2, s.Data.Captures.Notes.Count, "the section's note and the chapter's own");
            Assert.AreEqual("Gallery wide first, then the hatch.", s.NoteText("c-101", "s-1-1"));
            Assert.AreEqual("Consider opening the chapter on the gallery wide shot.", s.NoteText("c-101"), "the chapter as a whole has its own note");
            var outline = TripPackage.ExportDocumentJson(s.Data, DocumentKind.Outline, "b-001");
            Assert.IsFalse(outline.Contains("Gallery wide first"), "the outline document is not where notes go");
            s.SetNote("b-001", "c-101", "s-1-1", "  ");
            Assert.AreEqual(1, s.Data.Captures.Notes.Count);

            // a book with no outline still has chapters, and a chapter can carry a note
            s.SetNote("b-002", "c-202", null, "Try for the dead panel shot.");
            Assert.AreEqual("Try for the dead panel shot.", s.NoteText("c-202"));
        }

        [Test]
        public void Validator_FlagsDanglingNewReferences()
        {
            var s = new TripSession(Fixtures.LoadSample(), () => 0);
            Assert.IsEmpty(TripValidator.Validate(s.Data));
            s.SetNote("b-001", "c-101", "s-gone", "orphan");
            s.Data.Captures.HeroAssignments.Add(new HeroAssignment { Id = "ha-x", BookId = "b-001", ChapterId = "c-101", PhotoId = "ph-missing" });
            var issues = TripValidator.Validate(s.Data);
            Assert.IsTrue(issues.Any(i => i.Severity == IssueSeverity.Warning && i.EntityId == "s-gone"));
            Assert.IsTrue(issues.Any(i => i.Severity == IssueSeverity.Error && i.EntityId == "ha-x"));
        }

        [Test]
        public void NewFields_RoundTripThroughDisk()
        {
            var folder = Fixtures.NewTempFolder("covers");
            try
            {
                var data = Fixtures.LoadSample();
                data.FolderPath = folder;
                var s = new TripSession(data, () => 0);
                s.SetNote("b-001", "c-101", "s-1-2", "Two exits blocked.");
                s.AssignHero("b-002", "c-202", null, "Operator at the dead panel");
                s.AddCapture(new Capture { DayId = "d-002", Title = "Stamped" });
                s.SaveAll();

                var back = TripLoader.Load(folder);
                Assert.AreEqual("Two exits blocked.", back.Captures.Notes.Single(n => n.SectionId == "s-1-2").Text);
                Assert.AreEqual("Operator at the dead panel", back.Captures.HeroAssignments.Single().Text);
                Assert.IsNotNull(back.Captures.Captures.Last().At);
                Assert.IsNull(back.Captures.Captures.First().At, "older captures have no timestamp and none is invented");
                Assert.IsTrue(TripJson.SemanticallyEqual(
                    File.ReadAllText(Path.Combine(folder, TripLoader.CapturesFile)), TripSaver.ToJson(back.Captures), out var diff), diff);
            }
            finally { Fixtures.DeleteFolder(folder); }
        }
    }
}

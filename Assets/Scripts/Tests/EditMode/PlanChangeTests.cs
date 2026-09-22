using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MediaTrip.Authoring;
using MediaTrip.Export;
using MediaTrip.Model;
using MediaTrip.Persistence;
using MediaTrip.Session;
using MediaTrip.Status;
using MediaTrip.Validation;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace MediaTrip.Tests
{
    public class ChapterUnificationTests
    {
        private static OutlineDocument DriftedOutline() => new OutlineDocument
        {
            BookId = "b1",
            Chapters = new List<OutlineChapter>
            {
                // same number as the plan's chapter 1, its own id and a slightly different name
                new OutlineChapter { Id = "oc-a", Number = 1, Name = "Chapter 1 (outline wording)", Notes = "Open wide.",
                    Sections = new List<OutlineSection> { new OutlineSection { Id = "s-a1", Name = "First" } } },
                // no number; matches the plan's chapter two by name
                new OutlineChapter { Id = "oc-b", Number = 0, Name = "chapter two",
                    Sections = new List<OutlineSection> { new OutlineSection { Id = "s-b1", Name = "Second" } } },
                // matches nothing: typed into the outline only
                new OutlineChapter { Id = "oc-c", Number = 7, Name = "Appendix",
                    Sections = new List<OutlineSection> { new OutlineSection { Id = "s-c1", Name = "Third" } } },
            },
        };

        [Test]
        public void OutlineChapters_AttachToPlanChapters_ByNumberThenName_ElseAreCreatedInThePlan()
        {
            var d = Fixtures.SyntheticTrip();
            d.Outlines["b1"] = DriftedOutline();
            d.Captures.OutlineAssignments.Add(new OutlineAssignment { Id = "oa", MediaRef = new MediaRef(MediaRefKind.PlannedPhoto, "p1"), BookId = "b1", ChapterId = "oc-a", SectionId = "s-a1" });
            d.Captures.SectionNotes = new List<SectionNote> { new SectionNote { BookId = "b1", SectionId = "s-b1", Text = "Mine." } };

            var r = TripNormalizer.Normalize(d);
            Assert.IsTrue(r.ShotListChanged);
            var o = d.Outlines["b1"];
            CollectionAssert.AreEqual(new[] { "c1", "c2", "oc-c" }, o.Chapters.Select(c => c.Id).ToList());
            Assert.AreEqual("Chapter One", o.Chapters[0].Name, "the plan owns the name; the outline echoes it");
            Assert.AreEqual("s-a1", o.Chapters[0].Sections[0].Id);
            Assert.AreEqual("1.1", o.Chapters[0].Sections[0].Number);
            Assert.AreEqual("s-b1", o.Chapters[1].Sections[0].Id, "matched by name");

            var created = d.FindChapter("oc-c");
            Assert.IsNotNull(created, "a chapter typed into the outline is a chapter of the plan");
            Assert.AreEqual("Appendix", created.Name);
            Assert.AreEqual("b1", created.BookId);
            Assert.AreEqual(3, created.Number);

            Assert.AreEqual("c1", d.Captures.OutlineAssignments[0].ChapterId, "references follow the chapter");
            Assert.AreEqual("Open wide.", TripNormalizer.FindNote(d, "c1", null).Text, "chapter notes leave the outline");
            Assert.AreEqual("Mine.", TripNormalizer.FindNote(d, null, "s-b1").Text, "section notes join the same list");
            Assert.AreEqual("c2", TripNormalizer.FindNote(d, null, "s-b1").ChapterId);
            Assert.IsNull(d.Captures.SectionNotes);
            Assert.IsTrue(o.Chapters.All(c => c.Notes == null));
            Assert.IsEmpty(TripValidator.Validate(d));

            Assert.IsFalse(TripNormalizer.Normalize(d).Any, "a second pass changes nothing");
        }

        [Test]
        public void VersionOneFiles_Migrate_OnLoad()
        {
            var folder = Fixtures.NewTempFolder("v1");
            try
            {
                var data = Fixtures.LoadSample();
                TripSaver.SaveAll(data, folder);
                // put the files back the way version 1 wrote them
                var outlinePath = Path.Combine(folder, TripLoader.OutlinesDir, "book-1.json");
                var outline = TripJson.ParseObject(File.ReadAllText(outlinePath));
                outline["schemaVersion"] = 1;
                ((JObject)outline["chapters"][1])["notes"] = "Meter first.";
                File.WriteAllText(outlinePath, outline.ToString());
                var capPath = Path.Combine(folder, TripLoader.CapturesFile);
                var caps = TripJson.ParseObject(File.ReadAllText(capPath));
                caps["schemaVersion"] = 1;
                caps.Remove("notes");
                caps["sectionNotes"] = new JArray(new JObject { ["bookId"] = "b-001", ["sectionId"] = "s-2-2", ["text"] = "Oxygen first." });
                File.WriteAllText(capPath, caps.ToString());

                var back = TripLoader.Load(folder);
                Assert.AreEqual("Meter first.", TripNormalizer.FindNote(back, "c-102", null).Text);
                var sn = TripNormalizer.FindNote(back, null, "s-2-2");
                Assert.AreEqual("Oxygen first.", sn.Text);
                Assert.AreEqual("c-102", sn.ChapterId);
                Assert.AreEqual(Schema.CurrentVersion, back.Captures.SchemaVersion);
                Assert.IsEmpty(TripValidator.Validate(back));
            }
            finally { Fixtures.DeleteFolder(folder); }
        }

        [Test]
        public void ImportingAnOutline_IntoABook_CreatesItsChaptersInThePlan_AndRejectsOtherKinds()
        {
            var s = new TripSession(Fixtures.LoadSample(), () => 0);
            var json = @"{ ""bookTitle"": ""Lockout"", ""chapters"": [
                { ""number"": 1, ""name"": ""anything"", ""sections"": [ { ""id"": ""s-x1"", ""name"": ""Sources of energy"", ""nodes"": [] } ] },
                { ""number"": 3, ""name"": ""Group Lockout"", ""sections"": [ { ""id"": ""s-x2"", ""name"": ""The lock box"", ""nodes"": [] } ] } ] }";
            Assert.AreEqual(DocumentKind.Outline, TripPackage.DetectDocumentKind(TripJson.ParseObject(json)), "an outline is recognised by its sections even with no bookId");

            var report = TripPackage.ValidateJsonText(json, null, "typed-outline.json");
            Assert.IsNull(TripPackage.CheckExpectedKind(report, DocumentKind.Outline));
            StringAssert.Contains("not a shot list", TripPackage.CheckExpectedKind(report, DocumentKind.ShotList));
            var shotList = TripPackage.ValidateJsonText(TripPackage.ExportDocumentJson(s.Data, DocumentKind.ShotList), null, "shotlist.json");
            StringAssert.Contains("a shot list, not an outline", TripPackage.CheckExpectedKind(shotList, DocumentKind.Outline));

            TripPackage.ImportDocument(s, json, DocumentKind.Outline, "typed-outline.json", outlineBookId: "b-002");
            var o = s.Data.Outlines["b-002"];
            Assert.AreEqual(3, o.Chapters.Count);
            Assert.AreEqual("c-201", o.Chapters[0].Id, "chapter 1 matched by number");
            Assert.AreEqual("Isolating Energy Sources", o.Chapters[0].Name, "and keeps the plan's name");
            Assert.AreEqual(0, o.Chapters[1].Sections.Count, "the plan's chapter 2 has no sections in this outline");
            var made = s.Data.ChaptersOf("b-002").Last();
            Assert.AreEqual("Group Lockout", made.Name, "chapter 3 did not exist and is now in the shot list");
            Assert.AreEqual(made.Id, o.Chapters[2].Id);
            Assert.AreEqual("3.1", o.Chapters[2].Sections[0].Number);
            Assert.IsEmpty(TripValidator.Validate(s.Data));
        }
    }

    public class WorkingCopyEditTests
    {
        private static TripSession Sample() => new TripSession(Fixtures.LoadSample(), () => 0);

        [Test]
        public void Revise_ChangesTheWorkingCopy_NotTheOriginal_AndCoalescesWhileTyping()
        {
            var s = Sample();
            var edits = new PlanEdits(s, PlanEditMode.Working);
            var before = s.Data.Captures.Amendments.Count;
            edits.SetScene("v-004", "Wide of the M");
            edits.SetScene("v-004", "Wide of the MCC, three cameras.");
            edits.SetSmeIds("v-004", new List<string> { "p-006" });
            Assert.AreEqual(before + 1, s.Data.Captures.Amendments.Count, "one revise amendment for the edit session, not one per keystroke");
            var am = s.Data.Captures.Amendments.Last();
            Assert.AreEqual(AmendmentType.Revise, am.Type);
            Assert.AreEqual(2, am.Changes.Count);
            Assert.AreEqual("Motor control center, two cameras.", (string)am.Changes[0].Before);
            Assert.AreEqual("Wide of the MCC, three cameras.", (string)am.Changes[0].After);

            Assert.AreEqual("Wide of the MCC, three cameras.", s.Plan.FindItem("v-004").SceneDescription);
            CollectionAssert.AreEqual(new[] { "p-006" }, s.Plan.FindItem("v-004").SmeIds);
            Assert.AreEqual("Motor control center, two cameras.", s.Data.FindVideo("v-004").SceneDescription, "the original is as printed");
            CollectionAssert.AreEqual(new[] { "p-007" }, s.Data.FindVideo("v-004").SmeIds);

            // put back what it was: the change disappears
            edits.SetScene("v-004", "Motor control center, two cameras.");
            edits.SetSmeIds("v-004", new List<string> { "p-007" });
            Assert.AreEqual(before, s.Data.Captures.Amendments.Count);

            // a new edit session starts a new record
            edits.SetScene("v-004", "A");
            s.EndEditSession();
            edits.SetScene("v-004", "B");
            Assert.AreEqual(before + 2, s.Data.Captures.Amendments.Count);
            Assert.AreEqual("A", (string)s.Data.Captures.Amendments.Last().Changes[0].Before);
        }

        [Test]
        public void WorkingNotes_AreRevisions_OfACopy()
        {
            var s = Sample();
            var edits = new PlanEdits(s, PlanEditMode.Working);
            var notes = edits.Notes("v-004");
            notes.AddRoot("Get the tag being written.");
            Assert.AreEqual(2, s.Plan.FindItem("v-004").Notes.Count);
            Assert.AreEqual(1, s.Data.FindVideo("v-004").Notes.Count, "the printed notes are untouched");
            var am = s.Data.Captures.Amendments.Last();
            Assert.AreEqual(FieldChange.Notes, am.Changes.Single().Field);
            Assert.AreEqual(1, ((JArray)am.Changes[0].Before).Count);
            Assert.AreEqual(2, ((JArray)am.Changes[0].After).Count);
        }

        [Test]
        public void EditingInWorking_RenameIsRename_DeleteIsDrop_AddIsAdd()
        {
            var s = Sample();
            var edits = new PlanEdits(s, PlanEditMode.Working);
            edits.SetTitle("v-004", "Applying the First L");
            edits.SetTitle("v-004", "Applying the First Lock and Tag");
            var renames = s.Data.Captures.Amendments.Where(a => a.Type == AmendmentType.Rename && a.Targets.Contains("v-004")).ToList();
            Assert.AreEqual(1, renames.Count);
            Assert.AreEqual("Applying the First Lock and Tag", s.Plan.FindItem("v-004").Title);
            Assert.AreEqual("Applying the First Lock", s.Data.FindVideo("v-004").Title);
            Assert.AreEqual("Applying the First Lock and Tag", s.Data.FindCapture("cap-003").Title, "the summary follows, as it does for the Rename action");

            edits.DeleteVideo("v-004", "Line went down.");
            Assert.AreEqual(PlanItemStatus.Dropped, s.Plan.FindItem("v-004").Status);
            Assert.IsNotNull(s.Data.FindVideo("v-004"), "still on the original");
            Assert.IsNotNull(s.Data.FindCapture("cap-003"), "a capture is never deleted by a plan change");

            var vid = edits.AddVideo("c-202", "Restoring Power");
            Assert.AreEqual(PlanItemOrigin.Added, s.Plan.FindItem(vid).Origin);
            Assert.IsNull(s.Data.FindVideo(vid));
            Assert.Throws<System.InvalidOperationException>(() => edits.ReorderVideo("v-003", 0));
        }

        [Test]
        public void NewMedia_GoesThroughOnePath_AndShowsEverywhere()
        {
            var s = Sample();
            var id = s.CreateMedia(MediaKind.Photo, " Owen's lock on the breaker ", "b-002", "c-202", "Decided on site.");
            var p = s.Plan.FindPhoto(id);
            Assert.IsNotNull(p);
            Assert.IsTrue(p.IsNew);
            Assert.AreEqual("Owen's lock on the breaker", p.Description);
            Assert.AreEqual("c-202", p.ChapterId);
            Assert.IsNull(s.Data.FindPhoto(id), "not on the original list");
            Assert.AreEqual(id, s.Queries.PhotosOfBook("b-002").Last().Id, "in the working shot list, after the printed photos");
            CollectionAssert.Contains(s.Queries.PhotosOfChapter("c-202").Select(x => x.Id).ToList(), id);
            Assert.IsTrue(s.Search.SearchPhotos("owen lock", 5).Any(m => m.Item.Id == id), "findable wherever a photo is picked");

            // it can be captured, assigned to a slot and placed in an outline like any photo
            var pc = s.AddPhotoCapture(new PhotoCapture { DayId = "d-002", PhotoId = id });
            Assert.AreEqual(PhotoStatus.Captured, s.Plan.FindPhoto(id).Status);
            s.AssignHero("b-002", "c-202", id, null, pc.Id);
            Assert.AreEqual(id, s.Queries.CoverSlot("b-002", "c-202").Assigned.Single().Photo.Id);
            CollectionAssert.AreEqual(new[] { pc.Id }, s.Queries.UnplacedIn("c-202").Select(e => e.Id).ToList());
            s.Assign(new MediaRef(MediaRefKind.PhotoCapture, pc.Id), "b-002", "c-202", null, null, AssignmentSource.Manual, null, true);
            Assert.AreEqual(1, s.Queries.PlacedIn("c-202").Count, "placed on the chapter as a whole");
            Assert.IsEmpty(s.Queries.UnplacedIn("c-202"));
            Assert.IsEmpty(TripValidator.Validate(s.Data));

            // removed from the summary and added back
            s.RemovePhotoCapture(pc.Id);
            Assert.AreEqual(PhotoStatus.NotCaptured, s.Plan.FindPhoto(id).Status);
            s.AddPhotoCapture(new PhotoCapture { DayId = "d-002", PhotoId = id });
            Assert.AreEqual(PhotoStatus.Captured, s.Plan.FindPhoto(id).Status);

            var vid = s.CreateMedia(MediaKind.Video, "Lock box", null, "c-201");
            Assert.AreEqual("b-002", s.Plan.FindItem(vid).BookId, "the book comes from the chapter");
            Assert.Throws<System.ArgumentException>(() => s.CreateMedia(MediaKind.Photo, " ", "b-002", null));
        }

        [Test]
        public void MovingAPhotoToAnotherBook_IsAWorkingCopyChange()
        {
            var s = Sample();
            s.MovePhoto("ph-008", "b-001", "c-102", "Better fit for the permit chapter.");
            var p = s.Plan.FindPhoto("ph-008");
            Assert.AreEqual("b-001", p.BookId);
            Assert.AreEqual("c-102", p.ChapterId);
            Assert.IsTrue(p.WasMoved);
            Assert.AreEqual("b-002", s.Data.FindPhoto("ph-008").BookId, "the original list keeps it where it was printed");
            CollectionAssert.Contains(s.Queries.PhotosOfBook("b-001").Select(x => x.Id).ToList(), "ph-008");
            CollectionAssert.DoesNotContain(s.Queries.PhotosOfBook("b-002", false).Select(x => x.Id).ToList(), "ph-008");

            // a cover moved into a book that has one: both are planned covers, both are shown
            s.MovePhoto("ph-005", "b-001", null);
            Assert.AreEqual(2, s.Queries.CoverSlot("b-001", null).PlannedPhotos.Count);
            Assert.IsEmpty(s.Queries.CoverSlot("b-002", null).PlannedPhotos);
        }

        [Test]
        public void UncapturePhoto_WithdrawsEveryRecordOfIt()
        {
            var s = Sample();
            Assert.AreEqual(PhotoStatus.Captured, s.Plan.FindPhoto("ph-002").Status);
            Assert.AreEqual(1, s.UncapturePhoto("ph-002"));
            Assert.AreEqual(PhotoStatus.NotCaptured, s.Plan.FindPhoto("ph-002").Status);
            Assert.IsTrue(s.Queries.CoverSlot("b-001", "c-101").IsEmpty, "the slot is empty again");
            Assert.AreEqual(1, s.UncapturePhoto("ph-001"), "captured inside a video capture");
            Assert.IsNotNull(s.Data.FindCapture("cap-001"));
        }
    }

    public class OriginalEditTests
    {
        [Test]
        public void BeforeTheTrip_EditingTheOriginal_RecordsNothing()
        {
            var s = new TripSession(Fixtures.SyntheticTrip(), () => 0);
            Assert.IsFalse(s.HasFieldMedia);
            var edits = new PlanEdits(s, PlanEditMode.Original);
            edits.SetTitle("v1", "Renamed");
            edits.SetScene("v1", "A scene");
            edits.DeleteVideo("v2");
            edits.AddVideo("c1", "New one");
            Assert.IsEmpty(s.Data.Captures.Edits);
            Assert.IsEmpty(s.Data.Captures.Amendments);
            Assert.AreEqual("Renamed", s.Data.FindVideo("v1").Title);
        }

        [Test]
        public void OnceMediaExists_FixingTheOriginal_IsAllowed_AndLoggedAsACorrection()
        {
            var s = new TripSession(Fixtures.LoadSample(), () => 0);
            Assert.IsTrue(s.HasFieldMedia);
            Assert.Throws<PlanEditBlockedException>(() => s.PlanEditor.SetVideoTitle("v-004", "x"), "the guarded path still refuses");

            var edits = new PlanEdits(s, PlanEditMode.Original);
            edits.SetTitle("v-004", "Applying the Firs");
            edits.SetTitle("v-004", "Applying the First Locks");
            Assert.AreEqual("Applying the First Locks", s.Data.FindVideo("v-004").Title);
            var rec = s.Data.Captures.Edits.Single();
            Assert.AreEqual(ChangeRecordKind.Correction, rec.Kind);
            Assert.AreEqual(FieldChange.Title, rec.Field);
            Assert.AreEqual("Applying the First Lock", (string)rec.Before);
            Assert.AreEqual("Applying the First Locks", (string)rec.After);
            Assert.IsNotNull(rec.At);
            Assert.AreEqual("Applying the First Lock", PlanEdits.WasTitle(s, "v-004"));
            Assert.IsEmpty(s.Data.Captures.Amendments.Where(a => a.Type == AmendmentType.Rename && a.Targets.Contains("v-004")), "a correction is not a plan change");

            edits.SetTitle("v-004", "Applying the First Lock");
            Assert.IsEmpty(s.Data.Captures.Edits, "typed back to what it was: nothing to explain");
            Assert.IsNull(PlanEdits.WasTitle(s, "v-004"));
        }

        [Test]
        public void DeletingFromTheOriginal_DetachesInsteadOfOrphaning_AndNumbersAreRemembered()
        {
            var s = new TripSession(Fixtures.LoadSample(), () => 0);
            var impact = Deletions.ForVideo(s.Data, "v-003");
            Assert.AreEqual(1, impact.Captures);
            Assert.AreEqual(1, impact.Amendments);
            Assert.IsTrue(impact.NeedsConfirm);

            new PlanEdits(s, PlanEditMode.Original).DeleteVideo("v-003");
            Assert.IsNull(s.Data.FindVideo("v-003"));
            var cap = s.Data.FindCapture("cap-002");
            Assert.IsNotNull(cap, "the capture itself is never deleted");
            Assert.IsNull(cap.PlanVideoId);
            Assert.AreEqual(3, s.Data.FindVideo("v-004").Number, "numbering closes up");
            Assert.AreEqual(4, PlanEdits.WasNumber(s, "v-004"), "but the paper still says 4");
            Assert.IsNull(PlanEdits.WasNumber(s, "v-001"));
            Assert.IsTrue(s.Data.Captures.Edits.Any(e => e.Field == FieldChange.Deleted && e.EntityId == "v-003"));
            Assert.IsEmpty(TripValidator.Validate(s.Data));

            var photo = Deletions.ForPhoto(s.Data, "ph-005");
            Assert.AreEqual(1, photo.Captures);
            new PlanEdits(s, PlanEditMode.Original).DeletePhoto("ph-005");
            var entry = s.Data.FindCapture("cap-003").Photos.First(p => p.Text != null && p.Text.Contains("lock array"));
            Assert.IsNull(entry.PhotoId, "what was shot stays in the summary under its description");
            Assert.IsEmpty(TripValidator.Validate(s.Data));
        }

        [Test]
        public void ChapterChanges_AreLogged_AsChangesInWorking_AsCorrectionsInOriginal()
        {
            var s = new TripSession(Fixtures.LoadSample(), () => 0);
            new PlanEdits(s, PlanEditMode.Working).SetChapterName("c-201", "Isolating Energy");
            s.EndEditSession();
            new PlanEdits(s, PlanEditMode.Original).SetChapterName("c-202", "Verification & Release");
            var edits = s.Data.Captures.Edits;
            Assert.AreEqual(ChangeRecordKind.Change, edits.Single(e => e.EntityId == "c-201").Kind);
            Assert.AreEqual(ChangeRecordKind.Correction, edits.Single(e => e.EntityId == "c-202").Kind);
            Assert.AreEqual("Isolating Energy", s.Data.FindChapter("c-201").Name, "a chapter is one thing: both modes edit it");
        }
    }

    public class SummaryEntryEditTests
    {
        [Test]
        public void MoveEntryToDay_AppendsThere_AndClosesTheGap()
        {
            var s = new TripSession(Fixtures.LoadSample(), () => 0);
            s.MoveEntryToDay("cap-001", "d-002");
            CollectionAssert.AreEqual(new[] { "cap-002", "pc-001" }, s.Queries.DayTimeline("d-001").Select(e => e.Id).ToList());
            Assert.AreEqual(1, s.Data.FindCapture("cap-002").CapturedOrder);
            CollectionAssert.AreEqual(new[] { "cap-003", "cap-004", "pc-002", "cap-001" }, s.Queries.DayTimeline("d-002").Select(e => e.Id).ToList());
            s.MoveEntryToDay("pc-001", "d-002");
            Assert.AreEqual("d-002", s.Data.FindPhotoCapture("pc-001").DayId);
            Assert.IsNull(s.Data.FindPhotoCapture("pc-001").AfterCaptureId);
            Assert.Throws<KeyNotFoundException>(() => s.MoveEntryToDay("cap-001", "nope"));
        }

        [Test]
        public void SetEntryTime_KeepsTheDaysDate()
        {
            var s = new TripSession(Fixtures.LoadSample(), () => 0);
            s.SetEntryTime("cap-001", 9, 40);
            var at = PlanResolver.ParseTimestamp(s.Data.FindCapture("cap-001").At).Value.ToLocalTime();
            Assert.AreEqual(new System.DateTime(2026, 9, 15), at.Date, "d-001 is September 15");
            Assert.AreEqual(9, at.Hour);
            Assert.AreEqual(40, at.Minute);
        }

        [Test]
        public void TimesAreParsedTheWayPeopleTypeThem()
        {
            void Is(string text, int h, int m) { Assert.IsTrue(TripSession.TryParseTime(text, out var hh, out var mm), text); Assert.AreEqual(h, hh, text); Assert.AreEqual(m, mm, text); }
            Is("9:40", 9, 40); Is("09:40", 9, 40); Is("9:40 am", 9, 40); Is("2:05 PM", 14, 5); Is("2pm", 14, 0); Is("12am", 0, 0); Is("12:30 pm", 12, 30); Is("1430", 14, 30); Is("7", 7, 0);
            Assert.IsFalse(TripSession.TryParseTime("25:00", out _, out _));
            Assert.IsFalse(TripSession.TryParseTime("soon", out _, out _));
            Assert.IsFalse(TripSession.TryParseTime("", out _, out _));
        }
    }

    public class ExportTests
    {
        [Test]
        public void EveryExportSaysWhichDocumentItIs()
        {
            Assert.AreEqual("Export original shot list", TripExports.Label(ExportKind.OriginalShotList));
            Assert.AreEqual("Export working shot list", TripExports.Label(ExportKind.WorkingShotList));
            Assert.AreEqual("Export changes", TripExports.Label(ExportKind.Changes));
            Assert.AreEqual("Export media summary", TripExports.Label(ExportKind.MediaSummary));
        }

        [Test]
        public void WorkingShotList_IsThePlanAsItStands_AndOriginalIsAsPrinted()
        {
            var s = new TripSession(Fixtures.LoadSample(), () => 0);
            s.CreateMedia(MediaKind.Photo, "Lock on the breaker", "b-002", "c-202");

            var original = TripExports.Write(s, new ExportRequest(ExportKind.OriginalShotList));
            Assert.AreEqual("ACME_WDG_P5_original-shot-list.json", original.FileName);
            var o = TripJson.Deserialize<ShotListDocument>(Encoding.UTF8.GetString(original.Bytes));
            Assert.AreEqual(5, o.Videos.Count);
            Assert.AreEqual(8, o.Photos.Count);

            var working = TripExports.Write(s, new ExportRequest(ExportKind.WorkingShotList));
            Assert.AreEqual("ACME_WDG_P5_working-shot-list.json", working.FileName);
            var w = TripJson.Deserialize<ShotListDocument>(Encoding.UTF8.GetString(working.Bytes));
            CollectionAssert.AreEqual(new[] { "v-c001", "v-003", "v-004", "v-a001" }, w.Videos.Select(v => v.Id).ToList());
            Assert.AreEqual("Completing the Confined Space Entry Permit", w.Videos[1].Title, "renamed");
            Assert.AreEqual("1+2", (string)w.Videos[0].Extra["displayNumber"]);
            Assert.AreEqual(9, w.Photos.Count, "the photo added during the trip is on the working list");
            Assert.AreEqual("added", (string)w.Photos.Last().Extra["origin"]);

            var changes = TripJson.ParseObject(Encoding.UTF8.GetString(TripExports.Write(s, new ExportRequest(ExportKind.Changes)).Bytes));
            Assert.AreEqual(5, ((JArray)changes["amendments"]).Count);
            Assert.IsNotNull(changes["corrections"]);

            Assert.AreEqual("ACME_WDG_P5_media-summary.json", TripExports.Write(s, new ExportRequest(ExportKind.MediaSummary)).FileName);
        }

        private sealed class FakeWord : ITripExportFormat
        {
            public string Id => "docx";
            public string Label => "Word";
            public bool Supports(ExportKind kind) => kind == ExportKind.WorkingShotList || kind == ExportKind.MediaSummary;
            public ExportFile Write(TripSession session, ExportRequest request) => new ExportFile { FileName = TripExports.BaseName(session.Data, request) + ".docx", Bytes = new byte[] { 1 } };
        }

        [Test]
        public void ASecondFormat_DropsInBesideJson()
        {
            var s = new TripSession(Fixtures.LoadSample(), () => 0);
            TripExports.Register(new FakeWord());
            try
            {
                CollectionAssert.AreEquivalent(new[] { "json", "docx" }, TripExports.FormatsFor(ExportKind.MediaSummary).Select(f => f.Id).ToList());
                CollectionAssert.AreEquivalent(new[] { "json" }, TripExports.FormatsFor(ExportKind.Changes).Select(f => f.Id).ToList());
                Assert.AreEqual("ACME_WDG_P5_media-summary.docx", TripExports.Write(s, new ExportRequest(ExportKind.MediaSummary), "docx").FileName);
                Assert.Throws<System.NotSupportedException>(() => TripExports.Write(s, new ExportRequest(ExportKind.Changes), "docx"));
            }
            finally { TripExports.Unregister("docx"); }
        }
    }
}

using System;
using System.Linq;
using MediaTrip.Model;
using MediaTrip.Status;
using MediaTrip.UI.ViewModels;
using NUnit.Framework;

namespace MediaTrip.UI.Tests
{
    public class ShotListViewTests
    {
        [Test]
        public void Working_IsThePlanAsItStands()
        {
            var s = UiFixtures.Session();
            var v = ShotListView.Build(s, ShotListMode.Working, hideDone: false);
            var ids = v.Books.SelectMany(b => b.Chapters).SelectMany(c => c.Rows).Select(r => r.Item.Id).ToList();
            CollectionAssert.AreEqual(new[] { "v-c001", "v-003", "v-004", "v-a001" }, ids, "combined sources and the dropped video leave; the combined and added items appear");
            var rows = v.Books.SelectMany(b => b.Chapters).SelectMany(c => c.Rows).ToList();
            Assert.AreEqual("1+2", rows[0].Number);
            Assert.AreEqual("+", rows[3].Number);
            CollectionAssert.Contains(rows[3].Tags, "new", "added during the trip: tagged, not on the original");
            CollectionAssert.Contains(rows[1].Tags, "was named Filling Out the Entry Permit");
            Assert.AreEqual("Completing the Confined Space Entry Permit", rows[1].Title, "renamed title");
            Assert.IsTrue(rows.All(r => !r.Dim));
            Assert.AreEqual("ph-002", v.Books[0].Chapters[0].Hero.Id);
        }

        [Test]
        public void HideDone_LeavesOnlyWhatIsLeft()
        {
            var s = UiFixtures.Session();
            var v = ShotListView.Build(s, ShotListMode.Working, hideDone: true);
            Assert.IsEmpty(v.Books.SelectMany(b => b.Chapters).SelectMany(c => c.Rows), "everything still standing in the sample has been filmed");
            var heroes = v.Books.SelectMany(b => b.Chapters).Where(c => c.Hero != null).Select(c => c.Hero.Id).ToList();
            CollectionAssert.AreEqual(new[] { "ph-007" }, heroes, "only the hero that is still missing keeps its chapter on the list");

            s.Uncheck("v-004");
            v = ShotListView.Build(s, ShotListMode.Working, hideDone: true);
            CollectionAssert.AreEqual(new[] { "v-004" }, v.Books.SelectMany(b => b.Chapters).SelectMany(c => c.Rows).Select(r => r.Item.Id).ToList());
        }

        [Test]
        public void Original_IsThePrintedList_StruckWhereItChanged()
        {
            var s = UiFixtures.Session();
            var v = ShotListView.Build(s, ShotListMode.Original, hideDone: false);
            var rows = v.Books.SelectMany(b => b.Chapters).SelectMany(c => c.Rows).ToList();
            CollectionAssert.AreEqual(new[] { "v-001", "v-002", "v-003", "v-004", "v-005" }, rows.Select(r => r.Item.Id).ToList());
            Assert.AreEqual("Filling Out the Entry Permit", rows[2].Title, "the printed title, not the renamed one");
            Assert.IsTrue(rows[0].Dim && rows[1].Dim && rows[4].Dim);
            Assert.AreEqual(PlanItemStatus.Superseded, rows[0].Status);
            Assert.AreEqual(PlanItemStatus.Dropped, rows[4].Status);

            var left = ShotListView.Build(s, ShotListMode.Original, hideDone: true);
            Assert.IsEmpty(left.Books.SelectMany(b => b.Chapters).SelectMany(c => c.Rows));
        }

        [Test]
        public void Changes_AreInTimeOrder_WithWhereTheyLead()
        {
            var s = UiFixtures.Session();
            var v = ShotListView.Build(s, ShotListMode.Changes, false);
            CollectionAssert.AreEqual(new[] { "Combined", "Renamed", "New video", "Dropped" }, v.Changes.Select(c => c.Label).ToList());
            Assert.IsEmpty(v.Corrections);
            Assert.AreEqual("#1 + #2 → <b>Walkthrough and Entry Point Hazards</b>", v.Changes[0].Line);
            Assert.AreEqual("v-c001", v.Changes[0].OpenItemId);
            Assert.AreEqual("v-003", v.Changes[1].OpenItemId);
            Assert.AreEqual("v-a001", v.Changes[2].OpenItemId);
            Assert.AreEqual("ok", v.Changes[2].Style);
            Assert.AreEqual("bad", v.Changes[3].Style);
            StringAssert.Contains("<s>Try Step Verification</s>", v.Changes[3].Line);
        }

        [Test]
        public void Changes_KeepCorrectionsApart_AndDescribeRevisionsFieldByField()
        {
            var s = UiFixtures.Session();
            var working = new MediaTrip.Authoring.PlanEdits(s, MediaTrip.Authoring.PlanEditMode.Working);
            working.SetScene("v-004", "Three cameras at the MCC.");
            working.SetSmeIds("v-004", new System.Collections.Generic.List<string> { "p-006" });
            s.EndEditSession();
            new MediaTrip.Authoring.PlanEdits(s, MediaTrip.Authoring.PlanEditMode.Original).SetTitle("v-004", "Applying the First Locks");

            var v = ShotListView.Build(s, ShotListMode.Changes, false);
            var revised = v.Changes.Single(c => c.Label == "Revised");
            StringAssert.Contains("scene: “Motor control center, two cameras.” → <b>“Three cameras at the MCC.”</b>", revised.Line);
            StringAssert.Contains("SME: Nina Okoro → <b>Terry Blackwood</b>", revised.Line);
            Assert.AreEqual("v-004", revised.OpenItemId);

            var fix = v.Corrections.Single();
            Assert.AreEqual("Corrected", fix.Label);
            StringAssert.Contains("title: “Applying the First Lock” → <b>“Applying the First Locks”</b>", fix.Line);
            Assert.IsFalse(v.Changes.Any(c => c.Record != null && c.Record.Kind == MediaTrip.Model.ChangeRecordKind.Correction), "a correction is never listed as a change of plan");

            var row = ShotListView.Build(s, ShotListMode.Working, false).Books[1].Chapters[0].Rows[0];
            CollectionAssert.Contains(row.Tags, "revised");
            CollectionAssert.Contains(row.Tags, "was named Applying the First Lock");
        }

        [Test]
        public void Photos_AreListedWithTheirBook_InBothViews()
        {
            var s = UiFixtures.Session();
            s.CreateMedia(MediaTrip.Model.MediaKind.Photo, "Lock on the breaker", "b-002", "c-202");
            var working = ShotListView.Build(s, ShotListMode.Working, false);
            Assert.AreEqual(4, working.Books[0].Photos.Count);
            Assert.AreEqual(5, working.Books[1].Photos.Count, "the photo added during the trip is on the working list");
            Assert.AreEqual("Cover", working.Books[0].Photos[0].Use);
            Assert.AreEqual("Ch.1 hero", working.Books[0].Photos[1].Use);
            CollectionAssert.Contains(working.Books[1].Photos.Last().Tags, "new");

            var original = ShotListView.Build(s, ShotListMode.Original, false);
            Assert.AreEqual(4, original.Books[1].Photos.Count, "and not on the original");

            var left = ShotListView.Build(s, ShotListMode.Working, hideDone: true);
            CollectionAssert.AreEquivalent(new[] { "ph-007" }, left.Books.SelectMany(b => b.Photos).Where(p => !p.Photo.IsNew).Select(p => p.Photo.Id).ToList());
        }

        [Test]
        public void EditState_KeepsEmptyChaptersAndBooks()
        {
            var s = UiFixtures.Session();
            var ch = s.PlanEditor.AddChapter("b-002", "Empty one");
            Assert.IsFalse(ShotListView.Build(s, ShotListMode.Working, false).Books.SelectMany(b => b.Chapters).Any(c => c.Chapter.Id == ch.Id));
            Assert.IsTrue(ShotListView.Build(s, ShotListMode.Working, false, keepEmpty: true).Books.SelectMany(b => b.Chapters).Any(c => c.Chapter.Id == ch.Id));
        }
    }

    public class PhotoDraftTests
    {
        [Test]
        public void Batch_AppendsPickedAndTypedPhotos_ToTheDay()
        {
            var s = UiFixtures.Session();
            CollectionAssert.AreEqual(new[] { "ph-007" }, PhotoBatchDraft.NotYetShot(s).Select(p => p.Id).ToList());
            var b = new PhotoBatchDraft { DayId = "d-002" };
            Assert.IsFalse(b.CanSave);
            b.Toggle("ph-007");
            b.Text = " Sunset over the yard ";
            b.AddExtra();
            b.Text = "Still in the field";
            Assert.IsTrue(b.CanSave);
            var added = b.Save(s);
            Assert.AreEqual(3, added.Count, "text left in the field counts too");
            Assert.IsTrue(added.All(p => p.DayId == "d-002"));
            CollectionAssert.AreEqual(new[] { 4, 5, 6 }, added.Select(p => p.CapturedOrder).ToList(), "appended after what the day already has");
            Assert.AreEqual("Sunset over the yard", added[1].Text);
            Assert.AreEqual(PhotoStatus.Captured, s.Plan.FindPhoto("ph-007").Status);
            Assert.IsEmpty(PhotoBatchDraft.NotYetShot(s));

            b.Toggle("x"); b.Toggle("x");
            Assert.IsEmpty(b.Picked);
        }

        [Test]
        public void Covers_GotPlanned_AssignNew_AssignExisting()
        {
            var s = UiFixtures.Session();
            var slot = s.Queries.CoverSlot("b-002", "c-202");
            Assert.IsTrue(slot.IsEmpty);
            Assert.AreEqual(1, CoverActions.EmptyCount(s.Queries.CoverBoard()));
            Assert.AreEqual(6, CoverActions.SlotCount(s.Queries.CoverBoard()));

            // one search box over every photo of every book
            var found = CoverActions.Search(s, slot, "personal lock");
            Assert.AreEqual("ph-008", found[0].Id);
            Assert.AreEqual("Lockout / Tagout", Fmt.PhotoWhere(s.Data, found[0]), "a result says which book it is in");
            var other = CoverActions.Search(s, slot, "clipboard permit")[0];
            Assert.AreEqual("ph-003", other.Id, "photos of other books are found too");
            Assert.AreEqual("Confined Space Entry · Ch.2 hero", Fmt.PhotoWhere(s.Data, other), "and says WHICH chapter's hero it is");
            Assert.IsTrue(CoverActions.IsFromAnotherBook(slot, other));
            Assert.IsFalse(CoverActions.IsFromAnotherBook(slot, found[0]));
            Assert.IsTrue(CoverActions.IsExactMatch("Personal lock with name tag", found), "typing an existing photo's name does not offer to create it again");
            Assert.IsFalse(CoverActions.IsExactMatch("personal lock", found));

            // something new goes through the one path: shot list, summary, slot, and the chapter in Outlines
            var a = CoverActions.AssignNew(s, slot, "Owen's lock going on", "d-002");
            var made = s.Plan.FindPhoto(a.PhotoId);
            Assert.IsTrue(made.IsNew);
            Assert.AreEqual("c-202", made.ChapterId);
            CollectionAssert.Contains(s.Queries.PhotosOfBook("b-002").Select(p => p.Id).ToList(), a.PhotoId, "on the working shot list");
            Assert.IsNull(s.Data.FindPhoto(a.PhotoId), "not on the original");
            Assert.AreEqual(a.PhotoId, s.Data.FindPhotoCapture(a.PhotoCaptureId).PhotoId, "logged on the day");
            Assert.AreEqual(1, s.Queries.PlacedIn("c-202").Count, "placed on the chapter in Outlines");
            Assert.IsTrue(ShotListView.Build(s, ShotListMode.Changes, false).Changes.Any(c => c.Label == "New photo"));

            // removed from the summary, it can be added back
            s.RemovePhotoCapture(a.PhotoCaptureId);
            CollectionAssert.Contains(PhotoBatchDraft.NotYetShot(s).Select(p => p.Id).ToList(), a.PhotoId);

            CoverActions.AssignExisting(s, slot, "ph-008");
            slot = s.Queries.CoverSlot("b-002", "c-202");
            Assert.AreEqual(2, slot.Assigned.Count);
            CollectionAssert.DoesNotContain(CoverActions.Search(s, slot, "personal lock").Select(p => p.Id).ToList(), "ph-008", "what already fills the slot is not offered again");
            Assert.AreEqual(PhotoStatus.NotCaptured, s.Plan.FindPhoto("ph-007").Status, "assigning something else does not tick the planned photo");

            // the planned photo: got it, and taken back
            Assert.IsNotNull(CoverActions.GotPlanned(s, slot, "d-002"));
            slot = s.Queries.CoverSlot("b-002", "c-202");
            Assert.IsTrue(slot.Assigned[0].IsPlanned);
            Assert.AreEqual(3, slot.Assigned.Count);
            Assert.IsNull(CoverActions.GotPlanned(s, slot, "d-002"), "already shot");
            Assert.AreEqual(1, CoverActions.UngotPlanned(s, "ph-007"));
            Assert.AreEqual(2, s.Queries.CoverSlot("b-002", "c-202").Assigned.Count, "un-assigned again");
            Assert.IsNull(CoverActions.AssignNew(s, slot, "  ", "d-002"));
        }

        [Test]
        public void Covers_APhotoOfAnotherBook_IsMovedOrSharedOnlyWhenAsked()
        {
            var s = UiFixtures.Session();
            var slot = s.Queries.CoverSlot("b-002", "c-202");
            var where = CoverActions.WhereAssigned(s, s.Plan.FindPhoto("ph-003"));
            Assert.AreEqual("Confined Space Entry · Ch.2 hero", where[0]);

            CoverActions.AssignExisting(s, slot, "ph-003", move: false);
            Assert.AreEqual("b-001", s.Plan.FindPhoto("ph-003").BookId, "assign to both: it stays where it is");
            StringAssert.Contains("Lockout / Tagout · Ch.2 hero", CoverActions.WhereAssigned(s, s.Plan.FindPhoto("ph-003"))[1]);

            CoverActions.AssignExisting(s, slot, "ph-004", move: true);
            Assert.AreEqual("b-002", s.Plan.FindPhoto("ph-004").BookId, "move: it is now in this book on the working list");
            Assert.AreEqual("b-001", s.Data.FindPhoto("ph-004").BookId, "and still where it was printed on the original");
            Assert.IsTrue(ShotListView.Build(s, ShotListMode.Working, false).Books[1].Photos.Any(p => p.Photo.Id == "ph-004" && p.Tags.Any(t => t.StartsWith("moved from"))));
        }
    }

    public class DayPickerTests
    {
        [Test]
        public void PicksTodaysDay_CreatesOneDuringTheTrip_ElseTheLast()
        {
            var t = UiFixtures.Session().Data.Trip;   // arrive 09-14, depart 09-17, days on 09-15 and 09-16
            Assert.AreEqual("d-001", DayPicker.For(t, new DateTime(2026, 9, 15, 8, 0, 0)).Day.Id);

            var during = DayPicker.For(t, new DateTime(2026, 9, 17, 8, 0, 0));
            Assert.IsNull(during.Day);
            Assert.AreEqual("2026-09-17", during.CreateDate);
            Assert.AreEqual("Day 3", during.CreateLabel);

            Assert.AreEqual("d-002", DayPicker.For(t, new DateTime(2026, 8, 1)).Day.Id, "before the trip: no day is invented");
            Assert.AreEqual("d-002", DayPicker.For(t, new DateTime(2027, 1, 1)).Day.Id, "after the trip: the last day");

            t.Days.Clear();
            var none = DayPicker.For(t, new DateTime(2027, 1, 1));
            Assert.AreEqual("2027-01-01", none.CreateDate);
            Assert.AreEqual("Day 1", none.CreateLabel);
        }
    }

    public class FilmingDraftTests
    {
        [Test]
        public void OpenSuggestions_OfferOnlyWhatIsNotFilmedYet()
        {
            var s = UiFixtures.Session();
            var d = new CaptureDraft();
            d.SetTitle("lock");
            Assert.IsNotEmpty(d.Suggestions(s));
            Assert.IsEmpty(d.OpenSuggestions(s), "every lock video in the sample is already filmed or dropped");
            s.Uncheck("v-004");
            Assert.AreEqual("v-004", d.OpenSuggestions(s)[0].Item.Id);
            d.SetTitle("");
            Assert.IsEmpty(d.OpenSuggestions(s));
        }

        [Test]
        public void Pick_FillsThePlannedSme_AndTypingKeepsAHandPickedBook()
        {
            var s = UiFixtures.Session();
            var d = new CaptureDraft();
            d.Pick(s, "v-004");
            Assert.AreEqual("Nina Okoro", d.People.Single().Name);
            Assert.AreEqual("p-007", d.People[0].PersonId);

            d.SetTitle("Something unplanned");
            Assert.IsNull(d.BookId, "the pick's book goes with the pick");
            d.BookId = "b-002"; d.ChapterId = "c-202";
            d.SetTitle("Something unplanned, continued");
            Assert.AreEqual("b-002", d.BookId);
            Assert.AreEqual("c-202", d.ChapterId);

            d.ExtraPhoto = "Placard"; d.AddExtraPhoto();
            d.ExtraPhoto = "Gate";
            var cap = d.Save(s, "d-002");
            Assert.AreEqual("c-202", cap.ChapterId);
            CollectionAssert.AreEquivalent(new[] { "Placard", "Gate" }, cap.Photos.Where(p => p.PhotoId == null).Select(p => p.Text).ToList());
            Assert.AreEqual(AmendmentType.Add, s.Data.Captures.Amendments.Last().Type);
            Assert.AreEqual("c-202", s.Data.Captures.Amendments.Last().NewChapterId);
        }
    }
}

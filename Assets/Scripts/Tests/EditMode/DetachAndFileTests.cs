using System.Linq;
using MediaTrip.Authoring;
using MediaTrip.Model;
using MediaTrip.Query;
using MediaTrip.Session;
using MediaTrip.Status;
using MediaTrip.Validation;
using NUnit.Framework;

namespace MediaTrip.Tests
{
    /// <summary>Free-text photos become fileable; removing a filmed plan item detaches its capture by default.</summary>
    public class DetachAndFileTests
    {
        private static TripSession Sample() => new TripSession(Fixtures.LoadSample(), () => 0);

        [Test]
        public void LoosePhotos_AreTheFreeTextOnes_GroupedByText()
        {
            var s = Sample();
            var loose = LoosePhotos.Of(s.Data);
            CollectionAssert.AreEqual(new[] { "Unplanned: warning placard at the gallery entrance", "Golden hour exterior of the plant sign" }, loose.Select(l => l.Text).ToList());
            Assert.AreEqual("cap-001", loose[0].Captures.Single().Id);
            Assert.AreEqual("b-001", loose[0].BookId, "the book of the video it was shot with");
            Assert.AreEqual("pc-002", loose[1].PhotoCaptures.Single().Id);

            // the same text typed twice is one photo, whatever the case or punctuation
            s.AddPhotoCapture(new PhotoCapture { DayId = "d-002", Text = "golden hour exterior of the plant sign!" });
            loose = LoosePhotos.Of(s.Data);
            Assert.AreEqual(2, loose.Count);
            Assert.AreEqual(2, loose[1].PhotoCaptures.Count);

            Assert.AreEqual("Golden hour exterior of the plant sign", s.Search.SearchLoosePhotos("golden hour")[0].Item.Text);
        }

        [Test]
        public void FilePhoto_MakesARealPhotoOfTheWorkingList_AndTheRecordsPointAtIt()
        {
            var s = Sample();
            var id = s.FilePhoto("Golden hour exterior of the plant sign", "b-001", null, "Filed from Covers.");
            var p = s.Plan.FindPhoto(id);
            Assert.IsNotNull(p);
            Assert.IsTrue(p.IsNew, "on the working copy, not the original");
            Assert.AreEqual("b-001", p.BookId);
            Assert.AreEqual("Golden hour exterior of the plant sign", p.Description);
            Assert.AreEqual(PhotoStatus.Captured, p.Status, "it was shot: the photo capture now records it");
            var pc = s.Data.FindPhotoCapture("pc-002");
            Assert.AreEqual(id, pc.PhotoId);
            Assert.IsNull(pc.Text, "no duplicate text beside the id");
            Assert.AreEqual(1, LoosePhotos.Of(s.Data).Count, "it is no longer loose");
            Assert.IsEmpty(TripValidator.Validate(s.Data));

            // a photo inside a video capture files the same way
            var id2 = new PlanEdits(s, PlanEditMode.Working).FilePhoto("Unplanned: warning placard at the gallery entrance", "b-001", "c-101");
            var cp = s.Data.FindCapture("cap-001").Photos.Single(x => x.PhotoId == id2);
            Assert.IsNull(cp.Text);
            Assert.IsTrue(cp.Captured);
            Assert.IsEmpty(LoosePhotos.Of(s.Data));
            Assert.AreEqual(PhotoStatus.Captured, s.Plan.FindPhoto(id2).Status);
        }

        [Test]
        public void FilePhoto_OnTheOriginal_GoesOnTheOriginalList_LoggedAsACorrection()
        {
            var s = Sample();
            var edits = new PlanEdits(s, PlanEditMode.Original);
            var id = edits.FilePhoto("Golden hour exterior of the plant sign", "b-002", null);
            Assert.IsNotNull(s.Data.FindPhoto(id), "on the original list");
            Assert.IsFalse(s.Plan.FindPhoto(id).IsNew);
            Assert.AreEqual(id, s.Data.FindPhotoCapture("pc-002").PhotoId);
            var rec = s.Data.Captures.Edits.Last();
            Assert.AreEqual(ChangeRecordKind.Correction, rec.Kind);
            Assert.AreEqual(FieldChange.Added, rec.Field);
            Assert.AreEqual("photo", rec.Entity);
            Assert.IsEmpty(TripValidator.Validate(s.Data));
        }

        [Test]
        public void DropVideo_KeepsTheCaptureDetachedByDefault_TaggedWithWhatItWas()
        {
            var s = Sample();
            new PlanEdits(s, PlanEditMode.Working).DropVideo("v-004", "Not needed.");
            var cap = s.Data.FindCapture("cap-003");
            Assert.IsNotNull(cap, "the capture is never deleted by a plan change unless asked");
            Assert.IsNull(cap.PlanVideoId);
            Assert.AreEqual("Applying the First Lock", cap.Title);
            Assert.AreEqual("video 4: Applying the First Lock", cap.WasPlannedAs);
            Assert.IsTrue(s.Plan.FindItem("v-004").IsDropped);
            Assert.IsTrue(s.Plan.UnplannedCaptures.Any(c => c.Id == "cap-003"));
            Assert.IsEmpty(TripValidator.Validate(s.Data));

            var s2 = Sample();
            new PlanEdits(s2, PlanEditMode.Working).DropVideo("v-004", null, deleteCaptures: true);
            Assert.IsNull(s2.Data.FindCapture("cap-003"));
        }

        [Test]
        public void UndoAnAdd_KeepsTheCaptureDetached_UnlessToldToDeleteIt()
        {
            var s = Sample();
            s.UndoAmendment("am-003");
            Assert.IsNull(s.Plan.FindItem("v-a001"));
            var cap = s.Data.FindCapture("cap-004");
            Assert.IsNotNull(cap);
            Assert.IsNull(cap.PlanVideoId);
            Assert.AreEqual("new video: Group Lock Box Walkthrough", cap.WasPlannedAs);
            Assert.IsEmpty(TripValidator.Validate(s.Data));

            var s2 = Sample();
            s2.UndoAmendment("am-003", deleteCaptures: true);
            Assert.IsNull(s2.Data.FindCapture("cap-004"));
        }

        [Test]
        public void DeletingFromTheOriginal_DetachesByDefault_OrDeletesTheCaptureToo()
        {
            var s = Sample();
            var edits = new PlanEdits(s, PlanEditMode.Original);
            edits.DeleteVideo("v-004");
            Assert.IsNull(s.Data.FindVideo("v-004"));
            var cap = s.Data.FindCapture("cap-003");
            Assert.IsNull(cap.PlanVideoId);
            Assert.AreEqual("video 4: Applying the First Lock", cap.WasPlannedAs);
            Assert.AreEqual(4, cap.PlannedNumber, "the number it was shot against is kept");
            Assert.IsEmpty(TripValidator.Validate(s.Data));

            var s2 = Sample();
            new PlanEdits(s2, PlanEditMode.Original).DeleteVideo("v-004", null, deleteCaptures: true);
            Assert.IsNull(s2.Data.FindCapture("cap-003"));
            Assert.IsEmpty(TripValidator.Validate(s2.Data));

            // a photo: the record keeps the description and says what it was
            var s3 = Sample();
            new PlanEdits(s3, PlanEditMode.Original).DeletePhoto("ph-002");
            var pc = s3.Data.FindPhotoCapture("pc-001");
            Assert.IsNull(pc.PhotoId);
            Assert.AreEqual("Ch.1 hero: gallery of confined spaces", pc.Text);
            Assert.AreEqual("photo: Ch.1 hero: gallery of confined spaces", pc.WasPlannedAs);
            var s4 = Sample();
            new PlanEdits(s4, PlanEditMode.Original).DeletePhoto("ph-002", null, deleteCaptures: true);
            Assert.IsNull(s4.Data.FindPhotoCapture("pc-001"));
            Assert.IsEmpty(TripValidator.Validate(s4.Data));
        }

        [Test]
        public void ADetachedCapture_CanBeFiledAgain_ByFilingItsTextIsNotNeeded_ItIsAVideo()
        {
            // a detached capture is a plain unplanned capture: filming it again is a new add, as for any unplanned title
            var s = Sample();
            new PlanEdits(s, PlanEditMode.Working).DropVideo("v-004");
            var am = s.AddUnplanned("Applying the First Lock", "b-002", "c-201");
            s.UpdateCapture("cap-003", c => { c.PlanVideoId = am.Results[0]; c.WasPlannedAs = null; });
            Assert.AreEqual(PlanItemStatus.Captured, s.Plan.FindItem(am.Results[0]).Status);
            Assert.IsEmpty(TripValidator.Validate(s.Data));
        }
    }
}

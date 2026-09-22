using System;
using System.IO;
using System.Linq;
using MediaTrip.Authoring;
using MediaTrip.Model;
using MediaTrip.Persistence;
using MediaTrip.Session;
using NUnit.Framework;

namespace MediaTrip.Tests
{
    public class OutlineEditorTests
    {
        private string _temp;
        [SetUp] public void SetUp() => _temp = Fixtures.NewTempFolder("outline");
        [TearDown] public void TearDown() => Fixtures.DeleteFolder(_temp);

        private TripSession Sample()
        {
            var d = Fixtures.LoadSample();
            d.FolderPath = _temp;
            return new TripSession(d, () => 0);
        }

        [Test]
        public void Chapters_AreThePlansChapters_AddReorderRemove()
        {
            var s = Sample();
            var ch = s.Outline.AddChapter("b-001", "Rescue Planning");
            Assert.AreEqual(3, ch.Number);
            Assert.AreEqual("Rescue Planning", s.Data.FindChapter(ch.Id).Name, "typing a chapter into an outline creates it in the shot list: the same object");
            Assert.AreEqual("b-001", s.Data.FindChapter(ch.Id).BookId);

            s.Outline.ReorderChapter("b-001", ch.Id, 0);
            Assert.AreEqual(1, s.Data.FindChapter(ch.Id).Number);
            Assert.AreEqual(1, s.Outline.FindChapter("b-001", ch.Id).Number);
            Assert.AreEqual(2, s.Outline.FindChapter("b-001", "c-101").Number);
            Assert.AreEqual("2.1", s.Outline.FindSection("b-001", "s-1-1").Number, "sections renumber with their chapter");
            Assert.AreEqual("3.2", s.Outline.FindSection("b-001", "s-2-2").Number);

            s.Outline.SetChapterName("b-001", ch.Id, "Rescue");
            Assert.AreEqual("Rescue", s.Data.FindChapter(ch.Id).Name);
            Assert.AreEqual("Rescue", s.Outline.FindChapter("b-001", ch.Id).Name, "the outline only echoes the name");
            s.PlanEditor.UpdateChapter("c-101", c => c.Name = "Spotting a Confined Space");
            Assert.AreEqual("Spotting a Confined Space", s.Outline.FindChapter("b-001", "c-101").Name, "renamed in the shot list, renamed in the outline");

            s.Outline.RemoveChapter("b-001", ch.Id);
            Assert.IsNull(s.Data.FindChapter(ch.Id));
            Assert.AreEqual("1.1", s.Outline.FindSection("b-001", "s-1-1").Number);
        }

        [Test]
        public void RemoveChapter_WithDependents_IsRefusedUntilConfirmed_ThenNothingIsOrphaned()
        {
            var s = Sample();
            var impact = Deletions.ForChapter(s.Data, "c-101");
            Assert.AreEqual(2, impact.Videos.Count);
            Assert.AreEqual(2, impact.Sections.Count);
            Assert.AreEqual(2, impact.PlacedMedia, "oa-001 (s-1-1) and oa-003 (s-1-2)");
            Assert.AreEqual(1, impact.Notes);
            Assert.AreEqual(1, impact.Captures);
            CollectionAssert.AreEqual(new[] { "c-102" }, impact.MoveTargets.Select(c => c.Id).ToList(), "the dialog can offer somewhere for the videos to go");
            Assert.IsTrue(impact.NeedsConfirm);
            Assert.Throws<PlanEditBlockedException>(() => s.Outline.RemoveChapter("b-001", "c-101"));

            s.Outline.RemoveChapter("b-001", "c-101", cascade: true, moveContentsTo: "c-102");
            Assert.IsNull(s.Data.FindChapter("c-101"));
            Assert.AreEqual("c-102", s.Data.FindVideo("v-001").ChapterId, "planned videos moved, not lost");
            Assert.AreEqual("c-102", s.Data.FindCapture("cap-001").ChapterId, "the capture follows and is never deleted");
            Assert.AreEqual(4, s.Data.Captures.Captures.Count);
            Assert.AreEqual(1, s.Data.Captures.OutlineAssignments.Count);
            Assert.AreEqual("oa-002", s.Data.Captures.OutlineAssignments[0].Id);
            Assert.IsEmpty(s.Data.Captures.Notes);
            Assert.AreEqual(1, s.Outline.FindChapter("b-001", "c-102").Number);
            Assert.AreEqual(HeroType.None, s.Data.FindPhoto("ph-002").HeroType, "c-102 already has a hero, so the moved one becomes an ordinary photo");
            Assert.IsEmpty(MediaTrip.Validation.TripValidator.Validate(s.Data));
        }

        [Test]
        public void RemoveChapter_WithNowhereToMove_DetachesTheVideos()
        {
            var s = Sample();
            s.Outline.RemoveChapter("b-001", "c-102", cascade: true);
            Assert.IsNull(s.Data.FindVideo("v-003"));
            var cap = s.Data.FindCapture("cap-002");
            Assert.IsNotNull(cap, "the capture is kept");
            Assert.IsNull(cap.PlanVideoId);
            Assert.IsNull(cap.ChapterId);
            Assert.AreEqual(3, cap.PlannedNumber, "it still says what it was shot against");
            Assert.IsFalse(s.Data.Captures.Amendments.Any(a => a.Id == "am-002"), "the rename of the removed video goes with it");
            Assert.IsEmpty(MediaTrip.Validation.TripValidator.Validate(s.Data));
        }

        [Test]
        public void Sections_AddUpdateReorderRemoveMove()
        {
            var s = Sample();
            var sec = s.Outline.AddSection("b-001", "c-102", "Re-testing After Breaks");
            Assert.AreEqual("2.3", sec.Number);
            s.Outline.ReorderSection("b-001", sec.Id, 0);
            Assert.AreEqual("2.1", sec.Number);
            Assert.AreEqual("2.2", s.Outline.FindSection("b-001", "s-2-1").Number);
            s.Outline.SetSectionName("b-001", sec.Id, "Re-testing");
            Assert.AreEqual("Re-testing", sec.Name);

            Assert.Throws<PlanEditBlockedException>(() => s.Outline.RemoveSection("b-001", "s-2-1"));
            s.Outline.RemoveSection("b-001", sec.Id);
            Assert.AreEqual("2.1", s.Outline.FindSection("b-001", "s-2-1").Number);

            // Move 2.2 into chapter 1; nothing references it.
            s.Outline.MoveSection("b-001", "s-2-2", "c-101", 0);
            Assert.AreEqual("1.1", s.Outline.FindSection("b-001", "s-2-2").Number);
            Assert.AreEqual("1.2", s.Outline.FindSection("b-001", "s-1-1").Number);
            Assert.AreEqual(1, s.Outline.FindChapter("b-001", "c-102").Sections.Count);

            // Move an assigned section: the assignment's chapterId follows.
            s.Outline.MoveSection("b-001", "s-2-1", "c-101");
            Assert.AreEqual("c-101", s.Data.Captures.OutlineAssignments.First(a => a.Id == "oa-002").ChapterId);
        }

        [Test]
        public void Header_KeepsItsIdentity()
        {
            var s = Sample();
            s.Outline.UpdateHeader("b-001", o => { o.ProgramName = "WDG"; o.BookId = "hacked"; o.Chapters = null; });
            Assert.AreEqual("WDG", s.Data.Outlines["b-001"].ProgramName);
            Assert.AreEqual("b-001", s.Data.Outlines["b-001"].BookId);
            Assert.AreEqual(2, s.Data.Outlines["b-001"].Chapters.Count);
        }

        [Test]
        public void DeleteSection_UnassignsItsMedia_WhichIsThenListedAsUnplaced()
        {
            var s = Sample();
            s.SetNote("b-001", "c-101", "s-1-1", "Pull quote here.");
            var impact = Deletions.ForSection(s.Data, "b-001", "s-1-1");
            Assert.AreEqual(1, impact.PlacedMedia);
            Assert.AreEqual(1, impact.Notes);
            Assert.IsEmpty(s.Queries.UnplacedIn("c-101"));

            Deletions.DeleteSection(s, "b-001", "s-1-1");
            Assert.IsNull(s.Outline.FindSection("b-001", "s-1-1"));
            Assert.IsNotNull(s.Data.FindCapture("cap-001"), "the media stays in the book");
            CollectionAssert.AreEqual(new[] { "cap-001" }, s.Queries.UnplacedIn("c-101").Select(e => e.Id).ToList(), "listed on the chapter so it can be placed again");
            Assert.AreEqual("", s.NoteText("c-101", "s-1-1"));
            Assert.AreEqual("1.1", s.Outline.FindSection("b-001", "s-1-2").Number);
        }

        [Test]
        public void Bullets_IndentOutdentRelabel()
        {
            var s = Sample();
            var b = s.Outline.Bullets("b-001", "s-1-1");
            var d = b.AddSibling("o-111c", "Hazardous atmosphere possible");
            Assert.AreEqual("d", d.Label);
            b.Indent(d.Id);
            Assert.AreEqual("1", d.Label, "child of c, numeric at depth 1");
            b.Outdent("o-111b2i");
            Assert.AreEqual("3", Node.Find(b.Roots, "o-111b2i").Label, "now a sibling of 1 and 2 under b");
            b.Remove("o-111b");
            Assert.AreEqual("b", Node.Find(b.Roots, "o-111c").Label);
            Assert.IsNull(Node.Find(b.Roots, "o-111b2i"));
        }

        [Test]
        public void Ensure_CreatesOutlineFromShotListChapters_AndSaves()
        {
            var s = Sample();
            s.SaveAll();
            var o = s.Outline.Ensure("b-002");
            Assert.AreEqual(2, o.Chapters.Count);
            Assert.AreEqual("c-201", o.Chapters[0].Id, "linked to the shot-list chapter id");
            var sec = s.Outline.AddSection("b-002", "c-201", "Sources of Energy");
            Assert.AreEqual("1.1", sec.Number);
            s.SaveNow();
            var again = TripLoader.Load(_temp);
            Assert.AreEqual("Sources of Energy", again.Outlines["b-002"].Chapters[0].Sections[0].Name);
            Assert.IsEmpty(MediaTrip.Validation.TripValidator.Validate(again));
        }
    }

    public class PeopleEditorTests
    {
        private static TripSession Sample() => new TripSession(Fixtures.LoadSample(), () => 0);

        [Test]
        public void Add_Update_Roles()
        {
            var s = Sample();
            var p = s.People.AddFromName("Ana Maria Ruiz", Org.Client, new[] { PersonRole.Sme }, "Operator");
            Assert.AreEqual("Ana Maria", p.FirstName);
            Assert.AreEqual("Ruiz", p.LastName);
            s.People.AddRole(p.Id, PersonRole.Sme);
            s.People.AddRole(p.Id, PersonRole.Personnel);
            CollectionAssert.AreEqual(new[] { PersonRole.Sme, PersonRole.Personnel }, p.Roles);
            s.People.RemoveRole(p.Id, PersonRole.Sme);
            s.People.SetOrg(p.Id, Org.Leadership);
            s.People.Update(p.Id, x => { x.Title = "Shift Lead"; x.Id = "hacked"; });
            Assert.AreEqual("Shift Lead", s.Data.FindPerson(p.Id).Title);
            Assert.AreEqual(Org.Leadership, p.Org);
            Assert.Throws<ArgumentException>(() => s.People.Add("", ""));
            Assert.IsTrue(s.IsDirty);
        }

        [Test]
        public void Remove_Referenced_Blocked_DetachConvertsToFreeText()
        {
            var s = Sample();
            var ex = Assert.Throws<PlanEditBlockedException>(() => s.People.Remove("p-007")); // Nina: SME on videos, on captures
            Assert.IsTrue(ex.References.Any(r => r.Kind == "video"));
            Assert.IsTrue(ex.References.Any(r => r.Kind == "capture"));

            s.People.Remove("p-007", detach: true);
            Assert.IsNull(s.Data.FindPerson("p-007"));
            var cp = s.Data.FindCapture("cap-002").People[0];
            Assert.IsNull(cp.PersonId);
            Assert.AreEqual("Nina Okoro", cp.Name);
            Assert.IsEmpty(s.Data.FindVideo("v-003").SmeIds);
            Assert.AreEqual("Nina Okoro", s.Data.FindVideo("v-003").SmeText);

            s.People.Remove("p-005", detach: true); // Jo Varga is on book 2's team
            CollectionAssert.AreEqual(new[] { "p-002" }, s.Data.FindBook("b-002").Team.MemberIds);
            CollectionAssert.Contains(s.Data.FindBook("b-002").Team.MemberNames, "Jo Varga");

            s.People.Remove("p-008"); // Gus: unreferenced
            Assert.IsEmpty(MediaTrip.Validation.TripValidator.Validate(s.Data), "no dangling references left behind");
        }

        [Test]
        public void Merge_RepointsReferences_UnionsRoles()
        {
            var s = Sample();
            var dup = s.People.AddFromName("Nina  Okoro", Org.Other, new[] { PersonRole.Personnel });
            s.PlanEditor.UpdateVideo("v-005", v => v.SmeIds = new System.Collections.Generic.List<string> { dup.Id });
            s.Data.FindCapture("cap-004").People[0].PersonId = dup.Id;
            s.Data.FindBook("b-001").Team.MemberIds.Add(dup.Id);

            var groups = s.People.FindDuplicates();
            Assert.AreEqual(1, groups.Count);
            CollectionAssert.AreEquivalent(new[] { "p-007", dup.Id }, groups[0].Select(p => p.Id));

            var kept = s.People.Merge("p-007", dup.Id);
            Assert.AreSame(s.Data.FindPerson("p-007"), kept);
            Assert.IsNull(s.Data.FindPerson(dup.Id));
            CollectionAssert.AreEqual(new[] { "p-007" }, s.Data.FindVideo("v-005").SmeIds);
            Assert.AreEqual("p-007", s.Data.FindCapture("cap-004").People[0].PersonId);
            CollectionAssert.AreEqual(new[] { "p-002", "p-003", "p-007" }, s.Data.FindBook("b-001").Team.MemberIds);
            CollectionAssert.AreEquivalent(new[] { PersonRole.Sme, PersonRole.Personnel }, kept.Roles);
            Assert.IsEmpty(s.People.FindDuplicates());
            Assert.IsEmpty(MediaTrip.Validation.TripValidator.Validate(s.Data));
            Assert.Throws<ArgumentException>(() => s.People.Merge("p-007", "p-007"));
        }

        [Test]
        public void LikelyDuplicates_ByFuzzyName()
        {
            var s = Sample();
            s.People.AddFromName("Nina Okora");
            var likely = s.People.FindLikelyDuplicates();
            Assert.AreEqual(1, likely.Count);
            Assert.AreEqual("Okoro", likely[0].a.LastName);
        }
    }
}

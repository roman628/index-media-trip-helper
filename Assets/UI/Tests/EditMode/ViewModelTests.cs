using System.IO;
using System.Linq;
using MediaTrip.Model;
using MediaTrip.Persistence;
using MediaTrip.Session;
using MediaTrip.Status;
using MediaTrip.UI;
using MediaTrip.UI.ViewModels;
using NUnit.Framework;
using UnityEngine;

namespace MediaTrip.UI.Tests
{
    public static class UiFixtures
    {
        public static string SampleFolder => Path.Combine(Application.streamingAssetsPath, "SampleTrip");
        public static TripSession Session() => new TripSession(TripLoader.Load(SampleFolder), () => 0);
    }

    public class CaptureDraftTests
    {
        [Test]
        public void Pick_FillsContext_Photos_AndSuggestion()
        {
            var s = UiFixtures.Session();
            var d = new CaptureDraft();
            d.SetTitle("dead end");
            Assert.AreEqual("v-c001", d.Suggestions(s)[0].Item.Id, "superseded sources are hidden; the combined item matches on its original title");
            d.Pick(s, "v-001");
            Assert.AreEqual("v-c001", d.ItemId, "a superseded pick lands on what was actually shot");
            Assert.AreEqual("Walkthrough and Entry Point Hazards", d.Title);
            Assert.AreEqual("b-001", d.BookId);
            Assert.AreEqual("c-101", d.ChapterId);
            CollectionAssert.AreEquivalent(new[] { "ph-001", "ph-003", "ph-004" }, d.Photos.Keys);
            Assert.IsNotNull(d.Outline);
            Assert.AreEqual("s-1-2", d.Outline.SectionId);
            Assert.IsFalse(d.OutlineConfirmed);
            Assert.IsFalse(d.ShowSuggestions);

            d.SetTitle("something else");
            Assert.IsNull(d.ItemId);
            Assert.IsEmpty(d.Photos);
            Assert.IsNull(d.Outline);
        }

        [Test]
        public void Save_PlannedItem_WritesCapture_Assignment_AndPeople()
        {
            var s = UiFixtures.Session();
            var d = CaptureDraft.ForItem(s, "v-005");
            d.Description = "Second operator";
            d.CameraCount = 2;
            d.Photos["ph-006"] = true;
            d.ExtraPhoto = "Placard";
            d.More = true;
            d.AddPerson("Owen Pratt", "Electrician");
            d.Keywords = "try step, verification";
            d.OutlineConfirmed = d.Outline != null;
            var cap = d.Save(s, "d-002");
            Assert.AreEqual("v-005", cap.PlanVideoId);
            Assert.AreEqual(5, cap.PlannedNumber);
            Assert.AreEqual(4, cap.CapturedOrder);
            Assert.AreEqual(2, cap.CameraCount);
            Assert.AreEqual(2, cap.Photos.Count);
            Assert.IsTrue(cap.Photos.Any(p => p.PhotoId == "ph-006" && p.Captured));
            Assert.IsTrue(cap.Photos.Any(p => p.Text == "Placard"));
            CollectionAssert.AreEqual(new[] { "try step", "verification" }, cap.Keywords);
            Assert.AreEqual("Nina Okoro", cap.People[0].Name, "the planned SME is filled in from the pick");
            var owen = cap.People.Single(p => p.Name == "Owen Pratt");
            Assert.IsNotNull(owen.PersonId, "typed person was added to the registry");
            Assert.AreEqual("Owen Pratt", s.Data.FindPerson(owen.PersonId).FullName);
            Assert.AreEqual(PlanItemStatus.Dropped, s.Plan.FindItem("v-005").Status, "a drop amendment still stands; the capture is recorded against it");
            if (d.Outline != null) Assert.IsTrue(s.Data.Captures.OutlineAssignments.Any(a => a.MediaRef.Id == cap.Id && a.Confirmed));
        }

        [Test]
        public void Save_UnplannedTitle_AddsAmendment()
        {
            var s = UiFixtures.Session();
            var d = new CaptureDraft();
            d.SetTitle("Exterior of the plant");
            var cap = d.Save(s, "d-002");
            Assert.IsNotNull(cap.PlanVideoId);
            Assert.IsNull(cap.PlannedNumber);
            var am = s.Data.Captures.Amendments.Last();
            Assert.AreEqual(AmendmentType.Add, am.Type);
            Assert.AreEqual(cap.PlanVideoId, am.Results[0]);
            Assert.AreEqual("Exterior of the plant", s.Plan.FindItem(cap.PlanVideoId).Title);
            Assert.AreEqual(PlanItemStatus.Captured, s.Plan.FindItem(cap.PlanVideoId).Status);
        }

        [Test]
        public void ForCapture_RoundTripsAnExistingCapture()
        {
            var s = UiFixtures.Session();
            var d = CaptureDraft.ForCapture(s, s.Data.FindCapture("cap-003"));
            Assert.AreEqual("cap-003", d.EditingCaptureId);
            Assert.AreEqual(2, d.CameraCount);
            Assert.AreEqual(2, d.People.Count);
            Assert.IsTrue(d.Photos["ph-005"]);
            Assert.IsFalse(d.Photos["ph-007"]);
            d.Location = "MCC room 3";
            var cap = d.Save(s, "d-002");
            Assert.AreEqual("cap-003", cap.Id);
            Assert.AreEqual("MCC room 3", s.Data.FindCapture("cap-003").Location);
            Assert.AreEqual(4, s.Data.Captures.Captures.Count, "edited in place, not duplicated");
        }
    }

    public class AmendDraftTests
    {
        [Test]
        public void Combine_RecordsAmendment_AndMovesCapture()
        {
            var s = UiFixtures.Session();
            var a = AmendDraft.For(s, "v-004");
            a.SetMode(s, "combine");
            var sibs = a.Siblings(s);
            CollectionAssert.AreEqual(new[] { "v-a001" }, sibs.Select(x => x.Id).ToList(), "same chapter, active only");
            Assert.IsFalse(a.CanApply);
            a.ToggleWith(s, "v-a001");
            Assert.AreEqual("Applying the First Lock and Group Lock Box Walkthrough", a.Title);
            Assert.IsTrue(a.CanApply);
            var rid = a.Apply(s);
            Assert.AreEqual(PlanItemStatus.Superseded, s.Plan.FindItem("v-004").Status);
            Assert.AreEqual(rid, s.Plan.Resolve("v-004").Single().Id);
            Assert.AreEqual(PlanItemStatus.Captured, s.Plan.FindItem(rid).Status, "the existing capture moved onto the combined item");
            Assert.AreEqual(3, s.Data.Captures.Captures.Count, "two captures became one");
        }

        [Test]
        public void Rename_And_Drop()
        {
            var s = UiFixtures.Session();
            var a = AmendDraft.For(s, "v-004");
            a.SetMode(s, "rename");
            a.Title = "First Lock, Applied";
            a.Apply(s);
            Assert.AreEqual("First Lock, Applied", s.Plan.FindItem("v-004").Title);
            Assert.AreEqual("First Lock, Applied", s.Data.FindCapture("cap-003").Title);

            var b = AmendDraft.For(s, "v-004");
            b.SetMode(s, "drop");
            b.Reason = "line down";
            b.Apply(s);
            Assert.AreEqual(PlanItemStatus.Dropped, s.Plan.FindItem("v-004").Status);
            Assert.IsNotNull(s.Data.FindCapture("cap-003"), "a plan change never deletes a capture");
        }
    }

    public class JsonHighlighterTests
    {
        [Test]
        public void Colorizes_KeysStringsNumbers_AndFindsHighlightLines()
        {
            var json = "{\n  \"id\": \"v-003\",\n  \"number\": 3,\n  \"done\": true,\n  \"list\": [\n    \"a\"\n  ]\n}";
            var lines = JsonHighlighter.Lines(json, "v-003", "#111111", "#222222", "#333333");
            Assert.AreEqual(8, lines.Count);
            StringAssert.Contains("<color=#111111>\"id\"</color>:", lines[1].Rich);
            StringAssert.Contains("<color=#222222>\"v-003\"</color>,", lines[1].Rich);
            StringAssert.Contains("<color=#333333>3</color>,", lines[2].Rich);
            StringAssert.Contains("<color=#333333>true</color>", lines[3].Rich);
            Assert.IsTrue(lines[1].Highlight);
            Assert.IsFalse(lines[2].Highlight);
            Assert.AreEqual(1, JsonHighlighter.FirstHighlight(lines));
            Assert.AreEqual("<color=#222222>\"a\"</color>", lines[5].Rich.Trim());
        }

        [Test]
        public void AngleBrackets_AreEscaped()
        {
            var rich = JsonHighlighter.Colorize("  \"t\": \"<b>x</b>\",", "#1", "#2", "#3");
            Assert.IsFalse(rich.Contains("<b>"));
        }
    }

    public class RenumberTrackerTests
    {
        [Test]
        public void Shifted_UntilAccepted()
        {
            var s = UiFixtures.Session();
            var t = new RenumberTracker();
            t.Snapshot(s.Data);
            Assert.IsFalse(t.AnyShifted(s.Data));
            var v = s.PlanEditor.AddVideo("c-101", "Inserted", 0);
            t.Track(v);
            Assert.IsTrue(t.AnyShifted(s.Data));
            Assert.AreEqual(1, t.Was(s.Data.FindVideo("v-001")));
            Assert.AreEqual(3, t.Was(s.Data.FindVideo("v-003")));
            Assert.IsNull(t.Was(v), "a new video has no paper number");
            Assert.AreEqual(5, t.Shifted(s.Data).Count);
            t.Accept(s.Data);
            Assert.IsFalse(t.AnyShifted(s.Data));
        }
    }

    public class ValidationSummaryTests
    {
        [Test]
        public void Badge_Counts_Routing()
        {
            var s = UiFixtures.Session();
            var clean = ValidationSummary.Of(s.Data);
            Assert.IsNull(clean.BadgeText);
            s.Data.FindVideo("v-002").PhotoRefs.Add("ph-nope");
            s.Data.FindVideo("v-002").ChapterId = null;
            s.Data.Captures.Captures[0].DayId = "d-x";
            var v = ValidationSummary.Of(s.Data);
            Assert.AreEqual("3 errors", v.BadgeText);
            Assert.AreEqual(2, v.CountFor("v-002"));
            Assert.AreEqual(2, v.InDocument(DocumentKind.ShotList).Count);
            Assert.AreEqual(1, v.InDocument(DocumentKind.Captures).Count);
            Assert.AreEqual(Screen.ShotList, ValidationSummary.FixScreen(v.For("v-002")[0], s.Data));
            Assert.AreEqual(Screen.Summary, ValidationSummary.FixScreen(v.For("cap-001")[0], s.Data));
        }
    }

    public class GlobalSearchTests
    {
        [Test]
        public void Ch2Hero_FindsTheHeroPhoto_AndSectionsMatch()
        {
            var s = UiFixtures.Session();
            var r = GlobalSearch.Run(s, "ch2 hero", includePeople: true);
            Assert.IsTrue(r.Photos.Any(p => p.Item.Id == "ph-003" || p.Item.Id == "ph-007"), string.Join(", ", r.Photos.Select(p => p.Item.Id)));
            var r2 = GlobalSearch.Run(s, "permit", includePeople: true);
            Assert.AreEqual("v-003", r2.Items[0].Item.Id);
            Assert.IsTrue(r2.Sections.Any(x => x.Item.Section.Id == "s-2-1"));
            var r3 = GlobalSearch.Run(s, "nina", includePeople: true);
            Assert.AreEqual("p-007", r3.People[0].Item.Id);
            Assert.IsTrue(GlobalSearch.Run(s, "", true).IsEmpty);
        }
    }

    public class FmtAndThemeTests
    {
        [Test]
        public void Identity_Place_DayOf()
        {
            var s = UiFixtures.Session();
            Assert.AreEqual("ACME · WDG · P5", Fmt.Identity(s.Data.Trip));
            Assert.AreEqual("Boise, ID", Fmt.Place(s.Data.Trip));
            Assert.AreEqual("Day 2 of 2", Fmt.DayOf(s.Data.Trip, "d-002"));
            Assert.AreEqual("d-002", Fmt.TodayOrLast(s.Data.Trip, new System.DateTime(2026, 9, 16)).Id);
            Assert.AreEqual("d-002", Fmt.TodayOrLast(s.Data.Trip, new System.DateTime(2027, 1, 1)).Id, "falls back to the last day");
            Assert.AreEqual("Tue, Sep 15", U.FmtDate("2026-09-15"));
            Assert.AreEqual("Lockout / Tagout · Ch.1", Fmt.BookChapter(s.Data, "b-002", "c-201"));
        }

        [Test]
        public void ThemeVariables_AllSixteen_InEveryVariant()
        {
            var keys = new[] { "bg", "sf", "sf2", "bd", "tx", "tx2", "ac", "acTx", "ok", "okTx", "warn", "bad", "badTx", "chrome", "chromeTx", "bw" };
            foreach (var name in ThemeManager.Names)
                foreach (var dark in new[] { false, true })
                {
                    var v = ThemeManager.ReadVariables(name, dark);
                    Assert.AreEqual(16, v.Count, name + (dark ? " dark" : " light"));
                    foreach (var k in keys) Assert.IsTrue(v.ContainsKey(k), name + " missing " + k);
                }
            var beacon = ThemeManager.ReadVariables("Beacon", false);
            Assert.AreEqual("3px", beacon["bw"]);
            Assert.Greater(ThemeManager.Contrast(ThemeManager.ParseColor(beacon["acTx"], Color.black), ThemeManager.ParseColor(beacon["ac"], Color.white)), 14f);
        }

        [Test]
        public void ComponentStyles_HardcodeNoColors()
        {
            var uss = File.ReadAllText("Assets/UI/Resources/Styles/App.uss");
            var hex = System.Text.RegularExpressions.Regex.Matches(uss, @"#[0-9a-fA-F]{3,8}\b");
            Assert.AreEqual(0, hex.Count, "App.uss must only use var(--...) for colors; found " + string.Join(", ", hex.Cast<System.Text.RegularExpressions.Match>().Select(m => m.Value)));
        }
    }

    public class TransferHelperTests
    {
        [Test]
        public void SafeName_AndDocumentNames()
        {
            var d = TripLoader.Load(UiFixtures.SampleFolder);
            Assert.AreEqual("ACME_WDG_P5", Transfer.TripTransfer.SafeName(d));
            Assert.AreEqual("outlines/book-1.json", Transfer.TripTransfer.DocumentFileName(d, DocumentKind.Outline, "b-001"));
            Assert.AreEqual("shotlist.json", Transfer.TripTransfer.DocumentFileName(d, DocumentKind.ShotList, null));
        }
    }
}

using System.Linq;
using MediaTrip.Model;
using MediaTrip.UI.Shell;
using MediaTrip.UI.ViewModels;
using NUnit.Framework;

namespace MediaTrip.UI.Tests
{
    public class TripDaysTests
    {
        [Test]
        public void ShootingDays_AreTheDaysBetweenTheTravelDays()
        {
            CollectionAssert.AreEqual(new[] { "2026-09-15", "2026-09-16" }, TripDays.Between("2026-09-14", "2026-09-17"));
            CollectionAssert.AreEqual(new[] { "2026-09-14", "2026-09-15" }, TripDays.Between("2026-09-14", "2026-09-15"), "no day in between: shoot on the travel days");
            CollectionAssert.AreEqual(new[] { "2026-09-14" }, TripDays.Between("2026-09-14", "2026-09-14"));
            Assert.IsEmpty(TripDays.Between("2026-09-17", "2026-09-14"), "depart before arrive");
            Assert.IsEmpty(TripDays.Between("2026-09-14", ""), "not until both are in");
            Assert.IsEmpty(TripDays.Between("soon", "2026-09-14"));
        }

        [Test]
        public void AutoFill_AddsTheDays_LabelsThem_AndNeverTouchesADayWithMedia()
        {
            var s = UiFixtures.Session();   // arrive 09-14, depart 09-17, days on the 15th and 16th, both with captures
            Assert.IsFalse(TripDays.AutoFill(s), "already right");

            s.UpdateDates(d => d.Depart = "2026-09-19");
            Assert.IsTrue(TripDays.AutoFill(s));
            CollectionAssert.AreEqual(new[] { "2026-09-15", "2026-09-16", "2026-09-17", "2026-09-18" }, s.Data.Trip.Days.Select(d => d.Date).ToList());
            CollectionAssert.AreEqual(new[] { "Day 1", "Day 2", "Day 3", "Day 4" }, s.Data.Trip.Days.Select(d => d.Label).ToList());
            Assert.AreEqual("d-001", s.Data.Trip.Days[0].Id, "existing days keep their ids");

            s.UpdateDates(d => { d.Arrive = "2026-09-15"; d.Depart = "2026-09-17"; });
            Assert.IsTrue(TripDays.AutoFill(s));
            CollectionAssert.AreEqual(new[] { "2026-09-15", "2026-09-16" }, s.Data.Trip.Days.Select(d => d.Date).ToList(),
                "the 15th now falls on a travel day but has captures, so it stays; the empty days outside the range go");
        }

        [Test]
        public void ANewTrip_GetsItsDaysFromItsDates()
        {
            var s = new MediaTrip.Session.TripSession(TripData.CreateNew("NEW", "TRP"), () => 0);
            s.UpdateDates(d => { d.Arrive = "2026-10-05"; d.Depart = "2026-10-09"; });
            Assert.IsTrue(TripDays.AutoFill(s));
            Assert.AreEqual(3, s.Data.Trip.Days.Count);
            Assert.AreEqual("Day 3", s.Data.Trip.Days[2].Label);
        }
    }

    public class PlaceSummaryTests
    {
        [Test]
        public void ACollapsedRow_ShowsItsMedia_AndTheFirstWordsOfItsNote()
        {
            var s = UiFixtures.Session();
            var chapter = PlaceSummary.Of(s, "c-101", null);
            Assert.IsEmpty(chapter.Media, "nothing is placed on the chapter as a whole yet");
            Assert.AreEqual("Consider opening the chapter on the gallery wide…", chapter.NoteExcerpt, "the first few words, not the whole note");

            var section = PlaceSummary.Of(s, "c-101", "s-1-2");
            Assert.AreEqual(1, section.Media.Count);
            Assert.AreEqual(1, section.Suggested, "oa-003 is a suggestion nobody has kept yet");
            Assert.AreEqual("", section.NoteExcerpt);

            s.SetNote("b-001", "c-101", "s-1-2", "One two three four five six seven eight nine ten eleven");
            Assert.AreEqual("One two three four five six seven eight…", PlaceSummary.Of(s, "c-101", "s-1-2").NoteExcerpt);
            Assert.IsTrue(PlaceSummary.Of(s, "c-202", null).IsEmpty);
        }

        [Test]
        public void BookNames_AreShownAsTheyAre()
        {
            var s = UiFixtures.Session();
            Assert.AreEqual("Confined Space Entry", Fmt.BookName(s.Data.FindBook("b-001")), "no “Book 1” in front: real names carry their own numbers");
            Assert.AreEqual("Lockout / Tagout · Ch.2", Fmt.BookChapter(s.Data, "b-002", "c-202"));
            Assert.AreEqual("Untitled book", Fmt.BookName(new Book { Name = " " }));
        }
    }

    public class ShortcutsSheetTests
    {
        [Test]
        public void Shortcuts_AreTheOnesForTheScreen()
        {
            var outlineEdit = ShortcutsSheet.For(Screen.Outlines, edit: true);
            Assert.IsTrue(outlineEdit.Any(x => x.what.Contains("next section")));
            Assert.IsTrue(outlineEdit.Any(x => x.what.Contains("new chapter")));
            Assert.IsFalse(ShortcutsSheet.For(Screen.Outlines, edit: false).Any(x => x.what.Contains("next section")), "the normal state annotates; it has no structure keys");
            Assert.IsTrue(ShortcutsSheet.For(Screen.ShotList, edit: false).Any(x => x.what.Contains("Expand all")));
            Assert.IsFalse(ShortcutsSheet.For(Screen.Covers, edit: false).Any(x => x.what == "Edit"), "Covers has no edit state");
        }
    }
}

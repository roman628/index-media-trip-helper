using System;
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
    public class AutosaverTests
    {
        private double _now;
        private int _saves;

        private Autosaver Make(bool fail = false)
        {
            _now = 0; _saves = 0;
            return new Autosaver(() => _now, () => { if (fail) throw new IOException("disk full"); _saves++; })
            { DebounceSeconds = 1.0, MaxLatencySeconds = 5.0 };
        }

        [Test]
        public void Debounce_WaitsForQuiet()
        {
            var a = Make();
            Assert.IsFalse(a.Tick(), "nothing dirty");
            a.MarkDirty();
            _now = 0.5; Assert.IsFalse(a.Tick());
            a.MarkDirty();                       // keeps typing
            _now = 1.2; Assert.IsFalse(a.Tick()); // only 0.7s quiet
            _now = 1.6; Assert.IsTrue(a.Tick());  // 1.1s quiet
            Assert.AreEqual(1, _saves);
            Assert.IsFalse(a.IsDirty);
            Assert.IsFalse(a.Tick());
        }

        [Test]
        public void MaxLatency_ForcesSaveDuringContinuousEdits()
        {
            var a = Make();
            for (int i = 0; i < 20; i++)
            {
                _now = i * 0.5;
                a.MarkDirty();
                a.Tick();
            }
            Assert.GreaterOrEqual(_saves, 1, "continuous edits still get saved within the max latency");
        }

        [Test]
        public void FlushNow_AndFailureRetry()
        {
            var a = Make(fail: true);
            Exception seen = null;
            a.SaveFailed += e => seen = e;
            a.MarkDirty();
            Assert.IsFalse(a.FlushNow());
            Assert.IsNotNull(seen);
            Assert.IsTrue(a.IsDirty, "stays dirty so it retries");
            Assert.IsNotNull(a.LastError);
            _now = 0.5; Assert.IsFalse(a.Tick(), "retry is pushed out by one debounce window");
            _now = 1.5; Assert.IsFalse(a.Tick()); // still fails, but it did try
        }
    }

    public class SessionTests
    {
        private string _temp;
        private double _now;

        [SetUp]
        public void SetUp()
        {
            _temp = Fixtures.NewTempFolder("session");
            _now = 0;
        }

        [TearDown] public void TearDown() => Fixtures.DeleteFolder(_temp);

        private TripSession OpenCopyOfSample()
        {
            var data = Fixtures.LoadSample();
            data.FolderPath = _temp;
            TripSaver.SaveAll(data);
            var s = TripSession.Open(_temp, () => _now);
            s.Autosaver.DebounceSeconds = 1.0;
            return s;
        }

        [Test]
        public void Mutation_MarksDirty_AutosaveWritesOnlyThatDocument()
        {
            var s = OpenCopyOfSample();
            var tripBefore = File.GetLastWriteTimeUtc(Path.Combine(_temp, "trip.json"));
            var capturesText = File.ReadAllText(Path.Combine(_temp, "captures.json"));

            var cap = s.AddCapture(new Capture { DayId = "d-002", PlanVideoId = "v-004", Title = "Second take" });
            Assert.IsTrue(s.IsDirty);
            Assert.IsTrue(Guid.TryParse(cap.Id.Substring("cap-".Length), out _), "new capture id is a GUID");
            Assert.AreEqual(4, cap.CapturedOrder, "next order on day 2");
            Assert.AreEqual(4, cap.PlannedNumber);
            Assert.AreEqual("b-002", cap.BookId);

            _now = 0.5; s.Autosaver.Tick();
            Assert.AreEqual(capturesText, File.ReadAllText(Path.Combine(_temp, "captures.json")), "not yet written");
            _now = 2.0; Assert.IsTrue(s.Autosaver.Tick());
            Assert.IsFalse(s.IsDirty);

            var reloaded = TripLoader.Load(_temp);
            Assert.IsTrue(reloaded.Captures.Captures.Any(c => c.Id == cap.Id));
            Assert.AreEqual(tripBefore, File.GetLastWriteTimeUtc(Path.Combine(_temp, "trip.json")), "trip.json untouched");
        }

        [Test]
        public void Combine_Split_ThroughSession_ResolveAndPersist()
        {
            var s = OpenCopyOfSample();
            var am = s.Combine(new[] { "v-003", "v-004" }, "Permit and First Lock", "same SME");
            var newId = am.Results[0];
            Assert.AreEqual(PlanItemStatus.Superseded, s.Plan.FindItem("v-003").Status);
            Assert.AreEqual(newId, s.Plan.Resolve("v-004").Single().Id);
            Assert.AreEqual("Permit and First Lock", s.Plan.FindItem(newId).Title);
            // v-003 was captured before the combine; that capture still hangs off v-003, and the
            // combined item has none yet, so the combined item is not captured.
            Assert.AreEqual(PlanItemStatus.NotCaptured, s.Plan.FindItem(newId).Status);

            var split = s.Split(newId, new[] { "Permit", "First Lock" });
            Assert.AreEqual(2, split.Results.Count);
            Assert.AreEqual(2, s.Plan.Resolve("v-003").Count);
            Assert.AreEqual("Permit", s.Plan.FindItem(split.Results[0]).Title);

            s.SaveNow();
            var again = TripLoader.Load(_temp);
            Assert.AreEqual(6, again.Captures.Amendments.Count);
            Assert.AreEqual("Filling Out the Entry Permit", again.FindVideo("v-003").Title, "shot list never mutated");
        }

        [Test]
        public void CheckOff_ResolvesSupersededIds_AndRefusesAmbiguousSplit()
        {
            var s = OpenCopyOfSample();
            var cap = s.CheckOff("v-005", "d-002"); // dropped items can still be checked off; the drop is just an amendment
            Assert.AreEqual("v-005", cap.PlanVideoId);

            var cap2 = s.CheckOff("v-001", "d-002");
            Assert.AreEqual("v-c001", cap2.PlanVideoId, "checking off a combined source lands on the combined item");

            s.Split("v-004", new[] { "A", "B" });
            Assert.Throws<InvalidOperationException>(() => s.CheckOff("v-004", "d-002"));
        }

        [Test]
        public void ChangedEvent_Version_AndCachedViewsInvalidate()
        {
            var s = OpenCopyOfSample();
            int changes = 0;
            s.Changed += () => changes++;
            var before = s.Queries;
            var v = s.Version;
            s.Drop("v-004", "weather");
            Assert.AreEqual(1, changes);
            Assert.AreEqual(v + 1, s.Version);
            Assert.AreNotSame(before, s.Queries);
            Assert.AreEqual(PlanItemStatus.Dropped, s.Queries.Item("v-004").Status);
        }

        [Test]
        public void FindOrAddPerson_ReusesExact_AddsNew()
        {
            var s = OpenCopyOfSample();
            Assert.AreEqual("p-007", s.FindOrAddPerson("Nina Okoro").Id);
            Assert.IsFalse(s.IsDirty, "no change when the person exists and no new role");

            var owen = s.FindOrAddPerson("Owen Pratt", Org.Client, PersonRole.Sme, "Electrician");
            Assert.AreEqual("Owen", owen.FirstName);
            Assert.AreEqual("Pratt", owen.LastName);
            Assert.IsTrue(owen.HasRole(PersonRole.Sme));
            Assert.AreEqual(9, s.Data.Trip.People.Count);
            Assert.IsTrue(s.IsDirty);
            Assert.AreSame(owen, s.FindOrAddPerson("owen pratt"));

            var mono = s.FindOrAddPerson("Cher");
            Assert.AreEqual("Cher", mono.FirstName);
            Assert.AreEqual("", mono.LastName);
        }

        [Test]
        public void OutlineAssignment_FromSuggestion_ThenConfirm()
        {
            var s = OpenCopyOfSample();
            var cap = s.Data.FindCapture("cap-002");
            var suggestions = s.Search.SuggestOutlineFor(cap);
            Assert.IsNotEmpty(suggestions);
            Assert.AreEqual("s-2-1", suggestions[0].SectionId);
            var a = s.AssignSuggestion(new MediaRef(MediaRefKind.Capture, cap.Id), suggestions[0]);
            Assert.IsFalse(a.Confirmed);
            Assert.AreEqual(AssignmentSource.Auto, a.Source);
            Assert.AreEqual(suggestions[0].Score, a.Confidence);
            s.ConfirmAssignment(a.Id);
            Assert.IsTrue(s.Data.Captures.OutlineAssignments.Single(x => x.Id == a.Id).Confirmed);
        }

        [Test]
        public void CreateOutline_ForBookWithoutOne_SavesToBookNumberedFile()
        {
            var s = OpenCopyOfSample();
            s.EditOutline("b-002", o => o.Chapters[0].Sections.Add(new OutlineSection { Id = Ids.New("s"), Number = "1.1", Name = "Energy sources" }));
            s.SaveNow();
            Assert.IsTrue(File.Exists(Path.Combine(_temp, "outlines", "book-2.json")));
            var again = TripLoader.Load(_temp);
            Assert.AreEqual("Isolating Energy Sources", again.Outlines["b-002"].Chapters[0].Name);
            Assert.AreEqual("Energy sources", again.Outlines["b-002"].Chapters[0].Sections[0].Name);
        }

        [Test]
        public void Dispose_Flushes()
        {
            var s = OpenCopyOfSample();
            s.AddAction("Buy gaffer tape");
            s.Dispose();
            Assert.IsTrue(TripLoader.Load(_temp).Trip.Actions.Any(a => a.Text == "Buy gaffer tape"));
        }

        [Test]
        public void Validator_SampleIsClean_AndCatchesBrokenRefs()
        {
            var data = Fixtures.LoadSample();
            var issues = TripValidator.Validate(data);
            Assert.IsEmpty(issues, string.Join("\n", issues));

            data.ShotList.Videos[0].PhotoRefs.Add("ph-999");
            data.Captures.Captures[0].DayId = "d-999";
            issues = TripValidator.Validate(data);
            Assert.AreEqual(2, issues.Count(i => i.Severity == IssueSeverity.Error));
        }
    }
}

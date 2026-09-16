using System.IO;
using System.IO.Compression;
using System.Linq;
using MediaTrip.Model;
using MediaTrip.Persistence;
using MediaTrip.Session;
using MediaTrip.Validation;
using NUnit.Framework;

namespace MediaTrip.Tests
{
    public class ImportExportTests
    {
        private string _temp;
        private string Lib => Path.Combine(_temp, "Trips");

        [SetUp] public void SetUp() => _temp = Fixtures.NewTempFolder("pkg");
        [TearDown] public void TearDown() => Fixtures.DeleteFolder(_temp);

        private string ExportSample(string name = "sample.zip")
        {
            var zip = Path.Combine(_temp, name);
            TripPackage.ExportZip(Fixtures.LoadSample(), zip);
            return zip;
        }

        /// <summary>Write a zip from a set of (entryName, text) pairs.</summary>
        private string MakeZip(string name, params (string entry, string text)[] entries)
        {
            var zip = Path.Combine(_temp, name);
            using (var fs = new FileStream(zip, FileMode.Create))
            using (var z = new ZipArchive(fs, ZipArchiveMode.Create))
                foreach (var (entry, text) in entries)
                {
                    var e = z.CreateEntry(entry);
                    using (var w = new StreamWriter(e.Open())) w.Write(text);
                }
            return zip;
        }

        [Test]
        public void ExportZip_ContainsEveryDocument_AndValidatesClean()
        {
            var zip = ExportSample();
            var report = TripPackage.ValidateZip(zip, Lib);
            Assert.IsTrue(report.CanImport, string.Join("\n", report.Issues));
            CollectionAssert.AreEquivalent(new[] { "trip.json", "shotlist.json", "captures.json", "outlines/book-1.json" }, report.Files);
            Assert.AreEqual("trip-sample-0001", report.TripId);
            Assert.AreEqual(5, report.Videos);
            Assert.AreEqual(8, report.Photos);
            Assert.AreEqual(4, report.Captures);
            Assert.AreEqual(1, report.Outlines);
            Assert.IsFalse(report.ExistingTripConflict);
            StringAssert.Contains("ACME", report.DisplayName);
            Assert.IsFalse(Directory.Exists(Lib), "validation writes nothing");
        }

        [Test]
        public void ImportZip_RoundTripsLosslessly_ThenConflictsWithoutOverwrite()
        {
            var zip = ExportSample();
            var dest = TripPackage.ImportZip(zip, libraryRoot: Lib);
            Assert.AreEqual(Path.Combine(Lib, "trip-sample-0001"), dest);
            foreach (var f in new[] { "trip.json", "shotlist.json", "captures.json", Path.Combine("outlines", "book-1.json") })
                Assert.IsTrue(TripJson.SemanticallyEqual(File.ReadAllText(Path.Combine(Fixtures.SampleFolder, f)), File.ReadAllText(Path.Combine(dest, f)), out var diff), f + ": " + diff);
            Assert.IsEmpty(Directory.GetDirectories(Lib).Where(d => Path.GetFileName(d).StartsWith(".staging")));

            var again = TripPackage.ValidateZip(zip, Lib);
            Assert.IsTrue(again.ExistingTripConflict);
            Assert.IsTrue(again.CanImport, "conflict is a warning until you try to commit");
            var ex = Assert.Throws<ImportBlockedException>(() => TripPackage.ImportZip(zip, overwrite: false, libraryRoot: Lib));
            Assert.IsTrue(ex.Report.Errors.Any(e => e.Kind == ImportIssueKind.Conflict));

            // Overwrite replaces and leaves no backup folder behind.
            File.WriteAllText(Path.Combine(dest, "marker.txt"), "old");
            TripPackage.ImportZip(zip, overwrite: true, libraryRoot: Lib);
            Assert.IsFalse(File.Exists(Path.Combine(dest, "marker.txt")));
            Assert.AreEqual(1, Directory.GetDirectories(Lib).Length);
        }

        [Test]
        public void ImportZip_WithRootFolderPrefix_Works()
        {
            var zip = MakeZip("prefixed.zip",
                ("My Trip/trip.json", File.ReadAllText(Path.Combine(Fixtures.SampleFolder, "trip.json"))),
                ("My Trip/shotlist.json", File.ReadAllText(Path.Combine(Fixtures.SampleFolder, "shotlist.json"))),
                ("My Trip/captures.json", File.ReadAllText(Path.Combine(Fixtures.SampleFolder, "captures.json"))),
                ("My Trip/outlines/book-1.json", File.ReadAllText(Path.Combine(Fixtures.SampleFolder, "outlines", "book-1.json"))));
            var report = TripPackage.ValidateZip(zip, Lib);
            Assert.IsTrue(report.CanImport, string.Join("\n", report.Issues));
            Assert.AreEqual(1, report.Outlines);
            var dest = TripPackage.ImportZip(zip, libraryRoot: Lib);
            Assert.IsTrue(File.Exists(Path.Combine(dest, "outlines", "book-1.json")));
        }

        [Test]
        public void DryRun_ReportsParseSchemaMissingAndDangling_AndWritesNothing()
        {
            var badJson = MakeZip("bad.zip", ("trip.json", "{ not json"));
            var r1 = TripPackage.ValidateZip(badJson, Lib);
            Assert.IsFalse(r1.CanImport);
            Assert.IsTrue(r1.Errors.Any(e => e.Kind == ImportIssueKind.Parse && e.File == "trip.json"));
            Assert.IsNull(r1.Staged);

            var tooNew = MakeZip("new.zip", ("trip.json", @"{ ""schemaVersion"": 42, ""tripId"": ""t"" }"));
            var r2 = TripPackage.ValidateZip(tooNew, Lib);
            Assert.IsTrue(r2.Errors.Any(e => e.Kind == ImportIssueKind.Schema));

            var noTrip = MakeZip("notrip.zip", ("shotlist.json", @"{ ""schemaVersion"": 1 }"));
            var r3 = TripPackage.ValidateZip(noTrip, Lib);
            Assert.IsTrue(r3.Errors.Any(e => e.Kind == ImportIssueKind.MissingFile && e.File == "trip.json"));

            var dangling = MakeZip("dangling.zip",
                ("trip.json", @"{ ""schemaVersion"": 1, ""tripId"": ""t-d"", ""books"": [ { ""id"": ""b1"", ""number"": 1, ""name"": ""B"" } ], ""days"": [ { ""id"": ""d1"", ""date"": ""2026-09-15"" } ] }"),
                ("shotlist.json", @"{ ""schemaVersion"": 1, ""tripId"": ""t-d"",
                    ""chapters"": [ { ""id"": ""c1"", ""bookId"": ""b1"", ""number"": 1, ""name"": ""C"" } ],
                    ""videos"": [ { ""id"": ""v1"", ""number"": 1, ""bookId"": ""b1"", ""chapterId"": null, ""title"": ""No chapter"" },
                                  { ""id"": ""v1"", ""number"": 2, ""bookId"": ""b1"", ""chapterId"": ""c1"", ""title"": ""Dup id"", ""photoRefs"": [ ""ph-missing"" ] } ] }"),
                ("captures.json", @"{ ""schemaVersion"": 1, ""tripId"": ""t-d"", ""captures"": [ { ""id"": ""cap1"", ""dayId"": ""d1"", ""capturedOrder"": 1, ""planVideoId"": ""ghost"", ""title"": ""x"" } ] }"));
            var r4 = TripPackage.ValidateZip(dangling, Lib);
            Assert.IsFalse(r4.CanImport);
            Assert.IsTrue(r4.Errors.Any(e => e.Kind == ImportIssueKind.VideoWithoutChapter), string.Join("\n", r4.Issues));
            Assert.IsTrue(r4.Errors.Any(e => e.Kind == ImportIssueKind.DuplicateId && e.EntityId == "v1"));
            Assert.IsTrue(r4.Errors.Any(e => e.Kind == ImportIssueKind.DanglingReference && e.Message.Contains("ph-missing")));
            Assert.IsTrue(r4.Errors.Any(e => e.Kind == ImportIssueKind.DanglingReference && e.Message.Contains("ghost")));
            Assert.AreEqual(2, r4.Videos, "counts are still reported for the preview");
            Assert.IsNotNull(r4.Staged, "it parsed; it just failed validation");

            Assert.Throws<ImportBlockedException>(() => TripPackage.ImportZip(dangling, libraryRoot: Lib));
            Assert.IsFalse(Directory.Exists(Lib), "a blocked import writes nothing at all");

            var missing = TripPackage.ValidateZip(Path.Combine(_temp, "nope.zip"), Lib);
            Assert.IsTrue(missing.Errors.Any(e => e.Kind == ImportIssueKind.MissingFile));
            File.WriteAllText(Path.Combine(_temp, "garbage.zip"), "this is not a zip");
            Assert.IsTrue(TripPackage.ValidateZip(Path.Combine(_temp, "garbage.zip"), Lib).Errors.Any(e => e.Kind == ImportIssueKind.Parse));
        }

        [Test]
        public void ValidateFolder_AndImportFolder_FromSample()
        {
            var report = TripPackage.ValidateFolder(Fixtures.SampleFolder, Lib);
            Assert.IsTrue(report.CanImport);
            var dest = TripPackage.ImportFolder(Fixtures.SampleFolder, libraryRoot: Lib);
            Assert.IsTrue(File.Exists(Path.Combine(dest, "captures.json")));
        }

        [Test]
        public void SingleDocument_ExportAndImport_IntoSession()
        {
            var data = Fixtures.LoadSample();
            data.FolderPath = Path.Combine(_temp, "open");
            var s = new TripSession(data, () => 0);

            var outlineJson = TripPackage.ExportDocumentJson(data, DocumentKind.Outline, "b-001");
            var modified = outlineJson.Replace("What Makes a Space Confined", "What Makes a Space Confined (v2)");
            var report = TripPackage.ImportDocument(s, modified, DocumentKind.Outline, "book-1.json");
            Assert.IsTrue(report.CanImport);
            Assert.AreEqual("What Makes a Space Confined (v2)", s.Data.Outlines["b-001"].Chapters[0].Sections[0].Name);
            Assert.IsTrue(s.IsDirty);

            // A shot list that drops a captured video is refused and the session is untouched.
            var shot = TripJson.Clone(data.ShotList);
            shot.Videos.RemoveAll(v => v.Id == "v-004");
            var ex = Assert.Throws<ImportBlockedException>(() => TripPackage.ImportDocument(s, TripJson.Serialize(shot), DocumentKind.ShotList));
            Assert.IsTrue(ex.Report.Errors.Any(e => e.Kind == ImportIssueKind.DanglingReference && e.Message.Contains("v-004")));
            Assert.IsNotNull(s.Data.FindVideo("v-004"));

            // Dry run only.
            var dry = TripPackage.ValidateDocument(s.Data, TripJson.Serialize(shot), DocumentKind.ShotList);
            Assert.IsFalse(dry.CanImport);
            Assert.IsNotNull(s.Data.FindVideo("v-004"));

            // Outline for a book that is not in this trip.
            var foreign = outlineJson.Replace("\"b-001\"", "\"b-999\"");
            Assert.IsFalse(TripPackage.ValidateDocument(s.Data, foreign, DocumentKind.Outline).CanImport);

            // A captures document from a different tripId imports with a warning and gets the open trip's id.
            var caps = TripPackage.ExportDocumentJson(data, DocumentKind.Captures).Replace("trip-sample-0001", "other-trip");
            var capReport = TripPackage.ImportDocument(s, caps, DocumentKind.Captures);
            Assert.IsTrue(capReport.Warnings.Any(w => w.Message.Contains("other-trip")));
            Assert.AreEqual("trip-sample-0001", s.Data.Captures.TripId);

            TripPackage.ExportDocument(data, DocumentKind.ShotList, Path.Combine(_temp, "shot.json"));
            Assert.IsTrue(File.Exists(Path.Combine(_temp, "shot.json")));
        }
    }

    public class LifecycleTests
    {
        private string _temp;
        private string Lib => Path.Combine(_temp, "Trips");

        [SetUp] public void SetUp() => _temp = Fixtures.NewTempFolder("life");
        [TearDown] public void TearDown() => Fixtures.DeleteFolder(_temp);

        [Test]
        public void Create_Duplicate_Rename_Delete()
        {
            var created = TripLibrary.CreateTrip("NEW", "PRG", Lib);
            Assert.IsTrue(Directory.Exists(created.FolderPath));
            Assert.IsEmpty(TripValidator.Validate(created));

            TripLibrary.ImportSampleTrip(libraryRoot: Lib);
            var copy = TripLibrary.DuplicateTrip("trip-sample-0001", includeCaptures: false, editIdentity: i => i.PhaseNumber = 6, libraryRoot: Lib);
            Assert.AreNotEqual("trip-sample-0001", copy.TripId);
            Assert.AreEqual(copy.TripId, copy.ShotList.TripId);
            Assert.AreEqual(copy.TripId, copy.Outlines["b-001"].TripId);
            Assert.AreEqual(5, copy.ShotList.Videos.Count);
            Assert.IsEmpty(copy.Captures.Captures);
            Assert.IsEmpty(copy.Captures.Amendments);
            Assert.AreEqual(6, copy.Trip.Identity.PhaseNumber);
            Assert.AreEqual(5, TripLoader.Load(Fixtures.SampleFolder).Trip.Identity.PhaseNumber, "source untouched");
            var reloaded = TripLoader.Load(copy.FolderPath);
            Assert.AreEqual("book-1.json", reloaded.OutlineFileNames["b-001"]);
            Assert.IsEmpty(TripValidator.Validate(reloaded));

            var withCaptures = TripLibrary.DuplicateTrip("trip-sample-0001", includeCaptures: true, libraryRoot: Lib);
            Assert.AreEqual(4, withCaptures.Captures.Captures.Count);

            TripLibrary.UpdateIdentity(copy.TripId, i => { i.ClientAbbrev = "ACME2"; i.Location.City = "Nampa"; }, Lib);
            var list = TripLibrary.ListTrips(Lib);
            Assert.AreEqual(4, list.Count);
            var renamed = list.Single(t => t.TripId == copy.TripId);
            Assert.AreEqual("ACME2", renamed.ClientAbbrev);
            StringAssert.Contains("Nampa", renamed.DisplayName);

            TripLibrary.DeleteTrip(copy.TripId, Lib);
            TripLibrary.DeleteTrip(withCaptures.TripId, Lib);
            Assert.AreEqual(2, TripLibrary.ListTrips(Lib).Count);
        }

        [Test]
        public void Session_UpdateIdentityAndDates_MarksTripDirty()
        {
            var d = Fixtures.LoadSample();
            d.FolderPath = Path.Combine(_temp, "t");
            var s = new TripSession(d, () => 0);
            s.UpdateIdentity(i => i.ProgramAbbrev = "XYZ");
            s.UpdateDates(x => x.Depart = "2026-09-18");
            s.SaveNow();
            var again = TripLoader.Load(d.FolderPath);
            Assert.AreEqual("XYZ", again.Trip.Identity.ProgramAbbrev);
            Assert.AreEqual("2026-09-18", again.Trip.Dates.Depart);
        }
    }
}

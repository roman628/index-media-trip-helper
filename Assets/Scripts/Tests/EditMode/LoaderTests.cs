using System.IO;
using MediaTrip.Model;
using MediaTrip.Persistence;
using NUnit.Framework;

namespace MediaTrip.Tests
{
    public class LoaderTests
    {
        private string _temp;

        [SetUp] public void SetUp() => _temp = Fixtures.NewTempFolder("loader");
        [TearDown] public void TearDown() => Fixtures.DeleteFolder(_temp);

        [Test]
        public void MissingOptionalFiles_DoNotThrow()
        {
            File.WriteAllText(Path.Combine(_temp, "trip.json"), @"{ ""schemaVersion"": 1, ""tripId"": ""only-trip"" }");
            var data = TripLoader.Load(_temp);
            Assert.AreEqual("only-trip", data.TripId);
            Assert.IsNotNull(data.ShotList);
            Assert.AreEqual(0, data.ShotList.Videos.Count);
            Assert.AreEqual("only-trip", data.ShotList.TripId);
            Assert.IsNotNull(data.Captures);
            Assert.AreEqual(0, data.Captures.Captures.Count);
            Assert.AreEqual(0, data.Outlines.Count);
            // And the empty documents save fine.
            TripSaver.SaveAll(data);
            Assert.IsTrue(File.Exists(Path.Combine(_temp, "shotlist.json")));
            Assert.IsTrue(File.Exists(Path.Combine(_temp, "captures.json")));
        }

        [Test]
        public void MissingTripJson_Throws()
        {
            Assert.Throws<FileNotFoundException>(() => TripLoader.Load(_temp));
        }

        [Test]
        public void MissingFolder_Throws()
        {
            Assert.Throws<DirectoryNotFoundException>(() => TripLoader.Load(Path.Combine(_temp, "nope")));
        }

        [Test]
        public void MissingSchemaVersion_IsTreatedAsCurrent()
        {
            var doc = TripLoader.LoadDocumentFromJson<CapturesDocument>(@"{ ""tripId"": ""t"" }", DocumentKind.Captures);
            Assert.AreEqual(Schema.CurrentVersion, doc.SchemaVersion);
        }

        [Test]
        public void NewerSchemaVersion_Throws()
        {
            Assert.Throws<SchemaTooNewException>(() =>
                TripLoader.LoadDocumentFromJson<CapturesDocument>(@"{ ""schemaVersion"": 999, ""tripId"": ""t"" }", DocumentKind.Captures));
        }

        [Test]
        public void MalformedJson_ThrowsInvalidData_WithPath()
        {
            var path = Path.Combine(_temp, "trip.json");
            File.WriteAllText(path, "{ not json");
            var ex = Assert.Throws<InvalidDataException>(() => TripLoader.Load(_temp));
            StringAssert.Contains("trip.json", ex.Message);
        }

        [Test]
        public void OutlineWithoutBookId_IsKeyedByFileName_WithWarning()
        {
            File.WriteAllText(Path.Combine(_temp, "trip.json"), @"{ ""schemaVersion"": 1, ""tripId"": ""t"" }");
            Directory.CreateDirectory(Path.Combine(_temp, "outlines"));
            File.WriteAllText(Path.Combine(_temp, "outlines", "book-9.json"), @"{ ""schemaVersion"": 1, ""bookTitle"": ""Orphan"" }");
            var result = TripLoader.LoadWithWarnings(_temp);
            Assert.IsTrue(result.Data.Outlines.ContainsKey("book-9"));
            Assert.AreEqual(1, result.Warnings.Count);
        }

        [Test]
        public void Saver_RefusesStreamingAssets()
        {
            var data = Fixtures.LoadSample();
            Assert.Throws<System.InvalidOperationException>(() => TripSaver.SaveAll(data, Fixtures.SampleFolder));
            Assert.Throws<System.InvalidOperationException>(() => TripSaver.SaveAll(data)); // FolderPath is the sample folder
        }

        [Test]
        public void Saver_WritesAtomically_NoTempLeftBehind()
        {
            var data = Fixtures.LoadSample();
            TripSaver.SaveAll(data, _temp);
            TripSaver.SaveAll(data, _temp); // overwrite path
            Assert.IsEmpty(Directory.GetFiles(_temp, "*.tmp"));
            Assert.IsEmpty(Directory.GetFiles(Path.Combine(_temp, "outlines"), "*.tmp"));
        }

        [Test]
        public void NewOutline_GetsBookNumberFileName()
        {
            var data = Fixtures.LoadSample();
            data.Outlines["b-002"] = new OutlineDocument { TripId = data.TripId, BookId = "b-002", BookTitle = "Lockout / Tagout" };
            TripSaver.SaveAll(data, _temp);
            Assert.IsTrue(File.Exists(Path.Combine(_temp, "outlines", "book-2.json")));
            var again = TripLoader.Load(_temp);
            Assert.AreEqual(2, again.Outlines.Count);
            Assert.AreEqual("book-2.json", again.OutlineFileNames["b-002"]);
        }

        [Test]
        public void Library_ImportSample_CreateAndList()
        {
            var root = Path.Combine(_temp, "Trips");
            var dest = TripLibrary.ImportSampleTrip(overwrite: true, libraryRoot: root);
            Assert.IsTrue(File.Exists(Path.Combine(dest, "trip.json")));
            Assert.IsFalse(TripPaths.IsInsideStreamingAssets(dest));

            var created = TripLibrary.CreateTrip("NEW", "PRG", root);
            Assert.IsTrue(File.Exists(Path.Combine(created.FolderPath, "captures.json")));
            Assert.IsTrue(System.Guid.TryParse(created.TripId.Substring("trip-".Length), out _), "new trip id is a GUID");

            var list = TripLibrary.ListTrips(root);
            Assert.AreEqual(2, list.Count);
            Assert.IsTrue(list.Exists(t => t.TripId == "trip-sample-0001" && t.DisplayName.Contains("ACME")));

            Assert.Throws<IOException>(() => TripLibrary.ImportSampleTrip(overwrite: false, libraryRoot: root));
        }
    }
}

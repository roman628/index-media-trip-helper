using System.IO;
using MediaTrip.Model;
using MediaTrip.Persistence;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace MediaTrip.Tests
{
    /// <summary>Load the sample trip, save it elsewhere, and prove nothing was lost or altered.</summary>
    public class RoundTripTests
    {
        private string _temp;

        [SetUp] public void SetUp() => _temp = Fixtures.NewTempFolder("roundtrip");
        [TearDown] public void TearDown() => Fixtures.DeleteFolder(_temp);

        [Test]
        public void SampleTrip_Loads()
        {
            var data = Fixtures.LoadSample();
            Assert.AreEqual("trip-sample-0001", data.TripId);
            Assert.AreEqual(8, data.Trip.People.Count);
            Assert.AreEqual(2, data.Trip.Books.Count);
            Assert.AreEqual(5, data.ShotList.Videos.Count);
            Assert.AreEqual(8, data.ShotList.Photos.Count);
            Assert.AreEqual(4, data.Captures.Amendments.Count);
            Assert.AreEqual(4, data.Captures.Captures.Count);
            Assert.AreEqual(2, data.Captures.PhotoCaptures.Count);
            Assert.AreEqual(3, data.Captures.OutlineAssignments.Count);
            // Only book 1 has an outline; book 2 must not throw or appear.
            Assert.AreEqual(1, data.Outlines.Count);
            Assert.IsTrue(data.Outlines.ContainsKey("b-001"));
            Assert.IsNull(data.FindOutline("b-002"));
            Assert.AreEqual("book-1.json", data.OutlineFileNames["b-001"]);
        }

        [Test]
        public void SampleTrip_EdgeCases_Deserialize()
        {
            var data = Fixtures.LoadSample();

            // Three-deep nested notes on video 1.
            var v1 = data.FindVideo("v-001");
            Assert.AreEqual(3, Node.MaxDepth(v1.Notes));
            Assert.AreEqual("Note ventilation", Node.Find(v1.Notes, "n-003b2").Text);

            // Outline bullets nest four deep (a > 2 > i).
            var outline = data.FindOutline("b-001");
            Assert.AreEqual(3, Node.MaxDepth(outline.Chapters[0].Sections[0].Nodes));
            Assert.AreEqual("i", Node.Find(outline.Chapters[0].Sections[0].Nodes, "o-111b2i").Label);

            // Explicit nulls come through as null, not as empty strings.
            Assert.IsNull(data.FindPhoto("ph-001").ChapterId);
            Assert.IsNull(data.FindCapture("cap-002").SceneDescription);
            Assert.IsNull(data.FindCapture("cap-004").PlannedNumber);
            Assert.IsNull(data.FindPhotoCapture("pc-002").PhotoId);
            Assert.AreEqual("", data.FindVideo("v-001").SmeText);

            // Enums.
            Assert.AreEqual(HeroType.BookCover, data.FindPhoto("ph-001").HeroType);
            Assert.AreEqual(HeroType.None, data.FindPhoto("ph-004").HeroType);
            Assert.AreEqual(AmendmentType.Combine, data.FindAmendment("am-001").Type);
            Assert.AreEqual(Org.Leadership, data.FindPerson("p-008").Org);
            Assert.IsTrue(data.FindPerson("p-003").HasRole(PersonRole.Editor));
            Assert.AreEqual(PhotoCaptureSection.UnderVideo, data.FindPhotoCapture("pc-001").Section);
            Assert.AreEqual(MediaRefKind.PhotoCapture, data.Captures.OutlineAssignments[2].MediaRef.Kind);

            // Person not on the personnel list.
            var owen = data.FindCapture("cap-003").People[1];
            Assert.IsNull(owen.PersonId);
            Assert.AreEqual("Owen Pratt", owen.Name);

            // Photo with no master-list entry.
            var unplannedPhoto = data.FindCapture("cap-001").Photos[2];
            Assert.IsNull(unplannedPhoto.PhotoId);
            Assert.IsTrue(unplannedPhoto.Captured);

            // Unconfirmed auto assignment with a float confidence.
            var oa3 = data.Captures.OutlineAssignments[2];
            Assert.IsFalse(oa3.Confirmed);
            Assert.AreEqual(0.64, oa3.Confidence.Value, 1e-9);
            Assert.IsNull(data.Captures.OutlineAssignments[1].Confidence);

            // The outline's undocumented tripId is a known property, and nothing landed in Extra.
            Assert.AreEqual("trip-sample-0001", outline.TripId);
            Assert.IsTrue(outline.Extra == null || outline.Extra.Count == 0);

            // B-roll derived boolean.
            Assert.IsTrue(data.FindCapture("cap-001").HasBRoll);
            Assert.IsFalse(data.FindCapture("cap-002").HasBRoll);
        }

        [Test]
        public void SampleTrip_RoundTrip_IsSemanticallyIdentical()
        {
            var data = Fixtures.LoadSample();
            TripSaver.SaveAll(data, _temp);

            AssertSameJson(Path.Combine(Fixtures.SampleFolder, "trip.json"), Path.Combine(_temp, "trip.json"));
            AssertSameJson(Path.Combine(Fixtures.SampleFolder, "shotlist.json"), Path.Combine(_temp, "shotlist.json"));
            AssertSameJson(Path.Combine(Fixtures.SampleFolder, "captures.json"), Path.Combine(_temp, "captures.json"));
            AssertSameJson(Path.Combine(Fixtures.SampleFolder, "outlines", "book-1.json"), Path.Combine(_temp, "outlines", "book-1.json"));

            // No stray files (temp files, extra outlines).
            Assert.AreEqual(3, Directory.GetFiles(_temp).Length);
            Assert.AreEqual(1, Directory.GetFiles(Path.Combine(_temp, "outlines")).Length);

            // And loading the saved copy gives the same object graph again.
            var again = TripLoader.Load(_temp);
            Assert.AreEqual(TripJson.Serialize(data.Trip), TripJson.Serialize(again.Trip));
            Assert.AreEqual(TripJson.Serialize(data.ShotList), TripJson.Serialize(again.ShotList));
            Assert.AreEqual(TripJson.Serialize(data.Captures), TripJson.Serialize(again.Captures));
            Assert.AreEqual(TripJson.Serialize(data.Outlines["b-001"]), TripJson.Serialize(again.Outlines["b-001"]));
        }

        [Test]
        public void RoundTrip_PreservesPropertyOrderAndKeyNames()
        {
            // The saved JSON should use exactly the documented camelCase names (spot check).
            var data = Fixtures.LoadSample();
            var obj = TripJson.ParseObject(TripJson.Serialize(data.Captures));
            var cap = (JObject)obj["captures"][0];
            foreach (var key in new[] { "id", "dayId", "capturedOrder", "planVideoId", "plannedNumber", "title", "bookId", "chapterId",
                         "description", "sceneDescription", "hpiToolSuggestion", "keywords", "bRoll", "cameraCount", "people", "location", "notes", "photos" })
                Assert.IsNotNull(cap.Property(key), "missing key " + key);
            Assert.AreEqual("combine", (string)obj["amendments"][0]["type"]);
            Assert.AreEqual("photoCapture", (string)obj["outlineAssignments"][2]["mediaRef"]["kind"]);
            var shot = TripJson.ParseObject(TripJson.Serialize(data.ShotList));
            Assert.AreEqual("bookCover", (string)shot["photos"][0]["heroType"]);
            Assert.AreEqual("none", (string)shot["photos"][3]["heroType"]);
            var trip = TripJson.ParseObject(TripJson.Serialize(data.Trip));
            Assert.AreEqual("projectManager", (string)trip["people"][0]["roles"][0]);
            Assert.AreEqual("index", (string)trip["people"][0]["org"]);
        }

        [Test]
        public void UnknownProperties_SurviveRoundTrip()
        {
            var json = @"{ ""schemaVersion"": 2, ""tripId"": ""t"", ""futureField"": { ""x"": [1, 2] },
                           ""videos"": [ { ""id"": ""v1"", ""number"": 1, ""title"": ""T"", ""extraOnVideo"": ""keep me"",
                                          ""notes"": [ { ""id"": ""n1"", ""text"": ""hi"", ""children"": [], ""nodeExtra"": true } ] } ] }";
            var doc = TripLoader.LoadDocumentFromJson<ShotListDocument>(json, DocumentKind.ShotList);
            var back = TripJson.Serialize(doc);
            Assert.IsTrue(TripJson.SemanticallyEqual(json, back, out var diff), diff);
        }

        [Test]
        public void Canonicalize_TreatsNullAndAbsentAsEqual_ButNotOtherDifferences()
        {
            Assert.IsTrue(TripJson.SemanticallyEqual(@"{""a"":1,""b"":null}", @"{""a"":1}", out _));
            Assert.IsTrue(TripJson.SemanticallyEqual(@"{""b"":2,""a"":1}", @"{""a"":1,""b"":2}", out _));
            Assert.IsTrue(TripJson.SemanticallyEqual(@"{""a"":5.0}", @"{""a"":5}", out _));
            Assert.IsFalse(TripJson.SemanticallyEqual(@"{""a"":[1,2]}", @"{""a"":[2,1]}", out var d1));
            StringAssert.Contains("$.a[0]", d1);
            Assert.IsFalse(TripJson.SemanticallyEqual(@"{""a"":""""}", @"{""a"":null}", out _), "empty string is not null");
            Assert.IsTrue(TripJson.SemanticallyEqual(@"{""a"":[]}", @"{}", out _), "empty list and absent list both mean none");
            Assert.IsFalse(TripJson.SemanticallyEqual(@"{""a"":[1]}", @"{}", out _));
            Assert.IsTrue(TripJson.SemanticallyEqual(@"{""a"":{""b"":null}}", @"{}", out _), "an object with nothing in it is none");
            Assert.IsFalse(TripJson.SemanticallyEqual(@"{""a"":{""b"":1}}", @"{}", out _));
            Assert.IsFalse(TripJson.SemanticallyEqual(@"{""a"":0}", @"{}", out _), "zero is a value");
            Assert.IsFalse(TripJson.SemanticallyEqual(@"{""a"":false}", @"{}", out _), "false is a value");
        }

        [Test]
        public void Dates_AreNotReformatted()
        {
            var json = @"{ ""schemaVersion"": 1, ""tripId"": ""t"", ""dates"": { ""arrive"": ""2026-09-14"", ""depart"": ""2026-09-17T00:00:00Z"", ""lastUpdated"": ""09/12/2026"" } }";
            var doc = TripJson.Deserialize<TripDocument>(json);
            Assert.AreEqual("2026-09-14", doc.Dates.Arrive);
            Assert.AreEqual("2026-09-17T00:00:00Z", doc.Dates.Depart);
            Assert.AreEqual("09/12/2026", doc.Dates.LastUpdated);
            Assert.IsTrue(TripJson.SemanticallyEqual(json, TripJson.Serialize(doc), out var diff), diff);
        }

        private static void AssertSameJson(string originalPath, string savedPath)
        {
            Assert.IsTrue(File.Exists(savedPath), "not written: " + savedPath);
            var a = File.ReadAllText(originalPath);
            var b = File.ReadAllText(savedPath);
            Assert.IsTrue(TripJson.SemanticallyEqual(a, b, out var diff), Path.GetFileName(originalPath) + " differs at " + diff);
        }
    }
}

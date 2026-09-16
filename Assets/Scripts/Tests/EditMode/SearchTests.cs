using System.Linq;
using MediaTrip.Model;
using MediaTrip.Search;
using NUnit.Framework;

namespace MediaTrip.Tests
{
    public class SearchTests
    {
        private TripSearch S() => new TripSearch(Fixtures.LoadSample());

        // ---------------- Matcher primitives ----------------

        [Test]
        public void Normalize_StripsPunctuationCaseAndDiacritics()
        {
            Assert.AreEqual("double dead end space", FuzzyMatcher.Normalize("  Double-Dead End: Space!! "));
            Assert.AreEqual("jose nunez", FuzzyMatcher.Normalize("José Núñez"));
            Assert.AreEqual("", FuzzyMatcher.Normalize(null));
        }

        [Test]
        public void Tokenize_DropsStopWordsUnlessNothingElse()
        {
            CollectionAssert.AreEqual(new[] { "double", "dead", "end", "space" }, FuzzyMatcher.Tokenize("The Double Dead End Space"));
            CollectionAssert.AreEqual(new[] { "the" }, FuzzyMatcher.Tokenize("the"));
        }

        [Test]
        public void TokenScore_ExactPrefixTypoSubstring()
        {
            Assert.AreEqual(1.0, FuzzyMatcher.TokenScore("permit", "permit"));
            Assert.Greater(FuzzyMatcher.TokenScore("dou", "double"), 0.85);
            Assert.Greater(FuzzyMatcher.TokenScore("permitt", "permit"), 0.7);
            Assert.Greater(FuzzyMatcher.TokenScore("walkthru", "walkthrough"), 0.5);
            Assert.Greater(FuzzyMatcher.TokenScore("lock", "lockout"), 0.5);
            Assert.AreEqual(0, FuzzyMatcher.TokenScore("hatch", "permit"));
            Assert.AreEqual(1, FuzzyMatcher.DamerauLevenshtein("hatch", "hacth"), "transposition counts as one edit");
        }

        [Test]
        public void Score_IsDeterministicAndSymmetricEnough()
        {
            var a = FuzzyMatcher.Score("entry perm", "Filling Out the Entry Permit");
            var b = FuzzyMatcher.Score("entry perm", "Filling Out the Entry Permit");
            Assert.AreEqual(a, b);
            Assert.Greater(a, 0.5);
            Assert.AreEqual(1.0, FuzzyMatcher.Score("Try Step Verification", "try-step verification"));
            Assert.AreEqual(0, FuzzyMatcher.Score("", "anything"));
            Assert.Less(FuzzyMatcher.Score("lock box", "Filling Out the Entry Permit"), 0.3);
        }

        [Test]
        public void Rank_IsStableOnTies()
        {
            var items = new[] { "Alpha One", "Alpha Two", "Alpha Three", "Beta" };
            var r1 = FuzzyMatcher.Rank("alpha", items, s => new[] { s });
            var r2 = FuzzyMatcher.Rank("alpha", items, s => new[] { s });
            CollectionAssert.AreEqual(r1.Select(m => m.Item).ToList(), r2.Select(m => m.Item).ToList());
            Assert.AreEqual(3, r1.Count);
            Assert.AreEqual("Alpha One", r1[0].Item);
        }

        // ---------------- Outline suggestion ----------------

        [Test]
        public void OutlineSuggestion_DoubleDeadEnd_FindsSection12()
        {
            var s = S().SuggestOutlineSection("Walkthrough: Identifying a Double Dead End Space", "b-001", "c-101");
            Assert.IsNotEmpty(s);
            Assert.AreEqual("s-1-2", s[0].SectionId);
            Assert.AreEqual("c-101", s[0].ChapterId);
            Assert.AreEqual("b-001", s[0].BookId);
            Assert.Greater(s[0].BaseScore, 0.6);
            Assert.AreEqual(SearchOptions.Default.SameBookBias + SearchOptions.Default.SameChapterBias, s[0].Bias, 1e-9, "same book and same chapter bias");
            StringAssert.StartsWith("1.2", s[0].Label);

            // Without any bias the text alone still finds it.
            var unbiased = S().SuggestOutlineSection("Walkthrough: Identifying a Double Dead End Space");
            Assert.AreEqual("s-1-2", unbiased[0].SectionId);
            Assert.AreEqual(0, unbiased[0].Bias);
        }

        [Test]
        public void OutlineSuggestion_BookTopicWords_DoNotDominate()
        {
            // "confined space" appears in the book's chapter 1 and its sections, so it carries
            // little information; the item's own chapter (2) must win for a permit video.
            var weights = S().OutlineTokenWeights;
            Assert.Less(weights["space"], weights["permit"]);
            Assert.Less(weights["confined"], 1.0);
        }

        [Test]
        public void OutlineSuggestion_PermitVideo_FindsChapter2Sections()
        {
            var s = S().SuggestOutlineSection("Completing the Confined Space Entry Permit", "b-001", "c-102");
            Assert.AreEqual("s-2-1", s[0].SectionId, string.Join(" | ", s));
        }

        [Test]
        public void OutlineSuggestion_BiasRaisesOwnChapter_AndChapterLevelCandidatesExist()
        {
            var unbiased = S().SuggestOutlineSection("Testing order");
            Assert.AreEqual("s-2-2", unbiased[0].SectionId);
            var biased = S().SuggestOutlineSection("Testing order", "b-001", "c-102");
            Assert.AreEqual("s-2-2", biased[0].SectionId);
            Assert.Greater(biased[0].Score, unbiased[0].Score);

            // A title that is really the chapter's own phrase suggests the chapter itself.
            var chapterLevel = S().SuggestOutlineSection("Atmospheric testing");
            Assert.IsNull(chapterLevel[0].SectionId);
            Assert.AreEqual("c-102", chapterLevel[0].ChapterId);
            StringAssert.StartsWith("Ch.2", chapterLevel[0].Label);
        }

        [Test]
        public void OutlineSuggestion_NoOutline_NoCrash_NoResults()
        {
            var data = Fixtures.LoadSample();
            data.Outlines.Clear();
            Assert.IsEmpty(new TripSearch(data).SuggestOutlineSection("anything", "b-001", "c-101"));
            Assert.IsEmpty(S().SuggestOutlineSection("", "b-001", "c-101"));
        }

        [Test]
        public void OutlineSuggestion_ForCapture_UsesItsBookAndChapter_AndBulletText()
        {
            // "Walkthrough and Entry Point Hazards" shares no words with any section NAME, but
            // section 1.2 has the bullet "Walkthrough example", which is matched at reduced weight.
            var data = Fixtures.LoadSample();
            var s = new TripSearch(data).SuggestOutlineFor(data.FindCapture("cap-001"));
            Assert.IsNotEmpty(s);
            Assert.AreEqual("s-1-2", s[0].SectionId);
            Assert.Less(s[0].BaseScore, 0.6, "a bullet match is weaker than a name match");

            var noNodes = new TripSearch(data, null, new SearchOptions { NodeTextWeight = 0 }).SuggestOutlineFor(data.FindCapture("cap-001"));
            Assert.IsFalse(noNodes.Any(x => x.SectionId == "s-1-2"), "with bullet matching off there is no text match for 1.2");
        }

        [Test]
        public void OutlineSuggestion_ItemInBookWithoutOutline_GetsNothing()
        {
            var data = Fixtures.LoadSample();
            Assert.IsEmpty(new TripSearch(data).SuggestOutlineFor(data.FindCapture("cap-003")));
        }

        // ---------------- Shot list ----------------

        [Test]
        public void ShotListSearch_ByTitle_MatchesRenamedAndOriginalTitles()
        {
            var s = S();
            Assert.AreEqual("v-003", s.SearchShotList("entry perm")[0].Item.Id);
            Assert.AreEqual("v-003", s.SearchShotList("filling out")[0].Item.Id, "original title still matches");
            Assert.AreEqual("v-c001", s.SearchShotList("hazards")[0].Item.Id, "combined item matches; superseded v-002 is excluded");
            Assert.IsFalse(s.SearchShotList("hazards").Any(m => m.Item.Id == "v-002"));
            Assert.IsTrue(s.SearchShotList("hazards", includeSuperseded: true).Any(m => m.Item.Id == "v-002"));
            Assert.AreEqual("v-a001", s.SearchShotList("lock box")[0].Item.Id);
            Assert.AreEqual("v-004", s.SearchShotList("aplying the frist lock")[0].Item.Id, "typos");
        }

        [Test]
        public void ShotListSearch_ByNumber()
        {
            var s = S();
            Assert.AreEqual("v-004", s.SearchShotList("4")[0].Item.Id);
            Assert.AreEqual("v-c001", s.SearchShotList("1")[0].Item.Id, "video 1 was combined; the combined item is what you get");
            Assert.AreEqual("v-c001", s.SearchShotList("2")[0].Item.Id);
            Assert.IsEmpty(s.SearchShotList("9"));
        }

        // ---------------- Names ----------------

        [Test]
        public void Names_AllKnown_IncludesRegistryAndTypedNames()
        {
            var names = S().AllKnownNames();
            var owen = names.Single(n => n.Name == "Owen Pratt");
            Assert.IsFalse(owen.IsInRegistry);
            Assert.AreEqual("Electrician", owen.Title);
            Assert.IsTrue(owen.UsedAsSme);
            var nina = names.Single(n => n.Name == "Nina Okoro");
            Assert.IsTrue(nina.IsInRegistry);
            Assert.IsTrue(nina.UsedAsSme);
            Assert.AreEqual(9, names.Count, "8 registry people + Owen; team member names dedupe against the registry");
        }

        [Test]
        public void Names_ScopedSuggestions()
        {
            var s = S();
            Assert.AreEqual("Nina Okoro", s.SuggestNames("nina", NameScope.Sme)[0].Item.Name);
            Assert.AreEqual("Nina Okoro", s.SuggestNames("Okoru", NameScope.Sme)[0].Item.Name, "typo in last name");
            Assert.AreEqual("Owen Pratt", s.SuggestNames("owen", NameScope.Sme)[0].Item.Name);
            Assert.AreEqual("Sam Oyelaran", s.SuggestNames("sam", NameScope.Crew)[0].Item.Name);
            Assert.AreEqual("Marco Feld", s.SuggestNames("feld marco", NameScope.Any)[0].Item.Name);

            // Empty query: the preferred group comes first.
            var sme = s.SuggestNames("", NameScope.Sme, 3).Select(m => m.Item.Name).ToList();
            CollectionAssert.AreEquivalent(new[] { "Nina Okoro", "Owen Pratt", "Terry Blackwood" }, sme);
            var crew = s.SuggestNames("", NameScope.Crew, 5).Select(m => m.Item.Org).ToList();
            Assert.IsTrue(crew.All(o => o == Org.Index));
        }

        [Test]
        public void Names_BiasBreaksTiesTowardScope_ButNeverHidesOthers()
        {
            var data = Fixtures.LoadSample();
            data.Trip.People.Add(new Person { Id = "p-x", FirstName = "Sam", LastName = "Client", Org = Org.Client, Roles = { PersonRole.Sme } });
            var s = new TripSearch(data);
            var crew = s.SuggestNames("sam", NameScope.Crew).Select(m => m.Item.Name).ToList();
            Assert.AreEqual("Sam Oyelaran", crew[0]);
            CollectionAssert.Contains(crew, "Sam Client");
            var sme = s.SuggestNames("sam", NameScope.Sme).Select(m => m.Item.Name).ToList();
            Assert.AreEqual("Sam Client", sme[0]);
            CollectionAssert.Contains(sme, "Sam Oyelaran");
        }

        [Test]
        public void FindPersonByName_IsExactAfterNormalization()
        {
            var s = S();
            Assert.AreEqual("p-007", s.FindPersonByName("  nina OKORO ").Id);
            Assert.IsNull(s.FindPersonByName("Nina Okoru"));
        }
    }
}

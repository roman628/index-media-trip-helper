using System;
using System.Collections.Generic;
using System.Linq;
using MediaTrip.Model;
using MediaTrip.Status;

namespace MediaTrip.Search
{
    /// <summary>Which name group a name field should lean toward. Every scope accepts any name.</summary>
    public enum NameScope
    {
        /// <summary>No preference: book team members, general fields.</summary>
        Any,
        /// <summary>Crew roles: prefers Index personnel.</summary>
        Crew,
        /// <summary>SME fields: prefers client personnel plus any name already used as an SME on this trip.</summary>
        Sme,
    }

    public class NameSuggestion
    {
        /// <summary>Display name ("First Last" or the free text as typed).</summary>
        public string Name;
        /// <summary>Registry person, or null if the name only exists as free text somewhere on the trip.</summary>
        public Person Person;
        public string Title;
        public Org? Org;
        /// <summary>True if this name has been used as an SME anywhere on the trip.</summary>
        public bool UsedAsSme;
        public bool IsInRegistry => Person != null;
        public override string ToString() => Name;
    }

    public class OutlineSuggestion
    {
        public string BookId;
        public string ChapterId;
        /// <summary>Null when the suggestion is the chapter itself.</summary>
        public string SectionId;
        public OutlineDocument Outline;
        public OutlineChapter Chapter;
        public OutlineSection Section;
        public double Score;
        public double BaseScore;
        public double Bias;
        public string Label =>
            Section != null
                ? $"{Section.Number} {Section.Name}".Trim()
                : $"Ch.{Chapter?.Number} {Chapter?.Name}".Trim();
        public override string ToString() => $"{Score:0.00} {Label}";
    }

    public class SearchOptions
    {
        public double NameBias = 0.15;
        /// <summary>Added when a suggested section is in the book the item already belongs to.</summary>
        public double SameBookBias = 0.15;
        /// <summary>Added (on top of the book bias) when it is in the item's own chapter. Deliberately
        /// strong: the shot list already places the item in a chapter, and book-wide words like
        /// "confined space" otherwise pull suggestions toward the wrong chapter.</summary>
        public double SameChapterBias = 0.3;
        /// <summary>How much of a chapter-name match carries into its sections' scores.</summary>
        public double ChapterNameCarry = 0.25;
        /// <summary>Weight of a match against a section's bullet text relative to a match against its name.</summary>
        public double NodeTextWeight = 0.6;
        /// <summary>Down-weight tokens that occur across many outline names (document-frequency weighting).</summary>
        public bool UseDocumentFrequency = true;
        public double MinScore = FuzzyMatcher.DefaultMinScore;
        public double OutlineMinScore = 0.2;
        public static readonly SearchOptions Default = new SearchOptions();
    }

    /// <summary>
    /// The three fuzzy-search uses over one trip, all built on <see cref="FuzzyMatcher"/>:
    /// name fields, shot-list lookup while typing a title, and outline-section suggestion.
    /// Returns scored candidates only; the caller decides what to do with them.
    /// </summary>
    public class TripSearch
    {
        public TripData Data { get; }
        public ResolvedPlan Plan { get; }
        public SearchOptions Options { get; }

        public TripSearch(TripData data, ResolvedPlan plan = null, SearchOptions options = null)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));
            Plan = plan ?? PlanResolver.Resolve(data);
            Options = options ?? SearchOptions.Default;
        }

        // ------------------------------------------------------------------
        // Names
        // ------------------------------------------------------------------

        /// <summary>
        /// Every distinct name known on the trip: the registry plus names typed free-form in
        /// captures, book teams and SME text fields. Deduplicated on normalized name.
        /// </summary>
        public List<NameSuggestion> AllKnownNames()
        {
            var byKey = new Dictionary<string, NameSuggestion>();
            var smeNames = new HashSet<string>();

            void Add(string name, Person person, string title, Org? org)
            {
                var key = FuzzyMatcher.Normalize(name);
                if (key.Length == 0) return;
                if (byKey.TryGetValue(key, out var existing))
                {
                    if (existing.Person == null && person != null) { existing.Person = person; existing.Org = org; }
                    if (string.IsNullOrEmpty(existing.Title) && !string.IsNullOrEmpty(title)) existing.Title = title;
                    return;
                }
                byKey[key] = new NameSuggestion { Name = name.Trim(), Person = person, Title = title, Org = org };
            }

            foreach (var p in Data.Trip.People)
            {
                Add(p.FullName, p, p.Title, p.Org);
                if (p.HasRole(PersonRole.Sme)) smeNames.Add(FuzzyMatcher.Normalize(p.FullName));
            }
            foreach (var cap in Data.Captures.Captures)
                foreach (var cp in cap.People ?? Enumerable.Empty<CapturePerson>())
                {
                    if (string.IsNullOrWhiteSpace(cp.Name)) continue;
                    var person = cp.PersonId != null ? Data.FindPerson(cp.PersonId) : null;
                    Add(cp.Name, person, cp.Title, person?.Org);
                    smeNames.Add(FuzzyMatcher.Normalize(cp.Name)); // people on camera are SMEs in practice
                }
            foreach (var book in Data.Trip.Books)
                foreach (var name in book.Team?.MemberNames ?? Enumerable.Empty<string>())
                    Add(name, null, null, null);
            foreach (var v in Data.ShotList.Videos)
            {
                if (!string.IsNullOrWhiteSpace(v.SmeText))
                {
                    Add(v.SmeText, null, null, null);
                    smeNames.Add(FuzzyMatcher.Normalize(v.SmeText));
                }
                foreach (var id in v.SmeIds ?? Enumerable.Empty<string>())
                {
                    var p = Data.FindPerson(id);
                    if (p != null) smeNames.Add(FuzzyMatcher.Normalize(p.FullName));
                }
            }

            foreach (var kv in byKey) kv.Value.UsedAsSme = smeNames.Contains(kv.Key);
            return byKey.Values.OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// Name suggestions for a field. With an empty query, returns the preferred group first
        /// (so a field can offer choices before typing). Any name is still accepted by the caller.
        /// </summary>
        public List<ScoredMatch<NameSuggestion>> SuggestNames(string query, NameScope scope, int maxResults = 8)
        {
            var names = AllKnownNames();
            if (string.IsNullOrWhiteSpace(query))
            {
                return names
                    .Select((n, i) => (n, i, bias: NameBias(n, scope)))
                    .OrderByDescending(t => t.bias).ThenBy(t => t.n.Name, StringComparer.OrdinalIgnoreCase)
                    .Take(maxResults)
                    .Select(t => new ScoredMatch<NameSuggestion> { Item = t.n, Score = t.bias, BaseScore = 0, Bias = t.bias })
                    .ToList();
            }
            return FuzzyMatcher.Rank(query, names,
                n => NameFields(n),
                n => NameBias(n, scope),
                Options.MinScore, maxResults);
        }

        private static IEnumerable<string> NameFields(NameSuggestion n)
        {
            yield return n.Name;
            if (n.Person != null)
            {
                yield return n.Person.FirstName;
                yield return n.Person.LastName;
                if (!string.IsNullOrEmpty(n.Person.LastName) && !string.IsNullOrEmpty(n.Person.FirstName))
                    yield return n.Person.LastName + " " + n.Person.FirstName;
            }
        }

        private double NameBias(NameSuggestion n, NameScope scope)
        {
            switch (scope)
            {
                case NameScope.Crew:
                    return n.Org == Org.Index ? Options.NameBias : 0;
                case NameScope.Sme:
                    return (n.Org == Org.Client || n.UsedAsSme) ? Options.NameBias : 0;
                default:
                    return 0;
            }
        }

        /// <summary>Exact (normalized) name lookup in the registry, to avoid creating duplicates.</summary>
        public Person FindPersonByName(string name)
        {
            var key = FuzzyMatcher.Normalize(name);
            if (key.Length == 0) return null;
            return Data.Trip.People.FirstOrDefault(p => FuzzyMatcher.Normalize(p.FullName) == key);
        }

        // ------------------------------------------------------------------
        // Shot list
        // ------------------------------------------------------------------

        /// <summary>
        /// Shot-list entries matching a partially typed title (or a number). Superseded items
        /// are excluded by default because they can no longer be captured; their replacement is
        /// what matches instead.
        /// </summary>
        public List<ScoredMatch<PlanItem>> SearchShotList(string query, int maxResults = 10, bool includeSuperseded = false, bool includeDropped = true)
        {
            var items = Plan.Items.Where(i => (includeSuperseded || !i.IsSuperseded) && (includeDropped || !i.IsDropped));
            var trimmed = (query ?? "").Trim();

            if (trimmed.Length > 0 && trimmed.All(char.IsDigit))
            {
                // Typing a number: match the plan number(s).
                return items
                    .Select((i, idx) => (item: i, idx, score: NumberScore(i, trimmed)))
                    .Where(t => t.score > 0)
                    .OrderByDescending(t => t.score).ThenBy(t => t.idx)
                    .Take(maxResults)
                    .Select(t => new ScoredMatch<PlanItem> { Item = t.item, Score = t.score, BaseScore = t.score, MatchedField = t.item.DisplayNumber })
                    .ToList();
            }

            return FuzzyMatcher.Rank(trimmed, items,
                i => new[] { i.Title, i.OriginalTitle },
                null, Options.MinScore, maxResults);
        }

        private static double NumberScore(PlanItem item, string digits)
        {
            double best = 0;
            foreach (var n in item.SourceNumbers)
            {
                var s = n.ToString();
                if (s == digits) best = Math.Max(best, item.Origin == PlanItemOrigin.Planned ? 1.0 : 0.95);
                else if (s.StartsWith(digits, StringComparison.Ordinal)) best = Math.Max(best, 0.7);
            }
            return best;
        }

        // ------------------------------------------------------------------
        // Outline suggestion
        // ------------------------------------------------------------------

        private Dictionary<string, double> _outlineTokenWeights;

        /// <summary>
        /// Document-frequency weights over every chapter and section name in every outline, so
        /// the book's topic words ("confined", "space") stop dominating section matching.
        /// </summary>
        public IReadOnlyDictionary<string, double> OutlineTokenWeights
        {
            get
            {
                if (!Options.UseDocumentFrequency) return null;
                if (_outlineTokenWeights != null) return _outlineTokenWeights;
                var names = Data.Outlines.Values.SelectMany(o =>
                    o.Chapters.Select(c => c.Name).Concat(o.Chapters.SelectMany(c => c.Sections.Select(s => s.Name))));
                return _outlineTokenWeights = FuzzyMatcher.DocumentFrequencyWeights(names.Where(n => !string.IsNullOrEmpty(n)));
            }
        }

        /// <summary>
        /// Suggest outline locations for a video/photo title by matching it against chapter and
        /// section names (and, at reduced weight, section bullet text) across all outlines,
        /// biased toward the book and chapter the item already belongs to.
        /// Never auto-commits: the caller shows these and the user taps one.
        /// </summary>
        public List<OutlineSuggestion> SuggestOutlineSection(string title, string bookId = null, string chapterId = null, int maxResults = 5)
        {
            var results = new List<(OutlineSuggestion s, int idx)>();
            int idx = 0;
            if (string.IsNullOrWhiteSpace(title)) return new List<OutlineSuggestion>();

            var chapterNumber = ChapterNumberOf(chapterId);
            var weights = OutlineTokenWeights;

            foreach (var kv in Data.Outlines.OrderBy(k => BookIndex(k.Key)))
            {
                var outlineBookId = kv.Key;
                var outline = kv.Value;
                double bookBias = outlineBookId == bookId ? Options.SameBookBias : 0;

                foreach (var ch in outline.Chapters)
                {
                    bool sameChapter = chapterId != null &&
                                       (ch.Id == chapterId || (chapterNumber.HasValue && outlineBookId == bookId && ch.Number == chapterNumber.Value));
                    double chapterBias = sameChapter ? Options.SameChapterBias : 0;
                    double chapterScore = FuzzyMatcher.Score(title, ch.Name, weights);

                    // The chapter itself is a candidate (media can be assigned at chapter level).
                    AddSuggestion(results, ref idx, outlineBookId, outline, ch, null, chapterScore, bookBias + chapterBias);

                    foreach (var sec in ch.Sections)
                    {
                        double sectionScore = FuzzyMatcher.Score(title, sec.Name, weights);
                        if (Options.NodeTextWeight > 0 && sec.Nodes != null)
                        {
                            double bestNode = 0;
                            foreach (var (node, _) in Node.Walk(sec.Nodes))
                            {
                                var s = FuzzyMatcher.Score(title, node.Text, weights);
                                if (s > bestNode) bestNode = s;
                            }
                            sectionScore = Math.Max(sectionScore, bestNode * Options.NodeTextWeight);
                        }
                        double combined = Math.Max(sectionScore, Math.Min(1, sectionScore + Options.ChapterNameCarry * chapterScore));
                        AddSuggestion(results, ref idx, outlineBookId, outline, ch, sec, combined, bookBias + chapterBias);
                    }
                }
            }

            return results
                .Where(r => r.s.BaseScore > 0 && r.s.Score >= Options.OutlineMinScore)
                .OrderByDescending(r => r.s.Score).ThenByDescending(r => r.s.Bias).ThenByDescending(r => r.s.BaseScore).ThenBy(r => r.idx)
                .Take(maxResults)
                .Select(r => r.s)
                .ToList();
        }

        public List<OutlineSuggestion> SuggestOutlineFor(Capture capture, int maxResults = 5) =>
            SuggestOutlineSection(capture.Title, capture.BookId, capture.ChapterId, maxResults);

        public List<OutlineSuggestion> SuggestOutlineFor(Photo photo, int maxResults = 5) =>
            SuggestOutlineSection(photo.Description, photo.BookId, photo.ChapterId, maxResults);

        public List<OutlineSuggestion> SuggestOutlineFor(PlanItem item, int maxResults = 5) =>
            SuggestOutlineSection(item.Title, item.BookId, item.ChapterId, maxResults);

        private static void AddSuggestion(List<(OutlineSuggestion, int)> results, ref int idx, string bookId, OutlineDocument outline,
            OutlineChapter ch, OutlineSection sec, double baseScore, double bias)
        {
            var total = baseScore > 0 ? Math.Min(1, baseScore + bias) : 0;
            results.Add((new OutlineSuggestion
            {
                BookId = bookId,
                ChapterId = ch.Id,
                SectionId = sec?.Id,
                Outline = outline,
                Chapter = ch,
                Section = sec,
                Score = total,
                BaseScore = baseScore,
                Bias = bias,
            }, idx++));
        }

        private int? ChapterNumberOf(string chapterId)
        {
            if (chapterId == null) return null;
            var ch = Data.FindChapter(chapterId);
            if (ch != null) return ch.Number;
            foreach (var o in Data.Outlines.Values)
                foreach (var oc in o.Chapters)
                    if (oc.Id == chapterId) return oc.Number;
            return null;
        }

        private int BookIndex(string bookId)
        {
            var b = Data.FindBook(bookId);
            return b?.Number ?? int.MaxValue;
        }
    }
}

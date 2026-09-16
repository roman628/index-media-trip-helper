using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace MediaTrip.Search
{
    public class ScoredMatch<T>
    {
        public T Item;
        /// <summary>Final score including bias, clamped to 0..1.</summary>
        public double Score;
        /// <summary>Text similarity alone, 0..1.</summary>
        public double BaseScore;
        public double Bias;
        /// <summary>Which of the candidate's fields produced the best score.</summary>
        public string MatchedField;
        public override string ToString() => $"{Score:0.00} {Item}";
    }

    /// <summary>
    /// Deterministic fuzzy text matcher. No AI, no network, no randomness: the same inputs
    /// always produce the same scores and the same ordering.
    ///
    /// Scoring combines token coverage in both directions (query tokens found in the candidate,
    /// candidate tokens found in the query), so it works both for search-as-you-type (short
    /// query, long candidate) and for "which short section name is contained in this long video
    /// title" (long query, short candidate). Tokens match exactly, by prefix, or within a small
    /// edit distance (typos). Contiguous phrase matches get a bonus. Callers may pass per-token
    /// weights (e.g. document-frequency weights) so corpus-wide words count for less.
    /// </summary>
    public static class FuzzyMatcher
    {
        public const double DefaultMinScore = 0.3;

        private static readonly HashSet<string> StopWords = new HashSet<string>(StringComparer.Ordinal)
        {
            "a", "an", "the", "of", "and", "or", "to", "in", "on", "at", "for", "with", "is", "it", "vs",
        };

        // ------------------------------------------------------------------
        // Normalization
        // ------------------------------------------------------------------

        /// <summary>Lowercase, diacritics stripped, punctuation to spaces, whitespace collapsed.</summary>
        public static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var decomposed = s.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(decomposed.Length);
            bool lastSpace = true;
            foreach (var ch in decomposed)
            {
                var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
                if (cat == UnicodeCategory.NonSpacingMark) continue;
                if (char.IsLetterOrDigit(ch))
                {
                    sb.Append(char.ToLowerInvariant(ch));
                    lastSpace = false;
                }
                else if (!lastSpace)
                {
                    sb.Append(' ');
                    lastSpace = true;
                }
            }
            return sb.ToString().Trim();
        }

        /// <summary>Very light plural stemming: "permits" -> "permit", "policies" -> "policy". Applied to both sides, so it only needs to be consistent.</summary>
        public static string Stem(string token)
        {
            if (token.Length >= 5 && token.EndsWith("ies", StringComparison.Ordinal))
                return token.Substring(0, token.Length - 3) + "y";
            if (token.Length >= 5 && token.EndsWith("s", StringComparison.Ordinal) && !token.EndsWith("ss", StringComparison.Ordinal))
                return token.Substring(0, token.Length - 1);
            return token;
        }

        /// <summary>Normalized, stemmed tokens with stop words removed (unless nothing else remains).</summary>
        public static List<string> Tokenize(string s)
        {
            var norm = Normalize(s);
            if (norm.Length == 0) return new List<string>();
            var all = norm.Split(' ');
            var kept = all.Where(t => !StopWords.Contains(t)).Select(Stem).ToList();
            return kept.Count > 0 ? kept : all.Select(Stem).ToList();
        }

        // ------------------------------------------------------------------
        // Scoring
        // ------------------------------------------------------------------

        /// <summary>Similarity of a query to one candidate string, 0..1.</summary>
        /// <param name="tokenWeights">Optional multiplier per token (default 1). Lower values make a token matter less.</param>
        public static double Score(string query, string candidate, IReadOnlyDictionary<string, double> tokenWeights = null)
        {
            var nq = Normalize(query);
            var nc = Normalize(candidate);
            if (nq.Length == 0 || nc.Length == 0) return 0;
            if (nq == nc) return 1.0;

            var qt = Tokenize(query);
            var ct = Tokenize(candidate);
            if (qt.Count == 0 || ct.Count == 0) return 0;

            var coverQ = Coverage(qt, ct, tokenWeights, out var orderedQ); // query tokens found in candidate
            var coverC = Coverage(ct, qt, tokenWeights, out _);            // candidate tokens found in query
            var hi = Math.Max(coverQ, coverC);
            var lo = Math.Min(coverQ, coverC);
            var score = 0.65 * hi + 0.35 * lo;

            // Contiguous phrase matches beat scattered ones. Compare on stop-word-free token
            // strings so "the double dead end space" still counts as contained in
            // "identifying a double dead end space".
            var jq = string.Join(" ", qt);
            var jc = string.Join(" ", ct);
            if (jc == jq) return 1.0;
            if (jc.Contains(jq))
            {
                var ratio = (double)jq.Length / jc.Length;
                score = Math.Max(score, 0.55 + 0.45 * ratio);
                if (nc.StartsWith(nq, StringComparison.Ordinal) || jc.StartsWith(jq, StringComparison.Ordinal)) score += 0.05;
            }
            else if (jq.Contains(jc))
            {
                var ratio = (double)jc.Length / jq.Length;
                score = Math.Max(score, 0.6 + 0.4 * ratio);
            }
            else if (orderedQ && coverQ > 0.5)
            {
                score += 0.03;
            }

            return Clamp01(score);
        }

        /// <summary>Best score of the query against several fields of one candidate.</summary>
        public static double ScoreFields(string query, IEnumerable<string> fields, out string bestField, IReadOnlyDictionary<string, double> tokenWeights = null)
        {
            double best = 0;
            bestField = null;
            if (fields == null) return 0;
            foreach (var f in fields)
            {
                if (string.IsNullOrEmpty(f)) continue;
                var s = Score(query, f, tokenWeights);
                if (s > best) { best = s; bestField = f; }
            }
            return best;
        }

        /// <summary>
        /// Weighted average, over <paramref name="tokens"/>, of each token's best match in
        /// <paramref name="against"/>. Weight = token length (capped) times its optional
        /// corpus weight. <paramref name="inOrder"/> reports whether matches appear in the same
        /// relative order.
        /// </summary>
        private static double Coverage(List<string> tokens, List<string> against, IReadOnlyDictionary<string, double> tokenWeights, out bool inOrder)
        {
            double weightSum = 0, scoreSum = 0;
            int lastIndex = -1;
            inOrder = true;
            foreach (var t in tokens)
            {
                double w = Math.Min(t.Length, 10);
                if (w < 1) w = 1;
                if (tokenWeights != null && tokenWeights.TryGetValue(t, out var tw)) w *= tw;
                double best = 0;
                int bestIdx = -1;
                for (int i = 0; i < against.Count; i++)
                {
                    var s = TokenScore(t, against[i]);
                    if (s > best) { best = s; bestIdx = i; }
                }
                if (bestIdx >= 0 && best > 0)
                {
                    if (bestIdx < lastIndex) inOrder = false;
                    lastIndex = bestIdx;
                }
                weightSum += w;
                scoreSum += w * best;
            }
            return weightSum == 0 ? 0 : scoreSum / weightSum;
        }

        /// <summary>Similarity of two single tokens, 0..1.</summary>
        public static double TokenScore(string a, string b)
        {
            if (a == b) return 1.0;
            if (a.Length == 0 || b.Length == 0) return 0;

            var shorter = a.Length <= b.Length ? a : b;
            var longer = a.Length <= b.Length ? b : a;
            double ratio = (double)shorter.Length / longer.Length;

            // Prefix: "dou" -> "double", "walkthrough" vs "walkthroughs".
            if (shorter.Length >= 2 && longer.StartsWith(shorter, StringComparison.Ordinal))
                return 0.85 + 0.15 * ratio;

            // Typos: small edit distance on tokens of reasonable length.
            if (shorter.Length >= 4)
            {
                int dist = DamerauLevenshtein(a, b);
                double sim = 1.0 - (double)dist / longer.Length;
                if (sim >= 0.7) return sim * 0.9;
            }

            // Substring inside a token: "lock" in "lockout".
            if (shorter.Length >= 3 && longer.Contains(shorter))
                return 0.6 + 0.2 * ratio;

            return 0;
        }

        /// <summary>Optimal string alignment distance (Levenshtein plus adjacent transposition).</summary>
        public static int DamerauLevenshtein(string a, string b)
        {
            int n = a.Length, m = b.Length;
            if (n == 0) return m;
            if (m == 0) return n;
            var d = new int[n + 1, m + 1];
            for (int i = 0; i <= n; i++) d[i, 0] = i;
            for (int j = 0; j <= m; j++) d[0, j] = j;
            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    int v = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                    if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                        v = Math.Min(v, d[i - 2, j - 2] + 1);
                    d[i, j] = v;
                }
            }
            return d[n, m];
        }

        /// <summary>
        /// Document-frequency weights for a corpus of short names: a token that appears in many
        /// names (the book's topic words) gets a weight below 1 so it stops dominating matches.
        /// weight = 1 / (1 + ln(df)). Tokens absent from the corpus keep weight 1.
        /// </summary>
        public static Dictionary<string, double> DocumentFrequencyWeights(IEnumerable<string> names)
        {
            var df = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var name in names)
            {
                foreach (var t in Tokenize(name).Distinct())
                    df[t] = df.TryGetValue(t, out var n) ? n + 1 : 1;
            }
            return df.ToDictionary(kv => kv.Key, kv => 1.0 / (1.0 + Math.Log(kv.Value)), StringComparer.Ordinal);
        }

        // ------------------------------------------------------------------
        // Ranking
        // ------------------------------------------------------------------

        /// <summary>
        /// Score every candidate and return those at or above <paramref name="minScore"/>, best
        /// first. Order: score, then bias (so a scope preference breaks ties even when the score
        /// is clamped at 1), then text score, then the candidates' original order. Stable.
        /// </summary>
        /// <param name="fields">Strings of a candidate to match against; the best one counts.</param>
        /// <param name="bias">Additive bias (e.g. +0.15 for "same book"); applied after text scoring.</param>
        public static List<ScoredMatch<T>> Rank<T>(
            string query,
            IEnumerable<T> candidates,
            Func<T, IEnumerable<string>> fields,
            Func<T, double> bias = null,
            double minScore = DefaultMinScore,
            int maxResults = 10,
            IReadOnlyDictionary<string, double> tokenWeights = null)
        {
            var results = new List<(ScoredMatch<T> match, int index)>();
            int idx = 0;
            foreach (var c in candidates)
            {
                var baseScore = ScoreFields(query, fields(c), out var field, tokenWeights);
                var b = bias?.Invoke(c) ?? 0;
                var total = baseScore > 0 ? Clamp01(baseScore + b) : 0;
                if (baseScore > 0 && total >= minScore)
                    results.Add((new ScoredMatch<T> { Item = c, Score = total, BaseScore = baseScore, Bias = b, MatchedField = field }, idx));
                idx++;
            }
            return results
                .OrderByDescending(r => r.match.Score)
                .ThenByDescending(r => r.match.Bias)
                .ThenByDescending(r => r.match.BaseScore)
                .ThenBy(r => r.index)
                .Take(maxResults)
                .Select(r => r.match)
                .ToList();
        }

        public static double Clamp01(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);
    }
}

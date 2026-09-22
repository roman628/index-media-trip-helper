using System.Collections.Generic;
using System.Linq;
using MediaTrip.Model;
using MediaTrip.Search;

namespace MediaTrip.Query
{
    /// <summary>
    /// A photo typed as free text while filming or in the summary's Photos sheet: it is in the
    /// summary but not on the shot list. One entry may describe a group of photos ("SMEs doing
    /// X"), so it is never counted as one image. The same text typed twice is one loose photo.
    /// </summary>
    public sealed class LoosePhoto
    {
        public string Text;
        /// <summary>Normalized text; two entries with the same key are the same photo.</summary>
        public string Key;
        /// <summary>Video captures that list it among their photos.</summary>
        public List<Capture> Captures = new List<Capture>();
        /// <summary>Stand-alone photo captures of it.</summary>
        public List<PhotoCapture> PhotoCaptures = new List<PhotoCapture>();
        /// <summary>The book and chapter of the first video it was shot with; null for a stand-alone photo capture.</summary>
        public string BookId;
        public string ChapterId;
        public List<string> DayIds = new List<string>();
    }

    public static class LoosePhotos
    {
        public static string KeyOf(string text) => FuzzyMatcher.Normalize(text);

        /// <summary>Every free-text photo of the trip, in the order first logged.</summary>
        public static List<LoosePhoto> Of(TripData d)
        {
            var byKey = new Dictionary<string, LoosePhoto>();
            var list = new List<LoosePhoto>();
            LoosePhoto Get(string text)
            {
                var key = KeyOf(text);
                if (key.Length == 0) return null;
                if (!byKey.TryGetValue(key, out var lp)) { lp = new LoosePhoto { Text = text.Trim(), Key = key }; byKey[key] = lp; list.Add(lp); }
                return lp;
            }
            foreach (var c in d.Captures.Captures)
                foreach (var cp in c.Photos ?? new List<CapturePhoto>())
                {
                    if (cp.PhotoId != null || string.IsNullOrWhiteSpace(cp.Text)) continue;
                    var lp = Get(cp.Text);
                    if (lp == null) continue;
                    lp.Captures.Add(c);
                    if (lp.BookId == null) { lp.BookId = c.BookId; lp.ChapterId = c.ChapterId; }
                    if (c.DayId != null && !lp.DayIds.Contains(c.DayId)) lp.DayIds.Add(c.DayId);
                }
            foreach (var pc in d.Captures.PhotoCaptures)
            {
                if (pc.PhotoId != null || string.IsNullOrWhiteSpace(pc.Text)) continue;
                var lp = Get(pc.Text);
                if (lp == null) continue;
                lp.PhotoCaptures.Add(pc);
                if (pc.DayId != null && !lp.DayIds.Contains(pc.DayId)) lp.DayIds.Add(pc.DayId);
            }
            return list;
        }

        public static LoosePhoto Find(TripData d, string text)
        {
            var key = KeyOf(text);
            return key.Length == 0 ? null : Of(d).FirstOrDefault(lp => lp.Key == key);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MediaTrip.Model;
using MediaTrip.Session;

namespace MediaTrip.UI.ViewModels
{
    /// <summary>
    /// Arrive and depart are travel days. The shooting days are the days between them, so once
    /// both dates are in they are filled in: "Day 1", "Day 2", … A day that already has media is
    /// never removed or re-dated.
    /// </summary>
    public static class TripDays
    {
        private static bool TryDate(string iso, out DateTime d) =>
            DateTime.TryParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d);

        /// <summary>The dates strictly between arrive and depart; both when they are the same or adjacent (a one- or two-day trip still has a day to shoot on).</summary>
        public static List<string> Between(string arrive, string depart)
        {
            var list = new List<string>();
            if (!TryDate(arrive, out var a) || !TryDate(depart, out var b) || b < a || (b - a).TotalDays > 60) return list;
            for (var d = a.AddDays(1); d < b; d = d.AddDays(1)) list.Add(d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            if (list.Count == 0) { list.Add(arrive); if (b > a) list.Add(depart); }
            return list;
        }

        /// <summary>
        /// Make the trip's days match its dates. Adds the missing days, removes days outside the
        /// range that nothing was logged on, and labels them in date order. Returns true when
        /// anything changed.
        /// </summary>
        public static bool AutoFill(TripSession s)
        {
            var t = s.Data.Trip;
            var wanted = Between(t.Dates?.Arrive, t.Dates?.Depart);
            if (wanted.Count == 0) return false;
            var used = new HashSet<string>(s.Data.Captures.Captures.Select(c => c.DayId).Concat(s.Data.Captures.PhotoCaptures.Select(p => p.DayId)).Where(x => x != null));
            bool changed = false;
            foreach (var day in t.Days.ToList())
                if (!wanted.Contains(day.Date) && !used.Contains(day.Id)) { s.RemoveDay(day.Id); changed = true; }
            foreach (var date in wanted)
                if (t.Days.All(d => d.Date != date)) { s.AddDay(date, null); changed = true; }
            if (!changed) return false;
            s.EditTrip(x =>
            {
                x.Days = x.Days.OrderBy(d => d.Date ?? "", StringComparer.Ordinal).ToList();
                for (int i = 0; i < x.Days.Count; i++)
                    if (string.IsNullOrWhiteSpace(x.Days[i].Label) || IsAutoLabel(x.Days[i].Label)) x.Days[i].Label = "Day " + (i + 1);
            });
            return true;
        }

        private static bool IsAutoLabel(string label) =>
            label.StartsWith("Day ", StringComparison.OrdinalIgnoreCase) && int.TryParse(label.Substring(4), out _);
    }

    /// <summary>What a collapsed chapter or section shows: the media attached to it and the first words of its note.</summary>
    public sealed class PlaceSummary
    {
        public List<string> Media = new List<string>();
        public int Suggested;
        public string NoteExcerpt = "";
        public bool IsEmpty => Media.Count == 0 && NoteExcerpt.Length == 0;

        public static PlaceSummary Of(TripSession s, string chapterId, string sectionId)
        {
            var sum = new PlaceSummary { NoteExcerpt = Query.TripQueries.Excerpt(s.NoteText(chapterId, sectionId)) };
            foreach (var m in s.Queries.PlacedIn(chapterId, sectionId))
            {
                sum.Media.Add(m.Title ?? "");
                if (!m.Confirmed) sum.Suggested++;
            }
            return sum;
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using MediaTrip.Model;
using MediaTrip.Status;

namespace MediaTrip.UI.ViewModels
{
    /// <summary>Text builders shared by several screens.</summary>
    public static class Fmt
    {
        /// <summary>"ACME · WDG · P5"</summary>
        public static string Identity(TripDocument t)
        {
            var id = t?.Identity;
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(id?.ClientAbbrev)) parts.Add(id.ClientAbbrev);
            if (!string.IsNullOrEmpty(id?.ProgramAbbrev)) parts.Add(id.ProgramAbbrev);
            if (id?.PhaseNumber != null) parts.Add("P" + id.PhaseNumber);
            return parts.Count > 0 ? string.Join(" · ", parts) : "New trip";
        }

        /// <summary>"Boise, ID"</summary>
        public static string Place(TripDocument t)
        {
            var l = t?.Identity?.Location;
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(l?.City)) parts.Add(l.City);
            if (!string.IsNullOrEmpty(l?.State)) parts.Add(l.State);
            return string.Join(", ", parts);
        }

        /// <summary>"Day 2 of 2" for the day being viewed.</summary>
        public static string DayOf(TripDocument t, string dayId)
        {
            var days = t.Days;
            var idx = days.FindIndex(d => d.Id == dayId);
            if (idx < 0) return days.Count == 0 ? "No days" : days.Count + " days";
            return (days[idx].Label ?? "Day " + (idx + 1)) + " of " + days.Count;
        }

        /// <summary>The trip day matching the calendar date, else the last day, else null.</summary>
        public static Day TodayOrLast(TripDocument t, System.DateTime now)
        {
            var iso = now.ToString("yyyy-MM-dd");
            return t.Days.FirstOrDefault(d => d.Date == iso) ?? t.Days.LastOrDefault();
        }

        public static string SmeOf(TripData d, PlanItem it)
        {
            var names = (it.SmeIds ?? new List<string>()).Select(d.FindPerson).Where(p => p != null).Select(p => p.FullName).ToList();
            if (names.Count > 0) return string.Join(", ", names);
            return it.SmeText ?? "";
        }

        /// <summary>
        /// A book is shown by its name and nothing else: the real names carry their own numbers
        /// ("Book 46 …"), so the entry order is kept for sorting but never printed in front.
        /// </summary>
        public static string BookName(Book b) => b == null ? "Unknown book" : string.IsNullOrWhiteSpace(b.Name) ? "Untitled book" : b.Name;

        /// <summary>"Confined Space Entry · Ch.2"</summary>
        public static string BookChapter(TripData d, string bookId, string chapterId)
        {
            var c = d.FindChapter(chapterId);
            var b = d.FindBook(bookId ?? c?.BookId);
            return BookName(b) + (c != null ? " · Ch." + c.Number : "");
        }

        /// <summary>What a photo IS, for a search result: its book, its chapter, and whether it is the cover or a given chapter's hero.</summary>
        public static string PhotoWhere(TripData d, PhotoItem p)
        {
            var b = d.FindBook(p.BookId);
            var c = d.FindChapter(p.ChapterId);
            var use = p.HeroType == HeroType.BookCover ? "Cover" : p.HeroType == HeroType.ChapterHero ? "Ch." + (c?.Number.ToString() ?? "?") + " hero" : c != null ? "Ch." + c.Number : null;
            return BookName(b) + (use != null ? " · " + use : "") + (p.IsNew ? " · new" : "");
        }

        public static string OriginNote(PlanItem it, Capture c)
        {
            if (c?.PlannedNumber != null) return "planned #" + c.PlannedNumber;
            if (it != null && it.Origin == PlanItemOrigin.Combined) return "combined " + it.DisplayNumber;
            return "unplanned";
        }
    }
}

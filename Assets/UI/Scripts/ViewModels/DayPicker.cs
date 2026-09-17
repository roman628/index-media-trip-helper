using System;
using System.Globalization;
using System.Linq;
using MediaTrip.Model;

namespace MediaTrip.UI.ViewModels
{
    /// <summary>
    /// Which trip day something logged right now belongs to. A day is a calendar date. If a day
    /// already has today's date, that one. If the trip is under way (today falls inside the
    /// trip's dates, or the trip has no days at all) a new day is needed. Otherwise the last
    /// day, so working on a trip before or after its dates never invents days.
    /// </summary>
    public static class DayPicker
    {
        public sealed class Result
        {
            /// <summary>An existing day to use; null when <see cref="CreateDate"/> is set.</summary>
            public Day Day;
            /// <summary>ISO date of the day to create.</summary>
            public string CreateDate;
            public string CreateLabel;
        }

        public static Result For(TripDocument trip, DateTime now)
        {
            var iso = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var match = trip.Days.FirstOrDefault(d => d.Date == iso);
            if (match != null) return new Result { Day = match };
            if (trip.Days.Count == 0 || WithinTrip(trip, iso))
                return new Result { CreateDate = iso, CreateLabel = "Day " + (trip.Days.Count + 1) };
            return new Result { Day = trip.Days.OrderBy(d => d.Date ?? "", StringComparer.Ordinal).Last() };
        }

        private static bool WithinTrip(TripDocument trip, string iso)
        {
            var a = trip.Dates?.Arrive; var b = trip.Dates?.Depart;
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            return string.CompareOrdinal(iso, a) >= 0 && string.CompareOrdinal(iso, b) <= 0;
        }
    }
}

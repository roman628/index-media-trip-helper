using System.Collections.Generic;
using MediaTrip.Model;

namespace MediaTrip.UI.ViewModels
{
    /// <summary>
    /// Remembers the video numbers the crew's paper still shows. After an insert, move or
    /// delete renumbers the plan, every shifted video carries a "was N" tag until the numbers
    /// are accepted. Pure logic, no UI.
    /// </summary>
    public sealed class RenumberTracker
    {
        private readonly Dictionary<string, int> _original = new Dictionary<string, int>();

        /// <summary>Remember the current numbers as "what the paper says".</summary>
        public void Snapshot(TripData data)
        {
            _original.Clear();
            foreach (var v in data.ShotList.Videos) _original[v.Id] = v.Number;
        }

        public bool HasSnapshot => _original.Count > 0;

        /// <summary>The number a video had at the last snapshot, if it differs from now.</summary>
        public int? Was(Video v) => v != null && _original.TryGetValue(v.Id, out var n) && n != v.Number ? n : (int?)null;

        public bool AnyShifted(TripData data)
        {
            foreach (var v in data.ShotList.Videos) if (Was(v) != null) return true;
            return false;
        }

        public List<(Video video, int was)> Shifted(TripData data)
        {
            var list = new List<(Video, int)>();
            foreach (var v in data.ShotList.Videos) { var w = Was(v); if (w != null) list.Add((v, w.Value)); }
            return list;
        }

        /// <summary>A video added since the snapshot has no "was"; register it so later shifts are tracked from its first number.</summary>
        public void Track(Video v) { if (v != null && !_original.ContainsKey(v.Id)) _original[v.Id] = v.Number; }

        public void Accept(TripData data) => Snapshot(data);
    }
}

using System.Collections.Generic;
using System.Linq;
using MediaTrip.Model;
using MediaTrip.Session;
using MediaTrip.Status;

namespace MediaTrip.UI.ViewModels
{
    /// <summary>The field amendment sheet: rename, combine with siblings, or drop. Testable without UI.</summary>
    public sealed class AmendDraft
    {
        public string ItemId;
        /// <summary>menu | rename | combine | drop</summary>
        public string Mode = "menu";
        public string Title = "";
        public string Reason = "";
        public HashSet<string> With = new HashSet<string>();

        public static AmendDraft For(TripSession s, string itemId)
        {
            var item = s.Plan.FindItem(itemId);
            return new AmendDraft { ItemId = itemId, Title = item?.Title ?? "" };
        }

        public void SetMode(TripSession s, string mode)
        {
            Mode = mode;
            var item = s.Plan.FindItem(ItemId);
            if (item != null && (mode == "rename" || mode == "combine")) Title = item.Title;
            if (mode == "combine") With.Clear();
        }

        /// <summary>Active videos in the same chapter that could be combined with this one.</summary>
        public List<PlanItem> Siblings(TripSession s)
        {
            var item = s.Plan.FindItem(ItemId);
            if (item == null) return new List<PlanItem>();
            return s.Plan.Items.Where(i => i.ChapterId == item.ChapterId && i.Id != item.Id && !i.IsDropped && !i.IsSuperseded).ToList();
        }

        public void ToggleWith(TripSession s, string otherId)
        {
            if (!With.Remove(otherId)) With.Add(otherId);
            var item = s.Plan.FindItem(ItemId);
            var names = new List<string> { item?.Title ?? "" };
            names.AddRange(Siblings(s).Where(x => With.Contains(x.Id)).Select(x => x.Title));
            Title = string.Join(" and ", names);
        }

        public bool CanApply
        {
            get
            {
                switch (Mode)
                {
                    case "rename": return !string.IsNullOrWhiteSpace(Title);
                    case "combine": return With.Count > 0 && !string.IsNullOrWhiteSpace(Title);
                    case "drop": return true;
                    default: return false;
                }
            }
        }

        /// <summary>Record the amendment. Returns the id of the item that now represents it (the combined result for a combine).</summary>
        public string Apply(TripSession s)
        {
            var item = s.Plan.FindItem(ItemId);
            switch (Mode)
            {
                case "rename":
                {
                    s.Rename(ItemId, Title.Trim(), Reason);
                    foreach (var c in s.Data.Captures.Captures.Where(c => c.PlanVideoId == ItemId).ToList())
                        s.UpdateCapture(c.Id, x => x.Title = Title.Trim());
                    return ItemId;
                }
                case "combine":
                {
                    var ids = new List<string> { ItemId };
                    ids.AddRange(With);
                    var am = s.Combine(ids, Title.Trim(), Reason);
                    var rid = am.Results[0];
                    // A capture already recorded against one of the sources moves onto the combined item.
                    var caps = s.Data.Captures.Captures.Where(c => c.PlanVideoId != null && ids.Contains(c.PlanVideoId)).ToList();
                    if (caps.Count > 0)
                    {
                        var keep = caps[0];
                        s.UpdateCapture(keep.Id, x => { x.PlanVideoId = rid; x.Title = Title.Trim(); });
                        foreach (var extra in caps.Skip(1)) s.RemoveCapture(extra.Id);
                    }
                    return rid;
                }
                case "drop":
                {
                    // A plan change never deletes a capture: what was shot stays in the summary.
                    s.Drop(ItemId, Reason);
                    return ItemId;
                }
            }
            return ItemId;
        }

        public static string ModeTitle(string mode)
        {
            switch (mode)
            {
                case "rename": return "Rename";
                case "combine": return "Combine";
                case "drop": return "Drop";
                default: return "What changed?";
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using MediaTrip.Model;

namespace MediaTrip.Status
{
    public enum PlanItemStatus { NotCaptured, Captured, PartiallyCaptured, Dropped, Superseded }
    public enum PlanItemOrigin { Planned, Combined, Split, Added }
    public enum PhotoStatus { NotCaptured, Captured, Dropped }

    public class ResolveOptions
    {
        /// <summary>
        /// When true, a captured video whose photoRefs are not all captured yet reports
        /// PartiallyCaptured instead of Captured. Off, video status ignores photos entirely.
        /// </summary>
        public bool PhotoRefsAffectVideoStatus = true;

        public static readonly ResolveOptions Default = new ResolveOptions();
    }

    /// <summary>
    /// A video as the plan currently stands: a planned video with its amendments applied, or a
    /// virtual item created by a combine, split or add. The planned <see cref="Video"/> itself
    /// is never modified; effective values live here.
    /// </summary>
    public class PlanItem
    {
        public string Id;
        public PlanItemOrigin Origin;
        /// <summary>The shot-list entry; null for virtual (combined/split/added) items.</summary>
        public Video PlannedVideo;

        /// <summary>Planned number (planned and split items). Null for combined and added.</summary>
        public int? Number;
        /// <summary>All planned numbers behind this item (transitively). Empty for added items.</summary>
        public List<int> SourceNumbers = new List<int>();
        /// <summary>0-based part index for split results.</summary>
        public int? SplitIndex;

        public string Title;
        public string OriginalTitle;
        public string BookId;
        public string ChapterId;
        public string OriginalBookId;
        public string OriginalChapterId;
        public List<string> SmeIds = new List<string>();
        public string SmeText;
        public string SceneDescription;
        public List<Node> Notes = new List<Node>();
        public List<string> PhotoRefs = new List<string>();

        /// <summary>Direct sources (combined/split items).</summary>
        public List<string> SourceIds = new List<string>();
        /// <summary>Direct results, if this item was superseded by a combine or split.</summary>
        public List<string> ResultIds = new List<string>();
        /// <summary>Every amendment that names this ID as a target or a result, in list order.</summary>
        public List<Amendment> Amendments = new List<Amendment>();
        public Amendment DropAmendment;
        public Amendment CreatedBy;

        /// <summary>Captures whose planVideoId is exactly this ID.</summary>
        public List<Capture> Captures = new List<Capture>();

        /// <summary>This item's own status. Superseded when a combine/split replaced it.</summary>
        public PlanItemStatus Status;
        /// <summary>Status after following supersession to what was actually shot.</summary>
        public PlanItemStatus EffectiveStatus;
        /// <summary>Leaf items this resolves to (itself when not superseded).</summary>
        public List<PlanItem> ResolvedItems = new List<PlanItem>();

        public int PhotoRefsTotal;
        public int PhotoRefsCaptured;

        public bool IsSuperseded => ResultIds.Count > 0;
        public bool IsDropped => DropAmendment != null;
        public bool VideoCaptured => Captures.Count > 0;
        public bool IsVirtual => PlannedVideo == null;

        /// <summary>"4" for planned, "1+2" for combined, "5a" for split parts, "new" for added.</summary>
        public string DisplayNumber
        {
            get
            {
                switch (Origin)
                {
                    case PlanItemOrigin.Planned: return Number?.ToString() ?? "?";
                    case PlanItemOrigin.Combined: return SourceNumbers.Count > 0 ? string.Join("+", SourceNumbers) : "?";
                    case PlanItemOrigin.Split:
                        var letter = SplitIndex.HasValue && SplitIndex.Value < 26 ? ((char)('a' + SplitIndex.Value)).ToString() : "";
                        return (Number?.ToString() ?? "?") + letter;
                    default: return "new";
                }
            }
        }

        public override string ToString() => $"[{DisplayNumber}] {Title} ({Status}/{EffectiveStatus})";
    }

    /// <summary>A master-list photo with its derived capture status.</summary>
    public class PhotoItem
    {
        public Photo Photo;
        public string Id => Photo.Id;
        public string BookId => Photo.BookId;
        public string ChapterId => Photo.ChapterId;
        public HeroType HeroType => Photo.HeroType;
        /// <summary>Description after any rename amendment targeting the photo.</summary>
        public string Description;
        public PhotoStatus Status;
        public Amendment DropAmendment;
        /// <summary>Video captures that list this photo as captured.</summary>
        public List<Capture> CapturedInCaptures = new List<Capture>();
        /// <summary>Stand-alone photo captures of this photo.</summary>
        public List<PhotoCapture> CapturedInPhotoCaptures = new List<PhotoCapture>();
        public bool IsCaptured => Status == PhotoStatus.Captured;
    }

    /// <summary>The plan with amendments and captures applied. Rebuild after any change (it is cheap).</summary>
    public class ResolvedPlan
    {
        public TripData Data;
        public ResolveOptions Options;

        /// <summary>All items in "as printed" order: planned videos by number, each followed by the
        /// virtual items it spawned; added items at the end.</summary>
        public List<PlanItem> Items = new List<PlanItem>();
        public List<PhotoItem> Photos = new List<PhotoItem>();
        /// <summary>Captures with a planVideoId that matches nothing in the plan.</summary>
        public List<Capture> UnmatchedCaptures = new List<Capture>();
        /// <summary>Captures with no planVideoId at all (unplanned, no add amendment).</summary>
        public List<Capture> UnplannedCaptures = new List<Capture>();
        public List<string> Issues = new List<string>();

        private readonly Dictionary<string, PlanItem> _byId = new Dictionary<string, PlanItem>();
        private readonly Dictionary<string, PhotoItem> _photoById = new Dictionary<string, PhotoItem>();
        private readonly List<PlanItem> _allItems = new List<PlanItem>();

        internal IReadOnlyList<PlanItem> AllRegisteredItems => _allItems;

        internal void Register(PlanItem item)
        {
            _byId[item.Id] = item;
            _allItems.Add(item);
        }

        internal void Register(PhotoItem item)
        {
            _photoById[item.Id] = item;
            Photos.Add(item);
        }

        public PlanItem FindItem(string id) => id != null && _byId.TryGetValue(id, out var i) ? i : null;
        public PhotoItem FindPhoto(string id) => id != null && _photoById.TryGetValue(id, out var p) ? p : null;
        public bool HasItem(string id) => id != null && _byId.ContainsKey(id);

        /// <summary>
        /// What a plan ID actually resolves to: the item itself, or, if it was combined/split,
        /// the leaf result items. Looking up any source of a combine lands on the combined item.
        /// </summary>
        public IReadOnlyList<PlanItem> Resolve(string id)
        {
            var item = FindItem(id);
            return item == null ? (IReadOnlyList<PlanItem>)Array.Empty<PlanItem>() : item.ResolvedItems;
        }

        /// <summary>Captures for what a plan ID resolves to, in day/order sequence.</summary>
        public List<Capture> CapturesFor(string id)
        {
            var seen = new HashSet<string>();
            var result = new List<Capture>();
            foreach (var leaf in Resolve(id))
                foreach (var cap in leaf.Captures)
                    if (seen.Add(cap.Id)) result.Add(cap);
            return result.OrderBy(c => DayIndex(c.DayId)).ThenBy(c => c.CapturedOrder).ToList();
        }

        public IEnumerable<PlanItem> ItemsOfBook(string bookId) => Items.Where(i => i.BookId == bookId);
        public IEnumerable<PlanItem> ItemsOfChapter(string chapterId) => Items.Where(i => i.ChapterId == chapterId);

        internal int DayIndex(string dayId)
        {
            var days = Data.Trip.Days;
            for (int i = 0; i < days.Count; i++) if (days[i].Id == dayId) return i;
            return int.MaxValue;
        }
    }

    /// <summary>
    /// Applies amendments and captures to the immutable shot list and derives every status.
    /// Amendments are applied in list order (the order they were recorded).
    /// </summary>
    public static class PlanResolver
    {
        public static ResolvedPlan Resolve(TripData data, ResolveOptions options = null)
        {
            options = options ?? ResolveOptions.Default;
            var plan = new ResolvedPlan { Data = data, Options = options };

            // 1. Planned videos.
            var planned = data.ShotList.Videos
                .Select((v, i) => (v, i))
                .OrderBy(p => p.v.Number).ThenBy(p => p.i)
                .Select(p => p.v).ToList();
            var virtualByAnchor = new Dictionary<string, List<PlanItem>>(); // anchor planned id -> spawned items
            var added = new List<PlanItem>();

            foreach (var v in planned)
            {
                if (plan.HasItem(v.Id)) { plan.Issues.Add($"Duplicate video id '{v.Id}' in shot list; later entry ignored."); continue; }
                var item = new PlanItem
                {
                    Id = v.Id,
                    Origin = PlanItemOrigin.Planned,
                    PlannedVideo = v,
                    Number = v.Number,
                    SourceNumbers = new List<int> { v.Number },
                    Title = v.Title,
                    OriginalTitle = v.Title,
                    BookId = v.BookId,
                    ChapterId = v.ChapterId,
                    OriginalBookId = v.BookId,
                    OriginalChapterId = v.ChapterId,
                    SmeIds = new List<string>(v.SmeIds ?? new List<string>()),
                    SmeText = v.SmeText,
                    SceneDescription = v.SceneDescription,
                    Notes = v.Notes ?? new List<Node>(),
                    PhotoRefs = new List<string>(v.PhotoRefs ?? new List<string>()),
                };
                plan.Register(item);
            }

            // 2. Photos.
            foreach (var p in data.ShotList.Photos)
            {
                if (plan.FindPhoto(p.Id) != null) { plan.Issues.Add($"Duplicate photo id '{p.Id}' in shot list; later entry ignored."); continue; }
                plan.Register(new PhotoItem { Photo = p, Description = p.Description });
            }

            // 3. Amendments, in recorded order.
            foreach (var am in data.Captures.Amendments)
                ApplyAmendment(plan, am, virtualByAnchor, added);

            // 4. Captures.
            foreach (var cap in data.Captures.Captures)
            {
                if (string.IsNullOrEmpty(cap.PlanVideoId)) { plan.UnplannedCaptures.Add(cap); continue; }
                var item = plan.FindItem(cap.PlanVideoId);
                if (item == null)
                {
                    plan.UnmatchedCaptures.Add(cap);
                    plan.Issues.Add($"Capture '{cap.Id}' references unknown plan item '{cap.PlanVideoId}'.");
                    continue;
                }
                item.Captures.Add(cap);
                foreach (var cp in cap.Photos ?? Enumerable.Empty<CapturePhoto>())
                {
                    if (!cp.Captured || string.IsNullOrEmpty(cp.PhotoId)) continue;
                    var photo = plan.FindPhoto(cp.PhotoId);
                    if (photo == null) { plan.Issues.Add($"Capture '{cap.Id}' references unknown photo '{cp.PhotoId}'."); continue; }
                    photo.CapturedInCaptures.Add(cap);
                }
            }
            foreach (var pc in data.Captures.PhotoCaptures)
            {
                if (string.IsNullOrEmpty(pc.PhotoId)) continue;
                var photo = plan.FindPhoto(pc.PhotoId);
                if (photo == null) { plan.Issues.Add($"Photo capture '{pc.Id}' references unknown photo '{pc.PhotoId}'."); continue; }
                photo.CapturedInPhotoCaptures.Add(pc);
            }

            // 5. Photo status.
            foreach (var photo in plan.Photos)
            {
                if (photo.DropAmendment != null) photo.Status = PhotoStatus.Dropped;
                else if (photo.CapturedInCaptures.Count > 0 || photo.CapturedInPhotoCaptures.Count > 0) photo.Status = PhotoStatus.Captured;
                else photo.Status = PhotoStatus.NotCaptured;
            }

            // 6. Ordering: planned by number, each followed by the virtual items anchored to it.
            var ordered = new List<PlanItem>();
            var emitted = new HashSet<string>();
            foreach (var v in planned)
            {
                var item = plan.FindItem(v.Id);
                if (item == null || !emitted.Add(item.Id)) continue;
                ordered.Add(item);
                if (virtualByAnchor.TryGetValue(item.Id, out var spawned))
                    foreach (var s in spawned)
                        if (emitted.Add(s.Id)) ordered.Add(s);
            }
            foreach (var a in added) if (emitted.Add(a.Id)) ordered.Add(a);
            // Anything registered but not yet emitted (defensive; should not happen).
            foreach (var it in plan.AllRegisteredItems) if (emitted.Add(it.Id)) ordered.Add(it);
            plan.Items = ordered;

            // 7. Own status, then effective status through supersession.
            foreach (var item in plan.Items)
            {
                var refs = item.PhotoRefs.Select(plan.FindPhoto).Where(p => p != null && p.Status != PhotoStatus.Dropped).ToList();
                item.PhotoRefsTotal = refs.Count;
                item.PhotoRefsCaptured = refs.Count(p => p.Status == PhotoStatus.Captured);
                item.Status = OwnStatus(item, options);
            }
            foreach (var item in plan.Items)
            {
                item.ResolvedItems = Leaves(plan, item);
                item.EffectiveStatus = Aggregate(item.ResolvedItems.Select(l => l.Status), item.Status);
            }

            return plan;
        }

        private static void ApplyAmendment(ResolvedPlan plan, Amendment am, Dictionary<string, List<PlanItem>> virtualByAnchor, List<PlanItem> added)
        {
            var targets = am.Targets ?? new List<string>();
            var results = am.Results ?? new List<string>();

            switch (am.Type)
            {
                case AmendmentType.Rename:
                    foreach (var t in targets)
                    {
                        var item = plan.FindItem(t);
                        if (item != null)
                        {
                            if (!string.IsNullOrEmpty(am.NewTitle)) item.Title = am.NewTitle;
                            item.Amendments.Add(am);
                            continue;
                        }
                        var photo = plan.FindPhoto(t);
                        if (photo != null)
                        {
                            if (!string.IsNullOrEmpty(am.NewTitle)) photo.Description = am.NewTitle;
                            continue;
                        }
                        plan.Issues.Add($"Amendment '{am.Id}' (rename) targets unknown item '{t}'.");
                    }
                    break;

                case AmendmentType.Move:
                    foreach (var t in targets)
                    {
                        var item = plan.FindItem(t);
                        if (item == null) { plan.Issues.Add($"Amendment '{am.Id}' (move) targets unknown item '{t}'."); continue; }
                        if (!string.IsNullOrEmpty(am.NewChapterId))
                        {
                            item.ChapterId = am.NewChapterId;
                            var ch = plan.Data.FindChapter(am.NewChapterId);
                            item.BookId = !string.IsNullOrEmpty(am.NewBookId) ? am.NewBookId : (ch?.BookId ?? item.BookId);
                        }
                        else if (!string.IsNullOrEmpty(am.NewBookId))
                        {
                            item.BookId = am.NewBookId;
                        }
                        if (!string.IsNullOrEmpty(am.NewTitle)) item.Title = am.NewTitle;
                        item.Amendments.Add(am);
                    }
                    break;

                case AmendmentType.Drop:
                    foreach (var t in targets)
                    {
                        var item = plan.FindItem(t);
                        if (item != null) { item.DropAmendment = am; item.Amendments.Add(am); continue; }
                        var photo = plan.FindPhoto(t);
                        if (photo != null) { photo.DropAmendment = am; continue; }
                        plan.Issues.Add($"Amendment '{am.Id}' (drop) targets unknown item '{t}'.");
                    }
                    break;

                case AmendmentType.Add:
                    for (int i = 0; i < results.Count; i++)
                    {
                        var id = results[i];
                        if (plan.HasItem(id)) { plan.Issues.Add($"Amendment '{am.Id}' (add) result '{id}' already exists."); continue; }
                        var title = TitleFor(am, i) ?? "(untitled)";
                        var chapter = plan.Data.FindChapter(am.NewChapterId);
                        var item = new PlanItem
                        {
                            Id = id,
                            Origin = PlanItemOrigin.Added,
                            Title = title,
                            OriginalTitle = title,
                            BookId = am.NewBookId ?? chapter?.BookId,
                            ChapterId = am.NewChapterId,
                            OriginalBookId = am.NewBookId ?? chapter?.BookId,
                            OriginalChapterId = am.NewChapterId,
                            CreatedBy = am,
                        };
                        item.Amendments.Add(am);
                        plan.Register(item);
                        added.Add(item);
                    }
                    break;

                case AmendmentType.Combine:
                {
                    var sources = new List<PlanItem>();
                    foreach (var t in targets)
                    {
                        var s = plan.FindItem(t);
                        if (s == null) { plan.Issues.Add($"Amendment '{am.Id}' (combine) targets unknown item '{t}'."); continue; }
                        sources.Add(s);
                    }
                    if (sources.Count == 0 || results.Count == 0)
                    {
                        plan.Issues.Add($"Amendment '{am.Id}' (combine) needs at least one known target and one result.");
                        break;
                    }
                    if (results.Count > 1)
                        plan.Issues.Add($"Amendment '{am.Id}' (combine) has {results.Count} results; a combine normally has one.");

                    for (int i = 0; i < results.Count; i++)
                    {
                        var id = results[i];
                        if (plan.HasItem(id)) { plan.Issues.Add($"Amendment '{am.Id}' (combine) result '{id}' already exists."); continue; }
                        var first = sources[0];
                        var sameChapter = sources.All(s => s.ChapterId == first.ChapterId);
                        var item = new PlanItem
                        {
                            Id = id,
                            Origin = PlanItemOrigin.Combined,
                            Number = null,
                            SourceNumbers = sources.SelectMany(s => s.SourceNumbers).Distinct().OrderBy(n => n).ToList(),
                            Title = TitleFor(am, i) ?? string.Join(" + ", sources.Select(s => s.Title)),
                            OriginalTitle = string.Join(" + ", sources.Select(s => s.OriginalTitle)),
                            BookId = am.NewBookId ?? first.BookId,
                            ChapterId = am.NewChapterId ?? (sameChapter ? first.ChapterId : first.ChapterId),
                            OriginalBookId = first.BookId,
                            OriginalChapterId = first.ChapterId,
                            SmeIds = sources.SelectMany(s => s.SmeIds).Distinct().ToList(),
                            SmeText = JoinNonEmpty(sources.Select(s => s.SmeText), "; "),
                            SceneDescription = JoinNonEmpty(sources.Select(s => s.SceneDescription), "\n"),
                            Notes = sources.SelectMany(s => s.Notes ?? new List<Node>()).ToList(),
                            PhotoRefs = sources.SelectMany(s => s.PhotoRefs).Distinct().ToList(),
                            SourceIds = sources.Select(s => s.Id).ToList(),
                            CreatedBy = am,
                        };
                        item.Amendments.Add(am);
                        plan.Register(item);
                        Anchor(plan, virtualByAnchor, added, item, sources[0]);
                        foreach (var s in sources)
                        {
                            s.ResultIds.Add(id);
                            if (!s.Amendments.Contains(am)) s.Amendments.Add(am);
                        }
                    }
                    break;
                }

                case AmendmentType.Split:
                {
                    if (targets.Count != 1)
                        plan.Issues.Add($"Amendment '{am.Id}' (split) has {targets.Count} targets; a split has exactly one.");
                    var source = targets.Count > 0 ? plan.FindItem(targets[0]) : null;
                    if (source == null || results.Count == 0)
                    {
                        plan.Issues.Add($"Amendment '{am.Id}' (split) needs a known target and at least one result.");
                        break;
                    }
                    for (int i = 0; i < results.Count; i++)
                    {
                        var id = results[i];
                        if (plan.HasItem(id)) { plan.Issues.Add($"Amendment '{am.Id}' (split) result '{id}' already exists."); continue; }
                        var item = new PlanItem
                        {
                            Id = id,
                            Origin = PlanItemOrigin.Split,
                            Number = source.Number,
                            SourceNumbers = new List<int>(source.SourceNumbers),
                            SplitIndex = i,
                            Title = TitleFor(am, i) ?? $"{source.Title} (part {i + 1})",
                            OriginalTitle = source.OriginalTitle,
                            BookId = am.NewBookId ?? source.BookId,
                            ChapterId = am.NewChapterId ?? source.ChapterId,
                            OriginalBookId = source.BookId,
                            OriginalChapterId = source.ChapterId,
                            SmeIds = new List<string>(source.SmeIds),
                            SmeText = source.SmeText,
                            SceneDescription = source.SceneDescription,
                            Notes = source.Notes,
                            PhotoRefs = new List<string>(source.PhotoRefs),
                            SourceIds = new List<string> { source.Id },
                            CreatedBy = am,
                        };
                        item.Amendments.Add(am);
                        plan.Register(item);
                        Anchor(plan, virtualByAnchor, added, item, source);
                        source.ResultIds.Add(id);
                    }
                    if (!source.Amendments.Contains(am)) source.Amendments.Add(am);
                    break;
                }

                default:
                    plan.Issues.Add($"Amendment '{am.Id}' has unknown type {am.Type}.");
                    break;
            }
        }

        /// <summary>Place a virtual item after the planned video it descends from (first source, transitively).</summary>
        private static void Anchor(ResolvedPlan plan, Dictionary<string, List<PlanItem>> virtualByAnchor, List<PlanItem> added, PlanItem item, PlanItem firstSource)
        {
            var anchor = firstSource;
            var guard = new HashSet<string>();
            while (anchor != null && anchor.Origin != PlanItemOrigin.Planned && guard.Add(anchor.Id))
            {
                if (anchor.Origin == PlanItemOrigin.Added) { added.Add(item); return; }
                anchor = anchor.SourceIds.Count > 0 ? plan.FindItem(anchor.SourceIds[0]) : null;
            }
            if (anchor == null) { added.Add(item); return; }
            if (!virtualByAnchor.TryGetValue(anchor.Id, out var list)) virtualByAnchor[anchor.Id] = list = new List<PlanItem>();
            list.Add(item);
        }

        private static string TitleFor(Amendment am, int index)
        {
            if (am.NewTitles != null && index < am.NewTitles.Count && !string.IsNullOrEmpty(am.NewTitles[index]))
                return am.NewTitles[index];
            return string.IsNullOrEmpty(am.NewTitle) ? null : am.NewTitle;
        }

        private static string JoinNonEmpty(IEnumerable<string> parts, string separator)
        {
            var list = parts.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
            return list.Count == 0 ? null : string.Join(separator, list);
        }

        private static PlanItemStatus OwnStatus(PlanItem item, ResolveOptions options)
        {
            if (item.IsSuperseded) return PlanItemStatus.Superseded;
            if (item.IsDropped) return PlanItemStatus.Dropped;
            if (!item.VideoCaptured) return PlanItemStatus.NotCaptured;
            if (options.PhotoRefsAffectVideoStatus && item.PhotoRefsTotal > 0 && item.PhotoRefsCaptured < item.PhotoRefsTotal)
                return PlanItemStatus.PartiallyCaptured;
            return PlanItemStatus.Captured;
        }

        /// <summary>Follow ResultIds to the leaves. Cycle-safe.</summary>
        private static List<PlanItem> Leaves(ResolvedPlan plan, PlanItem item)
        {
            var leaves = new List<PlanItem>();
            var seen = new HashSet<string>();
            var stack = new Stack<PlanItem>();
            stack.Push(item);
            while (stack.Count > 0)
            {
                var cur = stack.Pop();
                if (!seen.Add(cur.Id)) continue;
                if (cur.ResultIds.Count == 0) { leaves.Add(cur); continue; }
                // Push in reverse so results come out in recorded order.
                for (int i = cur.ResultIds.Count - 1; i >= 0; i--)
                {
                    var r = plan.FindItem(cur.ResultIds[i]);
                    if (r != null) stack.Push(r);
                }
            }
            if (leaves.Count == 0) leaves.Add(item);
            return leaves;
        }

        /// <summary>Roll leaf statuses up: all captured, all dropped, none captured, or a mix.</summary>
        public static PlanItemStatus Aggregate(IEnumerable<PlanItemStatus> statuses, PlanItemStatus fallback)
        {
            int captured = 0, partial = 0, notCaptured = 0, dropped = 0, total = 0;
            foreach (var s in statuses)
            {
                total++;
                switch (s)
                {
                    case PlanItemStatus.Captured: captured++; break;
                    case PlanItemStatus.PartiallyCaptured: partial++; break;
                    case PlanItemStatus.Dropped: dropped++; break;
                    case PlanItemStatus.Superseded: // a leaf is never superseded; treat as not captured
                    case PlanItemStatus.NotCaptured: notCaptured++; break;
                }
            }
            if (total == 0) return fallback;
            if (captured == total) return PlanItemStatus.Captured;
            if (dropped == total) return PlanItemStatus.Dropped;
            if (captured + partial == 0) return PlanItemStatus.NotCaptured;
            return PlanItemStatus.PartiallyCaptured;
        }
    }
}

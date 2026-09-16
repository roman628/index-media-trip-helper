using System;
using System.Collections.Generic;
using System.Linq;
using MediaTrip.Authoring;
using MediaTrip.Model;
using MediaTrip.Persistence;
using MediaTrip.Query;
using MediaTrip.Search;
using MediaTrip.Status;

namespace MediaTrip.Session
{
    /// <summary>
    /// An open trip: the data, per-document dirty tracking, the debounced autosaver, and a
    /// cached resolved plan / query API that is rebuilt after every mutation.
    ///
    /// All edits should go through this class, either via the typed helpers (AddCapture,
    /// Combine, ...) or via <see cref="Edit"/> for anything else. Both mark the right documents
    /// dirty; the autosaver writes them shortly after. Nothing here ever edits a planned
    /// shot-list entry to record a capture.
    /// </summary>
    public sealed class TripSession : IDisposable
    {
        public TripData Data { get; }
        public Autosaver Autosaver { get; }
        public ResolveOptions ResolveOptions { get; set; } = ResolveOptions.Default;
        public SearchOptions SearchOptions { get; set; } = SearchOptions.Default;

        /// <summary>Increments on every mutation. UIs can compare it to know if they are stale.</summary>
        public int Version { get; private set; }

        /// <summary>Raised after any mutation (before the save happens).</summary>
        public event Action Changed;

        private readonly HashSet<DocumentKind> _dirtyDocs = new HashSet<DocumentKind>();
        private readonly HashSet<string> _dirtyOutlineBooks = new HashSet<string>();
        private readonly HashSet<string> _deletedOutlineFiles = new HashSet<string>();
        private ResolvedPlan _plan;
        private TripQueries _queries;
        private TripSearch _search;
        private ShotListEditor _planEditor;
        private OutlineEditor _outlineEditor;
        private PeopleEditor _peopleEditor;

        public TripSession(TripData data, Func<double> clock = null)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));
            clock = clock ?? DefaultClock;
            Autosaver = new Autosaver(clock, SaveDirty);
        }

        private static double DefaultClock() => UnityEngine.Time.realtimeSinceStartupAsDouble;

        /// <summary>Open a trip folder. Saves go back to the same folder.</summary>
        public static TripSession Open(string folder, Func<double> clock = null) =>
            new TripSession(TripLoader.Load(folder), clock);

        public static TripSession OpenFromLibrary(string tripId, Func<double> clock = null) =>
            Open(TripPaths.TripFolder(tripId), clock);

        // ------------------------------------------------------------------
        // Derived views (cached until the next mutation)
        // ------------------------------------------------------------------

        public ResolvedPlan Plan => _plan ?? (_plan = PlanResolver.Resolve(Data, ResolveOptions));
        public TripQueries Queries => _queries ?? (_queries = new TripQueries(Data, Plan));
        public TripSearch Search => _search ?? (_search = new TripSearch(Data, Plan, SearchOptions));

        // ------------------------------------------------------------------
        // Authoring editors (structural edits to the plan documents)
        // ------------------------------------------------------------------

        /// <summary>Books, chapters, videos, master photos, numbering.</summary>
        public ShotListEditor PlanEditor => _planEditor ?? (_planEditor = new ShotListEditor(this));
        /// <summary>Outline chapters, sections, notes, bullets.</summary>
        public OutlineEditor Outline => _outlineEditor ?? (_outlineEditor = new OutlineEditor(this));
        /// <summary>The people registry.</summary>
        public PeopleEditor People => _peopleEditor ?? (_peopleEditor = new PeopleEditor(this));

        /// <summary>Edit trip identity (client, program, phase, location): the trip's "name".</summary>
        public void UpdateIdentity(Action<TripIdentity> edit)
        {
            if (Data.Trip.Identity == null) Data.Trip.Identity = new TripIdentity();
            edit(Data.Trip.Identity);
            MarkDirty(DocumentKind.Trip);
        }

        public void UpdateDates(Action<TripDates> edit)
        {
            if (Data.Trip.Dates == null) Data.Trip.Dates = new TripDates();
            edit(Data.Trip.Dates);
            MarkDirty(DocumentKind.Trip);
        }

        /// <summary>Drop a book's outline document; its file is deleted on the next save.</summary>
        public void RemoveOutline(string bookId)
        {
            if (!Data.Outlines.Remove(bookId)) return;
            if (Data.OutlineFileNames.TryGetValue(bookId, out var file))
            {
                _deletedOutlineFiles.Add(file);
                Data.OutlineFileNames.Remove(bookId);
            }
            _dirtyOutlineBooks.Remove(bookId);
            Invalidate();
            Autosaver.MarkDirty();
        }

        // ------------------------------------------------------------------
        // Dirty tracking and saving
        // ------------------------------------------------------------------

        public bool IsDirty => Autosaver.IsDirty;

        /// <summary>Mark a document changed. Use DocumentKind.Outline with a bookId for outlines.</summary>
        public void MarkDirty(DocumentKind kind, string outlineBookId = null)
        {
            if (kind == DocumentKind.Outline)
            {
                if (outlineBookId == null) foreach (var k in Data.Outlines.Keys) _dirtyOutlineBooks.Add(k);
                else _dirtyOutlineBooks.Add(outlineBookId);
            }
            else _dirtyDocs.Add(kind);
            Invalidate();
            Autosaver.MarkDirty();
        }

        public void MarkAllDirty()
        {
            _dirtyDocs.Add(DocumentKind.Trip);
            _dirtyDocs.Add(DocumentKind.ShotList);
            _dirtyDocs.Add(DocumentKind.Captures);
            foreach (var k in Data.Outlines.Keys) _dirtyOutlineBooks.Add(k);
            Invalidate();
            Autosaver.MarkDirty();
        }

        /// <summary>Arbitrary edit. Marks every document dirty (cheap; the files are small).</summary>
        public void Edit(Action<TripData> edit)
        {
            edit(Data);
            MarkAllDirty();
        }

        /// <summary>Edit scoped to one document, so only that file is rewritten.</summary>
        public void Edit(DocumentKind kind, Action<TripData> edit, string outlineBookId = null)
        {
            edit(Data);
            MarkDirty(kind, outlineBookId);
        }

        /// <summary>Write everything dirty now (also what the autosaver calls).</summary>
        public void SaveNow() => Autosaver.FlushNow();

        /// <summary>Write every document regardless of dirty state.</summary>
        public void SaveAll()
        {
            TripSaver.SaveAll(Data);
            _dirtyDocs.Clear();
            _dirtyOutlineBooks.Clear();
        }

        private void SaveDirty()
        {
            if (_dirtyDocs.Contains(DocumentKind.Trip)) TripSaver.SaveTrip(Data);
            if (_dirtyDocs.Contains(DocumentKind.ShotList)) TripSaver.SaveShotList(Data);
            if (_dirtyDocs.Contains(DocumentKind.Captures)) TripSaver.SaveCaptures(Data);
            foreach (var bookId in _dirtyOutlineBooks.ToList())
                if (Data.Outlines.ContainsKey(bookId)) TripSaver.SaveOutline(Data, bookId);
            foreach (var file in _deletedOutlineFiles.ToList())
                if (!Data.OutlineFileNames.ContainsValue(file)) TripSaver.DeleteOutlineFile(Data, file);
            _dirtyDocs.Clear();
            _dirtyOutlineBooks.Clear();
            _deletedOutlineFiles.Clear();
        }

        private void Invalidate()
        {
            _plan = null;
            _queries = null;
            _search = null;
            Version++;
            Changed?.Invoke();
        }

        public void Dispose() => Autosaver.FlushNow();

        // ------------------------------------------------------------------
        // Typed mutations: captures
        // ------------------------------------------------------------------

        private static string Now() => DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");

        /// <summary>Add a capture. Fills id and capturedOrder if missing, and plannedNumber from the plan item.</summary>
        public Capture AddCapture(Capture capture)
        {
            if (capture == null) throw new ArgumentNullException(nameof(capture));
            if (string.IsNullOrEmpty(capture.Id)) capture.Id = Ids.New("cap");
            if (capture.CapturedOrder <= 0) capture.CapturedOrder = Queries.NextCapturedOrder(capture.DayId);
            var item = Plan.FindItem(capture.PlanVideoId);
            if (item != null)
            {
                if (capture.PlannedNumber == null && item.Number.HasValue) capture.PlannedNumber = item.Number;
                if (string.IsNullOrEmpty(capture.Title)) capture.Title = item.Title;
                if (string.IsNullOrEmpty(capture.BookId)) capture.BookId = item.BookId;
                if (string.IsNullOrEmpty(capture.ChapterId)) capture.ChapterId = item.ChapterId;
            }
            Data.Captures.Captures.Add(capture);
            MarkDirty(DocumentKind.Captures);
            return capture;
        }

        /// <summary>
        /// Quick check-off: record that a plan item was shot today with no other detail yet.
        /// If the ID is superseded, the capture goes on what it resolves to (one leaf only;
        /// throws if ambiguous so the UI can ask which part).
        /// </summary>
        public Capture CheckOff(string planItemId, string dayId)
        {
            var leaves = Plan.Resolve(planItemId);
            if (leaves.Count == 0) throw new ArgumentException("Unknown plan item " + planItemId, nameof(planItemId));
            if (leaves.Count > 1) throw new InvalidOperationException(planItemId + " was split; pick one of: " + string.Join(", ", leaves.Select(l => l.Id)));
            var item = leaves[0];
            return AddCapture(new Capture { DayId = dayId, PlanVideoId = item.Id, Title = item.Title, BookId = item.BookId, ChapterId = item.ChapterId });
        }

        public PhotoCapture AddPhotoCapture(PhotoCapture pc)
        {
            if (pc == null) throw new ArgumentNullException(nameof(pc));
            if (string.IsNullOrEmpty(pc.Id)) pc.Id = Ids.New("pc");
            if (pc.CapturedOrder <= 0) pc.CapturedOrder = Queries.NextCapturedOrder(pc.DayId);
            Data.Captures.PhotoCaptures.Add(pc);
            MarkDirty(DocumentKind.Captures);
            return pc;
        }

        public void RemoveCapture(string captureId)
        {
            Data.Captures.Captures.RemoveAll(c => c.Id == captureId);
            Data.Captures.OutlineAssignments.RemoveAll(a => a.MediaRef != null && a.MediaRef.Kind == MediaRefKind.Capture && a.MediaRef.Id == captureId);
            MarkDirty(DocumentKind.Captures);
        }

        /// <summary>Undo a check-off: remove every capture of what the plan item resolves to. Returns how many were removed.</summary>
        public int Uncheck(string planItemId)
        {
            var ids = new HashSet<string>(Plan.Resolve(planItemId).Select(i => i.Id)) { planItemId };
            var removed = Data.Captures.Captures.Where(c => c.PlanVideoId != null && ids.Contains(c.PlanVideoId)).Select(c => c.Id).ToList();
            foreach (var id in removed) RemoveCapture(id);
            if (removed.Count == 0) MarkDirty(DocumentKind.Captures);
            return removed.Count;
        }

        public void UpdateCapture(string captureId, Action<Capture> edit)
        {
            var c = Data.FindCapture(captureId) ?? throw new KeyNotFoundException("No capture " + captureId);
            var keepId = c.Id;
            edit(c);
            c.Id = keepId;
            MarkDirty(DocumentKind.Captures);
        }

        /// <summary>
        /// Flip a master-list photo's captured state: if a video capture lists it, toggle that
        /// entry; else add or remove a stand-alone photo capture (additional photography) on the day.
        /// Returns the new captured state.
        /// </summary>
        public bool TogglePhotoCaptured(string photoId, string dayId)
        {
            foreach (var c in Data.Captures.Captures)
            {
                var entry = c.Photos?.FirstOrDefault(p => p.PhotoId == photoId);
                if (entry != null)
                {
                    entry.Captured = !entry.Captured;
                    MarkDirty(DocumentKind.Captures);
                    return entry.Captured;
                }
            }
            var pc = Data.Captures.PhotoCaptures.FirstOrDefault(p => p.PhotoId == photoId);
            if (pc != null)
            {
                RemovePhotoCapture(pc.Id);
                return false;
            }
            AddPhotoCapture(new PhotoCapture { DayId = dayId, PhotoId = photoId, Section = PhotoCaptureSection.AdditionalPhotography });
            return true;
        }

        public void RemovePhotoCapture(string photoCaptureId)
        {
            Data.Captures.PhotoCaptures.RemoveAll(c => c.Id == photoCaptureId);
            Data.Captures.OutlineAssignments.RemoveAll(a => a.MediaRef != null && a.MediaRef.Kind == MediaRefKind.PhotoCapture && a.MediaRef.Id == photoCaptureId);
            MarkDirty(DocumentKind.Captures);
        }

        public void UpdateAmendment(string amendmentId, Action<Amendment> edit)
        {
            var a = Data.FindAmendment(amendmentId) ?? throw new KeyNotFoundException("No amendment " + amendmentId);
            var keepId = a.Id; var keepType = a.Type;
            edit(a);
            a.Id = keepId; a.Type = keepType;
            // Captures of an amendment result follow its title/chapter (the summary says what was shot).
            foreach (var c in Data.Captures.Captures.Where(c => c.PlanVideoId != null && (a.Results ?? new List<string>()).Contains(c.PlanVideoId)))
            {
                if (!string.IsNullOrEmpty(a.NewTitle)) c.Title = a.NewTitle;
                if (a.Type == AmendmentType.Add)
                {
                    if (a.NewChapterId != null) c.ChapterId = a.NewChapterId;
                    if (a.NewBookId != null) c.BookId = a.NewBookId;
                }
            }
            MarkDirty(DocumentKind.Captures);
        }

        /// <summary>
        /// Remove an amendment and put things back: captures of a combined result go back onto
        /// the first source with its planned title and number; a rename's captures get the
        /// planned title back; an unplanned add takes its captures (and their assignments) with it.
        /// </summary>
        public void UndoAmendment(string amendmentId)
        {
            var a = Data.FindAmendment(amendmentId);
            if (a == null) return;
            var results = a.Results ?? new List<string>();
            var targets = a.Targets ?? new List<string>();
            switch (a.Type)
            {
                case AmendmentType.Combine:
                case AmendmentType.Split:
                {
                    var first = targets.Count > 0 ? Data.FindVideo(targets[0]) : null;
                    foreach (var c in Data.Captures.Captures.Where(c => c.PlanVideoId != null && results.Contains(c.PlanVideoId)).ToList())
                    {
                        if (first == null) { RemoveCapture(c.Id); continue; }
                        c.PlanVideoId = first.Id;
                        c.Title = first.Title;
                        c.PlannedNumber = first.Number;
                        c.BookId = first.BookId;
                        c.ChapterId = first.ChapterId;
                    }
                    break;
                }
                case AmendmentType.Rename:
                {
                    var v = targets.Count > 0 ? Data.FindVideo(targets[0]) : null;
                    if (v != null)
                        foreach (var c in Data.Captures.Captures.Where(c => c.PlanVideoId == v.Id)) c.Title = v.Title;
                    break;
                }
                case AmendmentType.Add:
                {
                    foreach (var c in Data.Captures.Captures.Where(c => c.PlanVideoId != null && results.Contains(c.PlanVideoId)).ToList())
                        RemoveCapture(c.Id);
                    break;
                }
            }
            Data.Captures.Amendments.Remove(a);
            MarkDirty(DocumentKind.Captures);
        }

        // ------------------------------------------------------------------
        // Typed mutations: amendments
        // ------------------------------------------------------------------

        public Amendment AddAmendment(Amendment am)
        {
            if (am == null) throw new ArgumentNullException(nameof(am));
            if (string.IsNullOrEmpty(am.Id)) am.Id = Ids.New("am");
            if (string.IsNullOrEmpty(am.At)) am.At = Now();
            Data.Captures.Amendments.Add(am);
            MarkDirty(DocumentKind.Captures);
            return am;
        }

        public Amendment Rename(string planItemId, string newTitle, string reason = null) =>
            AddAmendment(new Amendment
            {
                Type = AmendmentType.Rename, Reason = reason,
                Targets = new List<string> { planItemId }, Results = new List<string> { planItemId },
                NewTitle = newTitle,
            });

        public Amendment Move(string planItemId, string newBookId, string newChapterId, string reason = null) =>
            AddAmendment(new Amendment
            {
                Type = AmendmentType.Move, Reason = reason,
                Targets = new List<string> { planItemId }, Results = new List<string> { planItemId },
                NewBookId = newBookId, NewChapterId = newChapterId,
            });

        public Amendment Drop(string planItemId, string reason = null) =>
            AddAmendment(new Amendment
            {
                Type = AmendmentType.Drop, Reason = reason,
                Targets = new List<string> { planItemId }, Results = new List<string>(),
            });

        /// <summary>Combine several plan items into one new item. Returns the amendment; the new item ID is Results[0].</summary>
        public Amendment Combine(IEnumerable<string> planItemIds, string newTitle, string reason = null)
        {
            var targets = planItemIds?.ToList() ?? new List<string>();
            if (targets.Count < 2) throw new ArgumentException("A combine needs at least two items.", nameof(planItemIds));
            return AddAmendment(new Amendment
            {
                Type = AmendmentType.Combine, Reason = reason,
                Targets = targets, Results = new List<string> { Ids.New("vc") },
                NewTitle = newTitle,
            });
        }

        /// <summary>Split one plan item into several. Returns the amendment; new item IDs are in Results.</summary>
        public Amendment Split(string planItemId, IList<string> newTitles, string reason = null)
        {
            if (newTitles == null || newTitles.Count < 2) throw new ArgumentException("A split needs at least two titles.", nameof(newTitles));
            return AddAmendment(new Amendment
            {
                Type = AmendmentType.Split, Reason = reason,
                Targets = new List<string> { planItemId },
                Results = newTitles.Select(_ => Ids.New("vs")).ToList(),
                NewTitles = newTitles.ToList(),
            });
        }

        /// <summary>Add an unplanned item to the plan. Returns the amendment; the new item ID is Results[0].</summary>
        public Amendment AddUnplanned(string title, string bookId, string chapterId, string reason = null) =>
            AddAmendment(new Amendment
            {
                Type = AmendmentType.Add, Reason = reason,
                Targets = new List<string>(), Results = new List<string> { Ids.New("va") },
                NewTitle = title, NewBookId = bookId, NewChapterId = chapterId,
            });

        // ------------------------------------------------------------------
        // Typed mutations: outline assignments
        // ------------------------------------------------------------------

        public OutlineAssignment Assign(MediaRef media, string bookId, string chapterId, string sectionId = null, string nodeId = null,
            AssignmentSource source = AssignmentSource.Manual, double? confidence = null, bool confirmed = true)
        {
            var a = new OutlineAssignment
            {
                Id = Ids.New("oa"), MediaRef = media, BookId = bookId, ChapterId = chapterId, SectionId = sectionId, NodeId = nodeId,
                Source = source, Confidence = source == AssignmentSource.Auto ? confidence : null, Confirmed = confirmed,
            };
            Data.Captures.OutlineAssignments.Add(a);
            MarkDirty(DocumentKind.Captures);
            return a;
        }

        /// <summary>Record a fuzzy suggestion as an unconfirmed auto assignment (one tap to confirm later).</summary>
        public OutlineAssignment AssignSuggestion(MediaRef media, OutlineSuggestion s) =>
            Assign(media, s.BookId, s.ChapterId, s.SectionId, null, AssignmentSource.Auto, s.Score, confirmed: false);

        public void ConfirmAssignment(string assignmentId)
        {
            var a = Data.Captures.OutlineAssignments.FirstOrDefault(x => x.Id == assignmentId);
            if (a == null) return;
            a.Confirmed = true;
            MarkDirty(DocumentKind.Captures);
        }

        public void RemoveAssignment(string assignmentId)
        {
            Data.Captures.OutlineAssignments.RemoveAll(x => x.Id == assignmentId);
            MarkDirty(DocumentKind.Captures);
        }

        // ------------------------------------------------------------------
        // Typed mutations: people, trip header
        // ------------------------------------------------------------------

        /// <summary>
        /// Name fields accept any name. Returns the registry person with that exact name, or adds
        /// one (splitting on the last space into first/last) and returns it.
        /// </summary>
        public Person FindOrAddPerson(string name, Org org = Org.Other, PersonRole? role = null, string title = null)
        {
            var existing = Search.FindPersonByName(name);
            if (existing != null)
            {
                if (role.HasValue && !existing.HasRole(role.Value))
                {
                    existing.Roles.Add(role.Value);
                    MarkDirty(DocumentKind.Trip);
                }
                return existing;
            }
            var trimmed = (name ?? "").Trim();
            if (trimmed.Length == 0) throw new ArgumentException("Name is empty.", nameof(name));
            var space = trimmed.LastIndexOf(' ');
            var person = new Person
            {
                Id = Ids.New("p"),
                FirstName = space > 0 ? trimmed.Substring(0, space) : trimmed,
                LastName = space > 0 ? trimmed.Substring(space + 1) : "",
                Org = org,
                Title = title,
            };
            if (role.HasValue) person.Roles.Add(role.Value);
            Data.Trip.People.Add(person);
            MarkDirty(DocumentKind.Trip);
            return person;
        }

        public Day AddDay(string isoDate, string label = null)
        {
            var day = new Day { Id = Ids.New("d"), Date = isoDate, Label = label ?? ("Day " + (Data.Trip.Days.Count + 1)) };
            Data.Trip.Days.Add(day);
            MarkDirty(DocumentKind.Trip);
            return day;
        }

        /// <summary>Remove a day. Refused while captures are attached to it.</summary>
        public void RemoveDay(string dayId)
        {
            var refs = ReferenceFinder.Find(Data, dayId);
            if (refs.Count > 0) throw new PlanEditBlockedException(dayId, refs, "Move or delete those captures first.", "Removing day");
            Data.Trip.Days.RemoveAll(d => d.Id == dayId);
            MarkDirty(DocumentKind.Trip);
        }

        /// <summary>Edit trip.json fields (identity, dates, weather, logistics, actions, books, days) in one go.</summary>
        public void EditTrip(Action<TripDocument> edit)
        {
            edit(Data.Trip);
            MarkDirty(DocumentKind.Trip);
        }

        /// <summary>
        /// A name typed in the field (capture person with no personId) becomes a registry person,
        /// and every capture entry with that name is linked to it. Returns the person.
        /// </summary>
        public Person AdoptTypedPerson(string name, string title = null, Org org = Org.Client, PersonRole role = PersonRole.Sme)
        {
            var person = FindOrAddPerson(name, org, role, title);
            var norm = FuzzyMatcher.Normalize(name);
            bool touched = false;
            foreach (var c in Data.Captures.Captures)
                foreach (var cp in c.People ?? Enumerable.Empty<CapturePerson>())
                    if (cp.PersonId == null && FuzzyMatcher.Normalize(cp.Name) == norm) { cp.PersonId = person.Id; touched = true; }
            if (touched) MarkDirty(DocumentKind.Captures);
            return person;
        }

        /// <summary>Names used in captures that are not in the registry (distinct, with the title first seen).</summary>
        public List<(string name, string title, Capture capture)> TypedButUnregisteredPeople()
        {
            var seen = new HashSet<string>();
            var registry = new HashSet<string>(Data.Trip.People.Select(p => FuzzyMatcher.Normalize(p.FullName)));
            var result = new List<(string, string, Capture)>();
            foreach (var c in Data.Captures.Captures)
                foreach (var cp in c.People ?? Enumerable.Empty<CapturePerson>())
                {
                    if (cp.PersonId != null || string.IsNullOrWhiteSpace(cp.Name)) continue;
                    var norm = FuzzyMatcher.Normalize(cp.Name);
                    if (registry.Contains(norm) || !seen.Add(norm)) continue;
                    result.Add((cp.Name, cp.Title, c));
                }
            return result;
        }

        public ActionItem AddAction(string text)
        {
            var a = new ActionItem { Id = Ids.New("a"), Text = text };
            Data.Trip.Actions.Add(a);
            MarkDirty(DocumentKind.Trip);
            return a;
        }

        public void SetActionDone(string actionId, bool done)
        {
            var a = Data.Trip.Actions.FirstOrDefault(x => x.Id == actionId);
            if (a == null || a.Done == done) return;
            a.Done = done;
            MarkDirty(DocumentKind.Trip);
        }

        // ------------------------------------------------------------------
        // Typed mutations: outlines (editable documents)
        // ------------------------------------------------------------------

        /// <summary>Create an empty outline for a book that has none yet.</summary>
        public OutlineDocument CreateOutline(string bookId)
        {
            if (Data.Outlines.TryGetValue(bookId, out var existing)) return existing;
            var book = Data.FindBook(bookId);
            var outline = new OutlineDocument { TripId = Data.TripId, BookId = bookId, BookTitle = book?.Name };
            foreach (var ch in Data.ChaptersOf(bookId))
                outline.Chapters.Add(new OutlineChapter { Id = ch.Id, Number = ch.Number, Name = ch.Name, Notes = "" });
            Data.Outlines[bookId] = outline;
            MarkDirty(DocumentKind.Outline, bookId);
            return outline;
        }

        public void EditOutline(string bookId, Action<OutlineDocument> edit)
        {
            var outline = Data.FindOutline(bookId) ?? CreateOutline(bookId);
            edit(outline);
            MarkDirty(DocumentKind.Outline, bookId);
        }

    }
}

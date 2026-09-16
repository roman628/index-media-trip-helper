using System;
using System.Collections.Generic;
using System.Linq;
using MediaTrip.Model;
using MediaTrip.Search;
using MediaTrip.Session;
using MediaTrip.Status;

namespace MediaTrip.UI.ViewModels
{
    /// <summary>
    /// The capture-entry form's state and its save logic, independent of the UI so it can be
    /// tested. Picking a shot-list match fills book/chapter/photos and asks the outline
    /// suggester; saving writes a capture (and an "add" amendment for an unplanned title),
    /// an unconfirmed outline assignment if one was suggested, and new people for typed names.
    /// </summary>
    public sealed class CaptureDraft
    {
        public string Title = "";
        /// <summary>Plan item id the capture is for; null for an unplanned title.</summary>
        public string ItemId;
        public string BookId;
        public string ChapterId;
        public string Description = "";
        public string Scene = "";
        public string Hpi = "";
        public string Keywords = "";
        public string BRoll = "N/A";
        public int CameraCount = 1;
        public List<CapturePerson> People = new List<CapturePerson>();
        public string PersonName = "";
        public string PersonTitle = "";
        public string Location = "";
        public string Notes = "";
        /// <summary>photoId -> captured, for the picked item's photoRefs.</summary>
        public Dictionary<string, bool> Photos = new Dictionary<string, bool>();
        public string ExtraPhoto = "";
        public bool More;
        public OutlineSuggestion Outline;
        public bool OutlineConfirmed;
        public bool ShowSuggestions = true;
        /// <summary>Existing capture being edited (null for a new one).</summary>
        public string EditingCaptureId;

        public bool CanSave => !string.IsNullOrWhiteSpace(Title);

        public static CaptureDraft ForItem(TripSession s, string planItemId)
        {
            var d = new CaptureDraft();
            d.Pick(s, planItemId);
            return d;
        }

        /// <summary>Start from an existing capture (edit).</summary>
        public static CaptureDraft ForCapture(TripSession s, Capture c)
        {
            var d = new CaptureDraft
            {
                EditingCaptureId = c.Id, Title = c.Title ?? "", ItemId = c.PlanVideoId, BookId = c.BookId, ChapterId = c.ChapterId,
                Description = c.Description ?? "", Scene = c.SceneDescription ?? "", Hpi = c.HpiToolSuggestion ?? "",
                Keywords = string.Join(", ", c.Keywords ?? new List<string>()), BRoll = c.BRoll ?? "N/A", CameraCount = c.CameraCount ?? 1,
                People = (c.People ?? new List<CapturePerson>()).Select(p => new CapturePerson { PersonId = p.PersonId, Name = p.Name, Title = p.Title }).ToList(),
                Location = c.Location ?? "", Notes = c.Notes ?? "", ShowSuggestions = false,
                More = !string.IsNullOrEmpty(c.SceneDescription) || !string.IsNullOrEmpty(c.HpiToolSuggestion) || (c.People?.Count ?? 0) > 0,
            };
            var item = s.Plan.FindItem(c.PlanVideoId);
            foreach (var pid in item?.PhotoRefs ?? new List<string>()) d.Photos[pid] = false;
            foreach (var cp in c.Photos ?? new List<CapturePhoto>())
            {
                if (cp.PhotoId != null) d.Photos[cp.PhotoId] = cp.Captured;
                else if (!string.IsNullOrEmpty(cp.Text)) d.ExtraPhoto = cp.Text;
            }
            var existing = s.Data.Captures.OutlineAssignments.FirstOrDefault(a => a.MediaRef?.Kind == MediaRefKind.Capture && a.MediaRef.Id == c.Id);
            if (existing != null)
            {
                var sec = s.Data.FindOutlineSection(existing.BookId, existing.SectionId);
                var ch = s.Data.FindOutline(existing.BookId)?.Chapters.FirstOrDefault(x => x.Id == existing.ChapterId);
                d.Outline = new OutlineSuggestion { BookId = existing.BookId, ChapterId = existing.ChapterId, SectionId = existing.SectionId, Chapter = ch, Section = sec, Score = existing.Confidence ?? 1, BaseScore = existing.Confidence ?? 1 };
                d.OutlineConfirmed = existing.Confirmed;
            }
            return d;
        }

        /// <summary>Title typed: forget the previous pick.</summary>
        public void SetTitle(string title)
        {
            Title = title ?? "";
            ItemId = null; BookId = null; ChapterId = null;
            Photos.Clear();
            Outline = null;
            OutlineConfirmed = false;
            ShowSuggestions = true;
        }

        /// <summary>Pick a shot-list match. A superseded id resolves to what was actually shot (first leaf).</summary>
        public void Pick(TripSession s, string planItemId)
        {
            var leaves = s.Plan.Resolve(planItemId);
            var eff = leaves.Count > 0 ? leaves[0] : s.Plan.FindItem(planItemId);
            if (eff == null) return;
            ItemId = eff.Id;
            Title = eff.Title;
            BookId = eff.BookId;
            ChapterId = eff.ChapterId;
            Photos.Clear();
            foreach (var pid in eff.PhotoRefs ?? new List<string>()) Photos[pid] = false;
            var suggestions = s.Search.SuggestOutlineFor(eff, 1);
            Outline = suggestions.Count > 0 ? suggestions[0] : null;
            OutlineConfirmed = false;
            ShowSuggestions = false;
        }

        public List<ScoredMatch<PlanItem>> Suggestions(TripSession s, int max = 6) =>
            string.IsNullOrWhiteSpace(Title) ? new List<ScoredMatch<PlanItem>>() : s.Search.SearchShotList(Title, max);

        public void AddPerson(string name, string title, string personId = null)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            People.Add(new CapturePerson { PersonId = personId, Name = name.Trim(), Title = (title ?? "").Trim() });
            PersonName = "";
            PersonTitle = "";
        }

        /// <summary>Write the capture. Returns it. Unplanned titles get an "add" amendment first.</summary>
        public Capture Save(TripSession s, string dayId)
        {
            if (!CanSave) throw new InvalidOperationException("A title is required.");
            var planId = ItemId;
            var item = planId != null ? s.Plan.FindItem(planId) : null;
            var bookId = BookId ?? item?.BookId ?? s.Data.Trip.Books.FirstOrDefault()?.Id;
            var chapterId = ChapterId ?? item?.ChapterId ?? s.Data.ChaptersOf(bookId).FirstOrDefault()?.Id;

            if (planId == null)
            {
                var am = s.AddUnplanned(Title.Trim(), bookId, chapterId, "Unplanned. Logged in the field.");
                planId = am.Results[0];
                item = s.Plan.FindItem(planId);
            }

            var photos = Photos.Select(kv => new CapturePhoto { PhotoId = kv.Key, Text = null, Captured = kv.Value }).ToList();
            if (!string.IsNullOrWhiteSpace(ExtraPhoto)) photos.Add(new CapturePhoto { PhotoId = null, Text = ExtraPhoto.Trim(), Captured = true });

            foreach (var p in People)
            {
                if (p.PersonId != null) continue;
                var existing = s.Search.FindPersonByName(p.Name);
                var person = existing ?? s.FindOrAddPerson(p.Name, Org.Client, PersonRole.Sme, p.Title);
                p.PersonId = person.Id;
            }

            Capture cap;
            if (EditingCaptureId != null && s.Data.FindCapture(EditingCaptureId) != null)
            {
                cap = s.Data.FindCapture(EditingCaptureId);
                s.UpdateCapture(cap.Id, c => Fill(c, planId, item, bookId, chapterId, photos));
                s.Data.Captures.OutlineAssignments.RemoveAll(a => a.MediaRef?.Kind == MediaRefKind.Capture && a.MediaRef.Id == cap.Id);
            }
            else
            {
                var old = s.Data.Captures.Captures.FirstOrDefault(c => c.PlanVideoId == planId);
                if (old != null) s.RemoveCapture(old.Id);
                cap = new Capture { DayId = dayId };
                Fill(cap, planId, item, bookId, chapterId, photos);
                s.AddCapture(cap);
            }

            if (Outline != null)
                s.Assign(new MediaRef(MediaRefKind.Capture, cap.Id), Outline.BookId, Outline.ChapterId, Outline.SectionId, null,
                    AssignmentSource.Auto, Math.Round(Outline.Score, 2), OutlineConfirmed);
            return cap;
        }

        private void Fill(Capture cap, string planId, PlanItem item, string bookId, string chapterId, List<CapturePhoto> photos)
        {
            cap.PlanVideoId = planId;
            cap.PlannedNumber = item?.Origin == PlanItemOrigin.Planned ? item.Number : null;
            cap.Title = Title.Trim();
            cap.BookId = bookId;
            cap.ChapterId = chapterId;
            cap.Description = Description ?? "";
            cap.SceneDescription = string.IsNullOrWhiteSpace(Scene) ? null : Scene;
            cap.HpiToolSuggestion = Hpi ?? "";
            cap.Keywords = (Keywords ?? "").Split(',').Select(k => k.Trim()).Where(k => k.Length > 0).ToList();
            cap.BRoll = string.IsNullOrWhiteSpace(BRoll) ? "N/A" : BRoll;
            cap.CameraCount = CameraCount;
            cap.People = People.Select(p => new CapturePerson { PersonId = p.PersonId, Name = p.Name, Title = p.Title }).ToList();
            cap.Location = Location ?? "";
            cap.Notes = Notes ?? "";
            cap.Photos = photos;
        }
    }
}

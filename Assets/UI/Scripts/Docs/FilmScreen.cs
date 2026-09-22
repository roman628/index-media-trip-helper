using System.Collections.Generic;
using System.Linq;
using MediaTrip.Model;
using MediaTrip.Search;
using MediaTrip.Status;
using MediaTrip.UI.Shell;
using MediaTrip.UI.ViewModels;
using UnityEngine;
using UnityEngine.UIElements;

namespace MediaTrip.UI.Docs
{
    /// <summary>
    /// The filming screen. Each entry field carries the matching line of the plan directly
    /// above it, so the plan is never hidden in any orientation. The "Plan" button opens the
    /// whole plan beside the entry (a column in landscape, a panel under the header otherwise)
    /// for a mid-entry reread without leaving the field being typed in. Typing in the title
    /// field offers matching videos from the shot list.
    /// </summary>
    public static class FilmScreen
    {
        public static VisualElement Build(AppController app)
        {
            var st = app.State; var F = st.Film; var s = app.Session;
            var d = F.Draft ?? (F.Draft = new CaptureDraft());
            var item = d.ItemId != null ? s.Plan.FindItem(d.ItemId) : null;
            var land = app.Layout.Land;

            var root = U.Col().Cls("grow fullsize minh0");
            var save = U.Btn("Save", () => Save(app), "sm pri");
            save.SetEnabled(d.CanSave);

            // ---- header: Cancel · Filming · Plan · Save
            var hdr = new VisualElement().Cls("hdr");
            var cancel = U.GlyphBtn(GlyphKind.ChevronLeft, "Cancel", () => Cancel(app), "sm ghost accent", 16);
            cancel.style.marginLeft = -10;
            hdr.Add(cancel);
            var title = U.Text(d.EditingCaptureId != null ? "Edit" : "Filming", "t grow");
            title.style.unityTextAlign = TextAnchor.MiddleCenter;
            hdr.Add(title);
            if (item != null) hdr.Add(U.Btn("Plan", () => { F.PlanOpen = !F.PlanOpen; app.RenderKeepFocus(app.FocusedFieldName); }, "sm mr8" + (F.PlanOpen ? " pri" : "")));
            hdr.Add(save);
            root.Add(hdr);

            var planOpen = F.PlanOpen && item != null;
            var form = Form(app, d, item, save, twoColumns: land && !planOpen);
            if (!planOpen) root.Add(form);
            else if (land)
            {
                var plan = U.Scroll(PlanFull(app, item)).Named("film-plan").Cls("pad bordered-right");
                plan.style.flexGrow = 0;
                plan.style.width = Mathf.Round(app.Layout.Width * 0.4f);
                root.Add(U.Row(plan, form).Cls("grow stretch minh0"));
            }
            else
            {
                var plan = U.Scroll(PlanFull(app, item)).Named("film-plan").Cls("planstrip");
                plan.style.flexGrow = 0;
                plan.style.maxHeight = Mathf.Round(app.Layout.Height * 0.45f);
                root.Add(plan);
                root.Add(form);
            }
            return root;
        }

        public static VisualElement Overlay(AppController app) => null;

        private static void Cancel(AppController app)
        {
            var F = app.State.Film;
            F.Draft = null; F.PlanOpen = false;
            app.Nav(F.Return == Screen.Film ? Screen.Summary : F.Return);
        }

        private static void Save(AppController app)
        {
            var F = app.State.Film; var s = app.Session; var d = F.Draft;
            if (d == null || !d.CanSave) return;
            try
            {
                var day = F.DayId != null && s.Data.FindDay(F.DayId) != null ? F.DayId : app.DayForNow();
                var editing = d.EditingCaptureId != null;
                var cap = d.Save(s, day);
                F.Draft = null; F.PlanOpen = false;
                app.Nav(Screen.Summary);
                app.State.SM.Day = cap.DayId;
                app.Render();
                var sec = d.Outline?.Section;
                app.Toast((editing ? "Saved" : "Added to " + app.DayLabel(cap.DayId)) + (sec != null && !d.OutlineConfirmed ? " · suggested for " + sec.Number : ""));
            }
            catch (System.Exception ex) { app.Toast("Could not save: " + ex.Message); }
        }

        // ------------------------------------------------------------------ the whole plan (the peek)

        private static VisualElement PlanFull(AppController app, PlanItem item)
        {
            var s = app.Session; var d = s.Data;
            var col = U.Col();
            col.Add(U.Row(U.Num(ShotListView.NumberOf(item)), U.Sub(U.Esc(Fmt.BookChapter(d, item.BookId, item.ChapterId)))).Mb(6));
            col.Add(U.H2(U.Esc(item.Title)).Mb(4));
            var sme = Fmt.SmeOf(d, item);
            if (sme.Length > 0) col.Add(U.Sub(U.Esc(sme)));
            if (!string.IsNullOrEmpty(item.SceneDescription)) { col.Add(U.Eyebrow("Scene").Mt(12)); col.Add(U.Text(U.Esc(item.SceneDescription), "body-sm").Mt(4)); }
            if (item.Notes.Count > 0) { col.Add(U.Eyebrow("Notes").Mt(12).Mb(6)); col.Add(NotesView.Build(item.Notes, 16)); }
            var photos = item.PhotoRefs.Select(s.Plan.FindPhoto).Where(p => p != null).ToList();
            if (photos.Count > 0)
            {
                col.Add(U.Eyebrow("Photos").Mt(12).Mb(6));
                foreach (var p in photos) col.Add(U.Row(new Glyph(GlyphKind.Frame, 14).Mr(8), U.Text(U.Esc(p.Description)).Font(15).Cls("grow")).Mb(4));
            }
            return col;
        }

        private static VisualElement PlanBox(string key, VisualElement value)
        {
            var box = new VisualElement().Cls("planbox");
            box.Add(U.Text(key.ToUpperInvariant(), "k"));
            box.Add(value);
            return box;
        }

        private static VisualElement PlanBox(string key, string value) => PlanBox(key, U.Text(U.Esc(value), "v"));

        // ------------------------------------------------------------------ the entry

        private static VisualElement Form(AppController app, CaptureDraft d, PlanItem item, Button save, bool twoColumns)
        {
            var s = app.Session; var data = s.Data; var F = app.State.Film;
            var scroll = U.Scroll().Named("film-form").Cls("pad");

            // ---- title, with the shot-list suggestions under it
            var fTitle = new VisualElement().Cls("fld");
            if (item != null) fTitle.Add(PlanBox("Planned title", "#" + ShotListView.NumberOf(item) + " · " + (item.OriginalTitle ?? item.Title)));
            fTitle.Add(U.Lbl("Title"));
            var where = U.Row().Cls("wrap").Mb(10);
            var titleField = U.Input(d.Title, "What are we filming?", null, false, "tall");
            titleField.name = "film.title";
            app.ClosePopupWhenBlurred(titleField);
            void Suggest()
            {
                var matches = d.ItemId == null && d.ShowSuggestions ? d.OpenSuggestions(s) : null;
                if (matches == null || matches.Count == 0) { app.ClosePopup(); return; }
                var box = new VisualElement().Cls("sugg");
                foreach (var m in matches)
                {
                    var it = m.Item;
                    var sme = Fmt.SmeOf(data, it);
                    box.Add(U.Tap(() => { d.Pick(s, it.Id); app.ClosePopup(); app.RenderKeepFocus(null); }, "sugg-row",
                        U.Num(ShotListView.NumberOf(it)),
                        U.Col(U.Text(U.Esc(it.Title), "bold"), U.Sub(U.Esc(Fmt.BookChapter(data, it.BookId, it.ChapterId) + (sme.Length > 0 ? " · " + sme : "")))).Cls("grow")));
                }
                var width = titleField.resolvedStyle.width;
                app.ShowPopup(titleField, box, float.IsNaN(width) || width < 200 ? 360 : width, 320);
            }
            titleField.RegisterValueChangedCallback(e =>
            {
                var hadPick = d.ItemId != null;
                d.SetTitle(e.newValue);
                save.SetEnabled(d.CanSave);
                if (hadPick) { app.RenderKeepFocus("film.title"); return; }
                FillWhere(app, d, where);
                Suggest();
            });
            fTitle.Add(titleField);
            FillWhere(app, d, where);

            // ---- SME
            var fSme = new VisualElement().Cls("fld");
            var plannedSme = item != null ? Fmt.SmeOf(data, item) : "";
            if (plannedSme.Length > 0) fSme.Add(PlanBox("Planned SME", plannedSme));
            fSme.Add(U.Lbl("SME"));
            var smes = U.Row().Cls("wrap");
            for (int i = 0; i < d.People.Count; i++)
            {
                var idx = i; var p = d.People[i];
                smes.Add(U.ChipX(U.Esc(p.Name + (string.IsNullOrEmpty(p.Title) ? "" : " · " + p.Title)), () => { d.People.RemoveAt(idx); app.RenderKeepFocus(null); }, "wrapok"));
            }
            fSme.Add(smes);
            // Name, then title, then Add: a match fills the name (and the title on file), a new
            // name is kept as typed, and the title can be typed either way before Add.
            fSme.Add(SmePicker.Build(app, "film.sme", d.Sme, n => d.People.Any(x => x.Name == n), sd => d.AddPerson(sd.Name, sd.Title, sd.PersonId)));

            // ---- cameras
            var fCam = U.Field("Cameras", U.Stepper(d.CameraCount, delta => { d.CameraCount = Mathf.Clamp(d.CameraCount + delta, 1, 6); app.RenderKeepFocus(null); }));

            // ---- description
            var fDesc = new VisualElement().Cls("fld");
            if (item != null && !string.IsNullOrEmpty(item.SceneDescription)) fDesc.Add(PlanBox("Planned scene", item.SceneDescription));
            fDesc.Add(U.Lbl("Description"));
            fDesc.Add(Bound("film.description", d.Description, v => d.Description = v, true));

            // ---- notes
            var fNotes = new VisualElement().Cls("fld");
            if (item != null && item.Notes.Count > 0) fNotes.Add(PlanBox("Plan notes", NotesView.Build(item.Notes, 15, 18)));
            fNotes.Add(U.Lbl("Notes"));
            fNotes.Add(Bound("film.notes", d.Notes, v => d.Notes = v, true));

            // ---- b-roll
            var fBroll = U.Field("B-roll", Bound("film.broll", d.BRoll == "N/A" ? "" : d.BRoll, v => d.BRoll = v, false));

            // ---- photos
            var fPhotos = new VisualElement().Cls("fld");
            fPhotos.Add(U.Lbl("Photos captured" + (d.Photos.Count > 0 ? " · planned with this setup" : "")));
            var chips = U.Row().Cls("wrap");
            foreach (var pid in d.Photos.Keys.ToList())
            {
                var id = pid; var on = d.Photos[pid]; var p = s.Plan.FindPhoto(pid);
                chips.Add(U.Tap(() => { d.Photos[id] = !on; app.RenderKeepFocus(null); }, "chip ok wrapok",
                    on ? new Glyph(GlyphKind.Check, 16, 3).Mr(6) : null,
                    p != null && p.HeroType != HeroType.None ? new Glyph(GlyphKind.Star, 14).Mr(6) : null,
                    U.Text(U.Esc(U.ShortDescription(p?.Description ?? pid)))).On(on));
            }
            for (int i = 0; i < d.ExtraPhotos.Count; i++)
            {
                var idx = i;
                chips.Add(U.ChipX(U.Esc(d.ExtraPhotos[i]), () => { d.ExtraPhotos.RemoveAt(idx); app.RenderKeepFocus(null); }, "on wrapok"));
            }
            fPhotos.Add(chips);
            var addExtra = U.Btn("Add", () => { d.AddExtraPhoto(); app.RenderKeepFocus("film.extra"); }, "sm ml8");
            addExtra.SetEnabled(!string.IsNullOrWhiteSpace(d.ExtraPhoto));
            var extra = U.Input(d.ExtraPhoto, "+ unplanned photo", v => { d.ExtraPhoto = v; addExtra.SetEnabled(!string.IsNullOrWhiteSpace(v)); }, false, "h44 grow");
            extra.name = "film.extra";
            fPhotos.Add(U.Row(extra, addExtra));

            // ---- the rest of the summary entry, out of the way until wanted
            var more = U.Col();
            if (d.More)
            {
                more.Add(U.Field("Location", Bound("film.location", d.Location, v => d.Location = v, false)));
                more.Add(U.Field("HPI tool", Bound("film.hpi", d.Hpi, v => d.Hpi = v, false)));
                more.Add(U.Field("Keywords", Bound("film.keywords", d.Keywords, v => d.Keywords = v, false)));
            }
            else
            {
                var b = U.Btn("More…", () => { d.More = true; app.RenderKeepFocus("film.location"); }, "sm ghost accent left");
                b.style.alignSelf = Align.FlexStart;
                more.Add(b);
            }

            if (twoColumns)
            {
                var left = U.Col(fTitle, where, fSme, fCam, fDesc).Cls("grow");
                left.style.marginRight = 20; left.style.flexBasis = 0;
                var right = U.Col(fNotes, fBroll, fPhotos, more).Cls("grow");
                right.style.flexBasis = 0;
                scroll.Add(U.Row(left, right).Cls("top-align"));
            }
            else foreach (var f in new[] { fTitle, where, fSme, fCam, fDesc, fNotes, fBroll, fPhotos, more }) scroll.Add(f);
            return scroll;
        }

        private static TextField Bound(string name, string value, System.Action<string> set, bool multiline)
        {
            var tf = U.Input(value, null, set, multiline);
            tf.name = name;
            return tf;
        }

        /// <summary>Book, chapter and day. For something unplanned the book and chapter are chosen here.</summary>
        private static void FillWhere(AppController app, CaptureDraft d, VisualElement where)
        {
            var s = app.Session; var data = s.Data; var F = app.State.Film;
            where.Clear();
            var day = F.DayId != null && data.FindDay(F.DayId) != null ? F.DayId : null;
            var dayText = day != null ? app.DayLabel(day) : "Today";
            if (d.ItemId != null)
            {
                var b = data.FindBook(d.BookId); var c = data.FindChapter(d.ChapterId);
                if (b != null) where.Add(U.Pill(Fmt.BookName(b), "ac md"));
                if (c != null) where.Add(U.Pill("Ch." + c.Number + " · " + c.Name, "ac md"));
                where.Add(U.Pill(dayText, "md"));
                return;
            }
            if (string.IsNullOrWhiteSpace(d.Title))
            {
                where.Add(U.Sub("Book and chapter fill in from the match").Mr(10));
                where.Add(U.Pill(dayText, "md"));
                return;
            }
            // unplanned: pick where it belongs
            if (d.BookId == null) d.BookId = data.Trip.Books.FirstOrDefault()?.Id;
            if (d.ChapterId == null || data.FindChapter(d.ChapterId)?.BookId != d.BookId) d.ChapterId = data.ChaptersOf(d.BookId).FirstOrDefault()?.Id;
            foreach (var b in data.Trip.Books.OrderBy(x => x.Number))
            {
                var bid = b.Id;
                where.Add(U.Chip(Fmt.BookName(b), d.BookId == bid, () => { d.BookId = bid; d.ChapterId = null; FillWhere(app, d, where); }, "wrapok"));
            }
            foreach (var c in data.ChaptersOf(d.BookId))
            {
                var cid = c.Id;
                where.Add(U.Chip("Ch." + c.Number, d.ChapterId == cid, () => { d.ChapterId = cid; FillWhere(app, d, where); }));
            }
            where.Add(U.Pill(dayText, "md"));
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using MediaTrip.Authoring;
using MediaTrip.Model;
using MediaTrip.Status;
using MediaTrip.UI.Shell;
using MediaTrip.UI.ViewModels;
using UnityEngine;
using UnityEngine.UIElements;

namespace MediaTrip.UI.Docs
{
    /// <summary>
    /// The shot list: Working / Original / Changes and "Hide done". Videos expand and collapse
    /// in place (Expand all reads like the printed document); each has a ⋯ menu with Film,
    /// Rename, Combine and Drop. Photos are listed with their book.
    ///
    /// Edit changes the document, and what that means depends on the view. On Original it is
    /// authoring (or, once media exists, fixing what was typed wrong: logged as a correction).
    /// On Working it is changing the plan during the trip: every change is recorded, a retitle
    /// is a rename, a delete is a drop. <see cref="PlanEdits"/> decides; this screen only calls it.
    /// </summary>
    public static class ShotListScreen
    {
        private static PlanEdits Edits(AppController app) =>
            new PlanEdits(app.Session, app.State.SL.View == ShotListMode.Original ? PlanEditMode.Original : PlanEditMode.Working);

        public static VisualElement Build(AppController app)
        {
            var st = app.State; var L = st.SL;
            var col = U.Col().Cls("grow minh0");
            col.Add(Controls(app));
            var list = U.Scroll().Named("sl-list:" + L.View).Cls("listpad");
            if (L.View == ShotListMode.Changes) FillChanges(app, list);
            else FillList(app, list);
            col.Add(list);
            return AppShell.Build(app, "Shot list", col, edit: true);
        }

        public static VisualElement Overlay(AppController app) => app.State.SL.Amend != null ? AmendSheet.Build(app) : null;

        /// <summary>
        /// Entering Edit on the original once media exists. A transcription fix corrects data
        /// entry and is always allowed; a change of plan belongs in the working copy.
        /// </summary>
        public static void OriginalGate(AppController app)
        {
            var L = app.State.SL;
            ChoiceSheet.Open(app, "Media has already been assigned",
                new[] { "Are you fixing something that was typed wrong, or did the plan change?", "A fix is logged as a correction, so it is always possible to see why the app and the paper differ. A change of plan is recorded in the working copy." },
                new ChoiceSheet.Option("Fix the original", () => { L.OriginalGatePassed = true; app.State.Edit = true; app.Render(); }),
                new ChoiceSheet.Option("Change the plan instead", () => { L.View = ShotListMode.Working; app.State.Edit = true; app.Render(); }));
        }

        private static VisualElement Controls(AppController app)
        {
            var st = app.State; var L = st.SL; var s = app.Session;
            var n = s.Data.Captures.Amendments.Count + s.Data.Captures.Edits.Count;
            var c = new VisualElement().Cls("controls");
            c.Add(U.Segmented(new List<(string, string)>
            {
                ("Working", "Working"), ("Original", "Original"), ("Changes", "Changes" + (n > 0 ? " · " + n : "")),
            }, L.View.ToString(), id =>
            {
                var to = (ShotListMode)System.Enum.Parse(typeof(ShotListMode), id);
                if (to == L.View) return;
                s.EndEditSession();
                st.Focus = null;
                if (st.Edit && to == ShotListMode.Original && s.HasFieldMedia && !L.OriginalGatePassed) { st.Edit = false; L.View = to; app.Render(); OriginalGate(app); return; }
                L.View = to;
                app.Render();
            }));
            if (L.View == ShotListMode.Changes) return c;
            c.Add(U.Toggle("Hide done", L.HideDone, () => { L.HideDone = !L.HideDone; app.Render(); }).Mr(8));
            var phone = app.Layout.Phone;
            c.Add(U.Btn(phone ? "Expand" : "Expand all", () => ExpandAll(app, true), "sm ghost accent"));
            c.Add(U.Btn(phone ? "Collapse" : "Collapse all", () => ExpandAll(app, false), "sm ghost accent"));
            if (st.Edit)
            {
                var edits = Edits(app);
                var note = edits.Working ? "Changing the plan. Every change is recorded and shows in Changes."
                    : edits.LogsCorrections ? "Fixing the original. Each fix is logged as a correction." : "Writing the plan. Nothing is recorded yet.";
                var line = U.Sub(note); line.style.width = Length.Percent(100); line.style.marginBottom = 6;
                c.Add(line);
            }
            return c;
        }

        public static void ExpandAll(AppController app, bool open)
        {
            var L = app.State.SL; var s = app.Session;
            L.Expanded.Clear();
            if (open)
            {
                var view = ShotListView.Build(s, L.View, L.HideDone, app.State.Edit);
                foreach (var r in view.Books.SelectMany(b => b.Chapters).SelectMany(c => c.Rows)) L.Expanded.Add(r.Item.Id);
            }
            app.State.Focus = null;
            app.Render();
        }

        private static void Toggle(AppController app, string id)
        {
            var L = app.State.SL;
            if (!L.Expanded.Remove(id)) L.Expanded.Add(id);
            app.State.Focus = null;
            app.Render();
        }

        // ------------------------------------------------------------------ the list

        private static void FillList(AppController app, ScrollView list)
        {
            var st = app.State; var L = st.SL; var s = app.Session; var d = s.Data; var E = st.Edit;
            var original = L.View == ShotListMode.Original;
            var view = ShotListView.Build(s, L.View, L.HideDone, keepEmpty: E);
            if (view.IsEmpty)
            {
                list.Add(U.Sub(d.Trip.Books.Count == 0 ? "No books yet. Add them in Trip." : "Nothing left.").Pad(20, 12));
                return;
            }
            var edits = Edits(app);
            foreach (var bg in view.Books)
            {
                var bookId = bg.Book.Id;
                list.Add(U.Text(U.Esc(Fmt.BookName(bg.Book)), "h2 bookhead wraptext"));
                foreach (var cg in bg.Chapters)
                {
                    var chId = cg.Chapter.Id;
                    if (E) list.Add(ChapterEditRow(app, edits, cg.Chapter));
                    else list.Add(U.Name("CH." + cg.Chapter.Number + " · " + (cg.Chapter.Name ?? "").ToUpperInvariant(), false, "eyebrow chaphead"));

                    if (cg.Hero != null && !E)
                    {
                        var hero = cg.Hero;
                        var ring = U.StPhoto(hero.Status, 30, true); ring.style.marginLeft = 7; ring.style.marginRight = 12;
                        list.Add(U.Tap(() => OpenCover(app, bookId, chId), "item h48", ring, U.Name("Hero · " + U.Esc(U.ShortDescription(hero.Description)), false, "sub bold grow")));
                    }
                    foreach (var r in cg.Rows) list.Add(VideoRow(app, edits, r, original));
                    if (E) list.Add(U.AddBtn("Video", () =>
                    {
                        var id = edits.AddVideo(chId, "");
                        L.Expanded.Add(id);
                        app.RenderKeepFocus("video:" + id + ":title");
                    }));
                }
                if (E) list.Add(U.AddBtn("Chapter", () => { var c = edits.AddChapter(bookId, ""); app.RenderKeepFocus("chapter:" + c.Id); }).Mt(6));

                // ---- the book's photos, with the book
                if (bg.Photos.Count > 0 || E)
                {
                    list.Add(U.Eyebrow("Photos").Cls("chaphead"));
                    foreach (var p in bg.Photos) list.Add(PhotoRow(app, edits, p, original));
                    if (E) list.Add(U.AddBtn("Photo", () =>
                    {
                        var id = edits.AddPhoto(bookId, null, "");
                        L.Expanded.Add(id);
                        app.RenderKeepFocus("photo:" + id);
                    }));
                }
            }
            if (L.ScrollTo != null) { app.ScrollToNamed("row:" + L.ScrollTo); L.ScrollTo = null; }
        }

        private static void OpenCover(AppController app, string bookId, string chapterId)
        {
            app.Nav(Screen.Covers);
            app.State.CV.SlotBook = bookId; app.State.CV.SlotKey = chapterId ?? "cover"; app.State.CV.Query = "";
            app.Render();
        }

        private static VisualElement Tags(List<string> tags)
        {
            if (tags.Count == 0) return null;
            var row = U.Row().Cls("wrap");
            foreach (var t in tags) row.Add(U.Tag(t, t == "new" ? "ok" : ""));
            return row;
        }

        // ------------------------------------------------------------------ one video

        private static VisualElement VideoRow(AppController app, PlanEdits edits, ShotListView.Row r, bool original)
        {
            var st = app.State; var L = st.SL; var E = st.Edit;
            var item = r.Item; var id = item.Id;
            var open = L.Expanded.Contains(id);
            var box = new VisualElement().Cls("xrow").On(open);
            box.name = "row:" + id;

            var title = string.IsNullOrEmpty(r.Title) ? "Untitled" : r.Title;
            var text = U.Col(U.Name(r.Dim ? U.Strike(title) : U.Esc(title), open, "title" + (r.Dim ? " strike" : "")),
                r.Sub.Length > 0 ? U.Name(U.Esc(r.Sub), open, "sub") : null, Tags(r.Tags)).Cls("grow");
            var head = U.Tap(() => Toggle(app, id), "xhead grow", U.Chevron(open), U.Num(r.Number).Ml(6), text);
            var line = U.Row(head, U.St(r.Status, 30).Ml(8));
            if (!E) line.Add(U.IconBtn(GlyphKind.Dots, null, "ghost sm").Also(b => b.clicked += () => VideoMenu(app, item, b)));
            box.Add(line);
            if (!open) return box;

            var body = new VisualElement().Cls("xbody");
            if (E && (!original || item.PlannedVideo != null)) FillVideoEditor(app, edits, item, body);
            else FillVideoRead(app, item, body, original);
            box.Add(body);
            return box;
        }

        /// <summary>The ⋯ on a video: the same actions as its expanded view, without opening it.</summary>
        private static void VideoMenu(AppController app, PlanItem item, VisualElement anchor)
        {
            var box = new VisualElement().Cls("sugg");
            void Item(GlyphKind g, string text, System.Action act, bool danger = false) =>
                box.Add(U.Tap(() => { app.ClosePopup(); act(); }, "sugg-row", new Glyph(g, 18).Mr(10), U.Text(text, "bold" + (danger ? " bad-text" : ""))));
            var cap = item.Captures.FirstOrDefault();
            var active = !item.IsDropped && !item.IsSuperseded;
            if (cap != null) Item(GlyphKind.Square, "Open in Summary", () => app.OpenSummary(cap.Id));
            else if (active) Item(GlyphKind.Play, "Film", () => app.StartFilm(item.Id));
            if (active)
            {
                Item(GlyphKind.Pencil, "Rename", () => Amend(app, item.Id, "rename"));
                if (item.Origin == PlanItemOrigin.Planned) Item(GlyphKind.Plus, "Combine…", () => Amend(app, item.Id, "combine"));
                Item(GlyphKind.Lines, "Split…", () => Amend(app, item.Id, "split"));
                if (item.Origin == PlanItemOrigin.Planned) Item(GlyphKind.Cross, "Drop", () => Amend(app, item.Id, "drop"), true);
            }
            if (box.childCount == 0) box.Add(U.Sub(item.IsDropped ? "Dropped. Undo it from Changes, in Edit." : "Combined into another video.").Pad(14));
            app.ShowPopup(anchor, box, 220, 320, -170, dismissOnOutsideTap: true);
        }

        private static void Amend(AppController app, string itemId, string mode)
        {
            var a = AmendDraft.For(app.Session, itemId);
            a.SetMode(app.Session, mode);
            app.State.SL.Amend = a;
            app.RenderKeepFocus(mode == "drop" ? "am.reason" : mode == "split" ? "am.part0" : "am.title");
        }

        private static void FillVideoRead(AppController app, PlanItem item, VisualElement body, bool original)
        {
            var s = app.Session; var d = s.Data; var L = app.State.SL;
            body.Add(U.Row(U.StatusPill(item.Status), U.Sub(U.Esc(Fmt.BookChapter(d, item.BookId, item.ChapterId))).Ml(10).Cls("grow ellipsis")).Mb(10));

            if (item.IsSuperseded)
                foreach (var r in item.ResolvedItems.Where(x => x.Id != item.Id))
                {
                    var rid = r.Id;
                    body.Add(U.GlyphBtn(GlyphKind.Arrow, "Shot as “" + U.Esc(r.Title) + "”", () => { L.View = ShotListMode.Working; L.Expanded.Add(rid); L.ScrollTo = rid; app.Render(); }, "left mb12"));
                }
            var cap = item.Captures.FirstOrDefault();
            if (cap != null)
            {
                var when = app.DayLabel(cap.DayId) + (string.IsNullOrEmpty(cap.At) ? "" : " · " + U.FmtTime(cap.At));
                body.Add(U.GlyphBtn(GlyphKind.Square, U.Esc(when) + " · open in Summary", () => app.OpenSummary(cap.Id), "left mb12"));
            }
            else if (!item.IsDropped && !item.IsSuperseded)
                body.Add(U.GlyphBtn(GlyphKind.Play, "Film", () => app.StartFilm(item.Id), "big pri mb12", 20));

            var v = original ? item.PlannedVideo : null;
            var scene = v != null ? v.SceneDescription : item.SceneDescription;
            var notes = v != null ? (v.Notes ?? new List<Node>()) : item.Notes;
            var refs = v != null ? (v.PhotoRefs ?? new List<string>()) : item.PhotoRefs;
            if (!string.IsNullOrEmpty(scene)) { body.Add(U.Eyebrow("Scene")); body.Add(U.Body(U.Esc(scene)).Mt(4).Mb(14)); }
            if (notes.Count > 0) { body.Add(U.Eyebrow("Notes").Mb(6)); body.Add(NotesView.Build(notes)); }
            var photos = refs.Select(s.Plan.FindPhoto).Where(p => p != null).ToList();
            if (photos.Count > 0)
            {
                body.Add(U.Eyebrow("Photos with this video").Mt(14).Mb(6));
                foreach (var p in photos)
                    body.Add(U.Row(U.StPhoto(p.Status, 30, p.HeroType != HeroType.None), U.Text(U.Esc(p.Description), "body-sm grow wraptext").Ml(10)).MinH(44));
            }
            var reason = item.DropAmendment?.Reason ?? item.CreatedBy?.Reason;
            if (!string.IsNullOrEmpty(reason)) body.Add(U.Sub(U.Esc(reason)).Mt(14));
            if (string.IsNullOrEmpty(scene) && notes.Count == 0 && photos.Count == 0 && string.IsNullOrEmpty(reason)) body.Add(U.Sub("No scene, notes or photos planned."));
        }

        private static void FillVideoEditor(AppController app, PlanEdits edits, PlanItem item, VisualElement body)
        {
            var s = app.Session; var d = s.Data; var L = app.State.SL; var id = item.Id;
            var working = edits.Working;
            var v = item.PlannedVideo;
            string title = working ? item.Title : v.Title, scene = working ? item.SceneDescription : v.SceneDescription, smeText = working ? item.SmeText : v.SmeText;
            var smeIds = (working ? item.SmeIds : v.SmeIds) ?? new List<string>();
            var refs = (working ? item.PhotoRefs : v.PhotoRefs) ?? new List<string>();

            var tf = U.Input(title, "Title", val => app.Edit(() => TripScreen.Guard(app, () => edits.SetTitle(id, val))), false, "tall");
            tf.name = "video:" + id + ":title";
            tf.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { e.StopPropagation(); Toggle(app, id); } }, TrickleDown.TrickleDown);
            body.Add(U.Field(working ? "Title · a change here is a rename" : "Title", tf));

            body.Add(U.Lbl("SME"));
            var smes = U.Row().Cls("wrap").Mb(6);
            foreach (var pid in smeIds)
            {
                var pidc = pid;
                smes.Add(U.ChipX(U.Esc(d.FindPerson(pid)?.FullName ?? pid), () => edits.SetSmeIds(id, smeIds.Where(x => x != pidc).ToList())));
            }
            if (!string.IsNullOrEmpty(smeText)) smes.Add(U.ChipX(U.Esc(smeText), () => edits.SetSmeText(id, "")));
            if (smes.childCount > 0) body.Add(smes);
            // name, title, Add: the same picker as Filming, and it creates people the same way
            body.Add(SmePicker.Build(app, "video:" + id + ":sme", L.SmeDraftFor(id),
                n => smeIds.Select(pid => d.FindPerson(pid)?.FullName).Contains(n),
                sd => TripScreen.Guard(app, () => edits.SetSmeIds(id, smeIds.Concat(new[] { AddSme(s, sd).Id }).ToList()))).Mb(10));

            var sc = U.Input(scene, null, val => app.Edit(() => edits.SetScene(id, val)), true, "h72");
            sc.name = "video:" + id + ":scene";
            body.Add(U.Field("Scene", sc));

            body.Add(U.Lbl("Notes"));
            body.Add(NodeEditor.Build(app, edits.Notes(id), new AppState.PasteState { Kind = "video", Id = id, Working = working }));

            // ---- photos with this video: the list, then one box to add to it
            body.Add(U.Lbl("Photos with this video").Mt(14));
            var listed = refs.Select(s.Plan.FindPhoto).Where(p => p != null).ToList();
            if (listed.Count == 0) body.Add(U.Sub("None yet.").Mb(6));
            foreach (var p in listed)
            {
                var pid = p.Id;
                body.Add(U.Row(U.StPhoto(p.Status, 28, p.HeroType != HeroType.None).Mr(10),
                    U.Col(U.Name(U.Esc(p.Description), true, "body-sm"), U.Sub(U.Esc(Fmt.PhotoWhere(d, p)))).Cls("grow"),
                    // removes only the association; the photo stays on its book's list
                    U.IconBtn(GlyphKind.Cross, () => TripScreen.Guard(app, () => edits.SetPhotoRefs(id, refs.Where(x => x != pid).ToList())), "ghost sm", 16)).MinH(44).Cls("bordered-bottom"));
            }
            body.Add(PickerField.Build(app, "video:" + id + ":photo", "", "Add a photo: find one, or type a new one…", null,
                q => PhotoRows(app, edits, item, refs, q), "h44 grow").Mt(6));

            var chapters = d.ChaptersOf(item.BookId).ToList();
            if (chapters.Count > 1)
            {
                body.Add(U.Lbl("Chapter").Mt(10));
                var chips = U.Row().Cls("wrap");
                foreach (var ch in chapters)
                {
                    var cid = ch.Id;
                    chips.Add(U.Chip("Ch." + ch.Number, item.ChapterId == cid, () => { if (item.ChapterId != cid) TripScreen.Guard(app, () => edits.MoveVideo(id, cid)); }));
                }
                body.Add(chips);
            }

            var foot = U.Row().Cls("wrap").Mt(10);
            if (!working && v != null)
            {
                var siblings = s.PlanEditor.VideosOfChapter(v.ChapterId);
                var at = siblings.FindIndex(x => x.Id == id);
                var up = U.GlyphBtn(GlyphKind.ArrowUp, "Up", () => edits.ReorderVideo(id, at - 1), "sm ghost", 16); up.SetEnabled(at > 0);
                var down = U.GlyphBtn(GlyphKind.ArrowDown, "Down", () => edits.ReorderVideo(id, at + 1), "sm ghost", 16); down.SetEnabled(at >= 0 && at < siblings.Count - 1);
                foot.Add(up); foot.Add(down);
            }
            foot.Add(U.Grow());
            foot.Add(U.Btn(working ? "Drop video" : "Delete video", () => DeleteVideo(app, edits, item), "sm ghost danger"));
            body.Add(foot);
        }

        /// <summary>The SME the picker produced: a registry person, found or created; a typed title goes on file for someone who had none.</summary>
        public static Person AddSme(MediaTrip.Session.TripSession s, SmeDraft sd)
        {
            var person = sd.PersonId != null ? s.Data.FindPerson(sd.PersonId) : null;
            if (person == null) person = s.Search.FindPersonByName(sd.Name) ?? s.FindOrAddPerson(sd.Name, Org.Client, PersonRole.Sme, sd.Title);
            if (string.IsNullOrWhiteSpace(person.Title) && !string.IsNullOrWhiteSpace(sd.Title)) s.EditTrip(_ => person.Title = sd.Title.Trim());
            return person;
        }

        /// <summary>
        /// The add box under a video's photos: every photo of every book (this book's first),
        /// photos typed in the field that are not on the list yet (picking one files them), and
        /// a new photo when nothing matches. New and filed photos go through the one create
        /// path into this book's master list, on the list this mode edits.
        /// </summary>
        private static List<PickerField.Row> PhotoRows(AppController app, PlanEdits edits, PlanItem item, List<string> refs, string q)
        {
            var s = app.Session; var d = s.Data; var id = item.Id;
            var field = "video:" + id + ":photo";
            void Link(string photoId) { TripScreen.Guard(app, () => edits.SetPhotoRefs(id, refs.Concat(new[] { photoId }).ToList())); app.RenderKeepFocus(field); }
            var rows = new List<PickerField.Row>();
            var found = s.Search.SearchPhotos(GlobalSearch.HeroAlias(q), 12)
                .Where(m => !refs.Contains(m.Item.Id) && m.Item.Status != PhotoStatus.Dropped && (edits.Working || !m.Item.IsNew))
                .Select((m, i) => (m, i))
                .OrderByDescending(t => System.Math.Round(t.m.Score, 2)).ThenBy(t => t.m.Item.Photo.BelongsToBook(item.BookId) ? 0 : 1).ThenBy(t => t.i)
                .Select(t => t.m.Item).Take(6).ToList();
            foreach (var p in found)
            {
                var photo = p;
                rows.Add(new PickerField.Row
                {
                    Lead = U.StPhoto(p.Status, 28, p.HeroType != HeroType.None).Mr(10), Title = U.ShortDescription(p.Description),
                    Sub = Fmt.PhotoWhere(d, p) + (p.Status == PhotoStatus.Captured ? " · shot" : " · not shot yet"),
                    Pick = () => Link(photo.Id),
                });
            }
            var loose = s.Search.SearchLoosePhotos(q, 4).Select(m => m.Item).ToList();
            foreach (var lp in loose)
            {
                var photo = lp;
                rows.Add(new PickerField.Row
                {
                    Lead = new Glyph(GlyphKind.Frame, 18).Cls("muted").Mr(10), Title = U.ShortDescription(lp.Text),
                    Sub = "Not on the shot list yet · " + Fmt.LooseWhere(app, lp) + " · files it under " + Fmt.BookChapter(d, item.BookId, item.ChapterId),
                    Pick = () => { string pid = null; TripScreen.Guard(app, () => pid = edits.FilePhoto(photo.Text, item.BookId, item.ChapterId)); if (pid != null) Link(pid); },
                });
            }
            var text = q.Trim();
            if (text.Length > 0 && !CoverActions.IsExactMatch(text, found) && !loose.Any(lp => lp.Key == MediaTrip.Query.LoosePhotos.KeyOf(text)))
                rows.Add(new PickerField.Row
                {
                    Lead = new Glyph(GlyphKind.Plus, 18).Mr(10), IsCreate = true, Title = "New photo “" + text + "”",
                    Sub = "Goes on the " + (edits.Working ? "working" : "original") + " shot list for " + Fmt.BookChapter(d, item.BookId, item.ChapterId) + (edits.Working ? ", marked new" : ""),
                    Pick = () => { string pid = null; TripScreen.Guard(app, () => pid = edits.AddPhoto(item.BookId, item.ChapterId, text)); if (pid != null) Link(pid); },
                });
            return rows;
        }

        /// <summary>
        /// Deleting a video that was filmed: the capture is kept and detached by default (it
        /// stays in the summary, unplanned, tagged with what it was); deleting it too is the
        /// other answer, for something logged by mistake.
        /// </summary>
        private static void DeleteVideo(AppController app, PlanEdits edits, PlanItem item)
        {
            var s = app.Session; var id = item.Id;
            if (edits.Working) { Amend(app, id, "drop"); return; }
            var impact = Deletions.ForVideo(s.Data, id);
            void Go(bool deleteCaptures) { TripScreen.Guard(app, () => { edits.DeleteVideo(id, null, deleteCaptures); app.State.SL.Expanded.Remove(id); }); }
            if (!impact.NeedsConfirm) { Go(false); return; }
            var lines = impact.Lines().Concat(new[] { "If the plan changed, drop it from the working copy instead: that keeps it on the original, struck through." });
            if (impact.Captures > 0)
                ChoiceSheet.Open(app, "Delete “" + item.Title + "” from the original? It was filmed.", lines,
                    new ChoiceSheet.Option("Detach and keep the capture", () => Go(false)),
                    new ChoiceSheet.Option("Delete the capture too", () => Go(true), danger: true));
            else
                ChoiceSheet.Open(app, "Delete “" + item.Title + "” from the original?", lines, new ChoiceSheet.Option("Delete from the original", () => Go(false), danger: true));
        }

        // ------------------------------------------------------------------ chapters (edit)

        private static VisualElement ChapterEditRow(AppController app, PlanEdits edits, Chapter ch)
        {
            var s = app.Session; var chId = ch.Id;
            var name = U.Input(ch.Name, "Chapter name", v => app.Edit(() => edits.SetChapterName(chId, v)), false, "h44 grow");
            name.name = "chapter:" + chId;
            var siblings = s.Data.ChaptersOf(ch.BookId).ToList();
            var at = siblings.FindIndex(c => c.Id == chId);
            var up = U.IconBtn(GlyphKind.ArrowUp, () => edits.ReorderChapter(chId, at - 1), "ghost sm", 18); up.SetEnabled(at > 0);
            var down = U.IconBtn(GlyphKind.ArrowDown, () => edits.ReorderChapter(chId, at + 1), "ghost sm", 18); down.SetEnabled(at < siblings.Count - 1);
            return U.Row(U.Num("Ch." + ch.Number, "sm"), name, up, down, U.IconBtn(GlyphKind.Cross, () => DeleteChapter(app, edits, chId), "ghost sm", 16)).Mt(12).Mb(4);
        }

        /// <summary>
        /// Deleting a chapter lists everything it would touch. If it still has planned videos the
        /// dialog offers to move them to another chapter rather than refusing and leaving the user stuck.
        /// </summary>
        public static void DeleteChapter(AppController app, PlanEdits edits, string chapterId, System.Action after = null)
        {
            var s = app.Session; var ch = s.Data.FindChapter(chapterId);
            if (ch == null) return;
            var impact = Deletions.ForChapter(s.Data, chapterId);
            void Go(string moveTo) { TripScreen.Guard(app, () => { edits.DeleteChapter(chapterId, moveTo); after?.Invoke(); }); }
            if (!impact.NeedsConfirm) { Go(null); return; }
            var options = new List<ChoiceSheet.Option>();
            bool hasContents = impact.Videos.Count > 0 || impact.Photos.Count > 0;
            if (hasContents)
                foreach (var t in impact.MoveTargets)
                {
                    var tid = t.Id;
                    options.Add(new ChoiceSheet.Option("Move its videos and photos to Ch." + t.Number + " " + t.Name, () => Go(tid)));
                }
            options.Add(new ChoiceSheet.Option(hasContents ? "Delete the chapter and its videos" : "Delete the chapter", () => Go(null), danger: true));
            var lines = impact.Lines();
            if (impact.Videos.Count > 0 && impact.MoveTargets.Count == 0) lines.Add("This is the book's only chapter, so there is nowhere to move its videos. Add another chapter first to keep them.");
            ChoiceSheet.Open(app, "Delete chapter “" + (string.IsNullOrEmpty(ch.Name) ? "Ch." + ch.Number : ch.Name) + "”?", lines, options.ToArray());
        }

        // ------------------------------------------------------------------ photos

        private static VisualElement PhotoRow(AppController app, PlanEdits edits, ShotListView.PhotoRow r, bool original)
        {
            var st = app.State; var L = st.SL; var E = st.Edit; var s = app.Session; var d = s.Data;
            var p = r.Photo; var id = p.Id;
            var open = L.Expanded.Contains(id);
            var box = new VisualElement().Cls("xrow").On(open);
            box.name = "row:" + id;
            var label = string.IsNullOrEmpty(r.Text) ? "Untitled photo" : U.ShortDescription(r.Text);
            var text = U.Col(U.Name(r.Dim ? U.Strike(label) : U.Esc(label), open, "body-sm"), Tags(r.Tags)).Cls("grow");
            var head = U.Tap(() => Toggle(app, id), "xhead grow", U.Chevron(open), new Glyph(GlyphKind.Frame, 18).Ml(10).Mr(12), text);
            box.Add(U.Row(head, r.Use != null ? U.Pill(r.Use, "ac").Ml(8) : null, U.StPhoto(p.Status, 30, p.HeroType != HeroType.None).Ml(8)));
            if (!open) return box;

            var body = new VisualElement().Cls("xbody");
            if (!E)
            {
                body.Add(U.Sub(U.Esc(Fmt.PhotoWhere(d, p))));
                var users = s.Plan.Items.Where(i => !i.IsDropped && !i.IsSuperseded && i.PhotoRefs.Contains(id)).ToList();
                if (users.Count > 0) body.Add(U.Sub("With " + string.Join(", ", users.Select(u => "#" + ShotListView.NumberOf(u)))).Mt(4));
            }
            else
            {
                var desc = U.Input(edits.Working ? p.Description : p.Planned?.Description, "What is in the photo", v => app.Edit(() => TripScreen.Guard(app, () => edits.SetPhotoDescription(id, v))), false, "tall");
                desc.name = "photo:" + id;
                desc.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { e.StopPropagation(); Toggle(app, id); } }, TrickleDown.TrickleDown);
                body.Add(U.Field("Description", desc));
                if (!edits.Working && p.Planned != null)
                {
                    body.Add(U.Lbl("Planned as"));
                    var chips = U.Row().Cls("wrap").Mb(6);
                    var ph = p.Planned;
                    chips.Add(U.Chip("Detail", ph.HeroType == HeroType.None, () => TripScreen.Guard(app, () => edits.SetPhotoUse(id, HeroType.None, null))));
                    chips.Add(U.Chip("Cover", ph.HeroType == HeroType.BookCover, () => TripScreen.Guard(app, () => edits.SetPhotoUse(id, HeroType.BookCover, null))));
                    foreach (var ch in d.ChaptersOf(ph.BookId))
                    {
                        var cid = ch.Id;
                        chips.Add(U.Chip("Ch." + ch.Number + " hero", ph.HeroType == HeroType.ChapterHero && ph.ChapterId == cid, () => TripScreen.Guard(app, () => edits.SetPhotoUse(id, HeroType.ChapterHero, cid))));
                    }
                    body.Add(chips);
                }
                else body.Add(U.Sub("What a photo is used as is set in Covers. The original list keeps what was planned."));
                body.Add(U.Row(U.Grow(), U.Btn(edits.Working ? "Drop photo" : "Delete photo", () => DeletePhoto(app, edits, p), "sm ghost danger")).Mt(6));
            }
            box.Add(body);
            return box;
        }

        private static void DeletePhoto(AppController app, PlanEdits edits, PhotoItem p)
        {
            var s = app.Session; var id = p.Id;
            void Go(bool deleteCaptures) { TripScreen.Guard(app, () => { edits.DeletePhoto(id, edits.Working ? "Dropped while editing the working list." : null, deleteCaptures); app.State.SL.Expanded.Remove(id); }); }
            var impact = Deletions.ForPhoto(s.Data, id);
            var verb = edits.Working ? "Drop" : "Delete";
            if (impact.Captures > 0)
            {
                var lines = impact.Lines().Concat(new[] { edits.Working ? "The drop shows in Changes." : "" }).Where(l => l.Length > 0);
                ChoiceSheet.Open(app, verb + " “" + U.ShortDescription(p.Description) + "”? It was shot.", lines,
                    new ChoiceSheet.Option("Detach and keep the record", () => Go(false)),
                    new ChoiceSheet.Option("Delete the record too", () => Go(true), danger: true));
                return;
            }
            if (edits.Working) { ConfirmSheet.Open(app, "Drop this photo?", "It stays on the original list, struck through, and the drop shows in Changes.", "Drop", () => Go(false)); return; }
            if (!impact.NeedsConfirm) { Go(false); return; }
            ChoiceSheet.Open(app, "Delete this photo from the original?", impact.Lines(), new ChoiceSheet.Option("Delete from the original", () => Go(false), danger: true));
        }

        // ------------------------------------------------------------------ changes

        private static void FillChanges(AppController app, ScrollView list)
        {
            var s = app.Session; var E = app.State.Edit;
            var view = ShotListView.Build(s, ShotListMode.Changes, false);
            if (view.Changes.Count == 0 && view.Corrections.Count == 0) { list.Add(U.Sub("No changes. The plan stands as printed.").Pad(20, 12)); return; }

            VisualElement Row(ShotListView.Change c)
            {
                var change = c;
                var when = U.FmtTimestamp(c.At);
                var text = U.Col(U.Text(c.Line, "body wraptext"), U.Sub(U.Esc(when + (string.IsNullOrEmpty(c.Reason) ? "" : " · " + c.Reason))).Mt(2)).Cls("grow");
                var row = U.Tap(() => { if (change.OpenItemId != null) app.OpenVideo(change.OpenItemId); }, "change grow", U.Pill(c.Label, c.Style + " label"), text);
                if (!E) return row;
                row.style.width = StyleKeyword.Auto;
                var am = change.Amendment;
                var filmed = am != null && am.Type == AmendmentType.Add && am.NewMedia != MediaKind.Photo
                    ? s.Data.Captures.Captures.Where(x => x.PlanVideoId != null && (am.Results?.Contains(x.PlanVideoId) ?? false)).ToList() : new List<Capture>();
                if (filmed.Count > 0)
                    return U.Row(row, U.Btn("Undo", () => ChoiceSheet.Open(app, "Undo the add? It was filmed.",
                        new[] { "“" + filmed[0].Title + "” is in the summary. Detached, it stays there as an unplanned capture tagged with what it was." },
                        new ChoiceSheet.Option("Detach and keep the capture", () => { s.UndoAmendment(am.Id); app.Toast("Undone · capture kept"); }),
                        new ChoiceSheet.Option("Delete the capture too", () => { s.UndoAmendment(am.Id, deleteCaptures: true); app.Toast("Undone"); }, danger: true)), "sm danger ml8"));
                var undo = U.Btn(change.Amendment != null ? "Undo" : "Remove", () => ConfirmSheet.Open(app,
                    change.Amendment != null ? "Undo “" + change.Label.ToLowerInvariant() + "”?" : "Remove this record?",
                    change.Amendment != null ? "The change is withdrawn and the working copy goes back to how it was before it." : "Only the record is removed. What it describes stays as it is now.",
                    change.Amendment != null ? "Undo" : "Remove",
                    () => { if (change.Amendment != null) s.UndoAmendment(change.Amendment.Id); else s.RemoveRecord(change.Record.Id); app.Toast("Done"); }), "sm danger ml8");
                return U.Row(row, undo);
            }

            if (view.Changes.Count > 0) list.Add(U.Eyebrow("Changes to the plan").Cls("chaphead"));
            foreach (var c in view.Changes) list.Add(Row(c));
            if (view.Corrections.Count > 0)
            {
                list.Add(U.Eyebrow("Corrections to the original").Cls("chaphead").Mt(22));
                list.Add(U.Sub("Fixes to what was typed. The plan did not change.").Ml(12).Mb(4));
                foreach (var c in view.Corrections) list.Add(Row(c));
            }
        }
    }

    /// <summary>Rename, combine or drop, each with a reason. The plan is not edited; the change is recorded.</summary>
    public static class AmendSheet
    {
        public static VisualElement Build(AppController app)
        {
            var s = app.Session; var L = app.State.SL; var a = L.Amend;
            var item = s.Plan.FindItem(a.ItemId);
            void Close() { L.Amend = null; app.State.Focus = null; app.Render(); }
            if (item == null) { L.Amend = null; return null; }

            void Apply()
            {
                try
                {
                    var id = a.Apply(s);
                    L.Amend = null;
                    L.Expanded.Remove(a.ItemId);
                    if (a.Mode == "combine" || a.Mode == "split") { L.Expanded.Add(id); L.ScrollTo = id; }
                    app.State.Focus = null;
                    app.Render();
                    app.Toast("Recorded in Changes");
                }
                catch (System.Exception ex) { app.Toast(ex.Message); }
            }
            var apply = U.Btn(a.Mode == "drop" ? "Drop" : a.Mode == "split" ? "Split" : "Save", () =>
            {
                // Dropping something filmed: the capture is kept and detached unless said otherwise.
                var filmed = a.Mode == "drop" ? a.CapturesOf(s) : new List<Capture>();
                if (filmed.Count == 0) { Apply(); return; }
                ChoiceSheet.Open(app, "Drop #" + ShotListView.NumberOf(item) + "? It was filmed.",
                    new[] { "“" + filmed[0].Title + "” is in the summary. Detached, it stays there as an unplanned capture tagged “was video " + ShotListView.NumberOf(item) + "”." },
                    new ChoiceSheet.Option("Detach and keep the capture", () => { a.DeleteCaptures = false; Apply(); }),
                    new ChoiceSheet.Option("Delete the capture too", () => { a.DeleteCaptures = true; Apply(); }, danger: true));
            }, "big " + (a.Mode == "drop" ? "danger" : "pri"));
            apply.SetEnabled(a.CanApply);

            var body = U.Scroll();
            if (a.Mode == "combine")
            {
                body.Add(U.Lbl("With"));
                var sibs = a.Siblings(s);
                if (sibs.Count == 0) body.Add(U.Sub("No other video in this chapter.").Mb(14));
                foreach (var sib in sibs)
                {
                    var sid = sib.Id;
                    var row = U.Tap(() => { a.ToggleWith(s, sid); app.RenderKeepFocus(null); }, "item",
                        U.With(new VisualElement(), "check sm" + (a.With.Contains(sid) ? " on" : ""), a.With.Contains(sid) ? new Glyph(GlyphKind.Check, 26, 3.5f) : null).Mr(10),
                        U.Num(ShotListView.NumberOf(sib)), U.Text(U.Esc(sib.Title), "bold grow wraptext"));
                    row.style.paddingLeft = 4;
                    body.Add(row);
                }
            }
            if (a.Mode == "drop") body.Add(U.Body("#" + ShotListView.NumberOf(item) + " " + U.Esc(item.Title)).Mb(14));
            else if (a.Mode == "split")
            {
                body.Add(U.Body("#" + ShotListView.NumberOf(item) + " " + U.Esc(item.Title)).Mb(10));
                body.Add(U.Field("Parts", U.Stepper(a.Parts.Count, delta => { a.SetPartCount(s, a.Parts.Count + delta); app.RenderKeepFocus(null); })));
                for (int i = 0; i < a.Parts.Count; i++)
                {
                    var idx = i;
                    var part = U.Input(a.Parts[i], null, v => { a.Parts[idx] = v; apply.SetEnabled(a.CanApply); });
                    part.name = "am.part" + i;
                    body.Add(U.Field("#" + ShotListView.NumberOf(item) + (char)('a' + i), part));
                }
                body.Add(U.Sub("Each part starts with #" + ShotListView.NumberOf(item) + "'s scene, SME, notes and photos. Edit them apart afterwards in Working edit. #" + ShotListView.NumberOf(item) + " is superseded and resolves to the parts.").Mb(14));
            }
            else
            {
                var title = U.Input(a.Title, null, v => { a.Title = v; apply.SetEnabled(a.CanApply); });
                title.name = "am.title";
                body.Add(U.Field(a.Mode == "rename" ? "Title as shot" : "Title", title).Mt(a.Mode == "combine" ? 8 : 0));
            }
            var reason = U.Input(a.Reason, null, v => a.Reason = v);
            reason.name = "am.reason";
            body.Add(U.Field("Reason", reason));

            var foot = U.Row(U.Grow(), apply).Mt(12);
            foot.style.flexShrink = 0;
            return app.Sheet(640, Close, app.SheetHead(AmendDraft.ModeTitle(a.Mode) + " · #" + ShotListView.NumberOf(item), Close), body, foot);
        }
    }
}

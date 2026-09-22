using System.Collections.Generic;
using System.Linq;
using MediaTrip.Authoring;
using MediaTrip.Model;
using MediaTrip.Persistence;
using MediaTrip.Query;
using MediaTrip.Status;
using MediaTrip.UI.Shell;
using MediaTrip.UI.Transfer;
using MediaTrip.UI.ViewModels;
using UnityEngine.UIElements;

namespace MediaTrip.UI.Docs
{
    /// <summary>
    /// One screen per book, nothing to drill into: chapters and their sections expand and
    /// collapse in place. A collapsed chapter or section shows what media is attached and the
    /// first words of its notes.
    ///
    /// The chapters are the shot list's chapters; the outline only hangs sections on them.
    /// NOTES and PLACED MEDIA are one idea at two levels: on the chapter as a whole, or on a
    /// section. Covers reads and writes the same chapter-level notes.
    ///
    /// The normal state annotates: notes, placing media, keeping a suggestion. Edit changes the
    /// document: chapter and section names, bullets, order, add and delete. So Edit has no
    /// notes field and no Place media.
    /// </summary>
    public static class OutlinesScreen
    {
        private static PlanEdits Edits(AppController app) => new PlanEdits(app.Session, PlanEditMode.Original);

        public static VisualElement Build(AppController app)
        {
            var st = app.State; var O = st.OL; var s = app.Session; var d = s.Data; var E = st.Edit;
            if (O.Book == null || d.FindBook(O.Book) == null) O.Book = d.Trip.Books.FirstOrDefault()?.Id;
            var bookId = O.Book;

            // Book buttons wrap, and a button grows to fit its whole name.
            var books = U.Row().Cls("wrap noshrink bookbar");
            foreach (var b in d.Trip.Books)
            {
                var bid = b.Id;
                books.Add(U.Chip(Fmt.BookName(b), bookId == bid, () => { O.Book = bid; O.PlaceAt = null; st.Focus = null; app.Render(); }, "wrapok"));
            }

            // Expand all / Collapse all, as on the shot list: the outline reads like the document.
            var phone = app.Layout.Phone;
            var controls = new VisualElement().Cls("controls");
            controls.Add(U.Btn(phone ? "Expand" : "Expand all", () => ExpandAll(app, true), "sm ghost accent"));
            controls.Add(U.Btn(phone ? "Collapse" : "Collapse all", () => ExpandAll(app, false), "sm ghost accent"));

            var list = U.Scroll().Named("ol:" + bookId).Cls("listpad");
            if (bookId == null) list.Add(U.Sub("Add a book in Trip first.").Pad(20, 12));
            else Fill(app, list, bookId);
            return AppShell.Build(app, "Outlines", U.Col(books, bookId == null ? null : controls, list).Cls("grow minh0"), edit: true);
        }

        /// <summary>Open (or close) every chapter and section of the book being looked at.</summary>
        public static void ExpandAll(AppController app, bool open)
        {
            var O = app.State.OL; var d = app.Session.Data;
            O.OpenChapters.Clear(); O.OpenSections.Clear(); O.PlaceAt = null;
            if (open && O.Book != null)
            {
                foreach (var ch in d.ChaptersOf(O.Book)) O.OpenChapters.Add(ch.Id);
                foreach (var oc in d.FindOutline(O.Book)?.Chapters ?? new List<OutlineChapter>())
                    foreach (var sec in oc.Sections) O.OpenSections.Add(sec.Id);
            }
            app.State.Focus = null;
            app.Render();
        }

        public static VisualElement Overlay(AppController app) => null;

        private static void Fill(AppController app, ScrollView list, string bookId)
        {
            var st = app.State; var O = st.OL; var s = app.Session; var d = s.Data; var E = st.Edit;
            var outline = d.FindOutline(bookId);
            var chapters = d.ChaptersOf(bookId).ToList();
            var hasSections = outline != null && outline.Chapters.Any(c => c.Sections.Count > 0);

            if (!hasSections && !E)
            {
                var card = U.Card().Mt(12).Mb(8);
                card.Add(U.H2("No outline yet").Mb(12));
                card.Add(U.Row(U.Btn("Type it in", () => TypeItIn(app, bookId), "big pri"), U.Btn("Import", () => ImportFlow.Begin(app, DocumentKind.Outline, bookId), "big ml10")).Cls("wrap"));
                list.Add(card);
                if (chapters.Count > 0) list.Add(U.Sub("Notes and media can already be put on a chapter as a whole.").Ml(12).Mt(4));
            }

            foreach (var ch in chapters)
            {
                var oc = outline?.Chapters.FirstOrDefault(c => c.Id == ch.Id);
                list.Add(E ? ChapterEdit(app, bookId, ch, oc) : ChapterRead(app, bookId, ch, oc));
            }
            if (E)
            {
                list.Add(U.AddBtn("Chapter", () => AddChapter(app, bookId, null)).Mt(8));
                WireKeyboard(app, bookId);
                list.AddManipulator(new ReorderManipulator(ids => ReorderChapters(app, ids), (id, h) => MoveMenu(app, h, () => MoveChapter(app, id, -1), () => MoveChapter(app, id, 1)), "ochap", "hdc"));
                list.AddManipulator(new ReorderManipulator(ids => ReorderSections(app, bookId, ids), (id, h) => MoveMenu(app, h, () => MoveSection(app, bookId, id, -1), () => MoveSection(app, bookId, id, 1)), "osec", "hds"));
            }
            else if (chapters.Count == 0) list.Add(U.Sub("This book has no chapters yet. Tap Edit to add them.").Pad(20, 12));
            if (O.ScrollTo != null) { app.ScrollToNamed(O.ScrollTo); O.ScrollTo = null; }
        }

        /// <summary>"Type it in" stays on this screen: the outline exists, Edit is on, and the caret is in the first chapter's name.</summary>
        private static void TypeItIn(AppController app, string bookId)
        {
            var s = app.Session;
            s.Outline.Ensure(bookId);
            var first = s.Data.ChaptersOf(bookId).FirstOrDefault();
            app.State.Edit = true;
            if (first == null) { AddChapter(app, bookId, null); return; }
            app.State.OL.OpenChapters.Add(first.Id);
            app.RenderKeepFocus(string.IsNullOrWhiteSpace(first.Name) ? "oc:" + first.Id : AddSectionQuiet(app, bookId, first.Id));
        }

        private static string AddSectionQuiet(AppController app, string bookId, string chapterId, int? index = null)
        {
            var s = app.Session;
            s.Outline.Ensure(bookId);
            var sec = s.Outline.AddSection(bookId, chapterId, "", index);
            app.State.OL.OpenChapters.Add(chapterId);
            return "os:" + sec.Id;
        }

        private static void AddChapter(AppController app, string bookId, string afterChapterId)
        {
            var s = app.Session;
            s.Outline.Ensure(bookId);
            var ch = Edits(app).AddChapter(bookId, "");
            if (afterChapterId != null)
            {
                var at = s.Data.ChaptersOf(bookId).ToList().FindIndex(c => c.Id == afterChapterId);
                if (at >= 0) s.PlanEditor.ReorderChapter(ch.Id, at + 1);
            }
            app.State.OL.OpenChapters.Add(ch.Id);
            app.RenderKeepFocus("oc:" + ch.Id);
        }

        // ------------------------------------------------------------------ normal state: read and annotate

        private static VisualElement Summary(PlaceSummary sum)
        {
            if (sum.IsEmpty) return null;
            var parts = new List<string>();
            if (sum.Media.Count > 0) parts.Add(string.Join(" · ", sum.Media.Select(m => U.ShortDescription(m))));
            if (sum.NoteExcerpt.Length > 0) parts.Add("“" + sum.NoteExcerpt + "”");
            return U.Name(U.Esc(string.Join("  —  ", parts)), false, "sub");
        }

        private static VisualElement CountPill(PlaceSummary sum) =>
            sum.Media.Count == 0 ? null : U.Pill(sum.Media.Count.ToString(), sum.Suggested > 0 ? "ac" : "ok").Ml(8);

        private static VisualElement ChapterRead(AppController app, string bookId, Chapter ch, OutlineChapter oc)
        {
            var O = app.State.OL; var s = app.Session; var chId = ch.Id;
            var open = O.OpenChapters.Contains(chId);
            var box = new VisualElement().Cls("xrow chapter").On(open);
            box.name = "ch:" + chId;
            var sum = PlaceSummary.Of(s, chId, null);
            var unplaced = s.Queries.UnplacedIn(chId);
            var text = U.Col(U.Name(U.Esc(string.IsNullOrEmpty(ch.Name) ? "Untitled chapter" : ch.Name), open, "h3"), open ? null : Summary(sum)).Cls("grow");
            var head = U.Tap(() => { if (!O.OpenChapters.Remove(chId)) O.OpenChapters.Add(chId); O.PlaceAt = null; app.State.Focus = null; app.Render(); }, "xhead grow", U.Chevron(open), U.Num(ch.Number.ToString(), "sm").Ml(6), text);
            box.Add(U.Row(head, unplaced.Count > 0 ? U.Pill(unplaced.Count + " unplaced", "warn").Ml(8) : null, CountPill(sum)));
            if (!open) return box;

            var body = new VisualElement().Cls("xbody");
            body.Add(Annotations(app, bookId, chId, null, "Chapter notes and media", unplaced));
            foreach (var sec in oc?.Sections ?? new List<OutlineSection>()) body.Add(SectionRead(app, bookId, ch, sec));
            if ((oc?.Sections.Count ?? 0) == 0) body.Add(U.Sub("No sections in this chapter yet.").Mt(8));
            box.Add(body);
            return box;
        }

        private static VisualElement SectionRead(AppController app, string bookId, Chapter ch, OutlineSection sec)
        {
            var O = app.State.OL; var s = app.Session; var sid = sec.Id;
            var open = O.OpenSections.Contains(sid);
            var box = new VisualElement().Cls("xrow section").On(open);
            box.name = "sec:" + sid;
            var sum = PlaceSummary.Of(s, ch.Id, sid);
            var text = U.Col(U.Name(U.Esc(string.IsNullOrEmpty(sec.Name) ? "Untitled section" : sec.Name), open, "body-sm bold"), open ? null : Summary(sum)).Cls("grow");
            var head = U.Tap(() => { if (!O.OpenSections.Remove(sid)) O.OpenSections.Add(sid); O.PlaceAt = null; app.State.Focus = null; app.Render(); }, "xhead grow",
                U.Chevron(open), U.Text(U.Esc(sec.Number ?? ""), "sub bold").W(40).Ml(6), text);
            box.Add(U.Row(head, CountPill(sum)));
            if (!open) return box;

            var body = new VisualElement().Cls("xbody");
            if (sec.Nodes.Count > 0) body.Add(NotesView.Build(sec.Nodes)); else body.Add(U.Sub("No bullets."));
            body.Add(Annotations(app, bookId, ch.Id, sid, "Notes and media", null).Mt(14));
            box.Add(body);
            return box;
        }

        /// <summary>Notes and placed media, for a chapter as a whole (sectionId null) or for one section. The same block at both levels.</summary>
        private static VisualElement Annotations(AppController app, string bookId, string chapterId, string sectionId, string label, List<TimelineEntry> unplaced)
        {
            var O = app.State.OL; var s = app.Session;
            var key = chapterId + "|" + (sectionId ?? "");
            var col = U.Col();
            col.Add(U.Eyebrow(label).Mb(6));
            var note = U.Input(s.NoteText(chapterId, sectionId), "Notes…", v => app.Edit(() => s.SetNote(bookId, chapterId, sectionId, v)), true, "h72");
            note.name = "note:" + key;
            col.Add(note.Mb(10));

            foreach (var m in s.Queries.PlacedIn(chapterId, sectionId)) col.Add(PlacedRow(app, m));
            foreach (var u in unplaced ?? new List<TimelineEntry>())
            {
                var entry = u;
                var title = entry.IsVideo ? entry.Capture.Title : (entry.PhotoCapture.PhotoId != null ? s.Plan.FindPhoto(entry.PhotoCapture.PhotoId)?.Description : entry.PhotoCapture.Text);
                var row = U.Row(new Glyph(entry.IsVideo ? GlyphKind.Play : GlyphKind.Frame, 18).Cls("muted").Mr(10),
                    U.Col(U.Name(U.Esc(title ?? ""), true, "bold"), U.Sub("not placed yet")).Cls("grow"),
                    U.Btn("Place here", () => s.Assign(new MediaRef(entry.IsVideo ? MediaRefKind.Capture : MediaRefKind.PhotoCapture, entry.Id), bookId, chapterId, null, null, AssignmentSource.Manual, null, true), "sm ml8")).MinH(52).Cls("bordered-bottom");
                col.Add(row);
            }

            if (O.PlaceAt == key)
            {
                var picker = PickerField.Build(app, "place:" + key, O.PlaceQuery, "Find a video or photo, or type a new one…", v => O.PlaceQuery = v,
                    q => PlaceRows(app, bookId, chapterId, sectionId, q));
                col.Add(U.Row(picker, U.Btn("Cancel", () => { O.PlaceAt = null; O.PlaceQuery = ""; st(app).Focus = null; app.Render(); }, "sm ghost ml8")).Mt(10));
            }
            else
            {
                var place = U.Btn("+ Place media", () => { O.PlaceAt = key; O.PlaceQuery = ""; app.RenderKeepFocus("place:" + key); }, "sm");
                place.style.alignSelf = Align.FlexStart;
                col.Add(place.Mt(10));
            }
            return col;
        }

        private static AppState st(AppController app) => app.State;

        private static VisualElement PlacedRow(AppController app, AssignedMedia m)
        {
            var s = app.Session; var aid = m.Assignment.Id;
            VisualElement icon;
            var isVideo = m.Kind == MediaRefKind.Capture || m.Kind == MediaRefKind.PlannedVideo;
            if (m.Confirmed) icon = new Glyph(isVideo ? GlyphKind.Play : GlyphKind.Frame, 18).Cls("ok-text");
            else icon = U.Text("?", "accent bold").Font(20);
            icon.style.width = 28;
            var row = U.Row(icon).MinH(52).Cls("bordered-bottom");
            var dayId = m.Capture?.DayId ?? m.PhotoCapture?.DayId;
            var time = U.FmtTime(m.Capture?.At ?? m.PhotoCapture?.At);
            var when = dayId != null ? app.DayLabel(dayId) + (time.Length > 0 ? " · " + time : "") : (m.PlannedVideo != null || m.PlannedPhoto != null ? "not shot yet" : "");
            if (!m.Confirmed) when += (when.Length > 0 ? " · " : "") + "suggested";
            row.Add(U.Col(U.Name(U.Esc(m.Title), true, "bold"), U.Sub(U.Esc(when))).Cls("grow"));
            if (!m.Confirmed) row.Add(U.GlyphBtn(GlyphKind.Check, "Keep", () => s.ConfirmAssignment(aid), "sm pri ml8", 16));
            row.Add(U.IconBtn(GlyphKind.Cross, () => s.RemoveAssignment(aid), "ghost sm", 16));
            return row;
        }

        /// <summary>
        /// One input: fuzzy matches over the shot list's videos and every photo, each saying what
        /// it is, then "new photo" and "new video" for what was typed. Something new goes through
        /// the one path for new media, so it lands in the working shot list in this book and chapter.
        /// </summary>
        private static List<PickerField.Row> PlaceRows(AppController app, string bookId, string chapterId, string sectionId, string q)
        {
            var O = app.State.OL; var s = app.Session; var d = s.Data;
            var here = new HashSet<string>(s.Queries.PlacedIn(chapterId, sectionId).Select(m => m.Assignment.MediaRef?.Id));
            void Done() { O.PlaceAt = null; O.PlaceQuery = ""; app.State.Focus = null; app.Render(); }
            void Place(MediaRefKind kind, string id) { s.Assign(new MediaRef(kind, id), bookId, chapterId, sectionId, null, AssignmentSource.Manual, null, true); Done(); }

            var rows = new List<PickerField.Row>();
            foreach (var m in s.Search.SearchShotList(q, 5))
            {
                var it = m.Item; var cap = it.Captures.FirstOrDefault();
                var refId = cap != null ? cap.Id : it.Id;
                if (here.Contains(refId) || here.Contains(it.Id)) continue;
                rows.Add(new PickerField.Row
                {
                    Lead = new Glyph(GlyphKind.Play, 18).Mr(10), Title = it.Title,
                    Sub = ShotListView.Ref(it) + " · " + Fmt.BookChapter(d, it.BookId, it.ChapterId) + (cap != null ? " · filmed" : " · not filmed yet"),
                    Pick = () => Place(cap != null ? MediaRefKind.Capture : MediaRefKind.PlannedVideo, refId),
                });
            }
            foreach (var m in s.Search.SearchPhotos(GlobalSearch.HeroAlias(q), 5))
            {
                var p = m.Item;
                if (here.Contains(p.Id) || p.Status == PhotoStatus.Dropped) continue;
                rows.Add(new PickerField.Row
                {
                    Lead = new Glyph(GlyphKind.Frame, 18).Mr(10), Title = p.Description,
                    Sub = Fmt.PhotoWhere(d, p) + (p.Status == PhotoStatus.Captured ? " · shot" : " · not shot yet"),
                    Pick = () => Place(MediaRefKind.PlannedPhoto, p.Id),
                });
            }
            // photos typed in the field, not on the shot list yet: picking one files it here
            foreach (var m in s.Search.SearchLoosePhotos(q, 4))
            {
                var lp = m.Item;
                rows.Add(new PickerField.Row
                {
                    Lead = new Glyph(GlyphKind.Frame, 18).Cls("muted").Mr(10), Title = lp.Text,
                    Sub = "Not on the shot list yet · " + Fmt.LooseWhere(app, lp) + " · files it under " + Fmt.BookChapter(d, bookId, chapterId),
                    Pick = () => Place(MediaRefKind.PlannedPhoto, s.FilePhoto(lp.Text, bookId, chapterId, "Filed from Outlines.")),
                });
            }
            var text = q.Trim();
            if (rows.Any(r => !r.IsCreate && string.Equals(r.Title, text, System.StringComparison.OrdinalIgnoreCase))) return rows;
            rows.Add(new PickerField.Row { Lead = new Glyph(GlyphKind.Plus, 18).Mr(10), IsCreate = true, Title = "New photo “" + text + "”", Sub = "Added to the working shot list, " + Fmt.BookChapter(d, bookId, chapterId),
                Pick = () => Place(MediaRefKind.PlannedPhoto, s.CreateMedia(MediaKind.Photo, text, bookId, chapterId, "Added from Outlines.")) });
            rows.Add(new PickerField.Row { Lead = new Glyph(GlyphKind.Plus, 18).Mr(10), IsCreate = true, Title = "New video “" + text + "”", Sub = "Added to the working shot list, " + Fmt.BookChapter(d, bookId, chapterId),
                Pick = () => Place(MediaRefKind.PlannedVideo, s.CreateMedia(MediaKind.Video, text, bookId, chapterId, "Added from Outlines.")) });
            return rows;
        }

        // ------------------------------------------------------------------ edit state: change the document

        private static VisualElement ChapterEdit(AppController app, string bookId, Chapter ch, OutlineChapter oc)
        {
            var O = app.State.OL; var s = app.Session; var chId = ch.Id;
            var box = new VisualElement().Cls("ochap");
            box.userData = chId;
            box.name = "ch:" + chId;
            var handle = new VisualElement().Cls("hdc handle"); handle.Add(new Glyph(GlyphKind.Lines, 20));
            var name = U.Input(ch.Name, "Chapter name", v => app.Edit(() => Edits(app).SetChapterName(chId, v)), false, "h48 grow bold");
            name.name = "oc:" + chId;
            box.Add(U.Row(handle, U.Num(ch.Number.ToString(), "sm"), name,
                U.IconBtn(GlyphKind.Cross, () => ShotListScreen.DeleteChapter(app, Edits(app), chId), "ghost sm", 16)).Mt(10));

            var sections = new VisualElement().Cls("osecs");
            foreach (var sec in oc?.Sections ?? new List<OutlineSection>()) sections.Add(SectionEdit(app, bookId, ch, sec));
            box.Add(sections);
            box.Add(U.AddBtn("Section", () => app.RenderKeepFocus(AddSectionQuiet(app, bookId, chId))).Ml(44));
            return box;
        }

        private static VisualElement SectionEdit(AppController app, string bookId, Chapter ch, OutlineSection sec)
        {
            var O = app.State.OL; var s = app.Session; var sid = sec.Id;
            var open = O.OpenSections.Contains(sid);
            var box = new VisualElement().Cls("osec");
            box.userData = sid;
            box.name = "sec:" + sid;
            var handle = new VisualElement().Cls("hds handle"); handle.Add(new Glyph(GlyphKind.Lines, 18));
            var name = U.Input(sec.Name, "Section name", v => app.Edit(() => s.Outline.SetSectionName(bookId, sid, v)), false, "h44 grow");
            name.name = "os:" + sid;
            var bullets = U.Tap(() => { if (!O.OpenSections.Remove(sid)) O.OpenSections.Add(sid); app.State.Focus = null; app.Render(); }, "ob", U.Chevron(open));
            box.Add(U.Row(handle, U.Text(U.Esc(sec.Number ?? ""), "sub bold").W(40), name, bullets,
                U.IconBtn(GlyphKind.Cross, () => DeleteSection(app, bookId, sid), "ghost sm", 16)));
            if (open)
            {
                var ed = new VisualElement().Cls("xbody");
                ed.style.marginLeft = 44;
                ed.Add(NodeEditor.Build(app, s.Outline.Bullets(bookId, sid), new AppState.PasteState { Kind = "section", BookId = bookId, Id = sid }));
                box.Add(ed);
            }
            else if (sec.Nodes.Count > 0) box.Add(U.Sub(U.Plural(NodeTree.Count(sec.Nodes), "bullet")).Ml(84).Mb(4));
            return box;
        }

        /// <summary>Deleting a section that has placed media: confirm, then unassign it. The media stays in the book, listed as unplaced on the chapter.</summary>
        private static void DeleteSection(AppController app, string bookId, string sectionId)
        {
            var s = app.Session;
            var sec = s.Data.FindOutlineSection(bookId, sectionId);
            if (sec == null) return;
            var impact = Deletions.ForSection(s.Data, bookId, sectionId);
            void Go() { TripScreen.Guard(app, () => { Deletions.DeleteSection(s, bookId, sectionId); app.State.OL.OpenSections.Remove(sectionId); }); }
            if (!impact.NeedsConfirm) { Go(); return; }
            ChoiceSheet.Open(app, "Delete section “" + (string.IsNullOrEmpty(sec.Name) ? sec.Number : sec.Name) + "”?", impact.Lines(), new ChoiceSheet.Option("Delete the section", Go, danger: true));
        }

        // ------------------------------------------------------------------ reorder: drag, or move up / down

        private static void MoveMenu(AppController app, VisualElement handle, System.Action up, System.Action down)
        {
            var box = new VisualElement().Cls("sugg");
            box.Add(U.Tap(() => { app.ClosePopup(); up(); }, "sugg-row", new Glyph(GlyphKind.ArrowUp, 18).Mr(10), U.Text("Move up", "bold")));
            box.Add(U.Tap(() => { app.ClosePopup(); down(); }, "sugg-row", new Glyph(GlyphKind.ArrowDown, 18).Mr(10), U.Text("Move down", "bold")));
            app.ShowPopup(handle, box, 200, 200, 0, dismissOnOutsideTap: true);
        }

        private static void ReorderChapters(AppController app, List<string> ids)
        {
            var edits = Edits(app);
            for (int i = 0; i < ids.Count; i++) if (ids[i] != null && app.Session.Data.FindChapter(ids[i]) != null) edits.ReorderChapter(ids[i], i);
            app.Render();
        }

        private static void MoveChapter(AppController app, string chapterId, int delta)
        {
            var ch = app.Session.Data.FindChapter(chapterId);
            if (ch == null) return;
            var at = app.Session.Data.ChaptersOf(ch.BookId).ToList().FindIndex(c => c.Id == chapterId);
            Edits(app).ReorderChapter(chapterId, at + delta);
            app.RenderKeepFocus("oc:" + chapterId);
        }

        private static void ReorderSections(AppController app, string bookId, List<string> ids)
        {
            for (int i = 0; i < ids.Count; i++) if (ids[i] != null && app.Session.Data.FindOutlineSection(bookId, ids[i]) != null) app.Session.Outline.ReorderSection(bookId, ids[i], i);
            app.Render();
        }

        private static void MoveSection(AppController app, string bookId, string sectionId, int delta)
        {
            var ch = app.Session.Outline.ChapterOfSection(bookId, sectionId);
            if (ch == null) return;
            var at = ch.Sections.FindIndex(x => x.Id == sectionId);
            app.Session.Outline.ReorderSection(bookId, sectionId, at + delta);
            app.RenderKeepFocus("os:" + sectionId);
        }

        // ------------------------------------------------------------------ keyboard: a whole outline without touching the screen

        private static void WireKeyboard(AppController app, string bookId)
        {
            var s = app.Session;
            app.NameKeyAction = (field, act) =>
            {
                var isChapter = field.StartsWith("oc:");
                var id = field.Substring(3);
                var order = NameFields(app, bookId);
                var at = order.IndexOf(field);
                switch (act)
                {
                    case "enter":
                        if (isChapter)
                        {
                            var first = s.Data.FindOutline(bookId)?.Chapters.FirstOrDefault(c => c.Id == id)?.Sections.FirstOrDefault();
                            app.RenderKeepFocus(first != null ? "os:" + first.Id : AddSectionQuiet(app, bookId, id));
                        }
                        else
                        {
                            var ch = s.Outline.ChapterOfSection(bookId, id);
                            app.RenderKeepFocus(AddSectionQuiet(app, bookId, ch.Id, ch.Sections.FindIndex(x => x.Id == id) + 1));
                        }
                        return true;
                    case "chapter":
                        AddChapter(app, bookId, isChapter ? id : s.Outline.ChapterOfSection(bookId, id)?.Id);
                        return true;
                    case "tab":
                        if (isChapter) return false;
                        app.State.OL.OpenSections.Add(id);
                        var bullets = s.Outline.Bullets(bookId, id);
                        var node = bullets.Roots.FirstOrDefault() ?? bullets.AddRoot("");
                        app.RenderKeepFocus("node:" + node.Id);
                        return true;
                    case "prev": if (at > 0) { app.RenderKeepFocus(order[at - 1]); return true; } return false;
                    case "next": if (at >= 0 && at < order.Count - 1) { app.RenderKeepFocus(order[at + 1]); return true; } return false;
                    case "moveup": case "movedown":
                        if (isChapter) MoveChapter(app, id, act == "moveup" ? -1 : 1); else MoveSection(app, bookId, id, act == "moveup" ? -1 : 1);
                        return true;
                    case "del":
                        if (isChapter) return false;
                        if (Deletions.ForSection(s.Data, bookId, id).NeedsConfirm) return false;
                        Deletions.DeleteSection(s, bookId, id);
                        app.RenderKeepFocus(at > 0 ? order[at - 1] : null);
                        return true;
                }
                return false;
            };
        }

        /// <summary>The name fields top to bottom: chapter, its sections, next chapter, …</summary>
        private static List<string> NameFields(AppController app, string bookId)
        {
            var d = app.Session.Data; var outline = d.FindOutline(bookId);
            var list = new List<string>();
            foreach (var ch in d.ChaptersOf(bookId))
            {
                list.Add("oc:" + ch.Id);
                foreach (var sec in outline?.Chapters.FirstOrDefault(c => c.Id == ch.Id)?.Sections ?? new List<OutlineSection>()) list.Add("os:" + sec.Id);
            }
            return list;
        }
    }
}

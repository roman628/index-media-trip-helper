using System.Collections.Generic;
using System.Linq;
using MediaTrip.Model;
using MediaTrip.Query;
using MediaTrip.UI.Shell;
using MediaTrip.UI.Transfer;
using MediaTrip.UI.ViewModels;
using UnityEngine.UIElements;

namespace MediaTrip.UI.Docs
{
    /// <summary>
    /// Book chips, the chapter list, and one section: the outline text with "My notes" and
    /// "Placed here" beside it. A suggested placement is kept with one tap. Edit is for typing
    /// an outline in and fixing mistakes.
    /// </summary>
    public static class OutlinesScreen
    {
        public static VisualElement Build(AppController app)
        {
            var st = app.State; var O = st.OL; var s = app.Session; var d = s.Data; var land = app.Layout.Land;
            if (O.Book == null || d.FindBook(O.Book) == null) O.Book = d.Trip.Books.FirstOrDefault()?.Id;
            var outline = O.Book != null ? d.FindOutline(O.Book) : null;

            var books = U.Row().Cls("wrap noshrink");
            books.style.paddingLeft = 12; books.style.paddingTop = 10; books.style.paddingRight = 4;
            foreach (var b in d.Trip.Books.OrderBy(x => x.Number))
            {
                var bid = b.Id;
                books.Add(U.Chip("Book " + b.Number + " · " + b.Name, O.Book == bid, () =>
                {
                    O.Book = bid;
                    O.Section = d.FindOutline(bid)?.Chapters.SelectMany(c => c.Sections).FirstOrDefault()?.Id;
                    O.ListOpen = true;
                    st.Focus = null;
                    app.Render();
                }, "wrapok"));
            }

            if (outline == null)
            {
                var empty = U.Col().Cls("grow center");
                empty.style.alignItems = Align.Center; empty.style.paddingLeft = 24; empty.style.paddingRight = 24;
                if (O.Book == null) empty.Add(U.Sub("Add a book in Trip first."));
                else
                {
                    empty.Add(U.H2("No outline yet").Mb(14));
                    empty.Add(U.Row(U.Btn("Type it in", () => Create(app), "big pri"), U.Btn("Import", () => ImportFlow.Begin(app), "big ml10")).Cls("wrap center"));
                }
                return AppShell.Build(app, "Outlines", U.Col(books, empty).Cls("grow minh0"));
            }

            OutlineSection cur = null; OutlineChapter curCh = null;
            foreach (var c in outline.Chapters) foreach (var sec in c.Sections) if (sec.Id == O.Section) { cur = sec; curCh = c; }
            if (cur == null && land)
            {
                curCh = outline.Chapters.FirstOrDefault(c => c.Sections.Count > 0);
                cur = curCh?.Sections[0];
                O.Section = cur?.Id;
            }

            VisualElement body;
            if (land)
            {
                var left = U.Col(books, ChapterList(app, outline)).Cls("bordered-right minh0 noshrink");
                left.style.width = app.Layout.XWide ? 360 : 320;
                body = U.Row(left, SectionView(app, outline, curCh, cur)).Cls("grow stretch minh0");
            }
            else if (O.ListOpen || cur == null) body = U.Col(books, ChapterList(app, outline)).Cls("grow minh0");
            else body = SectionView(app, outline, curCh, cur);
            return AppShell.Build(app, "Outlines", body, edit: true);
        }

        public static VisualElement Overlay(AppController app)
        {
            var O = app.State.OL;
            if (!O.Place || O.Section == null) return null;
            var sec = app.Session.Data.FindOutlineSection(O.Book, O.Section);
            return sec == null ? null : PlaceSheet(app, sec);
        }

        private static void Create(AppController app)
        {
            var O = app.State.OL; var s = app.Session;
            var outline = s.Outline.Ensure(O.Book);
            var first = outline.Chapters.FirstOrDefault();
            if (first == null) first = s.Outline.AddChapter(O.Book, "");
            var sec = s.Outline.AddSection(O.Book, first.Id, "");
            O.Section = sec.Id;
            O.ListOpen = false;
            app.State.Edit = true;
            app.RenderKeepFocus("os:" + sec.Id);
        }

        // ------------------------------------------------------------------ chapter list

        private static VisualElement ChapterList(AppController app, OutlineDocument outline)
        {
            var O = app.State.OL; var s = app.Session; var E = app.State.Edit; var bookId = O.Book;
            var list = U.Scroll().Named("ol-list:" + bookId);
            list.ContentPad(6, 8, 20, 8);
            var counts = s.Data.Captures.OutlineAssignments.Where(a => a.BookId == bookId && a.SectionId != null).GroupBy(a => a.SectionId).ToDictionary(g => g.Key, g => g.Count());
            foreach (var c in outline.Chapters)
            {
                var cid = c.Id;
                var head = U.Row(U.Num(c.Number.ToString(), "sm")).MinH(48);
                head.style.paddingLeft = 8; head.style.paddingTop = 10; head.style.paddingRight = 4;
                if (E)
                {
                    var name = U.Input(c.Name, "Chapter name", v => app.Edit(() => s.Outline.SetChapterName(bookId, cid, v)), false, "h44 grow");
                    name.name = "oc:" + cid;
                    head.Add(name);
                    head.Add(U.IconBtn(GlyphKind.Cross, () => TripScreen.Guard(app, () => s.Outline.RemoveChapter(bookId, cid)), "ghost sm", 16));
                }
                else head.Add(U.H3(U.Esc(c.Name)).Cls("grow"));
                list.Add(head);
                foreach (var sec in c.Sections)
                {
                    var sid = sec.Id;
                    counts.TryGetValue(sid, out var n);
                    var row = U.Tap(() => { O.Section = sid; O.ListOpen = false; app.State.Focus = null; app.Render(); }, "item h52",
                        U.Text(U.Esc(sec.Number ?? ""), "sub bold").W(40),
                        U.Text(U.Esc(string.IsNullOrEmpty(sec.Name) ? "Untitled" : sec.Name), "body-sm bold grow"),
                        n > 0 ? U.Pill(n.ToString(), "ok") : U.Pill("—")).On(O.Section == sid);
                    row.style.paddingLeft = 16;
                    list.Add(row);
                }
                if (E) list.Add(U.AddBtn("Section", () =>
                {
                    var sec = s.Outline.AddSection(bookId, cid, "");
                    O.Section = sec.Id; O.ListOpen = false;
                    app.RenderKeepFocus("os:" + sec.Id);
                }).Ml(10));
            }
            if (E) list.Add(U.AddBtn("Chapter", () => { var c = s.Outline.AddChapter(bookId, ""); app.RenderKeepFocus("oc:" + c.Id); }).Mt(8).Ml(4));
            return list;
        }

        // ------------------------------------------------------------------ one section

        private static VisualElement SectionView(AppController app, OutlineDocument outline, OutlineChapter ch, OutlineSection sec)
        {
            var O = app.State.OL; var s = app.Session; var E = app.State.Edit; var bookId = O.Book;
            if (sec == null) return U.Sub(E ? "Add a section to start." : "Pick a section.").Pad(20);
            var wide = app.Layout.XWide;
            var view = U.Scroll().Named("ol-sec:" + sec.Id).Cls("pad");
            if (!app.Layout.Land)
            {
                var back = U.GlyphBtn(GlyphKind.ChevronLeft, "Chapters", () => { O.ListOpen = true; app.State.Focus = null; app.Render(); }, "sm ghost accent");
                back.style.alignSelf = Align.FlexStart; back.style.marginLeft = -12; back.style.marginBottom = 6;
                view.Add(back);
            }
            view.Add(U.Eyebrow("Chapter " + ch.Number + " · " + ch.Name));
            if (E)
            {
                var name = U.Input(sec.Name, "Section name", v => app.Edit(() => s.Outline.SetSectionName(bookId, sec.Id, v)), false, "tall");
                name.name = "os:" + sec.Id;
                view.Add(name.Mt(6).Mb(12));
            }
            else view.Add(U.H1(U.Esc((sec.Number ?? "") + " " + sec.Name)).Mt(4).Mb(14));

            // ---- outline text (left)
            var text = U.Col().Cls("grow");
            if (E) text.Add(NodeEditor.Build(app, s.Outline.Bullets(bookId, sec.Id), new AppState.PasteState { Kind = "section", BookId = bookId, Id = sec.Id }));
            else if (sec.Nodes.Count > 0) text.Add(NotesView.Build(sec.Nodes));
            else text.Add(U.Sub("No bullets."));
            if (E || !string.IsNullOrEmpty(ch.Notes)) text.Add(ChapterNotes(app, bookId, ch, E));
            if (E)
            {
                var del = U.Btn("Delete section", () => TripScreen.Guard(app, () => { s.Outline.RemoveSection(bookId, sec.Id); O.Section = null; O.ListOpen = true; }), "sm ghost danger left");
                del.style.alignSelf = Align.FlexStart;
                text.Add(del.Mt(16));
            }

            // ---- my notes and placed here (beside it)
            var side = U.Col();
            side.Add(U.Eyebrow("My notes").Mb(6));
            var mine = U.Input(s.SectionNoteText(sec.Id), "…", v => app.Edit(() => s.SetSectionNote(bookId, sec.Id, v)), true, "h96");
            mine.name = "sn:" + sec.Id;
            side.Add(mine.Mb(16));
            side.Add(U.Eyebrow("Placed here").Mb(6));
            var placed = s.Data.Captures.OutlineAssignments.Where(a => a.BookId == bookId && a.SectionId == sec.Id).Select(s.Queries.ResolveAssignment).ToList();
            foreach (var m in placed) side.Add(PlacedRow(app, m));
            var place = U.Btn("+ Place media", () => { O.Place = true; app.Render(); }, "");
            place.style.alignSelf = Align.FlexStart;
            side.Add(place.Mt(10));

            if (wide)
            {
                text.style.marginRight = 28;
                side.W(380);
                view.Add(U.Row(text, side).Cls("top-align"));
            }
            else
            {
                side.style.marginTop = 22;
                view.Add(text);
                view.Add(side);
            }
            return view;
        }

        /// <summary>The chapter notes: the same text the Covers slot sheet edits.</summary>
        public static VisualElement ChapterNotes(AppController app, string bookId, OutlineChapter ch, bool editable)
        {
            var s = app.Session;
            var col = U.Col(U.Eyebrow("Chapter notes").Mt(18).Mb(6));
            if (editable)
            {
                var tf = U.Input(ch.Notes, null, v => app.Edit(() => s.Outline.SetChapterNotes(bookId, ch.Id, v)), true, "h72");
                tf.name = "ocn:" + ch.Id;
                col.Add(tf);
            }
            else col.Add(U.Text(U.Esc(ch.Notes), "body-sm muted"));
            return col;
        }

        private static string When(AppController app, AssignedMedia m)
        {
            var dayId = m.Capture?.DayId ?? m.PhotoCapture?.DayId;
            var at = m.Capture?.At ?? m.PhotoCapture?.At;
            if (dayId == null) return "";
            var time = U.FmtTime(at);
            return app.DayLabel(dayId) + (time.Length > 0 ? " · " + time : "");
        }

        private static VisualElement PlacedRow(AppController app, AssignedMedia m)
        {
            var s = app.Session; var aid = m.Assignment.Id;
            VisualElement icon;
            if (m.Confirmed) icon = new Glyph(m.Capture != null ? GlyphKind.Play : GlyphKind.Frame, 18).Cls("ok-text");
            else icon = U.Text("?", "accent bold").Font(20);
            icon.style.width = 28;
            var row = U.Row(icon).MinH(52).Cls("bordered-bottom");
            row.style.paddingTop = 6; row.style.paddingBottom = 6;
            var when = When(app, m);
            row.Add(U.Col(U.Text(U.Esc(m.Title), "bold"), U.Sub(U.Esc(when + (m.Confirmed ? "" : (when.Length > 0 ? " · " : "") + "suggested")))).Cls("grow"));
            if (!m.Confirmed) row.Add(U.GlyphBtn(GlyphKind.Check, "Keep", () => s.ConfirmAssignment(aid), "sm pri ml8", 16));
            row.Add(U.IconBtn(GlyphKind.Cross, () => s.RemoveAssignment(aid), "ghost sm", 16));
            return row;
        }

        private static VisualElement PlaceSheet(AppController app, OutlineSection sec)
        {
            var O = app.State.OL; var s = app.Session; var d = s.Data;
            void Close() { O.Place = false; app.Render(); }
            var here = new HashSet<string>(d.Captures.OutlineAssignments.Where(a => a.SectionId == sec.Id).Select(a => a.MediaRef?.Id));
            var ch = s.Outline.ChapterOfSection(O.Book, sec.Id);
            var list = U.Scroll();
            void Row(MediaRefKind kind, string id, GlyphKind g, string title, string dayId, string at)
            {
                var time = U.FmtTime(at);
                list.Add(U.Tap(() =>
                {
                    s.Assign(new MediaRef(kind, id), O.Book, ch?.Id, sec.Id, null, AssignmentSource.Manual, null, true);
                    O.Place = false;
                    app.Render();
                }, "item", new Glyph(g, 18).Mr(12), U.Col(U.Text(U.Esc(title), "bold"), U.Sub(U.Esc(app.DayLabel(dayId) + (time.Length > 0 ? " · " + time : "")))).Cls("grow")));
            }
            foreach (var c in d.Captures.Captures.Where(c => !here.Contains(c.Id)))
                Row(MediaRefKind.Capture, c.Id, GlyphKind.Play, c.Title, c.DayId, c.At);
            foreach (var p in d.Captures.PhotoCaptures.Where(p => !here.Contains(p.Id)))
                Row(MediaRefKind.PhotoCapture, p.Id, GlyphKind.Frame, p.PhotoId != null ? s.Plan.FindPhoto(p.PhotoId)?.Description ?? p.PhotoId : p.Text, p.DayId, p.At);
            if (list.childCount == 0) list.Add(U.Sub(d.Captures.Captures.Count + d.Captures.PhotoCaptures.Count == 0 ? "Nothing filmed yet." : "Everything is placed."));
            return app.Sheet(640, Close, app.SheetHead("Place in " + U.Esc(sec.Number ?? ""), Close), list);
        }
    }
}

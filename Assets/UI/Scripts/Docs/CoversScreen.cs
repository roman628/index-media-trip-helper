using System.Collections.Generic;
using System.Linq;
using MediaTrip.Model;
using MediaTrip.Query;
using MediaTrip.Status;
using MediaTrip.UI.Shell;
using MediaTrip.UI.ViewModels;
using UnityEngine;
using UnityEngine.UIElements;

namespace MediaTrip.UI.Docs
{
    /// <summary>
    /// Every book's cover and chapter heroes on one board. Each slot shows what was planned and
    /// what is assigned; an empty slot is obvious. A slot is filled by assignment, never by a
    /// checkbox, and planned and assigned both stay visible.
    ///
    /// The slot sheet has one input: it fuzzy-searches every photo of every book (each result
    /// says which book, which chapter, and whether it is a cover or which chapter's hero) and
    /// offers to create what was typed when nothing matches. Its notes are the chapter's notes,
    /// the same ones Outlines shows.
    /// </summary>
    public static class CoversScreen
    {
        public static VisualElement Build(AppController app)
        {
            var s = app.Session; var L = app.Layout;
            var board = s.Queries.CoverBoard();
            var scroll = U.Scroll().Named("covers");
            scroll.ContentPad(6, 6, 40, 16);

            var total = CoverActions.SlotCount(board); var empty = CoverActions.EmptyCount(board);
            var top = U.Row(U.Sub(U.Plural(total, "slot")).Cls("grow"), total == 0 ? null : empty > 0 ? U.Pill(empty + " empty", "fillbad md") : U.Pill("All assigned", "fill md")).MinH(44).Mt(6);
            top.style.paddingRight = 10;
            scroll.Add(top);
            if (total == 0) scroll.Add(U.Sub("Books come from Trip, chapters from the Shot list or Outlines."));

            int cols = L.BoardColumns;
            float width = Mathf.Floor((L.Width - (L.Land ? 96 : 0) - 22 - cols * 10) / cols);
            foreach (var cb in board)
            {
                scroll.Add(U.Text(U.Esc(Fmt.BookName(cb.Book)), "h2 wraptext").Mt(14).Mb(8));
                var row = U.Row().Cls("wrap stretch");
                foreach (var slot in cb.Slots) row.Add(SlotCard(app, slot, width));
                scroll.Add(row);
            }
            return AppShell.Build(app, "Covers", scroll);
        }

        public static string SlotLabel(CoverSlot slot) => slot.IsCover ? "Cover" : "Ch." + slot.Chapter.Number + " · " + slot.Chapter.Name;

        private static VisualElement SlotCard(AppController app, CoverSlot slot, float width)
        {
            var C = app.State.CV;
            var card = U.Tap(() => { C.SlotBook = slot.Book.Id; C.SlotKey = slot.Key; C.Query = ""; app.Render(); }, "slot").On(slot.IsEmpty, "empty");
            card.style.width = width;
            card.Add(U.Row(U.Name(U.Esc(SlotLabel(slot)), true, "h3 grow"), slot.IsEmpty ? U.Pill("Empty", "fillbad") : U.Pill("", "fill", GlyphKind.Check).Also(p => p.Q<Label>()?.Hide())).Cls("top-align"));
            if (slot.PlannedPhotos.Count == 0) card.Add(Line("PLANNED", "—", "planned"));
            foreach (var p in slot.PlannedPhotos) card.Add(Line("PLANNED", U.ShortDescription(p.Description), "planned"));
            foreach (var a in slot.Assigned) card.Add(Line(a.Photo != null && a.Photo.IsNew ? "NEW" : "ASSIGNED", U.ShortDescription(a.Text), "assigned"));
            return card;
        }

        private static VisualElement Line(string key, string value, string cls)
        {
            var ln = new VisualElement().Cls("ln");
            ln.Add(U.Text(key, "k"));
            ln.Add(U.Text(U.Esc(value), "v " + cls));
            return ln;
        }

        // ------------------------------------------------------------------ the slot sheet

        public static VisualElement Overlay(AppController app)
        {
            var C = app.State.CV; var s = app.Session;
            if (C.SlotKey == null) return null;
            var slot = s.Queries.CoverSlot(C.SlotBook, C.SlotKey == "cover" ? null : C.SlotKey);
            if (slot == null) { C.SlotKey = null; return null; }
            return SlotSheet(app, slot);
        }

        private static VisualElement SlotSheet(AppController app, CoverSlot slot)
        {
            var C = app.State.CV; var s = app.Session; var d = s.Data;
            void Close() { C.SlotKey = null; C.Query = ""; app.State.Focus = null; app.Render(); }
            var body = U.Scroll().Named("cv-slot:" + slot.Book.Id + slot.Key);

            body.Add(U.Eyebrow("Planned"));
            if (slot.PlannedPhotos.Count == 0) body.Add(U.Sub("Nothing was planned for this slot.").Pad(6, 0));
            foreach (var p in slot.PlannedPhotos)
            {
                var pid = p.Id;
                var row = U.Row(U.Text(U.Esc(U.ShortDescription(p.Description)), "body grow wraptext")).MinH(52);
                row.style.paddingTop = 4; row.style.paddingBottom = 4;
                if (p.Status == PhotoStatus.NotCaptured)
                    row.Add(U.GlyphBtn(GlyphKind.Check, "Got it", () =>
                    {
                        var day = app.DayForNow();
                        CoverActions.GotPlanned(s, slot, day, pid);
                        app.Toast("Assigned · added to " + app.DayLabel(day));
                    }, "sm pri ml8", 16));
                else
                {
                    // assigned because it was shot; taking that back unassigns it
                    row.Add(U.Pill("assigned", "ok").Ml(8));
                    row.Add(U.IconBtn(GlyphKind.Cross, () => ConfirmSheet.Open(app, "Unassign the planned photo?", "It is marked as not shot again and leaves the day's summary. The plan is not changed.", "Unassign",
                        () => CoverActions.UngotPlanned(s, pid)), "ghost sm", 16));
                }
                body.Add(row);
            }

            var others = slot.Assigned.Where(a => !a.IsPlanned).ToList();
            body.Add(U.Eyebrow("Also assigned").Mt(12));
            if (others.Count == 0) body.Add(U.Sub("Nothing else.").Pad(6, 0));
            foreach (var a in others)
            {
                var aid = a.Assignment.Id;
                var sub = a.Photo != null ? Fmt.PhotoWhere(d, a.Photo) : "described on the spot";
                body.Add(U.Row(new Glyph(GlyphKind.Check, 18).Cls("ok-text").Mr(8),
                    U.Col(U.Text(U.Esc(U.ShortDescription(a.Text)), "bold wraptext"), U.Sub(U.Esc(sub))).Cls("grow"),
                    U.IconBtn(GlyphKind.Cross, () => s.RemoveHeroAssignment(aid), "ghost sm", 16)).MinH(52));
            }

            // ---- one box: an existing photo, or a new one
            var picker = PickerField.Build(app, "cv.pick", C.Query, "Find a photo, or type a new one…", v => C.Query = v, q => PickRows(app, slot, q));
            body.Add(U.Row(picker).Mt(12).Mb(14));

            if (slot.Chapter != null)
            {
                var chId = slot.Chapter.Id;
                body.Add(U.Eyebrow("Chapter notes").Mb(6));
                var notes = U.Input(s.NoteText(chId), "Notes…", v => app.Edit(() => s.SetNote(slot.Book.Id, chId, null, v)), true, "h72");
                notes.name = "cv.notes";
                body.Add(notes);
            }

            VisualElement foot = null;
            if (slot.Chapter != null)
                foot = U.GlyphBtn(GlyphKind.Lines, "Open chapter in Outlines", () => { var b = slot.Book.Id; var c = slot.Chapter.Id; C.SlotKey = null; app.OpenChapter(b, c); }, "mt12");
            return app.Sheet(640, Close, app.SheetHead(U.Esc(SlotLabel(slot)), Close), U.Sub(U.Esc(Fmt.BookName(slot.Book))).Mb(10), body, foot);
        }

        private static List<PickerField.Row> PickRows(AppController app, CoverSlot slot, string q)
        {
            var C = app.State.CV; var s = app.Session; var d = s.Data;
            var found = CoverActions.Search(s, slot, q);
            var rows = new List<PickerField.Row>();
            foreach (var p in found)
            {
                var photo = p;
                rows.Add(new PickerField.Row
                {
                    Lead = U.StPhoto(p.Status, 28, p.HeroType != HeroType.None).Mr(10),
                    Title = U.ShortDescription(p.Description),
                    Sub = Fmt.PhotoWhere(d, p) + (p.Status == PhotoStatus.Captured ? " · shot" : " · not shot yet"),
                    Pick = () => PickExisting(app, slot, photo),
                });
            }
            // photos typed in the field, not on the shot list yet: picking one files it in this book and assigns it
            var loose = s.Search.SearchLoosePhotos(q, 4).Select(m => m.Item).ToList();
            foreach (var lp in loose)
            {
                var photo = lp;
                rows.Add(new PickerField.Row
                {
                    Lead = new Glyph(GlyphKind.Frame, 18).Cls("muted").Mr(10),
                    Title = U.ShortDescription(lp.Text),
                    Sub = "Not on the shot list yet · " + Fmt.LooseWhere(app, lp) + " · files it under " + Fmt.BookName(slot.Book),
                    Pick = () =>
                    {
                        CoverActions.FileAndAssign(s, slot, photo);
                        C.Query = "";
                        app.Render();
                        app.Toast("Filed on the shot list and assigned");
                    },
                });
            }
            if (!CoverActions.IsExactMatch(q, found) && !loose.Any(lp => lp.Key == MediaTrip.Query.LoosePhotos.KeyOf(q)))
            {
                var text = q.Trim();
                rows.Add(new PickerField.Row
                {
                    Lead = new Glyph(GlyphKind.Plus, 18).Mr(10), IsCreate = true, Title = "New photo “" + text + "”",
                    Sub = "Goes on the working shot list for " + Fmt.BookName(slot.Book) + (slot.Chapter != null ? " · Ch." + slot.Chapter.Number : "") + ", marked new",
                    Pick = () =>
                    {
                        var day = app.DayForNow();
                        CoverActions.AssignNew(s, slot, text, day);
                        C.Query = "";
                        app.Render();
                        app.Toast("Assigned · on the shot list and " + app.DayLabel(day));
                    },
                });
            }
            return rows;
        }

        /// <summary>
        /// A photo of another book is not moved or shared without asking: photos can belong to
        /// more than one book. Cancel is a real answer; the slot sheet is still there after it.
        /// </summary>
        private static void PickExisting(AppController app, CoverSlot slot, PhotoItem photo)
        {
            var C = app.State.CV; var s = app.Session;
            void Assign(bool move)
            {
                CoverActions.AssignExisting(s, slot, photo.Id, move);
                C.Query = "";
                app.Render();
                app.Toast(move ? "Moved to " + Fmt.BookName(slot.Book) + " and assigned" : "Assigned");
            }
            if (!CoverActions.IsFromAnotherBook(slot, photo)) { Assign(false); return; }
            var lines = new List<string> { "“" + U.ShortDescription(photo.Description) + "” is already somewhere else:" };
            lines.AddRange(CoverActions.WhereAssigned(s, photo));
            lines.Add("Move puts it in " + Fmt.BookName(slot.Book) + " on the working shot list (the original keeps it where it was printed). Assign to both leaves it where it is and uses it here too.");
            ChoiceSheet.Open(app, "This photo belongs to another book", lines,
                new ChoiceSheet.Option("Move", () => Assign(true)),
                new ChoiceSheet.Option("Assign to both", () => Assign(false)));
        }
    }
}

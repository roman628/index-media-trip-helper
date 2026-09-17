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
    /// what is assigned; an empty slot is obvious. A slot is filled by assignment (the planned
    /// photo once shot, another master-list photo, or a new one described on the spot), never
    /// by a checkbox, and planned and assigned both stay visible.
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
            if (total == 0) scroll.Add(U.Sub("Books and chapters come from Trip and the Shot list."));

            int cols = L.BoardColumns;
            float width = Mathf.Floor((L.Width - (L.Land ? 96 : 0) - 22 - cols * 10) / cols);
            foreach (var cb in board)
            {
                scroll.Add(U.H2("Book " + cb.Book.Number + " · " + U.Esc(cb.Book.Name)).Mt(14).Mb(8));
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
            var card = U.Tap(() => { C.SlotBook = slot.Book.Id; C.SlotKey = slot.Key; C.NewText = ""; C.Picking = false; app.Render(); }, "slot").On(slot.IsEmpty, "empty");
            card.style.width = width;
            card.Add(U.Row(U.H3(U.Esc(SlotLabel(slot))).Cls("grow"), slot.IsEmpty ? U.Pill("Empty", "fillbad") : U.Pill("", "fill", GlyphKind.Check).Also(p => p.Q<Label>()?.Hide())).Cls("top-align"));
            card.Add(Line("PLANNED", slot.Planned != null ? U.ShortDescription(slot.Planned.Description) : "—", "planned"));
            foreach (var a in slot.Assigned) card.Add(Line(a.IsNew ? "NEW" : "ASSIGNED", U.ShortDescription(a.Text), "assigned"));
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
            return C.Picking ? PickSheet(app, slot) : SlotSheet(app, slot);
        }

        private static VisualElement SlotSheet(AppController app, CoverSlot slot)
        {
            var C = app.State.CV; var s = app.Session;
            void Close() { C.SlotKey = null; app.State.Focus = null; app.Render(); }
            var body = U.Scroll().Named("cv-slot:" + slot.Book.Id + slot.Key);

            body.Add(U.Eyebrow("Planned"));
            var planned = U.Row(U.Body(slot.Planned != null ? U.Esc(U.ShortDescription(slot.Planned.Description)) : "—").Cls("grow")).MinH(52);
            planned.style.paddingTop = 6; planned.style.paddingBottom = 12;
            if (slot.Planned != null && slot.Planned.Status == PhotoStatus.NotCaptured)
                planned.Add(U.GlyphBtn(GlyphKind.Check, "Got it", () =>
                {
                    var day = app.DayForNow();
                    CoverActions.GotPlanned(s, slot, day);
                    app.Toast("Added to " + app.DayLabel(day));
                }, "sm pri ml8", 16));
            body.Add(planned);

            body.Add(U.Eyebrow("Assigned"));
            if (slot.IsEmpty) body.Add(U.Sub("Nothing yet.").Pad(6, 0));
            foreach (var a in slot.Assigned)
            {
                var row = U.Row(new Glyph(GlyphKind.Check, 18).Cls("ok-text").Mr(8), U.Text(U.Esc(U.ShortDescription(a.Text)), "bold grow")).MinH(48);
                if (a.IsPlanned) row.Add(U.Pill("planned"));
                else { var aid = a.Assignment.Id; row.Add(U.IconBtn(GlyphKind.Cross, () => s.RemoveHeroAssignment(aid), "ghost sm", 16)); }
                body.Add(row);
            }

            var add = U.Btn("Add", () => AddNew(app, slot), "pri ml8");
            add.SetEnabled(!string.IsNullOrWhiteSpace(C.NewText));
            var text = U.Input(C.NewText, "Something new…", v => { C.NewText = v; add.SetEnabled(!string.IsNullOrWhiteSpace(v)); }, false, "grow");
            text.name = "cv.new";
            body.Add(U.Row(text, add).Mt(10).Mb(8));
            if (CoverActions.Candidates(s, slot).Count > 0)
            {
                var pick = U.Btn("Use a photo from the list…", () => { C.Picking = true; app.Render(); }, "sm ghost accent left");
                pick.style.alignSelf = Align.FlexStart;
                body.Add(pick.Mb(10));
            }

            var ch = slot.Chapter == null ? null : s.Data.FindOutline(slot.Book.Id)?.Chapters.FirstOrDefault(c => c.Id == slot.Chapter.Id);
            if (ch != null) body.Add(OutlinesScreen.ChapterNotes(app, slot.Book.Id, ch, true));

            VisualElement foot = null;
            if (slot.Chapter != null)
            {
                foot = U.GlyphBtn(GlyphKind.Lines, "Open chapter in Outlines", () =>
                {
                    var first = ch?.Sections.FirstOrDefault()?.Id;
                    C.SlotKey = null;
                    app.OpenSection(slot.Book.Id, first);
                }, "mt12");
            }
            return app.Sheet(620, Close, app.SheetHead("Book " + slot.Book.Number + " · " + U.Esc(SlotLabel(slot)), Close), body, foot);
        }

        private static void AddNew(AppController app, CoverSlot slot)
        {
            var C = app.State.CV;
            if (string.IsNullOrWhiteSpace(C.NewText)) return;
            var day = app.DayForNow();
            CoverActions.AssignNew(app.Session, slot, C.NewText, day);
            C.NewText = "";
            app.Render();
            app.Toast("Assigned · added to " + app.DayLabel(day));
        }

        private static VisualElement PickSheet(AppController app, CoverSlot slot)
        {
            var C = app.State.CV; var s = app.Session;
            void Back() { C.Picking = false; app.Render(); }
            var list = U.Scroll();
            foreach (var p in CoverActions.Candidates(s, slot))
            {
                var pid = p.Id;
                list.Add(U.Tap(() => { CoverActions.AssignExisting(s, slot, pid); C.Picking = false; app.Render(); }, "item",
                    U.StPhoto(p.Status, 30, p.HeroType != HeroType.None).Mr(12), U.Text(U.Esc(U.ShortDescription(p.Description)), "grow")));
            }
            return app.Sheet(620, Back, app.SheetHead("Assign to " + U.Esc(SlotLabel(slot)), Back), list);
        }
    }
}

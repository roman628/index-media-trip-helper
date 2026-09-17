using System.Collections.Generic;
using System.Linq;
using MediaTrip.Model;
using MediaTrip.Query;
using MediaTrip.Status;
using MediaTrip.UI.Shell;
using MediaTrip.UI.ViewModels;
using UnityEngine.UIElements;

namespace MediaTrip.UI.Docs
{
    /// <summary>
    /// The media trip summary, by day, in the order things were shot. Video and Photos append
    /// at the bottom; rows are dragged by their handle to reorder, and a tap on the handle
    /// offers Move up / Move down for anyone who cannot drag.
    /// </summary>
    public static class SummaryScreen
    {
        public static VisualElement Build(AppController app)
        {
            var st = app.State; var Sm = st.SM; var s = app.Session; var d = s.Data;
            var days = d.Trip.Days.OrderBy(x => x.Date ?? "", System.StringComparer.Ordinal).ToList();
            if (Sm.Day == null || d.FindDay(Sm.Day) == null) Sm.Day = Fmt.TodayOrLast(d.Trip, System.DateTime.Now)?.Id;

            var body = U.Col().Cls("grow minh0");
            if (days.Count > 0)
            {
                var chips = U.ScrollX();
                chips.ContentPad(10, 4, 0, 12);
                foreach (var day in days)
                {
                    var id = day.Id;
                    chips.Add(U.Chip((string.IsNullOrEmpty(day.Label) ? "Day" : day.Label) + " · " + U.FmtDate(day.Date), Sm.Day == id, () => { Sm.Day = id; app.Render(); }));
                }
                body.Add(chips);
            }

            var list = U.Scroll().Named("sm-list:" + Sm.Day);
            list.ContentPad(12, 12, 20, 12);
            var entries = Sm.Day != null ? s.Queries.DayTimeline(Sm.Day) : new List<TimelineEntry>();
            if (entries.Count == 0) list.Add(U.Sub("Nothing yet.").Pad(12));
            foreach (var e in entries) list.Add(EntryRow(app, e));
            if (entries.Count > 1)
            {
                var dayId = Sm.Day;
                list.AddManipulator(new ReorderManipulator(
                    ids => s.ReorderDay(dayId, ids),
                    (id, handle) => MoveMenu(app, dayId, id, handle)));
            }
            body.Add(list);

            var footer = new VisualElement().Cls("footer");
            footer.Add(U.GlyphBtn(GlyphKind.Play, "Video", () => app.StartFilm(null, Sm.Day), "big pri grow", 20));
            footer.Add(U.GlyphBtn(GlyphKind.Frame, "Photos", () => { Sm.Batch = new PhotoBatchDraft { DayId = Sm.Day }; app.Render(); }, "big grow ml10", 20));
            body.Add(footer);
            return AppShell.Build(app, "Summary", body);
        }

        private static VisualElement EntryRow(AppController app, TimelineEntry e)
        {
            var Sm = app.State.SM; var s = app.Session; var d = s.Data;
            var row = new VisualElement().Cls("entry" + (e.IsVideo ? "" : " photos"));
            row.userData = e.Id;
            var handle = new VisualElement().Cls(ReorderManipulator.HandleClass);
            handle.Add(new Glyph(GlyphKind.Lines, 22));
            row.Add(handle);

            var time = U.FmtTime(e.At);
            string title, sub; GlyphKind glyph; System.Action open;
            if (e.IsVideo)
            {
                var c = e.Capture; var id = c.Id;
                glyph = GlyphKind.Play;
                title = c.Title;
                var parts = new List<string>();
                if (time.Length > 0) parts.Add(time);
                parts.Add(Fmt.BookChapter(d, c.BookId, c.ChapterId));
                parts.Add(c.PlannedNumber != null ? "#" + c.PlannedNumber : e.Item != null && e.Item.Origin == PlanItemOrigin.Combined ? e.Item.DisplayNumber : "unplanned");
                if (c.CameraCount != null) parts.Add(c.CameraCount + " cam");
                var photos = (c.Photos ?? new List<CapturePhoto>()).Count(p => p.Captured);
                if (photos > 0) parts.Add(U.Plural(photos, "photo"));
                sub = string.Join(" · ", parts);
                open = () => { Sm.OpenCapture = id; Sm.OpenPhoto = null; app.Render(); };
            }
            else
            {
                var p = e.PhotoCapture; var id = p.Id;
                glyph = GlyphKind.Frame;
                title = PhotoTitle(app, p);
                sub = (time.Length > 0 ? time + " · " : "") + "photo";
                open = () => { Sm.OpenPhoto = id; Sm.OpenCapture = null; app.Render(); };
            }
            var head = U.Row(new Glyph(glyph, 16).Mr(8), U.H3(U.Esc(title)).Cls("grow"));
            row.Add(U.Tap(open, "body", head, U.Sub(U.Esc(sub)).Mt(3)));
            return row;
        }

        private static string PhotoTitle(AppController app, PhotoCapture p) =>
            p.PhotoId != null ? app.Session.Plan.FindPhoto(p.PhotoId)?.Description ?? p.PhotoId : p.Text ?? "";

        /// <summary>The fallback for dragging: a tap on the handle offers the two moves.</summary>
        private static void MoveMenu(AppController app, string dayId, string entryId, VisualElement handle)
        {
            var s = app.Session;
            var ids = s.Queries.DayTimeline(dayId).Select(x => x.Id).ToList();
            var i = ids.IndexOf(entryId);
            var box = new VisualElement().Cls("sugg");
            var up = U.Tap(() => { app.ClosePopup(); s.MoveDayEntry(dayId, entryId, -1); }, "sugg-row", new Glyph(GlyphKind.ArrowUp, 18).Mr(10), U.Text("Move up", "bold"));
            var down = U.Tap(() => { app.ClosePopup(); s.MoveDayEntry(dayId, entryId, 1); }, "sugg-row", new Glyph(GlyphKind.ArrowDown, 18).Mr(10), U.Text("Move down", "bold"));
            up.SetEnabled(i > 0);
            down.SetEnabled(i >= 0 && i < ids.Count - 1);
            box.Add(up); box.Add(down);
            app.ShowPopup(handle, box, 200, dismissOnOutsideTap: true);
        }

        // ------------------------------------------------------------------ sheets

        public static VisualElement Overlay(AppController app)
        {
            var Sm = app.State.SM; var d = app.Session.Data;
            if (Sm.Batch != null) return BatchSheet(app);
            if (Sm.OpenCapture != null)
            {
                var c = d.FindCapture(Sm.OpenCapture);
                if (c != null) return CaptureSheet(app, c);
                Sm.OpenCapture = null;
            }
            if (Sm.OpenPhoto != null)
            {
                var p = d.FindPhotoCapture(Sm.OpenPhoto);
                if (p != null) return PhotoSheet(app, p);
                Sm.OpenPhoto = null;
            }
            return null;
        }

        private static VisualElement CaptureSheet(AppController app, Capture c)
        {
            var Sm = app.State.SM; var s = app.Session; var d = s.Data;
            void Close() { Sm.OpenCapture = null; app.Render(); }
            var body = U.Scroll();
            var time = U.FmtTime(c.At);
            body.Add(U.Sub(U.Esc(app.DayLabel(c.DayId) + (time.Length > 0 ? " · " + time : "") + " · " + Fmt.BookChapter(d, c.BookId, c.ChapterId))).Mb(8));
            void F(string k, string v) { if (!string.IsNullOrWhiteSpace(v)) body.Add(U.KV(k, U.Esc(v))); }
            F("SME", string.Join("\n", (c.People ?? new List<CapturePerson>()).Select(p => p.Name + (string.IsNullOrEmpty(p.Title) ? "" : " · " + p.Title))));
            F("Cameras", c.CameraCount?.ToString());
            F("Description", c.Description);
            F("Scene", c.SceneDescription);
            F("Notes", c.Notes);
            F("B-roll", c.HasBRoll ? c.BRoll : null);
            F("Photos", string.Join("\n", (c.Photos ?? new List<CapturePhoto>()).Where(p => p.Captured).Select(p => p.PhotoId != null ? s.Plan.FindPhoto(p.PhotoId)?.Description ?? p.PhotoId : p.Text)));
            F("HPI tool", c.HpiToolSuggestion);
            F("Keywords", string.Join(", ", c.Keywords ?? new List<string>()));
            F("Location", c.Location);

            var foot = U.Row(U.Btn("Remove", () => ConfirmSheet.Open(app, "Remove from the summary?", U.Esc(c.Title), "Remove", () => { s.RemoveCapture(c.Id); Sm.OpenCapture = null; app.Render(); }), "ghost danger"), U.Grow()).Mt(12);
            foot.style.flexShrink = 0;
            if (c.PlanVideoId != null && s.Plan.FindItem(c.PlanVideoId) != null)
                foot.Add(U.GlyphBtn(GlyphKind.List, "Plan", () => { Sm.OpenCapture = null; app.OpenVideo(c.PlanVideoId); }, ""));
            foot.Add(U.Btn("Edit", () => app.EditCapture(c.Id), "pri ml8"));
            return app.Sheet(700, Close, app.SheetHead(U.Esc(c.Title), Close), body, foot);
        }

        private static VisualElement PhotoSheet(AppController app, PhotoCapture p)
        {
            var Sm = app.State.SM; var s = app.Session;
            void Close() { Sm.OpenPhoto = null; app.Render(); }
            var time = U.FmtTime(p.At);
            var body = U.Col(U.Sub(U.Esc(app.DayLabel(p.DayId) + (time.Length > 0 ? " · " + time : "") + (p.PhotoId != null ? " · planned photo" : " · unplanned"))));
            var foot = U.Row(U.Btn("Remove", () => { s.RemovePhotoCapture(p.Id); Sm.OpenPhoto = null; app.Render(); }, "ghost danger"), U.Grow()).Mt(16);
            return app.Sheet(620, Close, app.SheetHead(U.Esc(U.ShortDescription(PhotoTitle(app, p))), Close), body, foot);
        }

        private static VisualElement BatchSheet(AppController app)
        {
            var Sm = app.State.SM; var s = app.Session; var b = Sm.Batch;
            void Close() { Sm.Batch = null; app.State.Focus = null; app.Render(); }
            if (b.DayId == null || s.Data.FindDay(b.DayId) == null) b.DayId = app.DayForNow();

            var save = U.Btn("Add to " + app.DayLabel(b.DayId), () =>
            {
                var added = b.Save(s);
                Sm.Day = b.DayId;
                Sm.Batch = null;
                app.State.Focus = null;
                app.Render();
                app.Toast(U.Plural(added.Count, "photo") + " added");
            }, "big pri");
            save.SetEnabled(b.CanSave);

            var body = U.Scroll().Named("sm-batch");
            body.Add(U.Lbl("Planned, not yet shot"));
            var chips = U.Row().Cls("wrap");
            var open = PhotoBatchDraft.NotYetShot(s);
            if (open.Count == 0) chips.Add(U.Sub("All planned photos are shot."));
            foreach (var p in open)
            {
                var pid = p.Id; var on = b.Picked.Contains(pid);
                var chip = U.Tap(() => { b.Toggle(pid); app.Render(); }, "chip ok wrapok",
                    on ? new Glyph(GlyphKind.Check, 16, 3).Mr(6) : null,
                    p.HeroType != HeroType.None ? new Glyph(GlyphKind.Star, 14).Mr(6) : null,
                    U.Text(U.Esc(U.ShortDescription(p.Description)))).On(on);
                chips.Add(chip);
            }
            body.Add(chips);
            body.Add(U.Lbl("Unplanned").Mt(12));
            for (int i = 0; i < b.Extra.Count; i++)
            {
                var idx = i;
                body.Add(U.Row(U.Body(U.Esc(b.Extra[i])).Cls("grow"), U.IconBtn(GlyphKind.Cross, () => { b.Extra.RemoveAt(idx); app.Render(); }, "ghost sm", 16)).MinH(44));
            }
            var add = U.Btn("Add", () => { b.AddExtra(); app.RenderKeepFocus("sm.extra"); }, "ml8");
            add.SetEnabled(!string.IsNullOrWhiteSpace(b.Text));
            var text = U.Input(b.Text, "Describe…", v => { b.Text = v; add.SetEnabled(!string.IsNullOrWhiteSpace(v)); save.SetEnabled(b.CanSave); }, false, "grow");
            text.name = "sm.extra";
            body.Add(U.Row(text, add).Mt(6));

            var foot = U.Row(U.Grow(), save).Mt(12);
            foot.style.flexShrink = 0;
            return app.Sheet(660, Close, app.SheetHead("Photos", Close), body, foot);
        }
    }
}

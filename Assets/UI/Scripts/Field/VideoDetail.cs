using System.Collections.Generic;
using System.Linq;
using MediaTrip.Model;
using MediaTrip.Status;
using MediaTrip.UI.ViewModels;
using UnityEngine.UIElements;

namespace MediaTrip.UI.Field
{
    /// <summary>A plan item, readable: title, where it sits, status actions, scene, nested notes, photos, amendment history.</summary>
    public static class VideoDetail
    {
        public static VisualElement Sheet(AppController app, string itemId)
        {
            var sheet = new VisualElement().Cls("sheet");
            sheet.style.height = 680;
            sheet.Add(Body(app, itemId, closeAction: () => { app.State.DetailItemId = null; app.Render(); }, openOther: id => { app.State.DetailItemId = id; app.Render(); }));
            return app.Overlay(sheet, () => { app.State.DetailItemId = null; app.Render(); });
        }

        public static VisualElement Body(AppController app, string itemId, System.Action closeAction, System.Action<string> openOther)
        {
            var s = app.Session;
            var d = s.Data;
            var it = s.Plan.FindItem(itemId);
            var root = U.Col().Cls("fill");
            if (it == null) { root.Add(U.Sub("Select a video.")); return root; }
            var st = it.Status;
            var cap = it.Captures.FirstOrDefault();
            var book = d.FindBook(it.BookId);
            var ch = d.FindChapter(it.ChapterId);
            var resolved = it.IsSuperseded ? s.Plan.Resolve(it.Id).FirstOrDefault() : null;

            var head = U.Row(U.Num(it.DisplayNumber, "md"), U.Grow(), U.StatusPill(st)).Mb(8);
            if (closeAction != null) head.Add(U.Btn("Close", closeAction, "sm ml10"));
            root.Add(head);

            var title = U.H2(st == PlanItemStatus.Dropped ? U.Strike(it.Title) : U.Esc(it.Title)).Mb(6);
            if (st == PlanItemStatus.Dropped || st == PlanItemStatus.Superseded) title.Cls("strike");
            root.Add(title);

            var where = "Book " + (book?.Number.ToString() ?? "?") + " · " + U.Esc(book?.Name ?? "") + " › " +
                        (ch != null ? "Ch." + ch.Number + " " + U.Esc(ch.Name) : "<color=#c00000>no chapter</color>");
            var sme = Fmt.SmeOf(d, it);
            if (!string.IsNullOrEmpty(sme)) where += " · SME " + U.Esc(sme);
            root.Add(U.Sub(where).Mb(14));

            if (resolved != null)
            {
                var goBtn = U.GlyphBtn(GlyphKind.Arrow, "Shot as “" + U.Esc(resolved.Title) + "” (" + resolved.DisplayNumber + ")", () => openOther?.Invoke(resolved.Id), "left mb14");
                goBtn.style.height = StyleKeyword.Auto; goBtn.style.minHeight = 52; goBtn.style.paddingTop = 8; goBtn.style.paddingBottom = 8;
                var goLabel = goBtn.Q<Label>();
                if (goLabel != null) { goLabel.style.whiteSpace = WhiteSpace.Normal; goLabel.style.flexShrink = 1; }
                root.Add(goBtn);
            }

            var actions = U.Row().Mb(16);
            bool inactive = st == PlanItemStatus.Dropped || st == PlanItemStatus.Superseded;
            if (!inactive)
            {
                var captured = it.VideoCaptured;
                var mark = U.GlyphBtn(captured ? GlyphKind.Check : GlyphKind.None, captured ? "Captured · tap to undo" : "Mark captured",
                    () => { app.ToggleItem(it.Id); }, (captured ? "" : "pri ") + "big grow");
                actions.Add(mark);
            }
            if (app.State.Mode == Mode.Field)
            {
                actions.Add(U.Btn("Amend", () => app.OpenAmend(it.Id), "big ml10"));
                if (!inactive) actions.Add(U.Btn(it.VideoCaptured ? "Details…" : "Log…", () =>
                {
                    if (cap != null) { app.State.Capture = CaptureDraft.ForCapture(s, cap); app.State.DetailItemId = null; app.Nav(Screen.Capture); }
                    else app.OpenCapture(it.Id);
                }, "big ml10"));
            }
            else
            {
                actions.Add(U.Btn("Edit in shot list", () =>
                {
                    var v = d.FindVideo(it.Id) ?? (it.SourceIds.Count > 0 ? d.FindVideo(it.SourceIds[0]) : null);
                    app.State.DetailItemId = null;
                    if (v != null) { app.State.SL.Pane = "videos"; app.State.SL.SelKind = "video"; app.State.SL.SelId = v.Id; app.State.SL.Book = v.BookId; }
                    app.Nav(Screen.ShotList);
                }, "big ml10"));
            }
            root.Add(actions);

            var body = U.Scroll();
            if (cap != null)
            {
                var day = d.FindDay(cap.DayId);
                var info = "Captured " + (day?.Label ?? "?") + " · #" + cap.CapturedOrder + " in order";
                if (string.IsNullOrEmpty(cap.Description) && (cap.Photos?.Count ?? 0) == 0) info += " · quick check-off, details pending";
                else if (cap.CameraCount != null) info += " · " + U.Plural(cap.CameraCount.Value, "camera");
                body.Add(U.Sub(info).Mb(12));
            }
            if (!string.IsNullOrEmpty(it.SceneDescription))
            {
                body.Add(U.Eyebrow("Scene"));
                body.Add(U.Body(U.Esc(it.SceneDescription)).Mt(6).Mb(16));
            }
            if (it.Notes != null && it.Notes.Count > 0)
            {
                body.Add(U.Eyebrow("Notes").Mb(8));
                body.Add(NotesView.Build(it.Notes));
            }
            if (it.PhotoRefs != null && it.PhotoRefs.Count > 0)
            {
                body.Add(U.Eyebrow("Photos with this setup").Mt(16).Mb(8));
                foreach (var pid in it.PhotoRefs)
                {
                    var p = s.Plan.FindPhoto(pid);
                    if (p == null)
                    {
                        body.Add(U.Row(new Glyph(GlyphKind.Cross, 18).Cls("bad-text").Mr(8), U.Text(pid + " — not in master photo list", "bold bad-text")).MinH(48));
                        continue;
                    }
                    var on = p.IsCaptured;
                    var pch = d.FindChapter(p.ChapterId);
                    var row = U.Tap(() => app.TogglePhoto(pid), "item");
                    row.style.paddingLeft = 6;
                    row.Add(U.Check(on, () => app.TogglePhoto(pid), "sm").Mr(12));
                    var col = U.Col(U.Text(U.Esc(p.Description), "bold")).Cls("grow");
                    if (p.HeroType != HeroType.None) col.Add(U.Row(new Glyph(GlyphKind.Star, 12).Cls("accent").Mr(4), U.Sub(U.HeroLabel(p.Photo, pch)).Cls("accent bold")));
                    row.Add(col);
                    body.Add(row);
                }
            }
            var history = History(app, it);
            if (history != null) body.Add(history);
            root.Add(body);
            return root;
        }

        private static VisualElement History(AppController app, PlanItem it)
        {
            var d = app.Session.Data;
            var rows = new List<string>();
            if (it.Origin == PlanItemOrigin.Combined)
                rows.Add("Combined from videos " + string.Join(" and ", it.SourceIds.Select(x => "#" + (app.Session.Plan.FindItem(x)?.DisplayNumber ?? "?"))) + (it.CreatedBy?.Reason != null ? " · " + U.Esc(it.CreatedBy.Reason) : ""));
            if (it.Origin == PlanItemOrigin.Split)
                rows.Add("Split from video #" + (it.Number?.ToString() ?? "?") + (it.CreatedBy?.Reason != null ? " · " + U.Esc(it.CreatedBy.Reason) : ""));
            if (it.Origin == PlanItemOrigin.Added)
                rows.Add("Unplanned addition" + (it.CreatedBy?.Reason != null ? " · " + U.Esc(it.CreatedBy.Reason) : ""));
            foreach (var a in it.Amendments.Where(a => a.Targets != null && a.Targets.Contains(it.Id)))
            {
                switch (a.Type)
                {
                    case AmendmentType.Rename: rows.Add("Renamed from “" + U.Esc(it.OriginalTitle) + "”" + (a.Reason != null ? " · " + U.Esc(a.Reason) : "")); break;
                    case AmendmentType.Drop: rows.Add("Dropped" + (a.Reason != null ? " · " + U.Esc(a.Reason) : "")); break;
                    case AmendmentType.Move: rows.Add("Moved to " + Fmt.BookChapter(d, a.NewBookId ?? it.BookId, a.NewChapterId ?? it.ChapterId) + (a.Reason != null ? " · " + U.Esc(a.Reason) : "")); break;
                    case AmendmentType.Combine: rows.Add("Superseded by " + U.Esc(a.NewTitle ?? "") + " (" + (app.Session.Plan.FindItem(a.Results.FirstOrDefault())?.DisplayNumber ?? "?") + ")"); break;
                    case AmendmentType.Split: rows.Add("Split into " + string.Join(", ", a.Results.Select(r => U.Esc(app.Session.Plan.FindItem(r)?.Title ?? r)))); break;
                }
            }
            if (rows.Count == 0) return null;
            var card = U.Col(U.Eyebrow("Amendments").Mb(6)).Cls("card sf2").Mt(16);
            foreach (var r in rows) card.Add(U.Body(r).Mb(4));
            return card;
        }
    }
}

using MediaTrip.UI.ViewModels;
using UnityEngine.UIElements;

namespace MediaTrip.UI.Field
{
    /// <summary>Record an amendment from the field: rename, combine with siblings, or drop.</summary>
    public static class AmendSheet
    {
        public static VisualElement Build(AppController app)
        {
            var a = app.State.Amend;
            var s = app.Session;
            var it = s.Plan.FindItem(a.ItemId);
            void Close() { app.State.Amend = null; app.Render(); }
            var sheet = new VisualElement().Cls("sheet");
            if (it == null) { Close(); return new VisualElement(); }

            sheet.Add(U.Row(
                U.Col(U.Eyebrow("Amendment · #" + it.DisplayNumber), U.H2(AmendDraft.ModeTitle(a.Mode))).Cls("grow"),
                U.Btn("Cancel", Close, "sm")).Mb(16));

            var body = U.Scroll();
            if (a.Mode == "menu")
            {
                foreach (var (m, t, desc) in new[]
                {
                    ("rename", "Rename", "Keep the plan number; record the title actually used."),
                    ("combine", "Combine with…", "Shoot two planned videos as one. Both become superseded."),
                    ("drop", "Drop", "Not shot this trip. Stays on the plan, marked dropped."),
                })
                {
                    var b = U.Tap(() => { a.SetMode(s, m); app.Render(); }, "item outlined mb8", U.Col(U.H3(t), U.Sub(desc)));
                    b.MinH(76);
                    body.Add(b);
                }
            }
            if (a.Mode == "rename")
            {
                var title = U.Input(a.Title, null, v => { a.Title = v; });
                title.name = "amend.title";
                body.Add(U.Field("New title", title));
                body.Add(U.Field("Reason (optional)", U.Input(a.Reason, "e.g. SME used the plant's wording", v => a.Reason = v)));
                app.State.Focus = "amend.title";
            }
            if (a.Mode == "combine")
            {
                body.Add(U.Lbl("Combine #" + it.DisplayNumber + " with"));
                var sibs = a.Siblings(s);
                if (sibs.Count == 0) body.Add(U.Sub("No other active videos in this chapter.").Pad(12));
                foreach (var sib in sibs)
                {
                    var on = a.With.Contains(sib.Id);
                    var row = U.Tap(() => { a.ToggleWith(s, sib.Id); app.Render(); }, "item");
                    row.style.paddingLeft = 6;
                    row.Add(U.Check(on, () => { a.ToggleWith(s, sib.Id); app.Render(); }, "sm").Mr(12));
                    row.Add(U.Num(sib.DisplayNumber));
                    row.Add(U.Text(U.Esc(sib.Title), "bold").Cls("grow"));
                    body.Add(row);
                }
                var title = U.Input(a.Title, null, v => a.Title = v);
                title.name = "amend.title";
                body.Add(U.Field("Title of the combined video", title).Mt(12));
                body.Add(U.Field("Reason (optional)", U.Input(a.Reason, null, v => a.Reason = v)));
            }
            if (a.Mode == "drop")
            {
                body.Add(U.Body("Mark <b>#" + it.DisplayNumber + " " + U.Esc(it.Title) + "</b> as not shot this trip. The plan keeps it; the summary will not.").Mb(16));
                var reason = U.Input(a.Reason, "e.g. Line was down", v => a.Reason = v);
                reason.name = "amend.reason";
                body.Add(U.Field("Reason", reason));
                app.State.Focus = "amend.reason";
            }
            sheet.Add(body);

            if (a.Mode != "menu")
            {
                var apply = U.Btn(a.Mode == "drop" ? "Drop video" : "Save amendment", () =>
                {
                    var wasTitle = it.Title;
                    var id = a.Apply(s);
                    app.State.Amend = null;
                    if (app.State.DetailItemId != null) app.State.DetailItemId = id;
                    if (app.State.PlanVideo != null) app.State.PlanVideo = id;
                    app.Toast(a.Mode == "rename" ? "Renamed · #" + it.DisplayNumber : a.Mode == "drop" ? "Dropped · #" + it.DisplayNumber : "Combined into one video");
                    app.Render();
                }, "big " + (a.Mode == "drop" ? "danger" : "pri"));
                sheet.Add(U.Row(U.Btn("Back", () => { a.SetMode(s, "menu"); app.Render(); }, "big"), U.Grow(), apply).Mt(16));
                apply.schedule.Execute(() => apply.SetEnabled(a.CanApply)).Every(150);
            }
            return app.Overlay(sheet, Close);
        }
    }
}

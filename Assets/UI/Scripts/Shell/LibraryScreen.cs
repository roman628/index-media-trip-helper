using System;
using System.Linq;
using MediaTrip.Persistence;
using MediaTrip.UI.Transfer;
using MediaTrip.UI.ViewModels;
using UnityEngine.UIElements;

namespace MediaTrip.UI.Shell
{
    /// <summary>The trip library: open a trip, start a new one, import one. Edit shows Duplicate and Delete.</summary>
    public static class LibraryScreen
    {
        public static VisualElement Build(AppController app)
        {
            var st = app.State;
            var root = U.Col().Cls("grow fullsize minh0");

            var hdr = new VisualElement().Cls("hdr");
            var title = U.Tap(app.DebugTap, "grow", U.Text("Trips", "t"));
            title.style.alignItems = Align.FlexStart;
            hdr.Add(title);
            var trips = TripLibrary.ListTrips();
            if (trips.Count > 0) hdr.Add(U.Btn(st.Edit ? "Done" : "Edit", app.ToggleEdit, "sm mr8" + (st.Edit ? " pri" : "")));
            hdr.Add(U.Btn("Theme", () => ThemeSheet.Open(app), "sm"));
            root.Add(hdr);

            var list = U.Scroll().Named("library").Cls("docpad");
            if (trips.Count == 0)
            {
                var empty = U.Col(U.H2("No trips yet").Mb(6), U.Sub("Start one, or import a trip file.")).Cls("center");
                empty.style.alignItems = Align.Center;
                empty.style.paddingTop = 60;
                list.Add(empty);
            }
            foreach (var t in trips)
            {
                var trip = t;
                var current = app.Session != null && t.TripId == app.Session.Data.TripId;
                var card = U.Card().Cls("row mb12" + (current ? " ac" : ""));
                card.style.minHeight = 88;
                card.style.flexWrap = Wrap.Wrap;

                var where = string.IsNullOrEmpty(t.City) ? null : t.City;
                var dates = string.IsNullOrEmpty(t.Arrive) ? null : U.FmtDate(t.Arrive) + " – " + U.FmtDate(t.Depart);
                var sub = t.LoadError != null ? "Cannot open: " + t.LoadError : string.Join(" · ", new[] { where, dates, current ? Progress(app) : null }.Where(x => !string.IsNullOrEmpty(x)));
                var head = U.Row(U.H2(U.Esc(Name(t))).Mr(10), current ? U.Pill("Open", "ac") : null).Cls("wrap");
                var open = U.Tap(() => Open(app, trip, current), "grow row",
                    U.Col(head, U.Sub(U.Esc(sub))).Cls("grow"),
                    st.Edit ? null : new Glyph(GlyphKind.ChevronRight, 22).Ml(10));
                open.style.minHeight = 56;
                open.style.minWidth = 200;
                card.Add(open);
                if (st.Edit)
                {
                    card.Add(U.Btn("Duplicate", () => Duplicate(app, trip.TripId), "sm ml8"));
                    card.Add(U.Btn("Delete", () => ConfirmDelete(app, trip), "sm danger ml8"));
                }
                list.Add(card);
            }
            root.Add(list);

            var footer = new VisualElement().Cls("footer");
            footer.Add(U.Btn("New trip", () => NewTrip(app), "big pri grow"));
            footer.Add(U.Btn("Import", () => ImportFlow.Begin(app), "big ml10"));
            root.Add(footer);
            return root;
        }

        /// <summary>"ACME · WDG · P5", or "New trip" until it has an identity.</summary>
        public static string Name(TripSummary t)
        {
            var parts = new[] { t.ClientAbbrev, t.ProgramAbbrev, t.PhaseNumber.HasValue ? "P" + t.PhaseNumber.Value : null }.Where(x => !string.IsNullOrEmpty(x)).ToList();
            return parts.Count > 0 ? string.Join(" · ", parts) : "New trip";
        }

        private static string Progress(AppController app)
        {
            var items = app.Session.Queries.CurrentShotList();
            var done = items.Count(i => i.VideoCaptured);
            return done + " of " + items.Count + " videos";
        }

        private static void Open(AppController app, TripSummary t, bool current)
        {
            if (t.LoadError != null) { app.Toast("This trip cannot be opened: " + t.LoadError); return; }
            if (current) app.Nav(Screen.Summary);
            else app.OpenTrip(t.TripId);
        }

        private static void NewTrip(AppController app)
        {
            try
            {
                var created = TripLibrary.CreateTrip();
                app.OpenTrip(created.TripId, Screen.Trip);
                app.State.Edit = true;
                app.RenderKeepFocus("trip:client");
            }
            catch (Exception ex) { app.Toast("Could not create a trip: " + ex.Message); }
        }

        private static void Duplicate(AppController app, string tripId)
        {
            try
            {
                app.Session?.SaveNow();
                var copy = TripLibrary.DuplicateTrip(tripId, includeCaptures: false);
                app.OpenTrip(copy.TripId, Screen.Trip);
                app.State.Edit = true;
                app.Render();
                app.Toast("Duplicated · plan and outlines kept, captures cleared");
            }
            catch (Exception ex) { app.Toast("Duplicate failed: " + ex.Message); }
        }

        private static void ConfirmDelete(AppController app, TripSummary t)
        {
            ConfirmSheet.Open(app, "Delete " + U.Esc(Name(t)) + "?", "This removes the trip from this device. Share it first if you want a copy.", "Delete", () =>
            {
                try
                {
                    if (app.Session != null && t.TripId == app.Session.Data.TripId) app.CloseTrip();
                    TripLibrary.DeleteTrip(t.TripId);
                    app.State.Edit = TripLibrary.ListTrips().Count > 0 && app.State.Edit;
                    app.Render();
                    app.Toast("Deleted " + Name(t));
                }
                catch (Exception ex) { app.Toast("Delete failed: " + ex.Message); }
            });
        }
    }
}

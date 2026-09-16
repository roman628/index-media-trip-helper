using MediaTrip.Persistence;
using MediaTrip.UI.Transfer;
using UnityEngine.UIElements;

namespace MediaTrip.UI.Shell
{
    /// <summary>What a fresh install shows: no trips yet, so create one or import one.</summary>
    public static class EmptyLibraryScreen
    {
        public static VisualElement Build(AppController app)
        {
            var root = U.Col().Cls("grow fill");
            root.style.alignItems = Align.Center;
            root.style.justifyContent = Justify.Center;
            var card = U.Card().W(560).Pad(32);
            card.Add(U.H1("No trips yet").Mb(8));
            card.Add(U.Body("Create a new trip and author it here, or import one that was exported from another device (a .zip, a trip bundle .json, or a plain trip folder).").Mb(20));
            var row = U.Row();
            row.Add(U.Btn("+ New trip", () =>
            {
                var created = TripLibrary.CreateTrip("NEW", "TRIP");
                app.OpenTrip(created.TripId);
                app.Nav(Screen.Trip);
                app.Toast("New trip · fill in client, program, phase and dates");
            }, "pri big"));
            row.Add(U.Btn("Import…", () => ImportFlow.Begin(app), "big ml10"));
            card.Add(row);
            var trips = TripLibrary.ListTrips();
            if (trips.Count > 0)
            {
                card.Add(U.Eyebrow("In the library").Mt(20).Mb(8));
                foreach (var t in trips)
                {
                    var id = t.TripId;
                    card.Add(U.Tap(() => { if (t.LoadError == null) app.OpenTrip(id); else app.Toast(t.LoadError); }, "item outlined mb6",
                        U.Col(U.Text(U.Esc(t.DisplayName), "bold"), U.Sub(t.LoadError ?? (U.FmtDate(t.Arrive) + " – " + U.FmtDate(t.Depart)))).Cls("grow")));
                }
            }
            var msg = app.State.IO.LastMessage;
            if (msg != null)
            {
                var b = new VisualElement().Cls("banner" + (app.State.IO.LastMessageIsError ? " bad" : " ok")).Mt(16);
                b.Add(U.Body(U.Esc(msg)).Cls("grow"));
                card.Add(b);
            }
            root.Add(card);
            return root;
        }
    }
}

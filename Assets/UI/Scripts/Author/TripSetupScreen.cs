using System.Collections.Generic;
using System.Linq;
using MediaTrip.Model;
using MediaTrip.Search;
using UnityEngine.UIElements;

namespace MediaTrip.UI.Author
{
    /// <summary>Trip setup: identity, dates, weather and media file, logistics, actions, books and teams, days.</summary>
    public static class TripSetupScreen
    {
        public static VisualElement Build(AppController app)
        {
            var st = app.State; var s = app.Session; var d = s.Data; var t = d.Trip;
            var root = U.Row().Cls("grow stretch"); root.style.minHeight = 0;

            var nav = new VisualElement().Cls("nav");
            var head = new VisualElement().Cls("head"); head.Add(U.H2("Trip setup").Cls("grow")); nav.Add(head);
            var nb = U.Scroll().Cls("bodyN");
            var secs = new[]
            {
                ("identity", "Identity", "client · program · phase · location"), ("dates", "Dates", "arrive · depart"), ("weather", "Weather & media file", ""),
                ("logistics", "Logistics", U.Plural(t.Logistics.Count, "line")), ("actions", "Actions", t.Actions.Count(a => !a.Done) + " open"),
                ("books", "Books & teams", U.Plural(t.Books.Count, "book")), ("days", "Days", U.Plural(t.Days.Count, "day")),
            };
            var body = U.Scroll().Cls("bodyE");
            body.name = "trip-body";
            foreach (var (id, label, sub) in secs)
            {
                var sid = id;
                var row = U.Tap(() =>
                {
                    st.TripSection = sid;
                    app.Render();
                    app.Root.schedule.Execute(() =>
                    {
                        var sv = app.Root.Q<ScrollView>("trip-body");
                        var target = sv?.Q("sec-" + sid);
                        if (sv != null && target != null) sv.ScrollTo(target);
                    }).StartingIn(30);
                }, "trow",
                    U.Col(U.Text(label, "bold"), U.Sub(sub)).Cls("grow"));
                row.MinH(60).On(st.TripSection == id);
                nb.Add(row);
            }
            nav.Add(nb);
            root.Add(nav);

            var edit = new VisualElement().Cls("edit");
            edit.Add(body);
            root.Add(edit);

            TextField F(string value, string ph, System.Action<string> set, string name)
            {
                var tf = U.Input(value, ph, v => app.Edit(() => s.EditTrip(_ => set(v))));
                tf.name = name;
                return tf;
            }

            // ---- identity
            var identity = U.Card().Mb(14); identity.name = "sec-identity";
            identity.Add(U.Eyebrow("Identity").Mb(12));
            t.Identity = t.Identity ?? new TripIdentity();
            t.Identity.Location = t.Identity.Location ?? new Location();
            var r1 = U.Row(); r1.style.alignItems = Align.FlexStart;
            r1.Add(U.Field("Client abbrev", F(t.Identity.ClientAbbrev, "ACME", v => t.Identity.ClientAbbrev = v, "trip:client")).W(200).Mr(14));
            r1.Add(U.Field("Program abbrev", F(t.Identity.ProgramAbbrev, "WDG", v => t.Identity.ProgramAbbrev = v, "trip:program")).W(200).Mr(14));
            r1.Add(U.Field("Phase", F(t.Identity.PhaseNumber?.ToString() ?? "", "5", v => t.Identity.PhaseNumber = int.TryParse(v, out var n) ? n : (int?)null, "trip:phase")).W(120).Mr(14));
            var printed = U.Col(U.Sub("Printed as <b>" + U.Esc(t.Identity.ClientAbbrev) + " · " + U.Esc(t.Identity.ProgramAbbrev) + " · Phase " + (t.Identity.PhaseNumber?.ToString() ?? "?") + "</b> on every summary. The shot list's “company / project” header is the same two values.")).Cls("grow");
            printed.style.paddingTop = 26;
            r1.Add(printed);
            identity.Add(r1);
            var r2 = U.Row(); r2.style.alignItems = Align.FlexStart;
            r2.Add(U.Field("City", F(t.Identity.Location.City, null, v => t.Identity.Location.City = v, "trip:city")).W(240).Mr(14));
            r2.Add(U.Field("State", F(t.Identity.Location.State, null, v => t.Identity.Location.State = v, "trip:state")).W(110).Mr(14));
            r2.Add(U.Field("Address", F(t.Identity.Location.Address, null, v => t.Identity.Location.Address = v, "trip:address")).Cls("grow"));
            identity.Add(r2);
            body.Add(identity);

            // ---- dates
            t.Dates = t.Dates ?? new TripDates();
            var dates = U.Card().Mb(14); dates.name = "sec-dates";
            dates.Add(U.Eyebrow("Dates · ISO yyyy-mm-dd · weekdays are rendered, not stored").Mb(12));
            var r3 = U.Row().Cls("wrap"); r3.style.alignItems = Align.FlexStart;
            r3.Add(U.Field("Arrive", F(t.Dates.Arrive, "2026-09-14", v => t.Dates.Arrive = v, "trip:arrive")).W(170).Mr(10));
            r3.Add(DateEcho(t.Dates.Arrive));
            r3.Add(U.Field("Depart", F(t.Dates.Depart, "2026-09-17", v => t.Dates.Depart = v, "trip:depart")).W(170).Mr(10));
            r3.Add(DateEcho(t.Dates.Depart));
            r3.Add(U.Field("Last updated", F(t.Dates.LastUpdated, null, v => t.Dates.LastUpdated = v, "trip:updated")).W(170));
            dates.Add(r3);
            body.Add(dates);

            // ---- weather
            var weather = U.Card().Mb(14); weather.name = "sec-weather";
            weather.Add(U.Eyebrow("Weather & media file").Mb(12));
            var wf = U.Input(t.WeatherForecast, null, v => app.Edit(() => s.EditTrip(x => x.WeatherForecast = v)), true).H(72); wf.name = "trip:weather";
            weather.Add(U.Field("Weather forecast", wf));
            weather.Add(U.Field("Media file name", F(t.MediaFileName, "ACME_WDG_P5_BOISE", v => t.MediaFileName = v, "trip:media")).W(420));
            body.Add(weather);

            // ---- logistics
            var log = U.Card().Mb(14); log.name = "sec-logistics";
            var addLine = U.BtnKbd("+ Line", null, () => { s.EditTrip(x => x.Logistics.Add("")); st.Focus = "log:" + (t.Logistics.Count - 1); app.Render(); }, "sm");
            log.Add(U.Row(U.Eyebrow("Logistics").Cls("grow"), addLine).Mb(10));
            for (int i = 0; i < t.Logistics.Count; i++)
            {
                var idx = i;
                var tf = U.Input(t.Logistics[i], null, v => app.Edit(() => s.EditTrip(x => x.Logistics[idx] = v))); tf.name = "log:" + i;
                log.Add(U.Row(new Glyph(GlyphKind.Lines, 20).Cls("handle").W(32), tf.Cls("grow"), U.Btn("", () => { s.EditTrip(x => x.Logistics.RemoveAt(idx)); }, "sm square ml8").Also(b => b.Add(new Glyph(GlyphKind.Cross, 16)))).Mb(6));
            }
            body.Add(log);

            // ---- actions
            var acts = U.Card().Mb(14); acts.name = "sec-actions";
            acts.Add(U.Row(U.Eyebrow("Actions").Cls("grow"), U.Btn("+ Action", () => { var a = s.AddAction(""); st.Focus = "act:" + a.Id; app.Render(); }, "sm")).Mb(10));
            foreach (var a in t.Actions)
            {
                var aid = a.Id;
                var tf = U.Input(a.Text, null, v => app.Edit(() => s.EditTrip(x => x.Actions.First(y => y.Id == aid).Text = v))); tf.name = "act:" + a.Id;
                if (a.Done) tf.Cls("strike");
                acts.Add(U.Row(U.Check(a.Done, () => s.SetActionDone(aid, !a.Done), "md").Mr(10), tf.Cls("grow"),
                    U.Btn("", () => s.EditTrip(x => x.Actions.RemoveAll(y => y.Id == aid)), "sm square ml8").Also(b => b.Add(new Glyph(GlyphKind.Cross, 16)))).Mb(6));
            }
            body.Add(acts);

            // ---- books & teams
            var books = U.Card().Mb(14); books.name = "sec-books";
            books.Add(U.Row(U.Eyebrow("Books & teams").Cls("grow"), U.Btn("+ Book", () => { var b = s.PlanEditor.AddBook(""); st.Focus = "book:" + b.Id + ":name"; app.Render(); }, "sm")).Mb(10));
            foreach (var b in t.Books)
            {
                var bid = b.Id;
                b.Team = b.Team ?? new BookTeam();
                var card = U.Row().Cls("card sf2").Mb(10).Pad(12); card.style.alignItems = Align.FlexStart;
                card.Add(U.Num(b.Number.ToString(), "lg").H(56).W(52).Font(20));
                var col = U.Col().Cls("grow");
                var rr = U.Row(); rr.style.alignItems = Align.FlexStart;
                var name = U.Input(b.Name, "Book name", v => app.Edit(() => s.PlanEditor.UpdateBook(bid, x => x.Name = v))); name.name = "book:" + b.Id + ":name";
                rr.Add(U.Field("Book name", name).Cls("grow").Mr(14).Mb(8));
                var tn = U.Input(b.Team.Number?.ToString() ?? "", null, v => app.Edit(() => s.PlanEditor.UpdateBook(bid, x => x.Team.Number = int.TryParse(v, out var n) ? n : (int?)null))); tn.name = "book:" + b.Id + ":team";
                rr.Add(U.Field("Team #", tn).W(120).Mb(8));
                rr.Add(U.Btn("Remove book", () => ShotListScreen.Guarded(app, () => { s.PlanEditor.RemoveBook(bid); app.Toast("Book removed"); }), "sm danger ml10").Mt(26));
                col.Add(rr);
                col.Add(U.Lbl("Team members · any known name"));
                var members = U.Row().Cls("wrap");
                foreach (var pid in b.Team.MemberIds ?? new List<string>())
                {
                    var p = d.FindPerson(pid); var pidc = pid;
                    members.Add(U.ChipX(U.Esc(p?.FullName ?? pid), () => s.PlanEditor.UpdateBook(bid, x => { x.Team.MemberIds.Remove(pidc); if (p != null) x.Team.MemberNames?.Remove(p.FullName); }), "h44"));
                }
                foreach (var n in b.Team.MemberNames ?? new List<string>())
                {
                    if (b.Team.MemberIds != null && b.Team.MemberIds.Any(id => d.FindPerson(id)?.FullName == n)) continue;
                    var nc = n;
                    members.Add(U.ChipX(U.Esc(n), () => s.PlanEditor.UpdateBook(bid, x => x.Team.MemberNames.Remove(nc)), "h44"));
                }
                var memberQ = new TextField(); memberQ.name = "member:" + b.Id; memberQ.Cls("inp h44").W(220);
                memberQ.textEdition.placeholder = "+ add name";
                app.ClosePopupWhenBlurred(memberQ);
                memberQ.RegisterValueChangedCallback(e =>
                {
                    var q = e.newValue;
                    if (string.IsNullOrWhiteSpace(q)) { app.ClosePopup(); return; }
                    var box = new VisualElement().Cls("sugg"); box.style.position = Position.Relative; box.style.marginTop = 0;
                    foreach (var m in s.Search.SuggestNames(q, NameScope.Any, 4))
                    {
                        var item = m.Item;
                        box.Add(U.Tap(() => AddMember(app, bid, item.Name, item.Person), "sugg-row", U.Text(U.Esc(item.Name), "bold").Cls("grow"), U.Sub(U.Esc(item.Title ?? ""))));
                    }
                    box.Add(U.Tap(() => AddMember(app, bid, q.Trim(), null), "sugg-row", U.Text("Add “" + U.Esc(q.Trim()) + "” as a new person").Cls("grow")));
                    app.ShowPopup(memberQ, box, 360);
                });
                members.Add(memberQ);
                col.Add(members);
                card.Add(col);
                books.Add(card);
            }
            body.Add(books);

            // ---- days
            var days = U.Card(); days.name = "sec-days";
            days.Add(U.Row(U.Eyebrow("Days · captures attach to a day · a day is a calendar date").Cls("grow"),
                U.Btn("+ Day", () => { var nd = s.AddDay(System.DateTime.Now.ToString("yyyy-MM-dd")); st.Focus = "day:" + nd.Id + ":date"; app.Render(); }, "sm")).Mb(10));
            foreach (var day in t.Days)
            {
                var did = day.Id;
                var caps = d.Captures.Captures.Count(c => c.DayId == day.Id) + d.Captures.PhotoCaptures.Count(c => c.DayId == day.Id);
                var label = U.Input(day.Label, "Day 1", v => app.Edit(() => s.EditTrip(x => x.Days.First(y => y.Id == did).Label = v))).W(160).Mr(10); label.name = "day:" + day.Id + ":label";
                var date = U.Input(day.Date, "2026-09-15", v => app.Edit(() => s.EditTrip(x => x.Days.First(y => y.Id == did).Date = v))).W(180).Mr(10); date.name = "day:" + day.Id + ":date";
                var rm = U.Btn("", () => ShotListScreen.Guarded(app, () => s.RemoveDay(did)), "sm square").Also(b => b.Add(new Glyph(GlyphKind.Cross, 16)));
                rm.SetEnabled(caps == 0);
                days.Add(U.Row(label, date, U.Sub(U.FmtDate(day.Date) + " · " + U.Plural(caps, "capture")).Cls("grow"), rm).Mb(6));
            }
            body.Add(days);
            return root;
        }

        private static VisualElement DateEcho(string iso)
        {
            var v = U.Col(U.Sub(U.FmtDate(iso))).W(100).Mr(10);
            v.style.paddingTop = 26;
            return v;
        }

        private static void AddMember(AppController app, string bookId, string name, Person person)
        {
            var s = app.Session;
            if (string.IsNullOrWhiteSpace(name)) return;
            var p = person ?? s.FindOrAddPerson(name, Org.Other, PersonRole.Personnel);
            s.PlanEditor.UpdateBook(bookId, b =>
            {
                b.Team.MemberIds = b.Team.MemberIds ?? new List<string>();
                b.Team.MemberNames = b.Team.MemberNames ?? new List<string>();
                if (!b.Team.MemberIds.Contains(p.Id)) b.Team.MemberIds.Add(p.Id);
                if (!b.Team.MemberNames.Contains(p.FullName)) b.Team.MemberNames.Add(p.FullName);
            });
            if (person == null) app.Toast("Added " + p.FullName + " to people");
            app.Render();
        }
    }

    internal static class Ext
    {
        public static T Also<T>(this T v, System.Action<T> a) { a(v); return v; }
    }
}

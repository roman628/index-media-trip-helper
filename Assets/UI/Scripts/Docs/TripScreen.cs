using System;
using System.Collections.Generic;
using System.Linq;
using MediaTrip.Authoring;
using MediaTrip.Model;
using MediaTrip.Search;
using MediaTrip.UI.Shell;
using MediaTrip.UI.ViewModels;
using UnityEngine;
using UnityEngine.UIElements;

namespace MediaTrip.UI.Docs
{
    /// <summary>
    /// The trip as one scrolling document. The section index (a sidebar in landscape, a chip
    /// bar otherwise) follows the scroll position and jumps on tap. People live here.
    /// </summary>
    public static class TripScreen
    {
        public static readonly (string id, string label)[] Sections =
        {
            ("identity", "Identity"), ("dates", "Dates"), ("location", "Location"), ("weather", "Weather"), ("media", "Media file"),
            ("logistics", "Logistics"), ("actions", "Actions"), ("books", "Books & teams"), ("people", "People"),
        };

        private const string ScrollName = "trip-doc";

        public static VisualElement Build(AppController app)
        {
            var st = app.State; var land = app.Layout.Land;
            var doc = U.Scroll().Named(ScrollName).Cls("docpad");
            var secs = new Dictionary<string, VisualElement>();
            foreach (var (id, label) in Sections)
            {
                var sec = new VisualElement().Cls("sec");
                sec.Add(U.Eyebrow(label).Mb(8));
                Fill(app, id, sec);
                secs[id] = sec;
                doc.Add(sec);
            }

            // ---- the index
            var buttons = new Dictionary<string, Button>();
            ScrollView jumpBar = null;
            VisualElement index;
            if (land) index = new VisualElement().Cls("idx");
            else { jumpBar = U.ScrollX(); jumpBar.Cls("jump"); index = jumpBar; }
            void Mark(string id)
            {
                foreach (var kv in buttons) kv.Value.EnableInClassList("on", kv.Key == id);
                if (jumpBar != null && buttons.TryGetValue(id, out var b) && b.panel != null) jumpBar.ScrollTo(b);
            }
            void Jump(string id)
            {
                st.Trip.Section = id;
                Mark(id);
                if (secs.TryGetValue(id, out var target)) doc.scrollOffset = new Vector2(0, Mathf.Max(0, target.layout.y - 8));
            }
            foreach (var (id, label) in Sections)
            {
                var sid = id;
                var b = new Button(() => Jump(sid)) { text = label }.Cls(land ? "idxb" : "jumpb").On(st.Trip.Section == id);
                buttons[id] = b;
                index.Add(b);
            }

            // ---- the index follows the scroll
            doc.verticalScroller.valueChanged += y =>
            {
                var tops = Sections.Select(x => secs[x.id].layout.y).ToList();
                var i = ListMath.ActiveSection(tops, y, 80f, doc.verticalScroller.highValue);
                if (i < 0 || Sections[i].id == st.Trip.Section) return;
                st.Trip.Section = Sections[i].id;
                Mark(st.Trip.Section);
            };

            if (st.Trip.ScrollTo != null)
            {
                var to = st.Trip.ScrollTo;
                st.Trip.ScrollTo = null;
                app.ResetScroll(ScrollName);
                EventCallback<GeometryChangedEvent> once = null;
                once = _ => { doc.contentContainer.UnregisterCallback(once); doc.schedule.Execute(() => Jump(to)); };
                doc.contentContainer.RegisterCallback(once);
            }

            VisualElement body;
            if (land) body = U.Row(index, doc).Cls("grow stretch minh0");
            else body = U.Col(index, doc).Cls("grow minh0");
            return AppShell.Build(app, "Trip", body, edit: true);
        }

        // ------------------------------------------------------------------ sections

        private static void Fill(AppController app, string id, VisualElement sec)
        {
            var s = app.Session; var d = s.Data; var t = d.Trip; var E = app.State.Edit;
            t.Identity = t.Identity ?? new TripIdentity();
            t.Identity.Location = t.Identity.Location ?? new Location();
            t.Dates = t.Dates ?? new TripDates();

            // A labelled field: once it has a value the placeholder no longer says what it is.
            VisualElement F(string name, string value, string label, Action<TripDocument, string> set, float width = 0)
            {
                var tf = U.Input(value, null, v => app.Edit(() => s.EditTrip(x => set(x, v))), false, "h48");
                tf.name = name;
                var col = U.Col(U.Lbl(label), tf);
                col.style.marginBottom = 8;
                if (width > 0) { col.W(width); col.style.marginRight = 10; }
                return col;
            }

            switch (id)
            {
                case "identity":
                    if (E)
                    {
                        sec.Add(U.Row(
                            F("trip:client", t.Identity.ClientAbbrev, "Client", (x, v) => x.Identity.ClientAbbrev = v, 140),
                            F("trip:program", t.Identity.ProgramAbbrev, "Program", (x, v) => x.Identity.ProgramAbbrev = v, 140),
                            F("trip:phase", t.Identity.PhaseNumber?.ToString() ?? "", "Phase", (x, v) => x.Identity.PhaseNumber = int.TryParse(v, out var n) ? n : (int?)null, 100)).Cls("wrap"));
                    }
                    else
                    {
                        var parts = new[] { t.Identity.ClientAbbrev, t.Identity.ProgramAbbrev, t.Identity.PhaseNumber != null ? "Phase " + t.Identity.PhaseNumber : null }.Where(x => !string.IsNullOrEmpty(x)).ToList();
                        sec.Add(U.H1(parts.Count > 0 ? U.Esc(string.Join(" · ", parts)) : "New trip").Mb(6));
                        if (parts.Count == 0) sec.Add(U.Sub("Tap Edit to fill it in."));
                    }
                    break;

                case "dates":
                    if (E)
                    {
                        sec.Add(U.Row(
                            F("trip:arrive", t.Dates.Arrive, "Arrive · yyyy-mm-dd", (x, v) => x.Dates.Arrive = v, 190),
                            F("trip:depart", t.Dates.Depart, "Depart · yyyy-mm-dd", (x, v) => x.Dates.Depart = v, 190)).Cls("wrap"));
                        foreach (var day in t.Days)
                        {
                            var did = day.Id;
                            var used = d.Captures.Captures.Any(c => c.DayId == did) || d.Captures.PhotoCaptures.Any(c => c.DayId == did);
                            var label = F("day:" + did + ":label", day.Label, "Day", (x, v) => x.Days.First(y => y.Id == did).Label = v, 120);
                            var date = F("day:" + did + ":date", day.Date, "Date", (x, v) => x.Days.First(y => y.Id == did).Date = v, 170);
                            var rm = U.IconBtn(GlyphKind.Cross, () => Guard(app, () => s.RemoveDay(did)), "ghost sm", 16);
                            rm.SetEnabled(!used);
                            rm.style.marginBottom = 8;
                            var dayRow = U.Row(label, date, rm);
                            dayRow.style.alignItems = Align.FlexEnd;
                            sec.Add(dayRow);
                        }
                        sec.Add(U.AddBtn("Day", () =>
                        {
                            var last = t.Days.LastOrDefault()?.Date;
                            var next = DateTime.TryParse(last, out var ld) ? ld.AddDays(1) : DateTime.Now;
                            var nd = s.AddDay(next.ToString("yyyy-MM-dd"), "Day " + (t.Days.Count + 1));
                            app.RenderKeepFocus("day:" + nd.Id + ":date");
                        }));
                    }
                    else
                    {
                        sec.Add(U.KV("Arrive", U.FmtDate(t.Dates.Arrive)));
                        sec.Add(U.KV("Depart", U.FmtDate(t.Dates.Depart)));
                        var chips = U.Row().Cls("wrap").Mt(10);
                        foreach (var day in t.Days) chips.Add(U.With(new VisualElement(), "chip static", U.Text(U.Esc((day.Label ?? "Day") + " · " + U.FmtDate(day.Date)))));
                        sec.Add(chips);
                    }
                    break;

                case "location":
                    if (E)
                    {
                        sec.Add(U.Row(
                            F("trip:city", t.Identity.Location.City, "City", (x, v) => x.Identity.Location.City = v, 200),
                            F("trip:state", t.Identity.Location.State, "State", (x, v) => x.Identity.Location.State = v, 90)).Cls("wrap"));
                        sec.Add(F("trip:address", t.Identity.Location.Address, "Address", (x, v) => x.Identity.Location.Address = v));
                    }
                    else
                    {
                        sec.Add(U.KV("City", U.Esc(Fmt.Place(t))));
                        sec.Add(U.KV("Address", U.Esc(t.Identity.Location.Address ?? "")));
                    }
                    break;

                case "weather":
                    if (E)
                    {
                        var wf = U.Input(t.WeatherForecast, null, v => app.Edit(() => s.EditTrip(x => x.WeatherForecast = v)), true, "h72");
                        wf.name = "trip:weather";
                        sec.Add(wf);
                    }
                    else sec.Add(U.Body(U.Esc(t.WeatherForecast ?? "")));
                    break;

                case "media":
                    if (E) sec.Add(F("trip:media", t.MediaFileName, "Media file name", (x, v) => x.MediaFileName = v, Mathf.Min(320, app.Layout.Width - 60)));
                    else sec.Add(U.Text(U.Esc(t.MediaFileName ?? ""), "h3"));
                    break;

                case "logistics":
                    for (int i = 0; i < t.Logistics.Count; i++)
                    {
                        var idx = i;
                        if (E)
                        {
                            var tf = U.Input(t.Logistics[i], null, v => app.Edit(() => s.EditTrip(x => x.Logistics[idx] = v)), false, "h48 grow");
                            tf.name = "log:" + i;
                            sec.Add(U.Row(tf, U.IconBtn(GlyphKind.Cross, () => s.EditTrip(x => x.Logistics.RemoveAt(idx)), "ghost sm", 16)).Mb(6));
                        }
                        else
                        {
                            var r = U.Row(U.Text("•", "muted").W(22), U.Body(U.Esc(t.Logistics[i])).Cls("grow")).MinH(44);
                            r.style.alignItems = Align.FlexStart;
                            r.style.paddingTop = 4;
                            sec.Add(r);
                        }
                    }
                    if (E) sec.Add(U.AddBtn("Line", () => { s.EditTrip(x => x.Logistics.Add("")); app.RenderKeepFocus("log:" + (t.Logistics.Count - 1)); }));
                    break;

                case "actions":
                    foreach (var a in t.Actions)
                    {
                        var aid = a.Id; var done = a.Done;
                        var row = U.Row(U.Check(a.Done, () => s.SetActionDone(aid, !done), "sm").Mr(12)).Mb(6);
                        if (E)
                        {
                            var tf = U.Input(a.Text, null, v => app.Edit(() => s.EditTrip(x => x.Actions.First(y => y.Id == aid).Text = v)), false, "h48 grow");
                            tf.name = "act:" + aid;
                            row.Add(tf);
                            row.Add(U.IconBtn(GlyphKind.Cross, () => s.EditTrip(x => x.Actions.RemoveAll(y => y.Id == aid)), "ghost sm", 16));
                        }
                        else row.Add(U.Body(a.Done ? U.Strike(a.Text) : U.Esc(a.Text)).Cls("grow" + (a.Done ? " strike" : "")));
                        sec.Add(row);
                    }
                    if (E) sec.Add(U.AddBtn("Action", () => { var a = s.AddAction(""); app.RenderKeepFocus("act:" + a.Id); }));
                    break;

                case "books":
                    foreach (var b in t.Books) sec.Add(BookRow(app, b));
                    if (E) sec.Add(U.AddBtn("Book", () => { var b = s.PlanEditor.AddBook(""); app.RenderKeepFocus("book:" + b.Id + ":name"); }));
                    break;

                case "people":
                    People(app, sec);
                    break;
            }
        }

        private static VisualElement BookRow(AppController app, Book b)
        {
            var s = app.Session; var d = s.Data; var E = app.State.Edit; var bid = b.Id;
            b.Team = b.Team ?? new BookTeam();
            var row = U.Row(U.Num(b.Number.ToString(), "md")).Cls("bordered-bottom top-align");
            row.style.paddingTop = 10; row.style.paddingBottom = 10;
            var col = U.Col().Cls("grow");
            var names = TeamNames(d, b);
            if (!E)
            {
                col.Add(U.H3(U.Esc(string.IsNullOrEmpty(b.Name) ? "Untitled book" : b.Name)));
                var team = (b.Team.Number != null ? "Team " + b.Team.Number : "Team") + (names.Count > 0 ? " · " + string.Join(", ", names) : "");
                col.Add(U.Sub(U.Esc(team)).Mt(2));
            }
            else
            {
                var name = U.Input(b.Name, "Book name", v => app.Edit(() => s.PlanEditor.UpdateBook(bid, x => x.Name = v)), false, "h48 grow");
                name.name = "book:" + bid + ":name";
                col.Add(U.Row(name, U.IconBtn(GlyphKind.Cross, () => Guard(app, () => s.PlanEditor.RemoveBook(bid)), "ghost sm", 16)).Mb(8));
                var members = U.Row().Cls("wrap");
                foreach (var pid in b.Team.MemberIds ?? new List<string>())
                {
                    var p = d.FindPerson(pid); var pidc = pid;
                    members.Add(U.ChipX(U.Esc(p?.FullName ?? pid), () => s.PlanEditor.UpdateBook(bid, x => { x.Team.MemberIds.Remove(pidc); if (p != null) x.Team.MemberNames?.Remove(p.FullName); })));
                }
                foreach (var n in b.Team.MemberNames ?? new List<string>())
                {
                    if (b.Team.MemberIds != null && b.Team.MemberIds.Any(x => d.FindPerson(x)?.FullName == n)) continue;
                    var nc = n;
                    members.Add(U.ChipX(U.Esc(n), () => s.PlanEditor.UpdateBook(bid, x => x.Team.MemberNames.Remove(nc))));
                }
                var q = U.Input("", "+ team member", null, false, "h44").W(190);
                q.name = "member:" + bid;
                q.style.marginBottom = 8;
                app.ClosePopupWhenBlurred(q);
                q.RegisterValueChangedCallback(e =>
                {
                    var text = e.newValue;
                    if (string.IsNullOrWhiteSpace(text)) { app.ClosePopup(); return; }
                    var box = new VisualElement().Cls("sugg");
                    foreach (var m in s.Search.SuggestNames(text, NameScope.Crew, 4))
                    {
                        var item = m.Item;
                        box.Add(U.Tap(() => AddMember(app, bid, item.Name, item.Person), "sugg-row", U.Text(U.Esc(item.Name), "bold").Cls("grow"), U.Sub(U.Esc(item.Title ?? ""))));
                    }
                    box.Add(U.Tap(() => AddMember(app, bid, text.Trim(), null), "sugg-row", U.Text("“" + U.Esc(text.Trim()) + "”").Cls("grow")));
                    app.ShowPopup(q, box, 340);
                });
                members.Add(q);
                col.Add(members);
            }
            row.Add(col);
            return row;
        }

        private static List<string> TeamNames(TripData d, Book b)
        {
            var names = (b.Team?.MemberIds ?? new List<string>()).Select(d.FindPerson).Where(p => p != null).Select(p => p.FullName).ToList();
            foreach (var n in b.Team?.MemberNames ?? new List<string>()) if (!names.Contains(n)) names.Add(n);
            return names;
        }

        private static void AddMember(AppController app, string bookId, string name, Person person)
        {
            var s = app.Session;
            if (string.IsNullOrWhiteSpace(name)) return;
            var p = person ?? s.FindOrAddPerson(name, Org.Index, PersonRole.Personnel);
            s.PlanEditor.UpdateBook(bookId, b =>
            {
                b.Team = b.Team ?? new BookTeam();
                b.Team.MemberIds = b.Team.MemberIds ?? new List<string>();
                b.Team.MemberNames = b.Team.MemberNames ?? new List<string>();
                if (!b.Team.MemberIds.Contains(p.Id)) b.Team.MemberIds.Add(p.Id);
                if (!b.Team.MemberNames.Contains(p.FullName)) b.Team.MemberNames.Add(p.FullName);
            });
            app.ClosePopup();
            app.Render();
        }

        // ------------------------------------------------------------------ people

        private static readonly (Org org, string label)[] Orgs = { (Org.Index, "Index"), (Org.Client, "Client"), (Org.Leadership, "Leadership"), (Org.Other, "Other") };

        private static void People(AppController app, VisualElement sec)
        {
            var s = app.Session; var t = s.Data.Trip; var E = app.State.Edit;
            foreach (var (org, label) in Orgs)
            {
                var people = t.People.Where(p => p.Org == org).ToList();
                if (people.Count == 0) continue;
                sec.Add(U.Eyebrow(label).Mt(10).Mb(4));
                foreach (var p in people)
                {
                    var pid = p.Id;
                    if (!E)
                    {
                        var name = U.Text(U.Esc(p.FullName), "bold");
                        name.style.minWidth = app.Layout.Phone ? 140 : 170; name.style.marginRight = 10;
                        sec.Add(U.Row(name, U.Sub(U.Esc(p.Title ?? "")).Cls("grow")).MinH(48));
                        continue;
                    }
                    TextField PF(string field, string value, string ph, Action<Person, string> set, float width)
                    {
                        var tf = U.Input(value, ph, v => app.Edit(() => s.People.Update(pid, x => set(x, v))), false, "h44");
                        tf.name = "person:" + pid + ":" + field;
                        if (width > 0) tf.W(width); else { tf.Cls("grow"); tf.style.minWidth = 140; }
                        tf.style.marginRight = 6; tf.style.marginBottom = 6;
                        return tf;
                    }
                    var row = U.Row(
                        PF("first", p.FirstName, "First", (x, v) => x.FirstName = v, 130),
                        PF("last", p.LastName, "Last", (x, v) => x.LastName = v, 150),
                        PF("title", p.Title, "Title", (x, v) => x.Title = v, 0)).Cls("wrap");
                    var next = Orgs[(Array.FindIndex(Orgs, o => o.org == p.Org) + 1) % Orgs.Length];
                    var orgChip = U.Chip(label, false, () => MoveOrg(app, pid, next.org));
                    orgChip.style.marginBottom = 6; orgChip.style.marginRight = 0;
                    row.Add(orgChip);
                    var rm = U.IconBtn(GlyphKind.Cross, () => RemovePerson(app, pid), "ghost sm", 16);
                    rm.style.marginBottom = 6;
                    row.Add(rm);
                    sec.Add(row);
                }
            }
            if (!E && t.People.Count == 0) sec.Add(U.Sub("Nobody yet."));
            if (E) sec.Add(U.AddBtn("Person", () =>
            {
                var p = s.People.Add("", "", Org.Client, new[] { PersonRole.Sme });
                app.RenderKeepFocus("person:" + p.Id + ":first");
            }).Mt(6));
        }

        /// <summary>Tapping the organisation chip moves the person to the next group. A role that only came from the old group follows.</summary>
        private static void MoveOrg(AppController app, string personId, Org org)
        {
            app.Session.People.Update(personId, p =>
            {
                var was = DefaultRole(p.Org);
                p.Org = org;
                if (p.Roles == null) p.Roles = new List<PersonRole>();
                if (p.Roles.Count == 0 || (p.Roles.Count == 1 && was != null && p.Roles[0] == was.Value))
                {
                    p.Roles.Clear();
                    var now = DefaultRole(org);
                    if (now != null) p.Roles.Add(now.Value);
                }
            });
        }

        private static PersonRole? DefaultRole(Org org)
        {
            switch (org)
            {
                case Org.Index: return PersonRole.Personnel;
                case Org.Client: return PersonRole.Sme;
                case Org.Leadership: return PersonRole.LeadershipRep;
                default: return null;
            }
        }

        private static void RemovePerson(AppController app, string personId)
        {
            var s = app.Session;
            var p = s.Data.FindPerson(personId);
            if (p == null) return;
            var refs = s.People.ReferencesTo(personId).Count;
            void Go() { s.People.Remove(personId, detach: true); app.Toast(refs > 0 ? "Removed · the name stays as text where it was used" : "Removed"); }
            if (refs == 0) Go();
            else ConfirmSheet.Open(app, "Remove " + U.Esc(p.FullName) + "?", "This person is used in " + U.Plural(refs, "place") + ". The name stays there as plain text.", "Remove", Go);
        }

        /// <summary>Run a plan edit; when captures or changes depend on the thing, say so instead of failing silently.</summary>
        public static void Guard(AppController app, Action edit)
        {
            try { edit(); }
            catch (PlanEditBlockedException ex) { app.Toast("Already filmed or changed (" + U.Plural(ex.References.Count, "record") + "). Use Rename or Drop on the video instead."); app.Render(); }
            catch (Exception ex) { app.Toast(ex.Message); app.Render(); }
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using MediaTrip.Model;
using UnityEngine.UIElements;

namespace MediaTrip.UI.Author
{
    /// <summary>The people registry: one entry per person with org and roles, plus names typed in the field that are not registered yet.</summary>
    public static class PeopleScreen
    {
        private static readonly (PersonRole role, string label)[] Roles =
        {
            (PersonRole.ProjectManager, "Project manager"), (PersonRole.Producer, "Producer"), (PersonRole.Photographer, "Photographer"), (PersonRole.Writer, "Writer"),
            (PersonRole.Editor, "Editor"), (PersonRole.MediaCoordinator, "Media coordinator"), (PersonRole.CoProducer, "Co-producer"), (PersonRole.Sme, "SME"),
            (PersonRole.LeadershipRep, "Leadership rep"), (PersonRole.Personnel, "Personnel"),
        };
        private static readonly (Org org, string label)[] Orgs = { (Org.Index, "Index"), (Org.Client, "Client"), (Org.Leadership, "Leadership"), (Org.Other, "Other") };

        public static string RoleLabel(PersonRole r) => Roles.FirstOrDefault(x => x.role == r).label ?? r.ToString();

        public static VisualElement Build(AppController app)
        {
            var st = app.State; var s = app.Session; var d = s.Data; var pe = s.People;
            var root = U.Row().Cls("grow stretch"); root.style.minHeight = 0;
            var sel = d.FindPerson(st.PeopleSel) ?? d.Trip.People.FirstOrDefault();
            st.PeopleSel = sel?.Id;

            var nav = new VisualElement().Cls("nav");
            var head = new VisualElement().Cls("head");
            var filter = U.Input(st.PeopleQ, "Filter people", v => { st.PeopleQ = v; app.RenderKeepFocus("people.q"); }, false, "h44").Cls("grow").Mr(8); filter.name = "people.q";
            head.Add(filter);
            head.Add(U.Btn("+ New", () => { var p = pe.AddFromName("New person", Org.Client, new[] { PersonRole.Sme }); st.PeopleSel = p.Id; st.Focus = "person:" + p.Id + ":first"; app.Render(); }, "sm pri"));
            nav.Add(head);
            var nb = U.Scroll().Cls("bodyN");
            var q = (st.PeopleQ ?? "").Trim().ToLowerInvariant();
            var list = d.Trip.People.Where(p => q.Length == 0 || (p.FullName + " " + p.Title).ToLowerInvariant().Contains(q)).ToList();
            foreach (var (org, label) in Orgs)
            {
                var rows = list.Where(p => p.Org == org).ToList();
                if (rows.Count == 0) continue;
                nb.Add(U.Eyebrow(label).Mt(10).Mb(4).Ml(10));
                foreach (var p in rows)
                {
                    var pid = p.Id;
                    var row = U.Tap(() => { st.PeopleSel = pid; app.Render(); }, "trow",
                        U.Col(U.Text(string.IsNullOrWhiteSpace(p.FullName) ? "<color=#888888>New person</color>" : U.Esc(p.FullName), "bold"),
                            U.Sub(U.Esc(p.Title ?? "") + (!string.IsNullOrEmpty(p.Title) && p.Roles.Count > 0 ? " · " : "") + string.Join(", ", p.Roles.Select(RoleLabel)))).Cls("grow"));
                    row.MinH(58).On(st.PeopleSel == pid);
                    nb.Add(row);
                }
            }
            var typed = s.TypedButUnregisteredPeople();
            if (typed.Count > 0)
            {
                nb.Add(U.Eyebrow("Typed in the field · not in registry").Cls("warn-text").Mt(14).Mb(4).Ml(10));
                foreach (var (name, title, cap) in typed)
                {
                    var row = new VisualElement().Cls("trow").MinH(64);
                    row.Add(U.Col(U.Text(U.Esc(name), "bold"), U.Sub(U.Esc(title ?? "") + " · in " + U.Esc(cap.Title))).Cls("grow"));
                    row.Add(U.Btn("Add", () => { var p = s.AdoptTypedPerson(name, title); st.PeopleSel = p.Id; app.Toast("Added " + name + " · linked in captures"); app.Render(); }, "sm pri"));
                    nb.Add(row);
                }
            }
            var dups = pe.FindLikelyDuplicates();
            if (dups.Count > 0)
            {
                nb.Add(U.Eyebrow("Possible duplicates").Cls("warn-text").Mt(14).Mb(4).Ml(10));
                foreach (var (a, b, score) in dups)
                {
                    var row = new VisualElement().Cls("trow").MinH(64);
                    row.Add(U.Col(U.Text(U.Esc(a.FullName) + " / " + U.Esc(b.FullName), "bold"), U.Sub((int)(score * 100) + "% similar")).Cls("grow"));
                    row.Add(U.Btn("Merge", () => { pe.Merge(a.Id, b.Id); st.PeopleSel = a.Id; app.Toast("Merged into " + a.FullName); }, "sm"));
                    nb.Add(row);
                }
            }
            nav.Add(nb);
            root.Add(nav);

            var edit = new VisualElement().Cls("edit");
            var body = U.Scroll().Cls("bodyE");
            edit.Add(body);
            if (sel == null) body.Add(U.Sub("Select a person, or add one."));
            else
            {
                var pid = sel.Id;
                var refs = pe.ReferencesTo(pid);
                var del = U.BtnKbd("Delete", Shortcuts.Meta + "⌫", () =>
                {
                    if (refs.Count > 0) { app.Toast("Referenced by " + refs.Count + " items — merge into another person or detach first"); return; }
                    pe.Remove(pid); st.PeopleSel = d.Trip.People.FirstOrDefault()?.Id; app.Render();
                }, "sm danger");
                body.Add(U.Row(U.H1(string.IsNullOrWhiteSpace(sel.FullName) ? "New person" : U.Esc(sel.FullName)).Cls("grow"), del).Mb(16));
                var r = U.Row(); r.style.alignItems = Align.FlexStart;
                var first = U.Input(sel.FirstName, null, v => app.Edit(() => pe.Update(pid, p => p.FirstName = v))); first.name = "person:" + pid + ":first";
                var last = U.Input(sel.LastName, null, v => app.Edit(() => pe.Update(pid, p => p.LastName = v))); last.name = "person:" + pid + ":last";
                var title = U.Input(sel.Title, null, v => app.Edit(() => pe.Update(pid, p => p.Title = v))); title.name = "person:" + pid + ":title";
                r.Add(U.Field("First name", first).W(240).Mr(14));
                r.Add(U.Field("Last name", last).W(260).Mr(14));
                r.Add(U.Field("Title", title).Cls("grow"));
                body.Add(r);
                body.Add(U.Lbl("Organisation"));
                body.Add(U.Segmented(Orgs.Select(o => (o.org.ToString(), o.label)).ToList(), sel.Org.ToString(), id => pe.SetOrg(pid, (Org)System.Enum.Parse(typeof(Org), id))).W(520).Mb(18));
                body.Add(U.Lbl("Roles · pick all that apply · drives which fields suggest this name"));
                var chips = U.Row().Cls("wrap").Mb(10);
                foreach (var (role, label) in Roles)
                {
                    var on = sel.HasRole(role);
                    chips.Add(U.Chip((on ? "✓ " : "") + label, on, () => { if (on) pe.RemoveRole(pid, role); else pe.AddRole(pid, role); }));
                }
                body.Add(chips);
                body.Add(U.Sub("Crew fields suggest Index personnel; SME fields suggest client people plus any SME name typed on this trip; book team fields search everyone. Every field still accepts any name.").Mb(18));
                var usage = U.Card(U.Eyebrow("On this trip").Mb(8));
                var smeOn = d.ShotList.Videos.Where(v => v.SmeIds != null && v.SmeIds.Contains(pid)).ToList();
                var caps = d.Captures.Captures.Where(c => c.People != null && c.People.Any(p => p.PersonId == pid)).ToList();
                var teams = d.Trip.Books.Where(b => b.Team?.MemberIds != null && b.Team.MemberIds.Contains(pid)).ToList();
                if (smeOn.Count > 0) usage.Add(U.Body("SME on " + string.Join(", ", smeOn.Select(v => "#" + v.Number + " " + U.Esc(v.Title)))).Mb(6));
                if (caps.Count > 0) usage.Add(U.Body("On camera in " + string.Join(", ", caps.Select(c => U.Esc(c.Title) + " (" + (d.FindDay(c.DayId)?.Label ?? "?") + ")"))).Mb(6));
                if (teams.Count > 0) usage.Add(U.Body("Team member, Book " + string.Join(", ", teams.Select(b => b.Number))));
                if (smeOn.Count == 0 && caps.Count == 0 && teams.Count == 0) usage.Add(U.Sub("Not referenced yet."));
                body.Add(usage);
            }
            root.Add(edit);

            app.ScreenKeys = e =>
            {
                var ids = d.Trip.People.Select(p => p.Id).ToList();
                var i = ids.IndexOf(st.PeopleSel);
                if (e.keyCode == UnityEngine.KeyCode.DownArrow && i + 1 < ids.Count) { st.PeopleSel = ids[i + 1]; app.Render(); return true; }
                if (e.keyCode == UnityEngine.KeyCode.UpArrow && i > 0) { st.PeopleSel = ids[i - 1]; app.Render(); return true; }
                return false;
            };
            return root;
        }
    }
}

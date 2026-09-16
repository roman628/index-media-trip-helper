using System.Linq;
using MediaTrip.Model;
using MediaTrip.Search;
using MediaTrip.Status;
using MediaTrip.UI.ViewModels;
using UnityEngine.UIElements;

namespace MediaTrip.UI.Field
{
    /// <summary>
    /// Log capture (Callsheet layout): the form on the left, the shot-list matches on the
    /// right until a title is picked, then that item's photos and the outline suggestion.
    /// </summary>
    public static class CaptureScreen
    {
        public static VisualElement Build(AppController app)
        {
            var st = app.State;
            var s = app.Session;
            var d = s.Data;
            var dr = st.Capture ?? (st.Capture = new CaptureDraft());
            var dayId = app.EnsureDay();
            var day = d.FindDay(dayId);
            var nextOrder = s.Queries.NextCapturedOrder(dayId);

            var root = U.Row().Cls("grow stretch");
            root.style.minHeight = 0;
            TextField title = null;
            VisualElement context = null;
            ScrollView rightBody = null;

            // ---------------- left: the form
            var left = U.Scroll().W(680).Pad(20, 24).Cls("bordered-right");
            left.Add(U.Row(U.Col(U.H1(dr.EditingCaptureId != null ? "Capture details" : "Log capture"),
                U.Sub(dr.EditingCaptureId != null ? "Editing a logged capture" : (day?.Label ?? "Today") + " · #" + nextOrder + " in today's order")).Cls("grow")).Mb(12));

            title = U.Input(dr.Title, "What did we just shoot?", v => { dr.SetTitle(v); RefreshRight(); }, false, "tall");
            title.name = "cap.title";
            left.Add(U.Field("Title · type a few letters, tap the shot list match", title));

            context = U.Row().Cls("wrap").Mb(16);
            left.Add(context);
            void FillContext()
            {
                context.Clear();
                var b = d.FindBook(dr.BookId); var c = d.FindChapter(dr.ChapterId);
                if (b != null)
                {
                    context.Add(U.Pill("Book " + b.Number + " · " + U.Esc(b.Name), "ac lg").Mr(8).Mb(8));
                    if (c != null) context.Add(U.Pill("Ch." + c.Number + " · " + U.Esc(c.Name), "ac lg").Mr(8).Mb(8));
                    var it = s.Plan.FindItem(dr.ItemId);
                    if (it != null) context.Add(U.Pill("Planned " + it.DisplayNumber, "lg").Mr(8).Mb(8));
                }
                else
                {
                    var none = !string.IsNullOrWhiteSpace(dr.Title) && dr.Suggestions(s).Count == 0;
                    context.Add(U.Sub("Book and chapter fill in when you pick a match." + (none ? " No match — this will be saved as an unplanned addition to Book " + (d.Trip.Books.FirstOrDefault()?.Number.ToString() ?? "1") + "." : "")));
                }
            }
            FillContext();

            left.Add(U.Field("Brief description", U.Input(dr.Description, "One or two lines for the summary", v => dr.Description = v, true)));
            var camRow = U.Row(); camRow.style.alignItems = Align.FlexStart;
            var stepperHost = new VisualElement();
            void FillStepper()
            {
                stepperHost.Clear();
                stepperHost.Add(U.Stepper(dr.CameraCount, delta => { dr.CameraCount = UnityEngine.Mathf.Clamp(dr.CameraCount + delta, 1, 6); FillStepper(); }));
            }
            FillStepper();
            camRow.Add(U.Field("Cameras", stepperHost).W(220).Mr(16));
            camRow.Add(U.Field("B-roll · “N/A” if none", U.Input(dr.BRoll, null, v => dr.BRoll = v)).Cls("grow"));
            left.Add(camRow);

            var more = U.Tap(() => { dr.More = !dr.More; app.Render(); }, "item outlined mb12",
                U.Row(U.H3(dr.More ? "Hide" : "More summary fields"), U.Sub(" · scene, HPI tool, keywords, people, location, notes").Ml(6)).Cls("grow"),
                new Glyph(dr.More ? GlyphKind.ChevronUp : GlyphKind.ChevronDown, 22));
            more.MinH(56).Mt(8);
            left.Add(more);
            if (dr.More)
            {
                left.Add(U.Field("Scene description", U.Input(dr.Scene, null, v => dr.Scene = v)));
                var two = U.Row(); two.style.alignItems = Align.FlexStart;
                two.Add(U.Field("HPI tool suggestion · free text", U.Input(dr.Hpi, "e.g. Peer check", v => dr.Hpi = v)).Cls("grow").Mr(16));
                two.Add(U.Field("Keywords · comma separated", U.Input(dr.Keywords, null, v => dr.Keywords = v)).Cls("grow"));
                left.Add(two);

                var people = U.Col().Cls("fld");
                people.Add(U.Lbl("People on camera · suggests client SMEs, accepts any name"));
                for (int i = 0; i < dr.People.Count; i++)
                {
                    var idx = i; var p = dr.People[i];
                    people.Add(U.Row(U.Text(U.Esc(p.Name) + (string.IsNullOrEmpty(p.Title) ? "" : " · " + U.Esc(p.Title)), "kw").H(44).Font(16),
                        U.Btn("Remove", () => { dr.People.RemoveAt(idx); app.Render(); }, "sm")).Mb(6));
                }
                Button addBtn = null;
                VisualElement suggHost = null;
                TextField nameField = null;
                nameField = U.Input(dr.PersonName, "Name", v => { dr.PersonName = v; RefreshPeopleSugg(); }); nameField.name = "cap.personName";
                var titleField = U.Input(dr.PersonTitle, "Title", v => dr.PersonTitle = v).W(220); titleField.style.marginLeft = 8; titleField.style.marginRight = 8;
                addBtn = U.Btn("Add", () => { dr.AddPerson(dr.PersonName, dr.PersonTitle); app.RenderKeepFocus("cap.personName"); }, "pri");
                var inputRow = U.Row(nameField.Cls("grow"), titleField, addBtn);
                people.Add(inputRow);
                suggHost = new VisualElement();
                app.ClosePopupWhenBlurred(nameField);
                void RefreshPeopleSugg()
                {
                    addBtn.SetEnabled(!string.IsNullOrWhiteSpace(dr.PersonName));
                    if (string.IsNullOrWhiteSpace(dr.PersonName)) { app.ClosePopup(); return; }
                    var matches = s.Search.SuggestNames(dr.PersonName, NameScope.Sme, 4);
                    if (matches.Count == 0) { app.ClosePopup(); return; }
                    var box = new VisualElement().Cls("sugg"); box.style.position = Position.Relative; box.style.marginTop = 0;
                    foreach (var m in matches)
                    {
                        var n = m.Item;
                        box.Add(U.Tap(() => { dr.AddPerson(n.Name, n.Title, n.Person?.Id); app.RenderKeepFocus("cap.personName"); }, "sugg-row",
                            U.Text(U.Esc(n.Name), "bold").Cls("grow"), U.Sub(U.Esc(n.Title ?? ""))));
                    }
                    app.ShowPopup(nameField, box, 420);
                }
                if (!string.IsNullOrWhiteSpace(dr.PersonName)) nameField.schedule.Execute(RefreshPeopleSugg).StartingIn(1);
                else addBtn.SetEnabled(false);
                left.Add(people);
                left.Add(U.Field("Location", U.Input(dr.Location, null, v => dr.Location = v)));
                left.Add(U.Field("Notes · dictation works here", U.Input(dr.Notes, null, v => dr.Notes = v, true)));
            }
            root.Add(left);

            // ---------------- right: matches, or photos + outline
            var right = U.Col().Cls("grow").Pad(20, 24);
            right.style.minHeight = 0;
            rightBody = U.Scroll().Cls("grow");
            right.Add(rightBody);
            void RefreshRight()
            {
                rightBody.Clear();
                FillContext();
                if (dr.ItemId == null) BuildMatches(app, dr, rightBody, title);
                else BuildPhotosAndOutline(app, dr, rightBody);
            }
            RefreshRight();
            var save = U.Btn(dr.EditingCaptureId != null ? "Save changes" : "Save capture", () =>
            {
                try
                {
                    var cap = dr.Save(s, dayId);
                    app.Toast("Saved · " + (day?.Label ?? "Today") + " #" + cap.CapturedOrder + " · " + cap.Title);
                    st.Capture = null;
                    app.Nav(Screen.Today);
                }
                catch (System.Exception ex) { app.Toast("Could not save: " + ex.Message); }
            }, "pri big grow ml10").H(72);
            save.schedule.Execute(() => save.SetEnabled(dr.CanSave)).Every(150);
            right.Add(U.Row(U.Btn("Cancel", () => { st.Capture = null; app.Nav(Screen.Today); }, "big"), save).Mt(16));
            root.Add(right);
            return root;
        }

        private static void BuildMatches(AppController app, CaptureDraft dr, VisualElement host, TextField titleField)
        {
            var s = app.Session; var d = s.Data;
            host.Add(U.Eyebrow("Matches on the shot list").Mb(8));
            var matches = dr.Suggestions(s);
            if (matches.Count == 0)
            {
                host.Add(U.Sub(string.IsNullOrWhiteSpace(dr.Title) ? "Start typing the title." : "No match. Save as an unplanned addition.").Pad(12, 0));
                return;
            }
            foreach (var m in matches)
            {
                var it = m.Item;
                var eff = s.Plan.Resolve(it.Id).FirstOrDefault() ?? it;
                var row = U.Tap(() => { dr.Pick(s, it.Id); titleField.SetValueWithoutNotify(dr.Title); app.Render(); }, "rowB");
                row.Add(U.Num(it.DisplayNumber).H(44));
                var ch = d.FindChapter(it.ChapterId);
                var sub = "Book " + (d.FindBook(it.BookId)?.Number.ToString() ?? "?") + " › " + (ch != null ? "Ch." + ch.Number : "no chapter") + " · " + U.StatusWord(it.Status)
                          + (it.IsSuperseded ? " · shot as " + eff.DisplayNumber + " " + U.Esc(eff.Title) : "");
                row.Add(U.Col(U.Text(U.Esc(it.Title), "bold"), U.Sub(sub)).Cls("grow"));
                row.Add(U.St(it.Status, 36));
                host.Add(row);
            }
        }

        private static void BuildPhotosAndOutline(AppController app, CaptureDraft dr, VisualElement host)
        {
            var s = app.Session; var d = s.Data;
            host.Add(U.Eyebrow("Photos with this setup").Mb(8));
            if (dr.Photos.Count == 0) host.Add(U.Sub("This item lists no planned photos.").Mb(8));
            foreach (var pid in dr.Photos.Keys.ToList())
            {
                var p = d.FindPhoto(pid);
                if (p == null) continue;
                var on = dr.Photos[pid];
                var row = U.Tap(() => { dr.Photos[pid] = !dr.Photos[pid]; app.Render(); }, "item");
                row.style.paddingLeft = 6;
                row.Add(U.Check(on, () => { dr.Photos[pid] = !dr.Photos[pid]; app.Render(); }, "md").Mr(12));
                var col = U.Col(U.Text(U.Esc(p.Description), "bold")).Cls("grow");
                if (p.HeroType != HeroType.None) col.Add(U.Row(new Glyph(GlyphKind.Star, 12).Cls("accent").Mr(4), U.Sub(U.HeroLabel(p, d.FindChapter(p.ChapterId))).Cls("accent bold")));
                row.Add(col);
                host.Add(row);
            }
            host.Add(U.Field(null, U.Input(dr.ExtraPhoto, "+ Unplanned photo (free text)", v => dr.ExtraPhoto = v)).Mt(8));

            host.Add(U.Eyebrow("Outline assignment").Mt(8).Mb(8));
            if (dr.Outline == null)
            {
                host.Add(U.Sub("No outline match for this title. Assign later from Coverage."));
                return;
            }
            var o = dr.Outline;
            var card = U.Card().Cls(dr.OutlineConfirmed ? "ok" : "ac").Pad(14);
            card.Add(U.Sub(dr.OutlineConfirmed ? "Confirmed" : "Suggested · " + (int)(o.Score * 100) + "% match · never auto-committed").Mb(4));
            card.Add(U.Text(U.Esc(o.Label) + (o.Section != null ? " <color=#888888>· Ch." + o.Chapter?.Number + "</color>" : ""), "bold").Font(18).Mb(12));
            if (dr.OutlineConfirmed) card.Add(U.Pill("Confirmed", "fill", GlyphKind.Check));
            else card.Add(U.Row(U.GlyphBtn(GlyphKind.Check, "Confirm", () => { dr.OutlineConfirmed = true; app.Render(); }, "pri grow"),
                U.Btn("Skip", () => { dr.Outline = null; app.Render(); }, "ml8")));
            host.Add(card);
            var alternatives = s.Search.SuggestOutlineSection(dr.Title, dr.BookId, dr.ChapterId, 4).Where(x => x.SectionId != o.SectionId || x.ChapterId != o.ChapterId).ToList();
            if (alternatives.Count > 0)
            {
                host.Add(U.Sub("Or pick another:").Mt(10).Mb(6));
                var chips = U.Row().Cls("wrap");
                foreach (var alt in alternatives) chips.Add(U.Chip(alt.Label, false, () => { dr.Outline = alt; dr.OutlineConfirmed = true; app.Render(); }, "h44"));
                host.Add(chips);
            }
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using MediaTrip.Model;
using MediaTrip.Status;
using MediaTrip.UI.Shell;
using MediaTrip.UI.ViewModels;
using UnityEngine;
using UnityEngine.UIElements;

namespace MediaTrip.UI.Docs
{
    /// <summary>
    /// The shot list: Working / Original / Changes and "Hide done". Tapping a video opens its
    /// detail, a side panel in landscape and a sheet otherwise, where Film is the primary
    /// action with Rename, Combine and Drop beneath it. Edit turns the list into the plan
    /// editor: videos, chapters and the master photo list.
    /// </summary>
    public static class ShotListScreen
    {
        public static VisualElement Build(AppController app)
        {
            var st = app.State; var L = st.SL; var s = app.Session; var E = st.Edit;
            if (L.VideoId != null && s.Plan.FindItem(L.VideoId) == null) L.VideoId = null;
            if (L.PhotoId != null && (!E || s.Data.FindPhoto(L.PhotoId) == null)) L.PhotoId = null;

            var col = U.Col().Cls("grow minh0");
            col.Add(Controls(app));
            var list = U.Scroll().Named("sl-list:" + L.View).Cls("listpad");
            if (L.View == ShotListMode.Changes) FillChanges(app, list);
            else FillList(app, list);
            col.Add(list);

            VisualElement body = col;
            if (app.Layout.Land && (L.VideoId != null || L.PhotoId != null))
            {
                var panel = new VisualElement().Cls("detail");
                panel.style.width = Mathf.Round(app.Layout.Width * 0.42f);
                panel.Add(L.PhotoId != null ? PhotoEditor(app, L.PhotoId) : VideoDetail.Build(app, L.VideoId));
                body = U.Row(col, panel).Cls("grow stretch minh0");
            }
            return AppShell.Build(app, "Shot list", body, edit: true);
        }

        public static VisualElement Overlay(AppController app)
        {
            var L = app.State.SL;
            if (L.Amend != null) return AmendSheet.Build(app);
            if (app.Layout.Land) return null;
            if (L.PhotoId != null) return app.Sheet(760, () => Close(app), PhotoEditor(app, L.PhotoId));
            if (L.VideoId != null) return app.Sheet(760, () => Close(app), VideoDetail.Build(app, L.VideoId));
            return null;
        }

        public static void Close(AppController app)
        {
            app.State.SL.VideoId = null;
            app.State.SL.PhotoId = null;
            app.State.Focus = null;
            app.Render();
        }

        private static VisualElement Controls(AppController app)
        {
            var L = app.State.SL; var n = app.Session.Data.Captures.Amendments.Count;
            var c = new VisualElement().Cls("controls");
            c.Add(U.Segmented(new List<(string, string)>
            {
                ("Working", "Working"), ("Original", "Original"), ("Changes", "Changes" + (n > 0 ? " · " + n : "")),
            }, L.View.ToString(), id => { L.View = (ShotListMode)System.Enum.Parse(typeof(ShotListMode), id); app.Render(); }));
            if (L.View != ShotListMode.Changes) c.Add(U.Toggle("Hide done", L.HideDone, () => { L.HideDone = !L.HideDone; app.Render(); }));
            return c;
        }

        private static void Select(AppController app, string videoId)
        {
            var L = app.State.SL;
            L.VideoId = videoId; L.PhotoId = null;
            app.State.Focus = null;
            app.Render();
        }

        private static void FillList(AppController app, ScrollView list)
        {
            var st = app.State; var L = st.SL; var s = app.Session; var d = s.Data; var E = st.Edit;
            var view = ShotListView.Build(s, L.View, L.HideDone, keepEmpty: E);
            if (view.IsEmpty)
            {
                list.Add(U.Sub(d.Trip.Books.Count == 0 ? "No books yet. Add them in Trip." : "Nothing left.").Pad(20, 12));
                return;
            }
            foreach (var bg in view.Books)
            {
                list.Add(U.H2("Book " + bg.Book.Number + " · " + U.Esc(bg.Book.Name)).Cls("bookhead"));
                foreach (var cg in bg.Chapters)
                {
                    var chId = cg.Chapter.Id;
                    if (E)
                    {
                        var name = U.Input(cg.Chapter.Name, "Chapter name", v => app.Edit(() => s.PlanEditor.UpdateChapter(chId, x => x.Name = v)), false, "h44 grow");
                        name.name = "chapter:" + chId;
                        list.Add(U.Row(U.Num("Ch." + cg.Chapter.Number, "sm"), name,
                            U.IconBtn(GlyphKind.Cross, () => TripScreen.Guard(app, () => s.PlanEditor.RemoveChapter(chId)), "ghost sm", 16)).Mt(12).Mb(4));
                    }
                    else list.Add(U.Eyebrow("Ch." + cg.Chapter.Number + " · " + cg.Chapter.Name).Cls("chaphead"));

                    if (cg.Hero != null && !E)
                    {
                        var hero = cg.Hero;
                        var ring = U.StPhoto(hero.Status, 30, true); ring.style.marginLeft = 7; ring.style.marginRight = 12;
                        list.Add(U.Tap(() => app.Nav(Screen.Covers), "item h48", ring, U.Text("Hero · " + U.Esc(U.ShortDescription(hero.Description)), "sub bold grow")));
                    }
                    foreach (var r in cg.Rows)
                    {
                        var id = r.Item.Id;
                        list.Add(U.Tap(() => Select(app, id), "item",
                            U.Num(r.Number),
                            U.Col(U.Text(r.Dim ? U.Strike(r.Title) : U.Esc(string.IsNullOrEmpty(r.Title) ? "Untitled" : r.Title), "title" + (r.Dim ? " strike" : "")), r.Sub.Length > 0 ? U.Sub(U.Esc(r.Sub)) : null).Cls("grow"),
                            U.St(r.Status, 36).Ml(8)).On(L.VideoId == id));
                    }
                    if (E) list.Add(U.AddBtn("Video", () =>
                    {
                        var v = s.PlanEditor.AddVideo(chId, "");
                        L.VideoId = v.Id; L.PhotoId = null;
                        app.RenderKeepFocus("video:" + v.Id + ":title");
                    }));
                }
                if (!E) continue;
                var bookId = bg.Book.Id;
                list.Add(U.AddBtn("Chapter", () => { var c = s.PlanEditor.AddChapter(bookId, ""); app.RenderKeepFocus("chapter:" + c.Id); }).Mt(6));

                list.Add(U.Eyebrow("Photos").Cls("chaphead"));
                foreach (var p in s.Queries.PhotosOfBook(bookId, false))
                {
                    var pid = p.Id;
                    var label = p.HeroType == HeroType.None ? null : U.Pill(U.HeroLabel(p.Photo, d.FindChapter(p.ChapterId)), "ac");
                    list.Add(U.Tap(() => { L.PhotoId = pid; L.VideoId = null; app.State.Focus = null; app.Render(); }, "item h52",
                        new Glyph(GlyphKind.Frame, 20).Ml(12).Mr(24),
                        U.Text(U.Esc(string.IsNullOrEmpty(p.Description) ? "Untitled photo" : U.ShortDescription(p.Description)), "grow"), label).On(L.PhotoId == pid));
                }
                list.Add(U.AddBtn("Photo", () =>
                {
                    var p = s.PlanEditor.AddPhoto(bookId, "");
                    L.PhotoId = p.Id; L.VideoId = null;
                    app.RenderKeepFocus("photo:" + p.Id);
                }));
            }
        }

        private static void FillChanges(AppController app, ScrollView list)
        {
            var s = app.Session; var E = app.State.Edit;
            var view = ShotListView.Build(s, ShotListMode.Changes, false);
            if (view.Changes.Count == 0) { list.Add(U.Sub("No changes. The plan stands as printed.").Pad(20, 12)); return; }
            foreach (var c in view.Changes)
            {
                var change = c;
                var text = U.Col(U.Body(c.Line), U.Sub(U.Esc(U.FmtTimestamp(c.Amendment.At) + (string.IsNullOrEmpty(c.Reason) ? "" : " · " + c.Reason))).Mt(2)).Cls("grow");
                var row = U.Tap(() => { if (change.OpenItemId != null) { app.State.SL.View = ShotListMode.Original; app.OpenVideo(change.OpenItemId); } }, "change", U.Pill(c.Label, c.Style + " label"), text);
                if (!E) { list.Add(row); continue; }
                var undo = U.Btn("Undo", () => ConfirmSheet.Open(app, "Undo “" + change.Label.ToLowerInvariant() + "”?", "The change is removed from the record and the plan goes back to how it was before it.", "Undo",
                    () => { s.UndoAmendment(change.Amendment.Id); app.Toast("Undone"); }), "sm danger ml8");
                var wrap = U.Row(row, undo);
                row.style.width = StyleKeyword.Auto; row.Cls("grow");
                list.Add(wrap);
            }
        }

        // ------------------------------------------------------------------ master photo editor (edit state)

        private static VisualElement PhotoEditor(AppController app, string photoId)
        {
            var s = app.Session; var d = s.Data; var ed = s.PlanEditor; var p = d.FindPhoto(photoId);
            var col = U.Col().Cls("grow minh0");
            var head = U.Row(U.Eyebrow("Photo · Book " + (d.FindBook(p.BookId)?.Number.ToString() ?? "?")).Cls("grow"), app.Layout.Land ? null : U.Btn("Close", () => Close(app), "sm")).Mb(10);
            head.style.flexShrink = 0;
            col.Add(head);
            var body = U.Scroll().Named("sl-photo:" + photoId);
            var desc = U.Input(p.Description, "What is in the photo", v => app.Edit(() => ed.UpdatePhoto(photoId, x => x.Description = v)), false, "tall");
            desc.name = "photo:" + photoId;
            body.Add(U.Field("Description", desc));
            body.Add(U.Lbl("Used as"));
            var chips = U.Row().Cls("wrap").Mb(10);
            chips.Add(U.Chip("Detail", p.HeroType == HeroType.None, () => TripScreen.Guard(app, () => ed.UpdatePhoto(photoId, x => { x.HeroType = HeroType.None; x.ChapterId = null; }))));
            chips.Add(U.Chip("Book cover", p.HeroType == HeroType.BookCover, () => TripScreen.Guard(app, () => ed.UpdatePhoto(photoId, x => { x.HeroType = HeroType.BookCover; x.ChapterId = null; }))));
            foreach (var ch in d.ChaptersOf(p.BookId))
            {
                var cid = ch.Id;
                chips.Add(U.Chip("Ch." + ch.Number + " hero", p.HeroType == HeroType.ChapterHero && p.ChapterId == cid,
                    () => TripScreen.Guard(app, () => ed.UpdatePhoto(photoId, x => { x.HeroType = HeroType.ChapterHero; x.ChapterId = cid; }))));
            }
            body.Add(chips);
            var del = U.Btn("Delete photo", () => TripScreen.Guard(app, () => { ed.RemovePhoto(photoId); app.State.SL.PhotoId = null; }), "sm ghost danger left");
            del.style.alignSelf = Align.FlexStart;
            body.Add(del.Mt(10));
            col.Add(body);
            return col;
        }
    }

    /// <summary>One video: read it, film it, change it. In the edit state, the plan editor for that video.</summary>
    public static class VideoDetail
    {
        public static VisualElement Build(AppController app, string itemId)
        {
            var item = app.Session.Plan.FindItem(itemId);
            if (item == null) return U.Sub("Gone.");
            return app.State.Edit && item.PlannedVideo != null ? Editor(app, item) : Read(app, item);
        }

        private static VisualElement Head(AppController app, PlanItem item, VisualElement right)
        {
            var head = U.Row(U.Num(ShotListView.NumberOf(item), "md"), U.Grow(), right, app.Layout.Land ? null : U.Btn("Close", () => ShotListScreen.Close(app), "sm ml8")).Mb(8);
            head.style.flexShrink = 0;
            return head;
        }

        private static VisualElement Read(AppController app, PlanItem item)
        {
            var s = app.Session; var d = s.Data; var L = app.State.SL;
            var col = U.Col().Cls("grow minh0");
            col.Add(Head(app, item, U.StatusPill(item.Status)));
            col.Add(U.H2(item.IsDropped ? U.Strike(item.Title) : U.Esc(item.Title)).Cls(item.IsDropped ? "strike" : "").Mb(6));
            var sme = Fmt.SmeOf(d, item);
            var b = d.FindBook(item.BookId); var ch = d.FindChapter(item.ChapterId);
            col.Add(U.Sub(U.Esc("Book " + (b?.Number.ToString() ?? "?") + " › " + (ch != null ? "Ch." + ch.Number + " " + ch.Name : "no chapter") + (sme.Length > 0 ? " · " + sme : ""))).Mb(14));

            if (item.IsSuperseded)
                foreach (var r in item.ResolvedItems.Where(x => x.Id != item.Id))
                {
                    var rid = r.Id;
                    col.Add(U.GlyphBtn(GlyphKind.Arrow, "Shot as “" + U.Esc(r.Title) + "”", () => { L.View = ShotListMode.Working; L.VideoId = rid; app.Render(); }, "left mb12"));
                }

            var cap = item.Captures.FirstOrDefault();
            if (cap != null)
            {
                var when = app.DayLabel(cap.DayId) + (string.IsNullOrEmpty(cap.At) ? "" : " · " + U.FmtTime(cap.At));
                col.Add(U.GlyphBtn(GlyphKind.Square, U.Esc(when) + " · open in Summary", () => app.OpenSummary(cap.Id), "left mb12"));
            }
            else if (!item.IsDropped && !item.IsSuperseded)
                col.Add(U.GlyphBtn(GlyphKind.Play, "Film", () => app.StartFilm(item.Id), "big pri mb12", 20));

            if (!item.IsDropped && !item.IsSuperseded)
            {
                var chips = U.Row().Cls("wrap").Mb(2);
                chips.Add(U.Chip("Rename", false, () => Amend(app, item.Id, "rename")));
                if (item.Origin == PlanItemOrigin.Planned)
                {
                    chips.Add(U.Chip("Combine…", false, () => Amend(app, item.Id, "combine")));
                    chips.Add(U.Chip("Drop", false, () => Amend(app, item.Id, "drop")));
                }
                col.Add(chips);
            }

            var body = U.Scroll().Named("sl-detail:" + item.Id);
            if (!string.IsNullOrEmpty(item.SceneDescription)) { body.Add(U.Eyebrow("Scene")); body.Add(U.Body(U.Esc(item.SceneDescription)).Mt(4).Mb(14)); }
            if (item.Notes.Count > 0) { body.Add(U.Eyebrow("Notes").Mb(6)); body.Add(NotesView.Build(item.Notes)); }
            var photos = item.PhotoRefs.Select(s.Plan.FindPhoto).Where(p => p != null).ToList();
            if (photos.Count > 0)
            {
                body.Add(U.Eyebrow("Photos with this setup").Mt(14).Mb(6));
                foreach (var p in photos)
                    body.Add(U.Row(U.StPhoto(p.Status, 30, p.HeroType != HeroType.None), U.Text(U.Esc(p.Description), "body-sm grow").Ml(10)).MinH(44));
            }
            var reason = item.DropAmendment?.Reason ?? item.CreatedBy?.Reason;
            if (!string.IsNullOrEmpty(reason)) body.Add(U.Sub(U.Esc(reason)).Mt(14));
            col.Add(body);
            return col;
        }

        private static void Amend(AppController app, string itemId, string mode)
        {
            var a = AmendDraft.For(app.Session, itemId);
            a.SetMode(app.Session, mode);
            app.State.SL.Amend = a;
            app.RenderKeepFocus(mode == "drop" ? "am.reason" : "am.title");
        }

        // ------------------------------------------------------------------ edit state: the plan editor for one video

        private static VisualElement Editor(AppController app, PlanItem item)
        {
            var s = app.Session; var d = s.Data; var ed = s.PlanEditor; var v = item.PlannedVideo; var vid = v.Id;
            var locked = ed.IsReferenced(vid);
            var col = U.Col().Cls("grow minh0");
            var siblings = ed.VideosOfChapter(v.ChapterId);
            var at = siblings.FindIndex(x => x.Id == vid);
            var up = U.IconBtn(GlyphKind.ArrowUp, () => ed.ReorderVideo(vid, at - 1), "ghost sm", 20); up.SetEnabled(at > 0);
            var down = U.IconBtn(GlyphKind.ArrowDown, () => ed.ReorderVideo(vid, at + 1), "ghost sm", 20); down.SetEnabled(at >= 0 && at < siblings.Count - 1);
            col.Add(Head(app, item, U.Row(up, down)));

            var body = U.Scroll().Named("sl-edit:" + vid);
            var title = U.Input(v.Title, "Title", val => app.Edit(() => TripScreen.Guard(app, () => ed.SetVideoTitle(vid, val))), false, "tall");
            title.name = "video:" + vid + ":title";
            title.SetEnabled(!locked);
            body.Add(U.Field("Title", title));
            if (locked) body.Add(U.Sub("Filmed or changed already. Use Rename outside Edit.").Mb(14));

            body.Add(U.Lbl("SME"));
            var smes = U.Row().Cls("wrap").Mb(6);
            foreach (var pid in v.SmeIds ?? new List<string>())
            {
                var pidc = pid;
                smes.Add(U.ChipX(U.Esc(d.FindPerson(pid)?.FullName ?? pid), () => ed.UpdateVideo(vid, x => x.SmeIds.Remove(pidc))));
            }
            if (!string.IsNullOrEmpty(v.SmeText)) smes.Add(U.ChipX(U.Esc(v.SmeText), () => ed.UpdateVideo(vid, x => x.SmeText = "")));
            foreach (var p in d.Trip.People.Where(p => p.HasRole(PersonRole.Sme) && !(v.SmeIds ?? new List<string>()).Contains(p.Id)))
            {
                var pidc = p.Id;
                smes.Add(U.Chip("+ " + p.FullName, false, () => ed.UpdateVideo(vid, x => { x.SmeIds = x.SmeIds ?? new List<string>(); x.SmeIds.Add(pidc); }), "dashed"));
            }
            body.Add(smes);

            var scene = U.Input(v.SceneDescription, null, val => app.Edit(() => ed.UpdateVideo(vid, x => x.SceneDescription = val)), true, "h72");
            scene.name = "video:" + vid + ":scene";
            body.Add(U.Field("Scene", scene));

            body.Add(U.Lbl("Notes"));
            body.Add(NodeEditor.Build(app, ed.Notes(vid), new AppState.PasteState { Kind = "video", Id = vid }));

            var photos = s.Queries.PhotosOfBook(v.BookId);
            if (photos.Count > 0)
            {
                body.Add(U.Lbl("Photos with this setup").Mt(14));
                var chips = U.Row().Cls("wrap");
                foreach (var p in photos)
                {
                    var pid = p.Id;
                    var on = v.PhotoRefs != null && v.PhotoRefs.Contains(pid);
                    chips.Add(U.Chip(U.ShortDescription(p.Description), on, () => { if (on) ed.RemovePhotoRef(vid, pid); else ed.AddPhotoRef(vid, pid); }, "wrapok"));
                }
                body.Add(chips);
            }
            var del = U.Btn("Delete video", () => TripScreen.Guard(app, () => { ed.RemoveVideo(vid); app.State.SL.VideoId = null; }), "sm ghost danger left");
            del.style.alignSelf = Align.FlexStart;
            del.SetEnabled(!locked);
            body.Add(del.Mt(16));
            col.Add(body);
            return col;
        }
    }

    /// <summary>Rename, combine or drop, each with a reason. The plan is not edited; the change is recorded.</summary>
    public static class AmendSheet
    {
        public static VisualElement Build(AppController app)
        {
            var s = app.Session; var L = app.State.SL; var a = L.Amend;
            var item = s.Plan.FindItem(a.ItemId);
            void Close() { L.Amend = null; app.State.Focus = null; app.Render(); }
            if (item == null) { L.Amend = null; return null; }

            var apply = U.Btn(a.Mode == "drop" ? "Drop" : "Save", () =>
            {
                try
                {
                    var id = a.Apply(s);
                    L.Amend = null;
                    L.VideoId = id;
                    if (a.Mode == "drop") L.View = ShotListMode.Original;
                    app.State.Focus = null;
                    app.Render();
                    app.Toast("Recorded in Changes");
                }
                catch (System.Exception ex) { app.Toast(ex.Message); }
            }, "big " + (a.Mode == "drop" ? "danger" : "pri"));
            apply.SetEnabled(a.CanApply);

            var body = U.Scroll();
            TextField title = null;
            if (a.Mode == "combine")
            {
                body.Add(U.Lbl("With"));
                var sibs = a.Siblings(s);
                if (sibs.Count == 0) body.Add(U.Sub("No other video in this chapter.").Mb(14));
                foreach (var sib in sibs)
                {
                    var sid = sib.Id;
                    var row = U.Tap(() => { a.ToggleWith(s, sid); app.RenderKeepFocus(null); }, "item",
                        U.With(new VisualElement(), "check sm" + (a.With.Contains(sid) ? " on" : ""), a.With.Contains(sid) ? new Glyph(GlyphKind.Check, 26, 3.5f) : null).Mr(10),
                        U.Num(ShotListView.NumberOf(sib)), U.Text(U.Esc(sib.Title), "bold grow"));
                    row.style.paddingLeft = 4;
                    body.Add(row);
                }
            }
            if (a.Mode == "drop") body.Add(U.Body("#" + ShotListView.NumberOf(item) + " " + U.Esc(item.Title)).Mb(14));
            else
            {
                title = U.Input(a.Title, null, v => { a.Title = v; apply.SetEnabled(a.CanApply); });
                title.name = "am.title";
                body.Add(U.Field(a.Mode == "rename" ? "Title as shot" : "Title", title).Mt(a.Mode == "combine" ? 8 : 0));
            }
            var reason = U.Input(a.Reason, null, v => a.Reason = v);
            reason.name = "am.reason";
            body.Add(U.Field("Reason", reason));

            var foot = U.Row(U.Grow(), apply).Mt(12);
            foot.style.flexShrink = 0;
            return app.Sheet(640, Close, app.SheetHead(AmendDraft.ModeTitle(a.Mode) + " · #" + ShotListView.NumberOf(item), Close), body, foot);
        }
    }
}

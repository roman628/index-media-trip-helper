using System;
using System.Collections.Generic;
using System.Linq;
using MediaTrip.Authoring;
using MediaTrip.Model;
using MediaTrip.Search;
using UnityEngine.UIElements;

namespace MediaTrip.UI.Author
{
    /// <summary>Shot list editor: the tree (videos or master photos), the editor for the selection, the accessory bar.</summary>
    public static class ShotListScreen
    {
        public static VisualElement Build(AppController app)
        {
            var st = app.State;
            var L = st.SL;
            var s = app.Session;
            var d = s.Data;
            var ed = s.PlanEditor;
            var val = app.CurrentValidation;
            if (L.Book == null || d.FindBook(L.Book) == null) L.Book = d.Trip.Books.FirstOrDefault()?.Id;
            if (L.SelKind == "video" && d.FindVideo(L.SelId) == null) { L.SelId = d.ShotList.Videos.FirstOrDefault()?.Id; if (L.SelId == null) L.SelKind = "none"; }
            if (L.SelKind == "photo" && d.FindPhoto(L.SelId) == null) { L.SelId = d.ShotList.Photos.FirstOrDefault()?.Id; if (L.SelId == null) L.SelKind = "none"; }
            if (L.SelKind == "chapter" && d.FindChapter(L.SelId) == null) L.SelKind = "none";

            var root = U.Row().Cls("grow stretch"); root.style.minHeight = 0;
            root.Add(Tree(app));
            var edit = new VisualElement().Cls("edit");
            var body = U.Scroll().Cls("bodyE");
            edit.Add(body);

            if (st.Renumber.AnyShifted(d))
            {
                var banner = new VisualElement().Cls("banner");
                banner.Add(U.Body("<b>Numbering changed.</b> Video numbers run continuously across books and chapters, so inserting or moving shifts everything after it. Shifted videos show “was N” until you accept — the crew's paper still says N.").Cls("grow"));
                banner.Add(U.Btn("Accept new numbers", () => { st.Renumber.Accept(d); app.Render(); }, "sm ml14"));
                body.Add(banner);
            }

            NodeListEditor notes = null;
            if (L.SelKind == "video" && L.SelId != null) { notes = ed.Notes(L.SelId); VideoEditor(app, body, d.FindVideo(L.SelId), notes); }
            else if (L.SelKind == "chapter") ChapterEditor(app, body, d.FindChapter(L.SelId));
            else if (L.SelKind == "photo") PhotoEditor(app, body, d.FindPhoto(L.SelId));
            else body.Add(U.Sub("Select a video, chapter or photo. Use + Video to start a plan."));

            bool nodeMode = L.SelKind == "video" && L.NoteSel != null && notes != null && NodeTree.Locate(notes.Roots, L.NoteSel) != null;
            if (!nodeMode && L.SelKind == "video") L.NoteSel = null;
            edit.Add(AccessoryBar.Build(app, nodeMode ? "node" : L.SelKind, canChild: nodeMode || L.SelKind == "chapter", canIndent: nodeMode, showPaste: L.SelKind == "video"));
            root.Add(edit);

            app.AccessoryAction = act => Accessory(app, act, notes, nodeMode);
            return root;
        }

        // ------------------------------------------------------------------ tree

        private static VisualElement Tree(AppController app)
        {
            var st = app.State; var L = st.SL; var d = app.Session.Data; var val = app.CurrentValidation;
            var nav = new VisualElement().Cls("nav").W(360);
            var head = new VisualElement().Cls("head");
            head.Add(U.Segmented(new List<(string, string)> { ("videos", "Videos"), ("photos", "Master photos") }, L.Pane, id =>
            {
                L.Pane = id; L.NoteSel = null;
                if (id == "photos" && L.SelKind != "photo") { var p = d.ShotList.Photos.FirstOrDefault(x => x.BookId == L.Book) ?? d.ShotList.Photos.FirstOrDefault(); L.SelKind = p != null ? "photo" : "none"; L.SelId = p?.Id; }
                if (id == "videos" && L.SelKind == "photo") { var v = d.ShotList.Videos.FirstOrDefault(); L.SelKind = v != null ? "video" : "none"; L.SelId = v?.Id; }
                app.Render();
            }, 44).Cls("grow"));
            nav.Add(head);
            var body = U.Scroll().Cls("bodyN");

            if (L.Pane == "videos")
            {
                foreach (var b in d.Trip.Books)
                {
                    var collapsed = L.Collapsed.Contains(b.Id);
                    var br = U.Tap(() => { if (!L.Collapsed.Remove(b.Id)) L.Collapsed.Add(b.Id); app.Render(); }, "trow",
                        new Glyph(collapsed ? GlyphKind.ChevronRight : GlyphKind.ChevronDown, 16).Cls("muted").W(22),
                        U.Text(("Book " + b.Number + " · " + b.Name).ToUpperInvariant(), "bold").Font(15).Cls("grow ellipsis"));
                    br.MinH(48);
                    body.Add(br);
                    if (collapsed) continue;
                    foreach (var c in d.ChaptersOf(b.Id))
                    {
                        var cr = U.Tap(() => Select(app, "chapter", c.Id), "trow indent1", U.Text("Ch." + c.Number + " " + U.Esc(c.Name), "bold muted").Font(15).Cls("grow ellipsis"));
                        cr.MinH(44);
                        cr.On(L.SelKind == "chapter" && L.SelId == c.Id);
                        body.Add(cr);
                        foreach (var v in d.ShotList.Videos.Where(x => x.ChapterId == c.Id)) body.Add(VideoRow(app, v, 2));
                    }
                }
                var orphans = d.ShotList.Videos.Where(v => d.FindChapter(v.ChapterId) == null).ToList();
                if (orphans.Count > 0)
                {
                    body.Add(U.Text("No chapter · pick one in the editor", "bold bad-text").Font(15).MinH(44).Pad(0, 10));
                    foreach (var v in orphans) body.Add(VideoRow(app, v, 0));
                }
                var addRow = U.Row().Pad(10, 6);
                addRow.Add(U.Btn("+ Chapter", () => AddChapter(app), "sm grow"));
                addRow.Add(U.BtnKbd("+ Video", "Enter", () => Accessory(app, "sib", null, false), "sm grow pri ml8"));
                body.Add(addRow);
            }
            else
            {
                foreach (var b in d.Trip.Books)
                {
                    body.Add(U.Eyebrow("Book " + b.Number + " · " + b.Name).Mt(10).Mb(4).Ml(10));
                    foreach (var p in d.ShotList.Photos.Where(x => x.BookId == b.Id).OrderBy(x => x.Order))
                    {
                        var used = d.ShotList.Videos.Count(v => v.PhotoRefs != null && v.PhotoRefs.Contains(p.Id));
                        var ch = d.FindChapter(p.ChapterId);
                        var pr = U.Tap(() => Select(app, "photo", p.Id), "trow",
                            new Glyph(GlyphKind.Lines, 18).Cls("handle"),
                            U.Num(p.Order.ToString(), "sm"),
                            U.Col(U.Text(U.Esc(p.Description), "bold").Font(15).Cls("ellipsis"),
                                U.Sub((p.HeroType != HeroType.None ? "★ " + U.HeroLabel(p, ch) + " · " : "") + "used by " + used + (p.IsShared ? " · shared" : ""))).Cls("grow"));
                        pr.On(L.SelKind == "photo" && L.SelId == p.Id);
                        var n = val.CountFor(p.Id);
                        if (n > 0) pr.Add(U.Pill(n.ToString(), "fillbad"));
                        body.Add(pr);
                    }
                    var add = U.Tap(() => AddPhoto(app, b.Id, false), "trow indent1", U.Text("+ Photo", "bold accent"));
                    add.MinH(44);
                    body.Add(add);
                }
            }
            nav.Add(body);
            return nav;
        }

        private static VisualElement VideoRow(AppController app, Video v, int indent)
        {
            var st = app.State; var L = st.SL; var val = app.CurrentValidation;
            var n = val.CountFor(v.Id);
            var was = st.Renumber.Was(v);
            var row = U.Tap(() => Select(app, "video", v.Id), "trow" + (indent == 2 ? " indent2" : ""),
                new Glyph(GlyphKind.Lines, 18).Cls("handle"),
                U.Num(v.Number.ToString(), "sm"),
                U.Text(string.IsNullOrWhiteSpace(v.Title) ? "<color=#888888>Untitled</color>" : U.Esc(v.Title), "bold").Font(15).Cls("grow ellipsis"));
            row.On(L.SelKind == "video" && L.SelId == v.Id);
            if (was != null) row.Add(U.Pill("was " + was, "warn").H(24).Font(12));
            if (n > 0) row.Add(U.Pill(n.ToString(), "fillbad").H(24).Font(12).Ml(6));
            return row;
        }

        private static void Select(AppController app, string kind, string id)
        {
            var L = app.State.SL; var d = app.Session.Data;
            L.SelKind = kind; L.SelId = id; L.NoteSel = null; L.SmeQ = "";
            if (kind == "video") L.Book = d.FindVideo(id)?.BookId ?? L.Book;
            if (kind == "photo") L.Book = d.FindPhoto(id)?.BookId ?? L.Book;
            if (kind == "chapter") L.Book = d.FindChapter(id)?.BookId ?? L.Book;
            app.State.Focus = null;
            app.Render();
        }

        // ------------------------------------------------------------------ editors

        private static void VideoEditor(AppController app, VisualElement body, Video v, NodeListEditor notes)
        {
            var st = app.State; var L = st.SL; var s = app.Session; var d = s.Data; var ed = s.PlanEditor; var val = app.CurrentValidation;
            var book = d.FindBook(v.BookId); var ch = d.FindChapter(v.ChapterId);
            var issues = val.For(v.Id);
            var head = U.Row(U.Num(v.Number.ToString(), "lg").W(56)).Mb(10);
            var was = st.Renumber.Was(v);
            if (was != null) head.Add(U.Pill("was " + was, "warn").Mr(10));
            head.Add(U.Sub("Book " + (book?.Number.ToString() ?? "?") + " › " + (ch != null ? "Ch." + ch.Number + " " + U.Esc(ch.Name) : "<color=#c00000><b>no chapter</b></color>") + " · number is automatic").Cls("grow"));
            if (issues.Count > 0) head.Add(U.Pill(U.Plural(issues.Count, "issue"), "fillbad"));
            if (ed.IsReferenced(v.Id)) head.Add(U.Pill("captured · title and position locked", "ac").Ml(8));
            body.Add(head);

            if (ch == null)
            {
                var banner = new VisualElement().Cls("banner bad");
                banner.Add(U.Body("<b>No chapter.</b> Pick one:").Cls("grow"));
                foreach (var c in d.ChaptersOf(v.BookId).Concat(d.ShotList.Chapters.Where(x => x.BookId != v.BookId)))
                    banner.Add(U.Btn("Ch." + c.Number + " " + c.Name, () => Guarded(app, () => { ed.MoveVideo(v.Id, c.Id); app.Toast("Chapter set · numbering rechecked"); }), "sm ml8"));
                body.Add(banner);
            }
            foreach (var i in issues.Where(i => i.Kind != Validation.ValidationKind.VideoWithoutChapter))
            {
                var b = new VisualElement().Cls("banner" + (i.Severity == Validation.IssueSeverity.Error ? " bad" : ""));
                b.Add(U.Body(U.Esc(i.Message)).Cls("grow"));
                body.Add(b);
            }

            var title = U.Input(v.Title, "Title", val2 => app.Edit(() => Guarded(app, () => ed.SetVideoTitle(v.Id, val2), silent: true)), false, "tall" + (string.IsNullOrWhiteSpace(v.Title) ? " err" : ""));
            title.name = "video:" + v.Id + ":title";
            body.Add(U.Field("Title", title));

            var two = U.Row(); two.style.alignItems = Align.FlexStart;
            var smeCol = U.Col().Cls("fld").W(360).Mr(16);
            smeCol.Add(U.Lbl("SME · suggests client people, accepts any name"));
            var chips = U.Row().Cls("wrap");
            foreach (var pid in v.SmeIds ?? new List<string>())
            {
                var p = d.FindPerson(pid);
                var pidc = pid;
                chips.Add(U.ChipX(U.Esc(p?.FullName ?? pid), () => ed.UpdateVideo(v.Id, x => x.SmeIds.Remove(pidc)), "h44"));
            }
            if (!string.IsNullOrEmpty(v.SmeText))
                chips.Add(U.ChipX(U.Esc(v.SmeText), () => ed.UpdateVideo(v.Id, x => x.SmeText = ""), "h44"));
            var smeInput = U.Input(L.SmeQ, "+ name", q => { L.SmeQ = q; app.RenderKeepFocus("sl.smeQ"); }, false, "h44").W(200);
            smeInput.name = "sl.smeQ";
            chips.Add(smeInput);
            smeCol.Add(chips);
            if (!string.IsNullOrWhiteSpace(L.SmeQ))
            {
                var box = new VisualElement().Cls("sugg"); box.style.position = Position.Relative;
                foreach (var m in s.Search.SuggestNames(L.SmeQ, NameScope.Sme, 4).Where(m => m.Item.Person != null && !(v.SmeIds ?? new List<string>()).Contains(m.Item.Person.Id)))
                {
                    var p = m.Item.Person;
                    box.Add(U.Tap(() => { L.SmeQ = ""; ed.UpdateVideo(v.Id, x => x.SmeIds.Add(p.Id)); }, "sugg-row", U.Text(U.Esc(p.FullName), "bold").Cls("grow"), U.Sub(U.Esc(p.Title ?? ""))));
                }
                box.Add(U.Tap(() =>
                {
                    var name = L.SmeQ.Trim(); L.SmeQ = "";
                    var p = s.FindOrAddPerson(name, Org.Client, PersonRole.Sme);
                    ed.UpdateVideo(v.Id, x => { if (!x.SmeIds.Contains(p.Id)) x.SmeIds.Add(p.Id); });
                    app.Toast("Added " + name + " to people as SME");
                }, "sugg-row", U.Text("Use “" + U.Esc(L.SmeQ.Trim()) + "” · adds to people as SME").Cls("grow")));
                smeCol.Add(box);
            }
            two.Add(smeCol);
            var scene = U.Input(v.SceneDescription, null, val2 => app.Edit(() => ed.UpdateVideo(v.Id, x => x.SceneDescription = val2)), true).H(64);
            scene.name = "video:" + v.Id + ":scene";
            two.Add(U.Field("Scene description", scene).Cls("grow"));
            body.Add(two);

            body.Add(U.Row(U.Eyebrow("Notes · nested, any depth").Cls("grow"),
                U.Sub(Shortcuts.HardwareKeyboardPresent ? "Enter new · Tab indent · ⇧Tab outdent · ⌥↑↓ move" : "select a bullet, then use the bar below")).Mt(6).Mb(8));
            body.Add(OutlinerView.Build(app, notes, L.NoteSel, id => { L.NoteSel = id; }, "video:" + v.Id));

            body.Add(U.Eyebrow("Photos shootable with this setup · tap to reference from Book " + (book?.Number.ToString() ?? "?") + "'s master list").Mt(18).Mb(8));
            var refs = U.Row().Cls("wrap");
            foreach (var p in s.Queries.PhotosOfBook(v.BookId))
            {
                var on = v.PhotoRefs != null && v.PhotoRefs.Contains(p.Id);
                var pch = d.FindChapter(p.ChapterId);
                var label = (on ? "✓ " : "") + (p.HeroType != HeroType.None ? "★ " : "") + U.Esc(U.ShortDescription(p.Description));
                refs.Add(U.Chip(label, on, () => { if (on) ed.RemovePhotoRef(v.Id, p.Id); else ed.AddPhotoRef(v.Id, p.Id); }, "h52"));
            }
            foreach (var r in (v.PhotoRefs ?? new List<string>()).Where(r => d.FindPhoto(r) == null))
            {
                var rid = r;
                var chip = new VisualElement().Cls("chip h52 bad");
                chip.Add(new Glyph(GlyphKind.Cross, 14).Mr(6));
                chip.Add(U.Text(rid + " not in master list").Bold());
                chip.Add(U.Btn("Create", () => { var np = ed.AddPhoto(v.BookId, ""); ed.UpdateVideo(v.Id, x => { x.PhotoRefs.Remove(rid); x.PhotoRefs.Add(np.Id); }); L.Pane = "photos"; Select(app, "photo", np.Id); app.State.Focus = "photo:" + np.Id + ":description"; app.Render(); }, "sm ml10").H(36));
                chip.Add(U.Btn("Remove", () => { ed.RemovePhotoRef(v.Id, rid); app.Toast("Reference removed"); }, "sm ml6").H(36));
                refs.Add(chip);
            }
            refs.Add(U.Chip("+ New photo", false, () => AddPhoto(app, v.BookId, true), "h52 ac"));
            body.Add(refs);
        }

        private static void ChapterEditor(AppController app, VisualElement body, Chapter c)
        {
            var s = app.Session; var d = s.Data; var ed = s.PlanEditor;
            if (c == null) { body.Add(U.Sub("Select a chapter.")); return; }
            var book = d.FindBook(c.BookId);
            body.Add(U.Eyebrow("Chapter " + c.Number + " · Book " + (book?.Number.ToString() ?? "?")).Mb(8));
            var name = U.Input(c.Name, "Chapter name", v => app.Edit(() => ed.UpdateChapter(c.Id, x => x.Name = v)), false, "tall");
            name.name = "chapter:" + c.Id + ":name";
            body.Add(U.Field("Chapter name", name));
            var hero = s.Queries.ChapterHero(c.Id);
            body.Add(U.Sub(U.Plural(d.ShotList.Videos.Count(v => v.ChapterId == c.Id), "video") + " · hero photo: " + U.Esc(hero?.Description ?? "none set")).Mb(14));
            var add = U.BtnKbd("+ Add video to this chapter", "⇧Enter", () => Accessory(app, "child", null, false), "pri");
            body.Add(U.Row(add, U.Btn("Remove chapter", () => Guarded(app, () => { ed.RemoveChapter(c.Id); app.State.SL.SelKind = "none"; app.Toast("Chapter removed"); }), "danger ml10")));
        }

        private static void PhotoEditor(AppController app, VisualElement body, Photo p)
        {
            var s = app.Session; var d = s.Data; var ed = s.PlanEditor;
            if (p == null) { body.Add(U.Sub("Select a photo.")); return; }
            var book = d.FindBook(p.BookId);
            var users = d.ShotList.Videos.Where(v => v.PhotoRefs != null && v.PhotoRefs.Contains(p.Id)).ToList();
            body.Add(U.Eyebrow("Master photo · Book " + (book?.Number.ToString() ?? "?") + " · order " + p.Order).Mb(8));
            var desc = U.Input(p.Description, "Description", v => app.Edit(() => ed.UpdatePhoto(p.Id, x => x.Description = v)), false, "tall");
            desc.name = "photo:" + p.Id + ":description";
            body.Add(U.Field("Description", desc));
            body.Add(U.Lbl("Hero"));
            var heroRow = U.Row().Cls("wrap").Mb(14);
            heroRow.Add(U.Chip("Not a hero", p.HeroType == HeroType.None, () => Guarded(app, () => ed.UpdatePhoto(p.Id, x => { x.HeroType = HeroType.None; x.ChapterId = null; }))));
            heroRow.Add(U.Chip("★ Book cover", p.HeroType == HeroType.BookCover, () => Guarded(app, () => ed.UpdatePhoto(p.Id, x => { x.HeroType = HeroType.BookCover; x.ChapterId = null; }))));
            foreach (var c in d.ChaptersOf(p.BookId))
                heroRow.Add(U.Chip("★ Ch." + c.Number + " hero", p.HeroType == HeroType.ChapterHero && p.ChapterId == c.Id, () => Guarded(app, () => ed.UpdatePhoto(p.Id, x => { x.HeroType = HeroType.ChapterHero; x.ChapterId = c.Id; }))));
            body.Add(heroRow);

            body.Add(U.Lbl("Also belongs to · rare: a photo used by more than one book or chapter"));
            var also = U.Row().Cls("wrap").Mb(14);
            foreach (var b in d.Trip.Books.Where(b => b.Id != p.BookId))
            {
                var on = p.AlsoBookIds != null && p.AlsoBookIds.Contains(b.Id);
                also.Add(U.Chip("Book " + b.Number, on, () => Guarded(app, () =>
                {
                    var books = new List<string>(p.AlsoBookIds ?? new List<string>());
                    if (on) books.Remove(b.Id); else books.Add(b.Id);
                    ed.SetPhotoAssociations(p.Id, books, p.AlsoChapterIds);
                }), "h44"));
            }
            foreach (var c in d.ShotList.Chapters.Where(c => c.Id != p.ChapterId))
            {
                var on = p.AlsoChapterIds != null && p.AlsoChapterIds.Contains(c.Id);
                var cb = d.FindBook(c.BookId);
                also.Add(U.Chip("B" + (cb?.Number.ToString() ?? "?") + " Ch." + c.Number, on, () => Guarded(app, () =>
                {
                    var chs = new List<string>(p.AlsoChapterIds ?? new List<string>());
                    if (on) chs.Remove(c.Id); else chs.Add(c.Id);
                    ed.SetPhotoAssociations(p.Id, p.AlsoBookIds, chs);
                }), "h44"));
            }
            body.Add(also);
            body.Add(U.Sub("Referenced by " + (users.Count > 0 ? string.Join(", ", users.Select(v => "#" + v.Number)) : "no videos") + ". Heroes sort first: cover, then chapters in order."));
        }

        // ------------------------------------------------------------------ actions

        private static void AddChapter(AppController app)
        {
            var L = app.State.SL; var d = app.Session.Data;
            var bookId = L.Book ?? d.Trip.Books.FirstOrDefault()?.Id;
            if (bookId == null) { app.Toast("Add a book in Trip setup first"); return; }
            var c = app.Session.PlanEditor.AddChapter(bookId, "");
            L.SelKind = "chapter"; L.SelId = c.Id; L.NoteSel = null;
            app.State.Focus = "chapter:" + c.Id + ":name";
            app.Render();
        }

        private static void AddPhoto(AppController app, string bookId, bool referenceFromSelectedVideo)
        {
            var L = app.State.SL; var ed = app.Session.PlanEditor;
            var np = ed.AddPhoto(bookId, "");
            if (referenceFromSelectedVideo && L.SelKind == "video" && L.SelId != null) ed.AddPhotoRef(L.SelId, np.Id);
            L.Pane = "photos"; L.SelKind = "photo"; L.SelId = np.Id; L.Book = bookId; L.NoteSel = null;
            app.State.Focus = "photo:" + np.Id + ":description";
            app.Render();
        }

        /// <summary>Run a plan edit; a blocked edit becomes a toast, not an exception.</summary>
        public static bool Guarded(AppController app, Action a, bool silent = false)
        {
            try { a(); return true; }
            catch (PlanEditBlockedException ex) { if (!silent) app.Toast(ex.Suggestion ?? ex.Message); else app.Toast("Locked: " + ex.Suggestion); return false; }
            catch (InvalidOperationException ex) { app.Toast(ex.Message); return false; }
            catch (ArgumentException ex) { app.Toast(ex.Message); return false; }
        }

        private static bool Accessory(AppController app, string act, NodeListEditor notes, bool nodeMode)
        {
            var st = app.State; var L = st.SL; var s = app.Session; var d = s.Data; var ed = s.PlanEditor;

            if (act == "paste")
            {
                if (L.SelKind != "video") { app.Toast("Select a video to paste notes into"); return true; }
                L.Paste = ""; app.Render(); return true;
            }

            if (nodeMode && notes != null)
            {
                var next = OutlinerView.Apply(notes, L.NoteSel, act, out var handled);
                if (!handled) return false;
                L.NoteSel = next;
                app.State.Focus = next != null ? "node:" + next : null;
                app.Render();
                return true;
            }

            switch (L.SelKind)
            {
                case "video":
                {
                    var v = d.FindVideo(L.SelId);
                    if (v == null) return false;
                    if (act == "sib" || act == "child")
                    {
                        var sibs = ed.VideosOfChapter(v.ChapterId);
                        var nv = ed.AddVideo(v.ChapterId ?? d.ShotList.Chapters.FirstOrDefault()?.Id, "", sibs.IndexOf(v) + 1);
                        st.Renumber.Track(nv);
                        L.SelId = nv.Id; L.NoteSel = null; app.State.Focus = "video:" + nv.Id + ":title"; app.Render(); return true;
                    }
                    if (act == "up" || act == "down")
                    {
                        var sibs = ed.VideosOfChapter(v.ChapterId); var i = sibs.IndexOf(v);
                        var j = act == "up" ? i - 1 : i + 1;
                        if (j < 0 || j >= sibs.Count) return true;
                        ed.ReorderVideo(v.Id, j); app.Render(); return true;
                    }
                    if (act == "dup") { var nv = ed.DuplicateVideo(v.Id); st.Renumber.Track(nv); L.SelId = nv.Id; L.NoteSel = null; app.Toast("Duplicated · videos after it renumbered"); app.Render(); return true; }
                    if (act == "del")
                    {
                        var sibs = ed.VideosOfChapter(v.ChapterId); var i = sibs.IndexOf(v);
                        if (Guarded(app, () => ed.RemoveVideo(v.Id)))
                        {
                            var nextSel = i + 1 < sibs.Count ? sibs[i + 1] : i - 1 >= 0 ? sibs[i - 1] : d.ShotList.Videos.FirstOrDefault();
                            L.SelId = nextSel?.Id; if (nextSel == null) L.SelKind = "none";
                        }
                        app.Render(); return true;
                    }
                    if (act == "prev" || act == "next")
                    {
                        var all = d.ShotList.Videos; var i = all.IndexOf(v); var j = act == "prev" ? i - 1 : i + 1;
                        if (j >= 0 && j < all.Count) { L.SelId = all[j].Id; L.NoteSel = null; app.Render(); }
                        return true;
                    }
                    return false;
                }
                case "chapter":
                {
                    var c = d.FindChapter(L.SelId);
                    if (c == null) return false;
                    if (act == "child") { var nv = ed.AddVideo(c.Id, ""); st.Renumber.Track(nv); L.SelKind = "video"; L.SelId = nv.Id; app.State.Focus = "video:" + nv.Id + ":title"; app.Render(); return true; }
                    if (act == "sib") { var chs = ed.ChaptersOf(c.BookId); var nc = ed.AddChapter(c.BookId, "", chs.IndexOf(c) + 1); L.SelId = nc.Id; app.State.Focus = "chapter:" + nc.Id + ":name"; app.Render(); return true; }
                    if (act == "up" || act == "down") { var chs = ed.ChaptersOf(c.BookId); var i = chs.IndexOf(c); var j = act == "up" ? i - 1 : i + 1; if (j >= 0 && j < chs.Count) { ed.ReorderChapter(c.Id, j); app.Render(); } return true; }
                    if (act == "del") { if (Guarded(app, () => ed.RemoveChapter(c.Id))) { L.SelKind = "none"; } app.Render(); return true; }
                    if (act == "dup") { app.Toast("Chapters are not duplicated; add a sibling instead"); return true; }
                    return false;
                }
                case "photo":
                {
                    var p = d.FindPhoto(L.SelId);
                    if (p == null) return false;
                    if (act == "sib" || act == "child") { AddPhoto(app, p.BookId, false); return true; }
                    if (act == "up" || act == "down")
                    {
                        if (p.HeroType != HeroType.None) { app.Toast("Hero shots are ordered automatically (cover, then chapter heroes)"); return true; }
                        var sibs = d.ShotList.Photos.Where(x => x.BookId == p.BookId && x.HeroType == HeroType.None).OrderBy(x => x.Order).ToList();
                        var i = sibs.IndexOf(p); var j = act == "up" ? i - 1 : i + 1;
                        if (j >= 0 && j < sibs.Count) { ed.ReorderPhoto(p.Id, j); app.Render(); }
                        return true;
                    }
                    if (act == "dup") { var np = ed.DuplicatePhoto(p.Id); L.SelId = np.Id; app.State.Focus = "photo:" + np.Id + ":description"; app.Render(); return true; }
                    if (act == "del")
                    {
                        var users = d.ShotList.Videos.Where(v => v.PhotoRefs != null && v.PhotoRefs.Contains(p.Id)).ToList();
                        if (users.Count > 0) { app.Toast("Referenced by #" + string.Join(", #", users.Select(v => v.Number)) + " — remove the references first"); return true; }
                        var sibs = d.ShotList.Photos.Where(x => x.BookId == p.BookId).OrderBy(x => x.Order).ToList(); var i = sibs.IndexOf(p);
                        if (Guarded(app, () => ed.RemovePhoto(p.Id))) { var n = i + 1 < sibs.Count ? sibs[i + 1] : i - 1 >= 0 ? sibs[i - 1] : d.ShotList.Photos.FirstOrDefault(); L.SelId = n?.Id; if (n == null) L.SelKind = "none"; }
                        app.Render(); return true;
                    }
                    if (act == "prev" || act == "next")
                    {
                        var all = d.ShotList.Photos; var i = all.IndexOf(p); var j = act == "prev" ? i - 1 : i + 1;
                        if (j >= 0 && j < all.Count) { L.SelId = all[j].Id; app.Render(); }
                        return true;
                    }
                    return false;
                }
                default:
                {
                    if (act == "sib" || act == "child")
                    {
                        var chapterId = d.ShotList.Chapters.FirstOrDefault(c => c.BookId == L.Book)?.Id ?? d.ShotList.Chapters.FirstOrDefault()?.Id;
                        if (chapterId == null) { app.Toast("Add a chapter first"); return true; }
                        var nv = ed.AddVideo(chapterId, "");
                        st.Renumber.Track(nv);
                        L.Pane = "videos"; L.SelKind = "video"; L.SelId = nv.Id; app.State.Focus = "video:" + nv.Id + ":title"; app.Render(); return true;
                    }
                    return false;
                }
            }
        }
    }
}

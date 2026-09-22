using System;
using System.Collections.Generic;
using System.Linq;
using MediaTrip.Search;
using MediaTrip.UI.ViewModels;
using UnityEngine;
using UnityEngine.UIElements;

namespace MediaTrip.UI.Shell
{
    /// <summary>
    /// Name, title, Add. The name box fuzzy-finds anyone known on the trip; a match fills the
    /// name and the title on file, a new name is kept as typed, and either way the title can be
    /// typed before Add. Used by Filming and by the shot list's SME field, so both create
    /// people the same way.
    /// </summary>
    public static class SmePicker
    {
        /// <param name="key">Field name prefix: "{key}.name" and "{key}.title" are the two inputs, for focus.</param>
        /// <param name="taken">Names already in the list, left out of the matches.</param>
        public static VisualElement Build(AppController app, string key, SmeDraft d, Func<string, bool> taken, Action<SmeDraft> onAdd)
        {
            var s = app.Session; var phone = app.Layout.Phone;
            void Add()
            {
                if (!d.CanAdd) return;
                onAdd(d);
                d.Clear();
                app.RenderKeepFocus(key + ".name");
            }
            var add = U.Btn("Add", Add, "sm");
            add.SetEnabled(d.CanAdd);

            var title = U.Input(d.Title, "Title", v => d.Title = v, false, "h44");
            title.name = key + ".title";
            title.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { e.StopPropagation(); Add(); } }, TrickleDown.TrickleDown);

            var name = PickerField.Build(app, key + ".name", d.Name, "+ SME name", v => { d.Name = v; d.PersonId = null; add.SetEnabled(d.CanAdd); }, q =>
            {
                var rows = new List<PickerField.Row>();
                foreach (var m in s.Search.SuggestNames(q, NameScope.Sme, 5).Where(m => !(taken?.Invoke(m.Item.Name) ?? false)))
                {
                    var n = m.Item;
                    rows.Add(new PickerField.Row
                    {
                        Title = n.Name, Sub = string.IsNullOrEmpty(n.Title) ? "no title on file · type one, then Add" : n.Title,
                        Pick = () => { d.Fill(n); app.RenderKeepFocus(key + ".title"); },
                    });
                }
                var typed = q.Trim();
                if (typed.Length > 0 && !rows.Any(r => string.Equals(r.Title, typed, StringComparison.OrdinalIgnoreCase)))
                    rows.Add(new PickerField.Row { IsCreate = true, Title = "New: “" + typed + "”", Sub = "Type their title, then Add", Pick = () => { d.FillNew(typed); app.RenderKeepFocus(key + ".title"); } });
                return rows;
            }, "h44 grow");

            if (phone)
            {
                // stacked: the name on its own line (not "grow": in a column that is a zero flex-basis height), then title and Add
                name.RemoveFromClassList("grow");
                name.style.width = Length.Percent(100);
                title.style.flexGrow = 1;
                return U.Col(name.Mb(6), U.Row(title, add.Ml(8)));
            }
            name.style.marginRight = 8;
            return U.Row(name, title.W(200).Mr(8), add);
        }
    }
}

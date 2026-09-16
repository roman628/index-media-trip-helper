using System.Linq;
using MediaTrip.Status;
using UnityEngine.UIElements;

namespace MediaTrip.UI.Field
{
    /// <summary>Every book cover and chapter hero, as tiles, with status. Tap a tile to check it off.</summary>
    public static class HeroesScreen
    {
        public static VisualElement Build(AppController app)
        {
            var s = app.Session;
            var d = s.Data;
            var q = s.Queries;
            var heroes = q.Heroes();
            var root = U.Scroll().Cls("grow").Pad(20, 24);
            var missing = heroes.Count(h => h.Status == PhotoStatus.NotCaptured);
            root.Add(U.Row(U.H1("Hero shots").Cls("grow"), U.Pill(missing + " missing", missing > 0 ? "fillbad lg" : "fill lg")).Mb(8));
            if (heroes.Count == 0) root.Add(U.Card(U.Sub("No hero shots on the master photo list yet. Mark photos as cover or chapter hero in the shot list editor.")).Mt(12));
            foreach (var b in d.Trip.Books)
            {
                var rows = heroes.Where(h => h.Book?.Id == b.Id).ToList();
                if (rows.Count == 0) continue;
                root.Add(U.Eyebrow("Book " + b.Number + " · " + b.Name).Mt(18).Mb(10));
                var grid = U.Row().Cls("wrap");
                grid.style.alignItems = Align.FlexStart;
                foreach (var h in rows)
                {
                    var on = h.IsCaptured;
                    var tile = U.Tap(() => app.TogglePhoto(h.Photo.Id), "tileB " + (on ? "on" : "miss") + (h.IsShared ? " shared" : ""));
                    var top = U.Row(U.H3(h.Label + (h.IsShared ? " · shared" : "")).Cls("grow"), U.StPhoto(h.Status, 36, true)).Cls("fill");
                    top.style.justifyContent = Justify.SpaceBetween;
                    tile.Add(top);
                    tile.Add(U.Sub(U.Esc(U.ShortDescription(h.Photo.Description))));
                    grid.Add(tile);
                }
                root.Add(grid);
            }
            return root;
        }
    }
}

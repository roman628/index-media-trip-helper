using System.Collections.Generic;
using MediaTrip.Model;
using MediaTrip.Search;
using MediaTrip.Session;
using MediaTrip.Status;

namespace MediaTrip.UI.ViewModels
{
    /// <summary>The top-bar search: videos, photos, people and outline sections in one list.</summary>
    public sealed class GlobalSearch
    {
        public List<ScoredMatch<PlanItem>> Items = new List<ScoredMatch<PlanItem>>();
        public List<ScoredMatch<PhotoItem>> Photos = new List<ScoredMatch<PhotoItem>>();
        public List<ScoredMatch<Person>> People = new List<ScoredMatch<Person>>();
        public List<ScoredMatch<TripSearch.SectionHit>> Sections = new List<ScoredMatch<TripSearch.SectionHit>>();

        public bool IsEmpty => Items.Count == 0 && Photos.Count == 0 && People.Count == 0 && Sections.Count == 0;

        public static GlobalSearch Run(TripSession s, string query, bool includePeople, int maxItems = 5, int maxPhotos = 4, int maxPeople = 3, int maxSections = 3)
        {
            var r = new GlobalSearch();
            if (string.IsNullOrWhiteSpace(query)) return r;
            var q = query.Trim();
            r.Items = s.Search.SearchShotList(q, maxItems, includeSuperseded: true);
            r.Photos = s.Search.SearchPhotos(HeroAlias(q), maxPhotos);
            if (includePeople) r.People = s.Search.SearchPeople(q, maxPeople);
            r.Sections = s.Search.SearchSections(q, maxSections);
            return r;
        }

        /// <summary>"ch2 hero" is how people ask; expand the shorthand so the photo fields match.</summary>
        public static string HeroAlias(string q)
        {
            var t = q.Trim();
            var lower = t.ToLowerInvariant();
            if (lower.StartsWith("ch") && lower.Length > 2 && char.IsDigit(lower[2]))
                t = "chapter " + t.Substring(2);
            return t;
        }
    }
}

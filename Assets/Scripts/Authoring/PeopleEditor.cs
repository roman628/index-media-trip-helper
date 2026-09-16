using System;
using System.Collections.Generic;
using System.Linq;
using MediaTrip.Model;
using MediaTrip.Persistence;
using MediaTrip.Search;
using MediaTrip.Session;

namespace MediaTrip.Authoring
{
    /// <summary>
    /// The people registry: add, edit, roles, merge duplicates, remove. Removal of a referenced
    /// person is refused unless <c>detach</c> is passed, which turns the references into the
    /// free-text form the schema already allows (capture person with no personId, book team
    /// memberNames, video smeText).
    /// </summary>
    public sealed class PeopleEditor
    {
        private readonly TripSession _s;
        private TripData D => _s.Data;

        internal PeopleEditor(TripSession session) { _s = session; }

        public List<PlanReference> ReferencesTo(string personId) => ReferenceFinder.Find(D, personId);

        public Person Add(string firstName, string lastName, Org org = Org.Other, IEnumerable<PersonRole> roles = null, string title = null)
        {
            var p = new Person
            {
                Id = Ids.New("p"), FirstName = (firstName ?? "").Trim(), LastName = (lastName ?? "").Trim(), Org = org, Title = title,
                Roles = (roles ?? Enumerable.Empty<PersonRole>()).Distinct().ToList(),
            };
            if (p.FirstName.Length == 0 && p.LastName.Length == 0) throw new ArgumentException("A person needs a name.");
            D.Trip.People.Add(p);
            Finish();
            return p;
        }

        /// <summary>Add from a typed "First Last" string (split on the last space).</summary>
        public Person AddFromName(string fullName, Org org = Org.Other, IEnumerable<PersonRole> roles = null, string title = null)
        {
            var (first, last) = SplitName(fullName);
            return Add(first, last, org, roles, title);
        }

        public static (string first, string last) SplitName(string fullName)
        {
            var t = (fullName ?? "").Trim();
            var space = t.LastIndexOf(' ');
            return space > 0 ? (t.Substring(0, space).Trim(), t.Substring(space + 1).Trim()) : (t, "");
        }

        /// <summary>Edit name, org, title, roles. Id is restored after the edit.</summary>
        public void Update(string id, Action<Person> edit)
        {
            var p = Require(id);
            var keepId = p.Id;
            edit(p);
            p.Id = keepId;
            if (p.Roles == null) p.Roles = new List<PersonRole>();
            p.Roles = p.Roles.Distinct().ToList();
            Finish();
        }

        public void SetRoles(string id, IEnumerable<PersonRole> roles) => Update(id, p => p.Roles = (roles ?? Enumerable.Empty<PersonRole>()).ToList());
        public void AddRole(string id, PersonRole role) => Update(id, p => { if (!p.Roles.Contains(role)) p.Roles.Add(role); });
        public void RemoveRole(string id, PersonRole role) => Update(id, p => p.Roles.Remove(role));
        public void SetOrg(string id, Org org) => Update(id, p => p.Org = org);

        /// <summary>
        /// Remove a person. Blocked if referenced, unless <paramref name="detach"/>: then capture
        /// entries keep the name with no personId, book teams get the name in memberNames, and
        /// videos get the name appended to smeText.
        /// </summary>
        public void Remove(string id, bool detach = false)
        {
            var p = Require(id);
            var refs = ReferenceFinder.Find(D, id);
            if (refs.Count > 0 && !detach)
                throw new PlanEditBlockedException(id, refs, "Pass detach: true to keep those references as free text, or merge into another person.", "Removing person");
            if (refs.Count > 0) Detach(p);
            D.Trip.People.Remove(p);
            Finish();
        }

        private void Detach(Person p)
        {
            var name = p.FullName;
            bool captures = false, shotList = false;
            foreach (var c in D.Captures.Captures)
                foreach (var cp in c.People ?? Enumerable.Empty<CapturePerson>())
                    if (cp.PersonId == p.Id)
                    {
                        cp.PersonId = null;
                        if (string.IsNullOrWhiteSpace(cp.Name)) cp.Name = name;
                        if (string.IsNullOrWhiteSpace(cp.Title)) cp.Title = p.Title;
                        captures = true;
                    }
            foreach (var b in D.Trip.Books)
            {
                if (b.Team?.MemberIds == null || !b.Team.MemberIds.Remove(p.Id)) continue;
                if (b.Team.MemberNames == null) b.Team.MemberNames = new List<string>();
                if (!b.Team.MemberNames.Contains(name)) b.Team.MemberNames.Add(name);
            }
            foreach (var v in D.ShotList.Videos)
            {
                if (v.SmeIds == null || !v.SmeIds.Remove(p.Id)) continue;
                v.SmeText = string.IsNullOrWhiteSpace(v.SmeText) ? name : v.SmeText + "; " + name;
                shotList = true;
            }
            if (captures) _s.MarkDirty(DocumentKind.Captures);
            if (shotList) _s.MarkDirty(DocumentKind.ShotList);
        }

        /// <summary>
        /// Merge <paramref name="mergeId"/> into <paramref name="keepId"/>: every reference is
        /// re-pointed, roles are unioned, empty fields on the kept person are filled from the
        /// merged one, and the merged person is removed. Returns the kept person.
        /// </summary>
        public Person Merge(string keepId, string mergeId)
        {
            if (keepId == mergeId) throw new ArgumentException("Cannot merge a person into themselves.");
            var keep = Require(keepId);
            var merge = Require(mergeId);

            bool captures = false, shotList = false;
            foreach (var c in D.Captures.Captures)
                foreach (var cp in c.People ?? Enumerable.Empty<CapturePerson>())
                    if (cp.PersonId == mergeId) { cp.PersonId = keepId; captures = true; }
            foreach (var b in D.Trip.Books)
            {
                var ids = b.Team?.MemberIds;
                if (ids == null || !ids.Contains(mergeId)) continue;
                ids.Remove(mergeId);
                if (!ids.Contains(keepId)) ids.Add(keepId);
            }
            foreach (var v in D.ShotList.Videos)
            {
                if (v.SmeIds == null || !v.SmeIds.Contains(mergeId)) continue;
                v.SmeIds.Remove(mergeId);
                if (!v.SmeIds.Contains(keepId)) v.SmeIds.Add(keepId);
                shotList = true;
            }

            foreach (var r in merge.Roles ?? new List<PersonRole>()) if (!keep.Roles.Contains(r)) keep.Roles.Add(r);
            if (string.IsNullOrWhiteSpace(keep.Title)) keep.Title = merge.Title;
            if (string.IsNullOrWhiteSpace(keep.FirstName)) keep.FirstName = merge.FirstName;
            if (string.IsNullOrWhiteSpace(keep.LastName)) keep.LastName = merge.LastName;
            if (keep.Org == Org.Other && merge.Org != Org.Other) keep.Org = merge.Org;

            D.Trip.People.Remove(merge);
            if (captures) _s.MarkDirty(DocumentKind.Captures);
            if (shotList) _s.MarkDirty(DocumentKind.ShotList);
            Finish();
            return keep;
        }

        /// <summary>Groups of registry entries whose normalized full names are identical.</summary>
        public List<List<Person>> FindDuplicates()
        {
            return D.Trip.People
                .GroupBy(p => FuzzyMatcher.Normalize(p.FullName))
                .Where(g => g.Key.Length > 0 && g.Count() > 1)
                .Select(g => g.ToList())
                .ToList();
        }

        /// <summary>Registry entries whose names fuzzy-match above a threshold (possible typos of the same person).</summary>
        public List<(Person a, Person b, double score)> FindLikelyDuplicates(double minScore = 0.8)
        {
            var people = D.Trip.People;
            var result = new List<(Person, Person, double)>();
            for (int i = 0; i < people.Count; i++)
                for (int j = i + 1; j < people.Count; j++)
                {
                    var s = FuzzyMatcher.Score(people[i].FullName, people[j].FullName);
                    if (s >= minScore) result.Add((people[i], people[j], s));
                }
            return result.OrderByDescending(t => t.Item3).ToList();
        }

        private void Finish() => _s.MarkDirty(DocumentKind.Trip);

        private Person Require(string id) => D.FindPerson(id) ?? throw new KeyNotFoundException("No person " + id);
    }
}

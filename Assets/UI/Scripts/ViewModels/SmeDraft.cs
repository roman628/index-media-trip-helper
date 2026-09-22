using MediaTrip.Search;

namespace MediaTrip.UI.ViewModels
{
    /// <summary>
    /// One SME being added: the name, then the title, then Add. Picking a fuzzy match FILLS the
    /// name (and the title on file, if none was typed) rather than adding straight away, so a
    /// title can be typed for anyone. The same draft serves Filming and the plan editor.
    /// </summary>
    public sealed class SmeDraft
    {
        public string Name = "";
        public string Title = "";
        /// <summary>The registry person the name was filled from; null for a name typed fresh.</summary>
        public string PersonId;

        public bool CanAdd => !string.IsNullOrWhiteSpace(Name);

        public void Fill(NameSuggestion n)
        {
            Name = n.Name;
            PersonId = n.Person?.Id;
            if (string.IsNullOrWhiteSpace(Title) && !string.IsNullOrEmpty(n.Title)) Title = n.Title;
        }

        /// <summary>A name typed fresh: keep whatever title was typed, no registry person yet.</summary>
        public void FillNew(string name)
        {
            Name = (name ?? "").Trim();
            PersonId = null;
        }

        public void Clear()
        {
            Name = ""; Title = ""; PersonId = null;
        }
    }
}

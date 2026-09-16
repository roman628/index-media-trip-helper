using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MediaTrip.Model
{
    public enum Org { Index, Client, Leadership, Other }

    public enum PersonRole
    {
        ProjectManager, Producer, Photographer, Writer, Editor,
        MediaCoordinator, CoProducer, Sme, LeadershipRep, Personnel
    }

    /// <summary>trip.json: trip identity, people registry, logistics, books, days.</summary>
    public class TripDocument : DocumentBase
    {
        public TripIdentity Identity { get; set; } = new TripIdentity();
        public TripDates Dates { get; set; } = new TripDates();
        public string WeatherForecast { get; set; }
        public string MediaFileName { get; set; }
        public List<Person> People { get; set; } = new List<Person>();
        public List<string> Logistics { get; set; } = new List<string>();
        public List<ActionItem> Actions { get; set; } = new List<ActionItem>();
        public List<Book> Books { get; set; } = new List<Book>();
        public List<Day> Days { get; set; } = new List<Day>();
    }

    public class TripIdentity
    {
        public string ClientAbbrev { get; set; }
        public string ProgramAbbrev { get; set; }
        public int? PhaseNumber { get; set; }
        public Location Location { get; set; }
        [JsonExtensionData] public IDictionary<string, JToken> Extra { get; set; }
    }

    public class Location
    {
        public string City { get; set; }
        public string State { get; set; }
        public string Address { get; set; }
        [JsonExtensionData] public IDictionary<string, JToken> Extra { get; set; }
    }

    /// <summary>All dates are ISO-8601 strings ("2026-09-14"); weekdays are rendered, not stored.</summary>
    public class TripDates
    {
        public string Arrive { get; set; }
        public string Depart { get; set; }
        public string LastUpdated { get; set; }
        [JsonExtensionData] public IDictionary<string, JToken> Extra { get; set; }
    }

    public class Person
    {
        public string Id { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Title { get; set; }
        public Org Org { get; set; } = Org.Other;
        public List<PersonRole> Roles { get; set; } = new List<PersonRole>();
        [JsonExtensionData] public IDictionary<string, JToken> Extra { get; set; }

        [JsonIgnore]
        public string FullName =>
            string.Join(" ", new[] { FirstName, LastName }.Where(s => !string.IsNullOrWhiteSpace(s)));

        public bool HasRole(PersonRole role) => Roles != null && Roles.Contains(role);
    }

    public class ActionItem
    {
        public string Id { get; set; }
        public string Text { get; set; }
        public bool Done { get; set; }
        [JsonExtensionData] public IDictionary<string, JToken> Extra { get; set; }
    }

    public class Book
    {
        public string Id { get; set; }
        public int Number { get; set; }
        public string Name { get; set; }
        public BookTeam Team { get; set; }
        [JsonExtensionData] public IDictionary<string, JToken> Extra { get; set; }
    }

    public class BookTeam
    {
        public int? Number { get; set; }
        public List<string> MemberIds { get; set; } = new List<string>();
        /// <summary>Free-text names for members who are not (yet) in the people registry.</summary>
        public List<string> MemberNames { get; set; } = new List<string>();
        [JsonExtensionData] public IDictionary<string, JToken> Extra { get; set; }
    }

    public class Day
    {
        public string Id { get; set; }
        /// <summary>ISO date.</summary>
        public string Date { get; set; }
        public string Label { get; set; }
        [JsonExtensionData] public IDictionary<string, JToken> Extra { get; set; }
    }
}

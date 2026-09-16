using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MediaTrip.Model;
using UnityEngine;

namespace MediaTrip.Persistence
{
    /// <summary>Where trips live on disk. Runtime data is always under persistentDataPath.</summary>
    public static class TripPaths
    {
        public const string LibraryFolderName = "Trips";
        public const string SampleTripFolderName = "SampleTrip";

        public static string LibraryRoot => Path.Combine(Application.persistentDataPath, LibraryFolderName);

        public static string TripFolder(string tripId) => Path.Combine(LibraryRoot, SanitizeFileName(tripId));

        /// <summary>The read-only fake trip shipped with the app. Import it; never write here.</summary>
        public static string SampleTripFolder => Path.Combine(Application.streamingAssetsPath, SampleTripFolderName);

        public static bool IsInsideStreamingAssets(string folder)
        {
            try
            {
                var full = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var sa = Path.GetFullPath(Application.streamingAssetsPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return full.Equals(sa, StringComparison.OrdinalIgnoreCase)
                       || full.StartsWith(sa + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                       || full.StartsWith(sa + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "unnamed";
            var invalid = Path.GetInvalidFileNameChars();
            var chars = name.Select(c => invalid.Contains(c) || c == ' ' ? '_' : c).ToArray();
            return new string(chars);
        }
    }

    /// <summary>A trip as listed in the library, read from trip.json only.</summary>
    public class TripSummary
    {
        public string TripId;
        public string Folder;
        public string ClientAbbrev;
        public string ProgramAbbrev;
        public int? PhaseNumber;
        public string City;
        public string Arrive;
        public string Depart;
        public string LoadError;

        public string DisplayName
        {
            get
            {
                var parts = new List<string>();
                if (!string.IsNullOrEmpty(ClientAbbrev)) parts.Add(ClientAbbrev);
                if (!string.IsNullOrEmpty(ProgramAbbrev)) parts.Add(ProgramAbbrev);
                if (PhaseNumber.HasValue) parts.Add("P" + PhaseNumber.Value);
                if (!string.IsNullOrEmpty(City)) parts.Add(City);
                return parts.Count > 0 ? string.Join(" ", parts) : (TripId ?? Path.GetFileName(Folder));
            }
        }
    }

    /// <summary>
    /// The multi-trip library: one sub-folder per trip under <see cref="TripPaths.LibraryRoot"/>.
    /// Zip import/export is a later phase; folder import/export is here now.
    /// </summary>
    public static class TripLibrary
    {
        public static List<TripSummary> ListTrips(string libraryRoot = null)
        {
            libraryRoot = libraryRoot ?? TripPaths.LibraryRoot;
            var list = new List<TripSummary>();
            if (!Directory.Exists(libraryRoot)) return list;

            foreach (var folder in Directory.GetDirectories(libraryRoot).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                var tripPath = Path.Combine(folder, TripLoader.TripFile);
                if (!File.Exists(tripPath)) continue;
                var summary = new TripSummary { Folder = folder };
                try
                {
                    var trip = TripLoader.LoadDocument<TripDocument>(tripPath, DocumentKind.Trip);
                    summary.TripId = trip.TripId;
                    summary.ClientAbbrev = trip.Identity?.ClientAbbrev;
                    summary.ProgramAbbrev = trip.Identity?.ProgramAbbrev;
                    summary.PhaseNumber = trip.Identity?.PhaseNumber;
                    summary.City = trip.Identity?.Location?.City;
                    summary.Arrive = trip.Dates?.Arrive;
                    summary.Depart = trip.Dates?.Depart;
                }
                catch (Exception ex)
                {
                    summary.LoadError = ex.Message;
                }
                list.Add(summary);
            }
            return list;
        }

        /// <summary>Create a new empty trip in the library and write it to disk.</summary>
        public static TripData CreateTrip(string clientAbbrev = null, string programAbbrev = null, string libraryRoot = null)
        {
            var data = TripData.CreateNew(clientAbbrev, programAbbrev);
            data.FolderPath = Path.Combine(libraryRoot ?? TripPaths.LibraryRoot, TripPaths.SanitizeFileName(data.TripId));
            TripSaver.SaveAll(data);
            return data;
        }

        /// <summary>
        /// Copy a trip folder (e.g. the StreamingAssets sample, or a folder dropped in by the
        /// user) into the library. Returns the destination folder. Files are loaded and re-saved
        /// through the migrator, so an older schema is upgraded on import.
        /// </summary>
        public static string ImportFolder(string sourceFolder, bool overwrite = false, string libraryRoot = null)
        {
            var data = TripLoader.Load(sourceFolder);
            var tripId = string.IsNullOrEmpty(data.TripId) ? Ids.New("trip") : data.TripId;
            data.Trip.TripId = tripId;
            var dest = Path.Combine(libraryRoot ?? TripPaths.LibraryRoot, TripPaths.SanitizeFileName(tripId));
            if (Directory.Exists(dest))
            {
                if (!overwrite) throw new IOException("A trip with id '" + tripId + "' already exists in the library: " + dest);
                Directory.Delete(dest, true);
            }
            data.FolderPath = dest;
            TripSaver.SaveAll(data);
            return dest;
        }

        public static string ImportSampleTrip(bool overwrite = true, string libraryRoot = null) =>
            ImportFolder(TripPaths.SampleTripFolder, overwrite, libraryRoot);

        /// <summary>First run: if the library is empty, import the bundled sample so the app is never blank. Returns the folder or null.</summary>
        public static string EnsureSampleIfEmpty(string libraryRoot = null)
        {
            if (ListTrips(libraryRoot).Count > 0) return null;
            if (!Directory.Exists(TripPaths.SampleTripFolder)) return null;
            return ImportSampleTrip(overwrite: false, libraryRoot: libraryRoot);
        }

        /// <summary>Copy a library trip out to another folder (a plain folder export; zip comes later).</summary>
        public static void ExportFolder(string tripId, string destinationFolder, string libraryRoot = null)
        {
            var src = Path.Combine(libraryRoot ?? TripPaths.LibraryRoot, TripPaths.SanitizeFileName(tripId));
            var data = TripLoader.Load(src);
            data.FolderPath = destinationFolder;
            TripSaver.SaveAll(data);
        }

        public static void DeleteTrip(string tripId, string libraryRoot = null)
        {
            var folder = Path.Combine(libraryRoot ?? TripPaths.LibraryRoot, TripPaths.SanitizeFileName(tripId));
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }

        /// <summary>
        /// Copy a trip under a new tripId (typically to reuse a plan for the next phase). Every
        /// document's tripId is rewritten. Captures, amendments and assignments are dropped
        /// unless <paramref name="includeCaptures"/>; entity IDs inside the plan are kept so
        /// outlines and photo refs keep lining up. Returns the saved copy.
        /// </summary>
        public static TripData DuplicateTrip(string tripId, bool includeCaptures = false, Action<TripIdentity> editIdentity = null, string libraryRoot = null)
        {
            var root = libraryRoot ?? TripPaths.LibraryRoot;
            var src = TripLoader.Load(Path.Combine(root, TripPaths.SanitizeFileName(tripId)));
            return DuplicateTrip(src, includeCaptures, editIdentity, root);
        }

        public static TripData DuplicateTrip(TripData src, bool includeCaptures = false, Action<TripIdentity> editIdentity = null, string libraryRoot = null)
        {
            var copy = new TripData
            {
                Trip = TripJson.Clone(src.Trip),
                ShotList = TripJson.Clone(src.ShotList),
                Captures = includeCaptures ? TripJson.Clone(src.Captures) : new CapturesDocument(),
            };
            foreach (var kv in src.Outlines) copy.Outlines[kv.Key] = TripJson.Clone(kv.Value);
            foreach (var kv in src.OutlineFileNames) copy.OutlineFileNames[kv.Key] = kv.Value;

            var newId = Ids.New("trip");
            copy.Trip.TripId = newId;
            copy.ShotList.TripId = newId;
            copy.Captures.TripId = newId;
            foreach (var o in copy.Outlines.Values) o.TripId = newId;
            if (copy.Trip.Identity == null) copy.Trip.Identity = new TripIdentity();
            editIdentity?.Invoke(copy.Trip.Identity);

            copy.FolderPath = Path.Combine(libraryRoot ?? TripPaths.LibraryRoot, TripPaths.SanitizeFileName(newId));
            TripSaver.SaveAll(copy);
            return copy;
        }

        /// <summary>
        /// "Rename" a trip: its display identity is client/program/phase/location, so renaming
        /// is editing those on a closed trip. The folder (named by tripId) does not move.
        /// </summary>
        public static void UpdateIdentity(string tripId, Action<TripIdentity> edit, string libraryRoot = null)
        {
            var folder = Path.Combine(libraryRoot ?? TripPaths.LibraryRoot, TripPaths.SanitizeFileName(tripId));
            var tripPath = Path.Combine(folder, TripLoader.TripFile);
            var trip = TripLoader.LoadDocument<TripDocument>(tripPath, DocumentKind.Trip);
            if (trip.Identity == null) trip.Identity = new TripIdentity();
            edit(trip.Identity);
            TripSaver.WriteDocument(trip, tripPath);
        }
    }
}

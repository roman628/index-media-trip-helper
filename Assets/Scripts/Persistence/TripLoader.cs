using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MediaTrip.Model;

namespace MediaTrip.Persistence
{
    /// <summary>
    /// Loads a trip folder into a <see cref="TripData"/>. Only trip.json is required;
    /// shotlist.json, captures.json and outlines/*.json are optional and default to empty.
    /// Non-fatal oddities (an outline with no bookId, duplicate outlines) go into
    /// <see cref="LoadResult.Warnings"/> rather than throwing.
    /// </summary>
    public static class TripLoader
    {
        public const string TripFile = "trip.json";
        public const string ShotListFile = "shotlist.json";
        public const string CapturesFile = "captures.json";
        public const string OutlinesDir = "outlines";

        public class LoadResult
        {
            public TripData Data;
            public List<string> Warnings = new List<string>();
        }

        public static TripData Load(string folder) => LoadWithWarnings(folder).Data;

        public static LoadResult LoadWithWarnings(string folder)
        {
            if (string.IsNullOrEmpty(folder)) throw new ArgumentNullException(nameof(folder));
            if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("Trip folder not found: " + folder);

            var result = new LoadResult { Data = new TripData { FolderPath = folder } };
            var data = result.Data;

            var tripPath = Path.Combine(folder, TripFile);
            if (!File.Exists(tripPath))
                throw new FileNotFoundException("A trip folder must contain " + TripFile, tripPath);
            data.Trip = LoadDocument<TripDocument>(tripPath, DocumentKind.Trip);

            var shotListPath = Path.Combine(folder, ShotListFile);
            data.ShotList = File.Exists(shotListPath)
                ? LoadDocument<ShotListDocument>(shotListPath, DocumentKind.ShotList)
                : new ShotListDocument { TripId = data.Trip.TripId };

            var capturesPath = Path.Combine(folder, CapturesFile);
            data.Captures = File.Exists(capturesPath)
                ? LoadDocument<CapturesDocument>(capturesPath, DocumentKind.Captures)
                : new CapturesDocument { TripId = data.Trip.TripId };

            var outlinesDir = Path.Combine(folder, OutlinesDir);
            if (Directory.Exists(outlinesDir))
            {
                foreach (var file in Directory.GetFiles(outlinesDir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
                {
                    var outline = LoadDocument<OutlineDocument>(file, DocumentKind.Outline);
                    var fileName = Path.GetFileName(file);
                    var key = outline.BookId;
                    if (string.IsNullOrEmpty(key))
                    {
                        key = Path.GetFileNameWithoutExtension(file);
                        result.Warnings.Add($"{fileName}: outline has no bookId; keyed by file name '{key}'.");
                    }
                    if (data.Outlines.ContainsKey(key))
                    {
                        result.Warnings.Add($"{fileName}: duplicate outline for book '{key}' replaces '{data.OutlineFileNames[key]}'.");
                    }
                    data.Outlines[key] = outline;
                    data.OutlineFileNames[key] = fileName;
                }
            }

            foreach (var doc in new DocumentBase[] { data.ShotList, data.Captures }.Concat(data.Outlines.Values))
            {
                if (!string.IsNullOrEmpty(doc.TripId) && !string.IsNullOrEmpty(data.Trip.TripId) && doc.TripId != data.Trip.TripId)
                    result.Warnings.Add($"{doc.GetType().Name} tripId '{doc.TripId}' does not match trip.json '{data.Trip.TripId}'.");
            }

            return result;
        }

        /// <summary>Read, migrate, deserialize one document.</summary>
        public static T LoadDocument<T>(string path, DocumentKind kind) where T : DocumentBase
        {
            var text = File.ReadAllText(path);
            try
            {
                return LoadDocumentFromJson<T>(text, kind, path);
            }
            catch (Exception ex) when (!(ex is SchemaTooNewException))
            {
                throw new InvalidDataException("Failed to load " + path + ": " + ex.Message, ex);
            }
        }

        public static T LoadDocumentFromJson<T>(string json, DocumentKind kind, string sourceName = null) where T : DocumentBase
        {
            var obj = TripJson.ParseObject(json);
            obj = SchemaMigrator.Migrate(obj, kind, sourceName);
            return TripJson.FromJObject<T>(obj);
        }
    }
}

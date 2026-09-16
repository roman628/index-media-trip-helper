using System;
using System.IO;
using System.Text;
using MediaTrip.Model;

namespace MediaTrip.Persistence
{
    /// <summary>
    /// Writes documents back to a trip folder. Each file is written to a temp file first and
    /// then swapped in, so a crash mid-write never leaves a half-written document behind.
    /// </summary>
    public static class TripSaver
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        public static void SaveAll(TripData data, string folder = null)
        {
            folder = ResolveFolder(data, folder);
            SaveTrip(data, folder);
            SaveShotList(data, folder);
            SaveCaptures(data, folder);
            SaveOutlines(data, folder);
        }

        public static void SaveTrip(TripData data, string folder = null) =>
            WriteDocument(data.Trip, Path.Combine(ResolveFolder(data, folder), TripLoader.TripFile));

        public static void SaveShotList(TripData data, string folder = null) =>
            WriteDocument(data.ShotList, Path.Combine(ResolveFolder(data, folder), TripLoader.ShotListFile));

        public static void SaveCaptures(TripData data, string folder = null) =>
            WriteDocument(data.Captures, Path.Combine(ResolveFolder(data, folder), TripLoader.CapturesFile));

        public static void SaveOutlines(TripData data, string folder = null)
        {
            folder = ResolveFolder(data, folder);
            foreach (var bookId in data.Outlines.Keys)
                SaveOutline(data, bookId, folder);
        }

        public static void SaveOutline(TripData data, string bookId, string folder = null)
        {
            folder = ResolveFolder(data, folder);
            if (!data.Outlines.TryGetValue(bookId, out var outline))
                throw new ArgumentException("No outline loaded for book " + bookId, nameof(bookId));
            var dir = Path.Combine(folder, TripLoader.OutlinesDir);
            Directory.CreateDirectory(dir);
            var fileName = OutlineFileName(data, bookId);
            data.OutlineFileNames[bookId] = fileName;
            WriteDocument(outline, Path.Combine(dir, fileName));
        }

        /// <summary>Existing file name if the outline was loaded from disk, else book-N.json, else bookId.json.</summary>
        public static string OutlineFileName(TripData data, string bookId)
        {
            if (data.OutlineFileNames.TryGetValue(bookId, out var existing) && !string.IsNullOrEmpty(existing))
                return existing;
            var book = data.FindBook(bookId);
            if (book != null) return "book-" + book.Number + ".json";
            return TripPaths.SanitizeFileName(bookId) + ".json";
        }

        public static string ToJson(DocumentBase doc)
        {
            doc.SchemaVersion = Schema.CurrentVersion;
            return TripJson.Serialize(doc);
        }

        public static void WriteDocument(DocumentBase doc, string path)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            WriteAtomic(path, ToJson(doc));
        }

        public static void WriteAtomic(string path, string text)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, text, Utf8NoBom);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        private static string ResolveFolder(TripData data, string folder)
        {
            folder = folder ?? data.FolderPath;
            if (string.IsNullOrEmpty(folder))
                throw new InvalidOperationException("No folder to save to: pass one or set TripData.FolderPath.");
            if (TripPaths.IsInsideStreamingAssets(folder))
                throw new InvalidOperationException("Refusing to write into StreamingAssets. Import the trip into the library first.");
            Directory.CreateDirectory(folder);
            return folder;
        }
    }
}

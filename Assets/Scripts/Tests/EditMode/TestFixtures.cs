using System;
using System.IO;
using MediaTrip.Model;
using MediaTrip.Persistence;
using UnityEngine;

namespace MediaTrip.Tests
{
    /// <summary>Shared helpers: the read-only sample trip and throw-away temp folders.</summary>
    public static class Fixtures
    {
        public static string SampleFolder => Path.Combine(Application.streamingAssetsPath, "SampleTrip");

        public static TripData LoadSample() => TripLoader.Load(SampleFolder);

        /// <summary>A fresh temp folder under the OS temp dir. Never inside the project.</summary>
        public static string NewTempFolder(string tag = "mt")
        {
            var path = Path.Combine(Path.GetTempPath(), "MediaTripTests", tag + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        public static void DeleteFolder(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, true); }
            catch { /* best effort */ }
        }

        /// <summary>A small trip built in code for combine/split edge cases.</summary>
        public static TripData SyntheticTrip()
        {
            var d = TripData.CreateNew("TST", "PRG");
            d.Trip.Books.Add(new Book { Id = "b1", Number = 1, Name = "Book One" });
            d.Trip.Days.Add(new Day { Id = "d1", Date = "2026-09-15", Label = "Day 1" });
            d.Trip.Days.Add(new Day { Id = "d2", Date = "2026-09-16", Label = "Day 2" });
            d.ShotList.Chapters.Add(new Chapter { Id = "c1", BookId = "b1", Number = 1, Name = "Chapter One" });
            d.ShotList.Chapters.Add(new Chapter { Id = "c2", BookId = "b1", Number = 2, Name = "Chapter Two" });
            for (int i = 1; i <= 5; i++)
                d.ShotList.Videos.Add(new Video { Id = "v" + i, Number = i, BookId = "b1", ChapterId = i <= 3 ? "c1" : "c2", Title = "Video " + i });
            d.ShotList.Photos.Add(new Photo { Id = "p1", BookId = "b1", HeroType = HeroType.BookCover, Order = 1, Description = "Cover" });
            d.ShotList.Photos.Add(new Photo { Id = "p2", BookId = "b1", ChapterId = "c1", HeroType = HeroType.ChapterHero, Order = 2, Description = "Ch1 hero" });
            d.ShotList.Videos[0].PhotoRefs.Add("p1");
            d.ShotList.Videos[0].PhotoRefs.Add("p2");
            return d;
        }
    }
}

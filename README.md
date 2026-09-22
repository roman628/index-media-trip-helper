# Media Trip Helper

An iPad app that replaces the clipboard on media trips. The crew captures video and photos
against a printed shot list; the app holds that plan, records what was actually shot, tracks
the changes made in the field, and keeps the book outlines and the cover and hero assignments in one place.
Everything is JSON on disk, fully offline. No accounts, no network, no cloud services.

## What it does

The app opens on the **trip library** (New trip, Import, Theme). A trip is five documents, on
a rail in landscape and a bottom bar in portrait and on a phone. The rule everywhere: **Edit
changes the document; the normal state annotates it.** Trip, Shot list and Outlines have an
Edit toggle in the header; Covers and Summary are annotation only. Search and a menu (Share,
the screen's named exports, Import, Theme, Shortcuts) sit beside it.

- **Trip**: one scrolling document (identity, dates and days, location, weather, media file,
  logistics, actions, books and teams, people) with a section index that follows the scroll.
  Arrive and depart are travel days; the shooting days between them fill themselves in.
- **Shot list**: Working (the plan as it now stands), Original (the printed list) and Changes,
  plus "Hide done". Videos and photos expand and collapse in place (Expand all reads like the
  printed document); each video has a menu with Film, Rename, Combine, Split and Drop.
  Photos are listed with their book. A video's "Photos with this video" is a list with one
  add box under it that finds any photo of any book, or creates a new one; removing from the
  list removes only the link. Edit means two different things:
  - *Original + Edit* is authoring the plan. It records nothing, until media exists; then
    the app asks whether this is a fix to what was typed (logged as a correction) or a change
    of plan (which belongs in Working).
  - *Working + Edit* is changing the plan during the trip. Every change is recorded: a
    retitle is a rename, a delete is a drop, a new video or photo is an add, and content
    (scene, SME, notes, photos) is a "revise" carrying the field-level before and after.
  Changes lists what changed, and, under their own heading, corrections to the original.
- **Outlines**: one screen per book. Chapters and their sections expand and collapse in
  place; a collapsed row shows the media attached and the first words of its notes. Notes and
  placed media are one idea at two levels: on the chapter as a whole, or on a section. Edit
  types the outline in (names, bullets, drag or move up/down to reorder, the whole thing from
  the keyboard) and has no notes or Place media, because that is annotation.
- **Covers**: every book's cover and chapter heroes on one board. Each slot shows what was
  planned and what is assigned. One search box finds any photo of any book (saying which
  book, which chapter, cover or which hero) or creates what was typed. A photo of another
  book is moved or shared only after asking. A slot is assigned to, never ticked, and its
  notes are the chapter's notes, the same ones Outlines shows.
- **Summary**: what was shot, by day, in order. Video and Photos append at the bottom; rows
  are dragged by their handle to reorder (a tap on the handle offers Move up / Move down).
  An entry's day and time can be changed.
- **Filming**: the entry form with the matching line of the plan above each field, a Plan
  button that opens the whole plan beside the form, shot-list suggestions while typing the
  title, and an SME picker (name, then title, then Add; the same one the shot list uses).
  Saving writes a capture (and an "add" for something unplanned). Photos typed here stay
  free text for speed; they show in every photo picker as not on the list yet, and picking
  one anywhere files it as a real photo with the records still pointing at it.
- **Transfer**: Share writes a zip (or a single-file JSON bundle where zipping is unavailable)
  and hands it to the platform: the iOS share sheet, a save dialog in the Editor, or the
  app's Export folder in a standalone build. Exports are named for what they are: original
  shot list, working shot list, changes, media summary, an outline. Import accepts a whole
  trip or a single document and detects which; an import started for one kind of document
  (an outline, from Outlines) refuses anything else. Every import is validated first and
  applied all at once or not at all. A fresh install starts with an empty library.

A chapter is one thing, owned by the shot list; an outline attaches sections to it by id, so
a chapter typed into an outline is a chapter of the plan. New photos and videos, wherever
they are created (Covers, Outlines, the shot list), go through one operation: they land in
the working shot list in the right book and chapter, marked new, and show in Changes.
Nothing is silently orphaned: a delete that would break an assignment lists what it touches
first, and then detaches or moves it. Removing something that was filmed (a delete, a drop,
an undone add) asks: detach and keep the capture (the default: it stays in the summary,
unplanned, tagged "was video 4: …") or delete the capture too.

Layouts are built in points for three kinds of screen (phone, portrait tablet, landscape
tablet) chosen at run time from the panel size; every screen works in all of them. A
read-only JSON inspector with validation is kept for debugging: five quick taps on the
library's "Trips" title, or Ctrl/Cmd+Shift+J.

## Opening the project

- Unity 6000.3.10f1. Open the project folder in the Unity Hub or Editor.
- The scene is `Assets/Scenes/SampleScene.unity`. Press Play. A fresh library is empty: create
  a trip, or import `Assets/StreamingAssets/SampleTrip` (a plain trip folder) to try the app
  with the fake sample data.
- Development and testing happen on Windows in Play mode. iOS builds are made on a Mac:
  switch the platform to iOS, build, open the Xcode project, sign, and run to a device. The
  post-build step in `Assets/Editor` sets the Info.plist flags that expose the app's folder
  in the Files app.
- If the scene lost its UI setup, run **Media Trip > Set Up Scene** from the menu bar.

## Running the tests

Window > General > Test Runner, EditMode tab, Run All. The data-layer tests live in
`Assets/Scripts/Tests/EditMode` and the UI view-model tests in `Assets/UI/Tests/EditMode`.
They run against the fake sample trip in `Assets/StreamingAssets/SampleTrip` and against
temp folders; nothing is written into the project.

## Folder layout

| Folder | Contents |
|---|---|
| `Assets/Scripts/Model` | JSON model classes for `trip.json`, `shotlist.json`, `outlines/*.json`, `captures.json`, and `TripNormalizer` (the rules that span documents). |
| `Assets/Scripts/Persistence` | Load, save, migrations, the trip library, zip and JSON bundle packaging, autosave. |
| `Assets/Scripts/Status` | The plan resolver: amendments applied to the immutable shot list, derived status per item. |
| `Assets/Scripts/Query` | Read API for every view (full list, remaining, heroes, day summary, outline coverage). |
| `Assets/Scripts/Search` | Deterministic fuzzy matcher and the trip-wide search helpers. |
| `Assets/Scripts/Authoring` | Structural editing of the plan, outlines, people, node trees, paste parsing; `PlanEdits` (what an edit records in Original and in Working) and `Deletions` (what a delete would touch). |
| `Assets/Scripts/Export` | The export seam: named exports and one writer per format (JSON today). |
| `Assets/Scripts/Session` | `TripSession`: the open trip, dirty tracking, autosave, typed mutations. |
| `Assets/Scripts/Validation` | Referential-integrity checks. |
| `Assets/Scripts/Tests` | EditMode tests for the data layer. |
| `Assets/UI` | UI Toolkit app: UXML, USS, themes, the shell and the document screens, transfer routes, view models and their tests. |
| `Assets/Editor` | Scene setup menu and the iOS post-build step. |
| `Assets/Plugins/iOS` | The Objective-C Files picker plugin, compiled by Xcode. |
| `Assets/StreamingAssets/SampleTrip` | A complete fake trip used for development and tests. |
| `TripData/` | Real trip JSON, ignored by git. Never commit client content. |

## The TripSession entry point

All application code talks to one object:

```csharp
using MediaTrip.Session;

var session = TripSession.OpenFromLibrary(tripId);   // load a trip from the library
TripAutosaveBehaviour.Attach(session);               // debounced autosave every frame

session.Queries.Remaining();                         // views over the resolved plan
session.Queries.Heroes();
session.Queries.DaySummary(dayId);
session.Search.SearchShotList("entry perm");         // fuzzy search
session.AddCapture(capture);                         // filming writes captures
session.CreateMedia(MediaKind.Photo, text, bookId, chapterId); // the one path for new media
new PlanEdits(session, PlanEditMode.Working).SetScene(id, text); // recorded as a revise
session.SetNote(bookId, chapterId, sectionId, text); // notes: a chapter, or one of its sections
session.ReorderDay(dayId, orderedIds);               // the summary's order
session.AssignHero(bookId, chapterId, photoId, null); // cover and hero slots are assigned
session.Combine(new[] { "v-001", "v-002" }, "Both"); // plan changes are amendments
session.PlanEditor.AddVideo(chapterId, "Title");     // authoring edits the plan itself
session.Changed += Refresh;                          // rebuild the view after any change
```

Every mutation marks the affected document dirty and the autosaver writes it shortly after.
The low-level plan editor still refuses to delete, move or retitle an item that a capture or
amendment references. The app goes through `PlanEdits` instead: in Working the edit becomes a
recorded change, and in Original it is a logged correction (or, for a delete, a confirmed
detach), so the paper list, the capture log and the app never silently disagree.

## Data rules

- The shot list is the plan and is immutable in the field. Captures reference it by ID.
- Video numbers run continuously across books and chapters. Captures keep the number they
  were shot against even if the plan is renumbered later.
- Every document carries a `schemaVersion` (currently 2); migrations live in `SchemaMigrator`,
  and the rules that span documents (chapters, notes) in `TripNormalizer`, run on every load.
- Exporting is one interface per format (`ITripExportFormat`). JSON is the first; a Word
  writer for the company templates registers beside it without touching the UI.
- Real trip data lives in `TripData/` at the repo root and is never committed. Use the sample
  trip for development.

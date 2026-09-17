# Media Trip Helper

An iPad app that replaces the clipboard on media trips. The crew captures video and photos
against a printed shot list; the app holds that plan, records what was actually shot, tracks
the changes made in the field, and keeps the book outlines and the cover and hero assignments in one place.
Everything is JSON on disk, fully offline. No accounts, no network, no cloud services.

## What it does

The app opens on the **trip library** (New trip, Import). A trip is five documents, on a rail
in landscape and a bottom bar in portrait and on a phone. Each has a read state; Trip, Shot
list and Outlines also have an Edit toggle in the header. Search and a menu (Share, Import,
Theme) sit beside it, with a one-tap sun button for the high-contrast scheme.

- **Trip**: one scrolling document (identity, dates and days, location, weather, media file,
  logistics, actions, books and teams, people) with a section index that follows the scroll.
- **Shot list**: Working (the plan as it now stands), Original (the printed list) and Changes
  (the amendment log), plus "Hide done". A video's detail has Film as its primary action, with
  Rename, Combine and Drop beneath it. Those record amendments; the plan itself is only
  edited in the Edit state, and never for something already filmed or changed.
- **Outlines**: book chips, chapter list, section view. The outline text sits beside "My
  notes" and "Placed here", where a suggested placement is kept with one tap.
- **Covers**: every book's cover and chapter heroes on one board. Each slot shows what was
  planned and what is assigned: the planned photo once shot, another photo from the master
  list, or a new one described on the spot. A slot is assigned to, never ticked.
- **Summary**: what was shot, by day, in order. Video and Photos append at the bottom; rows
  are dragged by their handle to reorder (a tap on the handle offers Move up / Move down).
- **Filming**: the entry form with the matching line of the plan above each field, a Plan
  button that opens the whole plan beside the form, and shot-list suggestions while typing
  the title. Saving writes a capture (and an "add" amendment for something unplanned).
- **Transfer**: Share writes a zip (or a single-file JSON bundle where zipping is unavailable)
  and hands it to the platform: the iOS share sheet, a save dialog in the Editor, or the
  app's Export folder in a standalone build. Import accepts a whole trip or a single document
  and detects which. Every import is validated first and applied all at once or not at all.
  A fresh install starts with an empty library.

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
| `Assets/Scripts/Model` | JSON model classes for `trip.json`, `shotlist.json`, `outlines/*.json`, `captures.json`. |
| `Assets/Scripts/Persistence` | Load, save, migrations, the trip library, zip and JSON bundle packaging, autosave. |
| `Assets/Scripts/Status` | The plan resolver: amendments applied to the immutable shot list, derived status per item. |
| `Assets/Scripts/Query` | Read API for every view (full list, remaining, heroes, day summary, outline coverage). |
| `Assets/Scripts/Search` | Deterministic fuzzy matcher and the trip-wide search helpers. |
| `Assets/Scripts/Authoring` | Structural editing of the plan, outlines, people, node trees, paste parsing. |
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
session.ReorderDay(dayId, orderedIds);               // the summary's order
session.AssignHero(bookId, chapterId, photoId, null); // cover and hero slots are assigned
session.Combine(new[] { "v-001", "v-002" }, "Both"); // plan changes are amendments
session.PlanEditor.AddVideo(chapterId, "Title");     // authoring edits the plan itself
session.Changed += Refresh;                          // rebuild the view after any change
```

Every mutation marks the affected document dirty and the autosaver writes it shortly after.
Once a capture or amendment references a planned item, deleting, moving or retitling that item
in the plan is refused and routed through an amendment instead, so the paper list, the capture
log and the app never silently disagree.

## Data rules

- The shot list is the plan and is immutable in the field. Captures reference it by ID.
- Video numbers run continuously across books and chapters. Captures keep the number they
  were shot against even if the plan is renumbered later.
- Every document carries a `schemaVersion`; migrations live in one place (`SchemaMigrator`).
- Real trip data lives in `TripData/` at the repo root and is never committed. Use the sample
  trip for development.

# MediaTrip.Data — the data layer

Everything under `Assets/Scripts/` except `Tests/` compiles into the `MediaTrip.Data` assembly.
No UI in here. The UI (UXML/USS, `Assets/UI/`) sits on top of `TripSession`.

## Entry point for the UI

```csharp
using MediaTrip.Session;

var session = TripSession.OpenFromLibrary(tripId);      // or TripSession.Open(folder)
TripAutosaveBehaviour.Attach(session);                  // drives the debounced autosave each frame

session.Queries.Remaining();                            // List<PlanItem>
session.Queries.Heroes();                               // List<HeroItem>
session.Queries.DaySummary(dayId);                      // captures in order + photos under/additional
session.Queries.OutlineCoverage(bookId);                // sections with/without media
session.Search.SearchShotList("entry perm");            // scored plan items
session.Search.SuggestNames("nin", NameScope.Sme);      // scored names, any name still accepted
session.Search.SuggestOutlineFor(capture);              // scored outline sections, never auto-commits

session.CheckOff("v-004", dayId);                       // adds a Capture; never edits the shot list
session.Combine(new[] { "v-001", "v-002" }, "Both");    // amendment; sources become superseded
session.Changed += Refresh;                             // rebuild views after any mutation
```

Every mutation goes through `TripSession` (typed helpers, or `Edit(...)` for anything else).
That marks the affected document dirty; `Autosaver` writes it after ~0.75 s of quiet, or
within 5 s of continuous edits, and on pause/quit. Never write to `StreamingAssets`; the
saver refuses. Runtime data lives under `Application.persistentDataPath/Trips/<tripId>/`.
`TripLibrary.ImportSampleTrip()` copies the fake sample trip there.

## Authoring (Phase 2)

```csharp
session.PlanEditor.AddVideo(chapterId, "Title", index: 0);   // renumbers everything; captures keep plannedNumber
session.PlanEditor.MoveVideo(videoId, otherChapterId);      // throws PlanEditBlockedException if captured -> use session.Move
session.PlanEditor.Notes(videoId).AddChild(nodeId, "text"); // nested notes, relabelled 1 / a / 1
session.Outline.AddSection(bookId, chapterId, "Name");      // sections renumbered "c.n"
session.Outline.Bullets(bookId, sectionId).Indent(nodeId);  // relabelled a / 1 / i
session.People.Merge(keepId, duplicateId);                  // re-points every reference
IndentedTextParser.Parse(pastedText);                       // tabs/spaces -> node tree
TripPackage.ValidateZip(path).CanImport;                    // dry run, nothing written
TripPackage.ImportZip(path, overwrite: true);               // staged, then swapped in
TripLibrary.DuplicateTrip(tripId);                          // new tripId, captures dropped
```

**Guard rule.** Once a capture or amendment references a plan item, deleting, moving or
retitling it in the plan is refused (`PlanEditBlockedException` lists the references and the
amendment to use instead). Working content on that item (notes, SMEs, photo refs, scene
description) stays editable, and new items can always be added.

## Folders

| Folder | What |
|---|---|
| `Model/` | POCOs for `trip.json`, `shotlist.json`, `outlines/*.json`, `captures.json`; `Node` is the recursive bullet. Unknown JSON keys are preserved in `Extra`. |
| `Persistence/` | `TripJson` (settings + canonical comparison), `SchemaMigrator` (the one place migrations go), `TripLoader`, `TripSaver` (atomic writes), `TripLibrary`/`TripPaths`, `Autosaver`. |
| `Status/` | `PlanResolver` applies amendments to the immutable plan and derives every status. `PlanItem` is a video as the plan now stands; `PhotoItem` a master-list photo with status. |
| `Query/` | `TripQueries`: the views (full / remaining / captured / heroes / day / coverage). |
| `Search/` | `FuzzyMatcher` (deterministic) and `TripSearch` (names, shot list, outline suggestion). |
| `Authoring/` | `ShotListEditor`, `OutlineEditor`, `PeopleEditor`, `NodeTree`/`NodeListEditor`/`LabelScheme`, `IndentedTextParser`, `ReferenceFinder`. |
| `Session/` | `TripSession`: data + dirty tracking + cached plan/queries/search + typed mutations. |
| `Validation/` | `TripValidator`: referential-integrity warnings, never throws. |
| `Tests/EditMode/` | NUnit tests against the sample trip and synthetic combine/split cases. |

## Status rules (see `PlanResolver`)

- `Superseded`: a combine/split targeted the item. `Resolve(id)` follows the chain to what was actually shot.
- `Dropped`: a drop amendment targeted it.
- `Captured`: at least one capture references the item's ID.
- `PartiallyCaptured`: for a superseded item, only some of its split/combine results are
  captured. (Optionally, with `ResolveOptions.PhotoRefsAffectVideoStatus = true`, a captured
  video with un-captured photoRefs; off by default because photoRefs are candidates, not
  requirements. `PlanItem.PhotoProgress` gives "3 of 5" either way.)
- `NotCaptured`: none of the above.

`Remaining()` = NotCaptured + PartiallyCaptured. `Captured()` = Captured only.
Amendments are applied in timestamp order (`at`), ties broken by list index.

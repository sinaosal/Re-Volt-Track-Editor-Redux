# Roadmap

Status of the Studio work on the `feature/studio-ui-overhaul` branch and what should come next.

## Phase 1: done

**Reliability**
- Unity-independent TDF codec (`Assets/Scripts/Core/TdfDocument.cs`). It writes the same bytes as
  the previous writer and validates sizes before allocating. It reads legacy UTF-8 and Latin-1 names.
- Atomic saves (temp file + replace). Save errors are reported and the old file is kept.
- Loads are validated completely before the open track changes. Bad placements (unknown modules,
  overlaps, pieces past the grid edge) produce warnings instead of crashes.
- One damaged `.TDF` no longer breaks the load screen or editor startup.
- Snapshot-based undo/redo for every edit, with exact dirty tracking against the saved state.
  History depth adapts to the grid size so memory stays bounded on unlimited-mode grids.
- Autosave to the user data folder and crash recovery offered on the next start.
- Unsaved changes prompt when the window is closed.
- Export runs as a guarded job: failures end with an error instead of a stuck export screen, and
  failed background tasks no longer produce partial exports.

**Studio interface** (`Assets/Scripts/Studio`)
- Menu bar, toolbar, module library with rendered previews, inspector (Place, Track, History,
  Issues), status bar, resizable and toggleable panels, persisted layout.
- Viewport: orbit/pan/zoom, top view, framing, elevation-aware picking, place/paint, pickup and
  erase tools, footprint preview, context menu, problem highlighting.
- Dialogs: welcome/recovery, open, save as, unsaved changes, export, preferences, rebindable
  shortcuts, about. The classic interface is unchanged and one key away (F10).
- Block selection with support pillar colors (`!Name.tdx` sidecar), race data editing (AI nodes,
  position nodes, zones, lights, objects, instances, force fields).

## Phase 2: feature inventory

Inspected before starting. "Partial" means it existed but had gaps that mattered.

| Area | Before phase 2 | Phase 2 |
| --- | --- | --- |
| Selection | One block at a time; drag, arrows, R, PgUp/PgDn | Multi-selection: Shift/Ctrl click, box select, select all/none/invert, select connected section (Ctrl+L), select same type |
| Transforms | Single-block move/rotate/height | Group move (drag, arrows, gizmo arrows, typed X/Y/level), group rotation around the selection's middle, group height; one undo step each |
| Clipboard | Missing | Copy, cut, paste, duplicate with 3D ghost previews; R/PgUp/PgDn while pasting; paste repeats until Esc |
| Connections | Lap check only (first break, on demand) | Every joint classified from the track unit's zone link points: joined, jump, height step, open end, separate sections. Viewport markers, placement preview, "fit to neighbours" (A) |
| Issues | Flat list of messages | Severity groups and filters, titles, check source, suggested fix, click selects the blocks involved; re-checked automatically after edits |
| Module library | Groups with previews, text search | Categories built from the original groups, favorites, recently placed, three preview sizes, details (size, connection points, surfaces) and a clickable variant strip |
| Hierarchy | Missing | Outliner tab: every block in track order (following connections from the start grid), by type or by position; search; synced selection; lock and hide |
| Camera | Perspective, top, orbit steps, frame track | Front and side orthographic views (heights along the track), orthographic toggle, frame selection, reset, zoom towards the pointer, minimap with click-to-look |
| Commands | Menus, toolbar, rebindable keys | Command palette (Ctrl+Shift+P) over the same registry |
| Overview | Module/pickup counts | Types, cells, footprint, height range, connections, open ends, sections, check state, lap length |
| Export safety | Level folder emptied without a copy; missing imported models found after emptying it | The level folder is copied to the user data folder first (last 5 kept); missing models stop the export before anything is touched |

### Deliberately not implemented

- **Scale tool.** Track modules are fixed grid pieces; the TDF stores a module id, rotation and
  height per cell and nothing that could scale. Zones and force fields already have size fields in
  Race data.
- **Overlap and duplicate detection while editing.** Cells belong to one block, so the editor can't
  create overlaps. Files that contain them (hand edited, other tools) are reported when loaded.
- **Renaming blocks.** The TDF has no per-block names and the sidecar wasn't extended for it.
- **Persisted locks and hidden blocks.** Both are editing-session state, cleared when another track
  is opened, and only the Studio interface respects them (the classic one can't show them).
- **Side view shortcut.** Numpad 3 doubles as Page Down with Num Lock off, so the side view has no
  default key (it can be bound in the shortcut editor). Front is Numpad 1, orthographic Numpad 5.

### How connections are worked out

`Assets/Scripts/Core/TrackConnectivity.cs` uses the two link points of each zone in
`trackunit.rtu`, which the exporter already chains to build the lap
(`TrackExporter.FindNextZone`): link points within a quarter unit horizontally meet. At the same
height they're joined; at different heights they're a step (the lap check only continues
downhill across one, pipes must be level); a link point more than 3000 units above its module is a
jump exit. Module data was checked against the original track unit: every outer link point lies
on a cell edge, bridge decks sit up to 2000 units high, the jump exit at 5000, bank edges half a
level up.

## Needs verification in Unity

All of this is compile-checked against Unity 2020.3 reference assemblies and the core logic is unit
tested (`Tests/Core.Tests`), but phase 2 has not been run in Unity yet. Before merging:

1. Select tool: click, Shift/Ctrl click, box select, drag-move, gizmo arrows (X, Z, level), arrow
   keys, R, PgUp/PgDn, Del; undo each.
2. Copy/paste/duplicate: ghosts follow the pointer, R and PgUp/PgDn while pasting, Esc, pickups and
   support colors carried along, pasting over blocks replaces them (amber), locked blocks refuse.
3. Connections: markers on a closed loop, an open end, a step, a jump; placement preview colors;
   A (fit) next to an open end and next to a raised piece.
4. Issues tab updates on its own after edits; clicking a separate section selects it.
5. Outliner: track order follows the lap; selection syncs both ways; lock and hide.
6. Camera: front/side/ortho, zoom towards the pointer, minimap clicks, frame selection (F), Home.
7. Export over an existing level: a backup appears under the user data folder's `ExportBackups`.
8. Phase 1 checks: classic interface (F10), gamepad, 100/150/200% scale, crash recovery.

## Next

- **Studio screenshot** for the README once it has been run.
- **Native file dialogs and drag-and-drop into the window** (Unity's runtime has neither).
- **Track preview in the open dialog**: render a top-down thumbnail of each saved track.
- **Suggested next piece**: from an open end, offer the modules that would connect, using
  `TrackConnectivity.Evaluate` over the library.
- **Localization of the Studio interface** and **gamepad navigation of Studio dialogs**.

# ReVoltTrackEditor
A re-make of the Re-Volt Track Editor in Unity 2020.3.21f1.

The editor has two interfaces that share the same tracks, files and editing tools:

- **Studio** (default on desktop): a workspace with a module library, 3D viewport, inspector,
  mouse editing, undo history, track checks and rebindable shortcuts.
- **Classic**: the original editor's look with keyboard and gamepad controls. Press **F10** at any
  time to switch. Android always starts in the classic interface.

![Preview Image](preview.png?raw=true "Classic interface")

## Releases
You can find releases over on Re-Volt World: https://www.revoltworld.net/dl/track-editor-unity-edition/

## Running (in the Unity Editor)
This requires a copy of the original Track Editor files to use (wavs, bitmaps, and trackunit.rtu). Open `editor_path.txt` and set this to the path where your editor files exist, from there it will just run.

You can obtain these files [here](https://www.mediafire.com/file/6rimevzh3xljmzn/editor_files.zip/file) (Note that these files come from the original editor and are not covered under the LICENSE)

You can only export levels on desktop platforms if the editor detects it's installed within a Re-Volt installation, like so:

Games\Re-Volt  
\-- editor  
\--- trackunit.rtu  
\--- etc  
\-- levels 

Exporting levels to Android works by automatically detecting an RVGL installation.

## The Studio interface

| Area | What it does |
| --- | --- |
| Modules (left) | Every module group from the original module screen, with rendered previews. Categories (straights, curves, dips & humps, banks, structures, start), favorites (star a card), recently placed modules, three preview sizes, and details of the module to place: size, connection points, and every variant to click. Search by name or code name (`bank_10_2_lh`). |
| Outliner (left, second tab) | Every placed block: in track order (following connections from the start grid), by type or by position. Search, click/Shift/Ctrl to select, double-click to frame, lock and hide. |
| Viewport (centre) | Click to place, drag to paint straights, right-drag to orbit, middle-drag to pan, wheel to zoom towards the pointer. Connection markers, a minimap (click to look there), and view presets in the view bar. |
| Inspector (right) | **Place**: module variants, surface, rotation, height, how the module would connect at the cursor. With the select tool: position, level and rotation as numbers, support colors, connections and actions for the selection. **Track**: name, grid size, moving all modules, overview. **History**: every undo step, click to jump. **Issues**: the track check, grouped by severity. |
| Status bar | Tool hint, connection summary, selection, cursor cell and level, grid size, module and pickup counts, save and autosave state. |

Tools: **Place** (1), **Pickups** (2), **Erase** (3), **Select** (4). Alt+click picks the module under
the pointer. Right-click opens a context menu.

**Select tool (4):** click a block to select it, Shift+click to add, Ctrl+click to toggle, or drag a
box over empty space. Drag selected blocks (or the X/Z/level arrows of the gizmo) to move them; arrow
keys, R, PgUp/PgDn, Z/X/T and Del work on the whole selection, and the inspector takes typed
positions and levels. Ctrl+C/X/V copy, cut and paste, Ctrl+D duplicates: the copy follows the pointer
as a 3D preview until you click (R and PgUp/PgDn adjust it, Esc ends). Ctrl+L selects the whole
connected section. Blocks can be locked against changes and hidden from view for the session.
The inspector also sets the color of the **support pillars** under raised blocks (presets or
custom top/bottom colors), which appear in the editor and in the exported track.

**Connections:** the editor works out where pieces join from the link points each module has in
the track unit file, the same points the export uses to follow the lap. Green markers are joined,
amber marks a step in height, red an open end. While placing, the cursor module shows where it
would join or block an opening; **A** turns and raises it to fit the track next to it.

### Race data (F6)

Tracks start out as just a layout. Switch the toolbar to **Race data** to generate the data the
export would create (AI nodes, position nodes, track zones, lights and pickups) and edit it:

- move nodes and their red/green ends, insert and delete nodes (the lap is relinked automatically),
  and set priorities, racing and overtaking lines, wall flags and links
- move and resize zones; change light types, colors, reach and behavior; place any of the game's objects
- place **instances** of imported `.prm` models (shown in the viewport; copied into the level on export)
- add **force fields** (push, wind, spin…; box or sphere; uniform or gradient)

Edited data is exported instead of the generated data (the reversed version stays generated). If the
layout changes afterwards, the editor warns you and offers to regenerate. Instances, force fields and
imported models are kept when you regenerate.

### Default shortcuts

All of these can be changed in *Help → Keyboard Shortcuts & Controls* (F1).

| Action | Keys |
| --- | --- |
| New / Open / Save / Save As | Ctrl+N / Ctrl+O / Ctrl+S / Ctrl+Shift+S |
| Export to Re-Volt | Ctrl+E |
| Undo / Redo | Ctrl+Z / Ctrl+Y (or Ctrl+Shift+Z) |
| Rotate module | R / Shift+R (Space also works) |
| Toggle pickup at cursor | P |
| Fit module to neighbours | A |
| Copy / Cut / Paste / Duplicate | Ctrl+C / Ctrl+X / Ctrl+V / Ctrl+D |
| Select all / Deselect / Invert | Ctrl+A / Ctrl+Shift+A / Ctrl+Shift+I |
| Select connected section | Ctrl+L |
| Hide selected / Show hidden | H / Alt+H |
| Command palette | Ctrl+Shift+P |
| Check track | F7 |
| Race data editing | F6 |
| Select tool | 4 |
| Frame selection / Frame track / Focus cursor | F / Home / Shift+F |
| Top view / Front view / Orthographic | V / Numpad 1 / Numpad 5 |
| Grid | G |
| Modules panel / Inspector / History | Ctrl+B / Ctrl+I / Ctrl+H |
| Preferences | Ctrl+, |
| Full screen | F11 |
| Classic interface | F10 |

The original editing keys keep working in both interfaces: arrow keys move the cursor, Enter places,
Delete deletes, Page Up/Down change the height, Z/X cycle variants, T toggles the surface, C picks
the module under the cursor, and [ and ] rotate the view. Gamepads work as in the classic editor.

## Your work is protected

- **Undo/redo** for every edit, including pickups, resizing, moving the track and renaming.
- **Safe saving**: a track is written to a temporary file and then swapped in, so a failed save
  (full disk, no permission) never destroys the previous version.
- **Validated loading**: a file is checked completely before the open track is replaced. Damaged
  files are reported with a reason instead of crashing the editor, and one damaged file in the
  save folder no longer stops the editor from starting.
- **Autosave and crash recovery**: unsaved changes are written to the user data folder (not your
  track folder) every two minutes by default. If the editor closes unexpectedly, the welcome screen
  (or a prompt in the classic interface) offers to recover them.
- **Unsaved changes prompt** when creating, opening or quitting, including closing the window.
- **Track check**, automatically a moment after each change: start grid, closed lap, racing line,
  open ends, height steps, separate sections, pickup limit, grid size, missing imported models.
  Each issue says what to do and selects the blocks involved when clicked.
- **Export backups**: exporting replaces everything in the level folder, so the folder is copied to
  the user data folder first (`ExportBackups`, the last 5 per track are kept).

Track designs (`.TDF`) stay byte-for-byte compatible with the original editor. Track names are
limited to printable ASCII, which is what the format and the original editor support. Things the
format can't store (support colors, race data) are kept in a small sidecar file next to the track
(`!Name.tdx`) that the original editor and the game ignore.

## Command line

| Option | Effect |
| --- | --- |
| `path\to\track.tdf` | Opens the track on startup (also what happens when a `.tdf` is dropped onto the executable). |
| `-studio` / `-classic` | Starts in that interface for this session. |
| `-unlimited` | Lifts the grid size, height and pickup limits. |
| `-exportscale <number>` | Overrides the export scale. |

Preferences, recent tracks and autosaves are stored in Unity's persistent data folder
(on Windows: `%USERPROFILE%\AppData\LocalLow\Dummiesman\RVTrackEdUnity`).

## Development

Scripts are organised as:

- `Assets/Scripts/Core`: Unity-independent track model, TDF reading/writing, undo history.
- `Assets/Scripts/TrackEditor`: editor logic, edit modes (classic interface), session handling,
  autosave, export.
- `Assets/Scripts/Studio`: the Studio interface (IMGUI, built entirely from code, so the scene and
  the classic UI are untouched).

Without Unity you can still run the core unit tests and check that everything compiles against
Unity 2020.3 (see [Tools/CompileCheck](Tools/CompileCheck/README.md)):

```sh
dotnet run --project Tests/Core.Tests
bash Tools/CompileCheck/fetch.sh && dotnet build Tools/CompileCheck/Game/Assembly-CSharp.csproj
```

CI runs both on every push.

## Platform Support
The editor has been tested on Windows, Linux, and Android. It should also work on Mac. There is currently no Android UI and to use it on Android you must use a gamepad.

## Builds
Pre-built binaries are available on the [Releases](https://github.com/Dummiesman/ReVoltTrackEditor/releases) page

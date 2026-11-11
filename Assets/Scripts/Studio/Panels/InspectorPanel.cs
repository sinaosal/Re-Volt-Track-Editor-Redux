/*
    Re-Volt Track Editor - Unity Edition
    A version of the track editor re-built from the ground up in Unity
    Copyright (C) 2022 Dummiesman

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/

using ReVolt.Track;
using Studio.Icons;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Studio
{
    public enum InspectorTab
    {
        Place,
        Track,
        History,
        Issues
    }

    /// <summary>
    /// Right side panel: placement settings, track properties, undo history, and track problems.
    /// </summary>
    public sealed class InspectorPanel
    {
        private const string NameField = "studio.inspector.name";
        private static readonly string[] TabLabels = { "Place", "Track", "History", "Issues" };

        private readonly StudioShell shell;
        private readonly BlockInspector blockInspector;
        private readonly RaceInspector raceInspector;
        public InspectorTab Tab = InspectorTab.Place;

        private float scroll;
        private int issueFilter; // 0 all, 1 errors, 2 warnings, 3 notes
        private string nameBuffer;
        private bool nameFocused;
        private int pendingWidth = -1, pendingHeight = -1;

        private StudioTheme T => shell.Theme;
        private static TrackEditingMode Editing => TrackEditor.GetEditMode<TrackEditingMode>();

        public InspectorPanel(StudioShell shell)
        {
            this.shell = shell;
            blockInspector = new BlockInspector(shell);
            raceInspector = new RaceInspector(shell);
            if (System.Enum.TryParse(AppSettings.Data.InspectorTab, out InspectorTab saved))
                Tab = saved;
        }

        public void SetTab(InspectorTab tab)
        {
            if (Tab == tab)
                return;
            Tab = tab;
            scroll = 0f;
            AppSettings.Data.InspectorTab = tab.ToString();
        }

        public void Draw(Rect rect)
        {
            SGUI.Fill(rect, Palette.Panel);

            // tabs
            var tabs = new Rect(rect.x + T.S(10f), rect.y + T.S(8f), rect.width - T.S(20f), T.S(28f));
            int issues = shell.LastValidation?.Problems.Count(x => x.Severity != TrackProblemSeverity.Info) ?? 0;
            string[] labels = (string[])TabLabels.Clone();
            bool racing = shell.Race.Active;
            bool selecting = !racing && shell.Viewport.Tool == StudioTool.Select;
            if (racing)
                labels[0] = "Element";
            else if (selecting)
                labels[0] = "Block";
            if (issues > 0)
                labels[3] = $"Issues {issues}";
            var selected = (InspectorTab)SGUI.Segmented(tabs, (int)Tab, labels);
            if (selected != Tab)
                SetTab(selected);

            var body = new Rect(rect.x, tabs.yMax + T.S(10f), rect.width, rect.yMax - tabs.yMax - T.S(10f));
            SGUI.HLine(rect.x, body.y - 1f, rect.width, Palette.Divider);

            switch (Tab)
            {
                case InspectorTab.Place:
                    if (racing)
                        DrawScrolling(body, raceInspector.ContentHeight, raceInspector.Draw);
                    else if (selecting)
                        DrawScrolling(body, blockInspector.ContentHeight, blockInspector.Draw);
                    else
                        DrawScrolling(body, PlaceHeight(), DrawPlace);
                    break;
                case InspectorTab.Track: DrawScrolling(body, TrackHeight(), DrawTrack); break;
                case InspectorTab.History: DrawHistory(body); break;
                case InspectorTab.Issues: DrawIssues(body); break;
            }
        }

        private void DrawScrolling(Rect body, float contentHeight, System.Action<Rect> draw)
        {
            var content = SGUI.BeginScroll(body, ref scroll, contentHeight);
            draw(new Rect(content.x + T.Padding, content.y + T.S(12f), content.width - T.Padding * 2f, contentHeight - T.S(12f)));
            SGUI.EndScroll(body, ref scroll, contentHeight);
        }

        // Layout helpers
        private float Section(ref float y, Rect r, string title)
        {
            SGUI.SectionHeader(new Rect(r.x, y, r.width, T.S(16f)), title);
            y += T.S(24f);
            return y;
        }

        private void Property(ref float y, Rect r, string label, string value, Color? color = null)
        {
            var row = new Rect(r.x, y, r.width, T.S(22f));
            SGUI.Label(row.TakeLeft(T.S(96f)), label, T.Small, Palette.TextFaint);
            var valueRect = row.CutLeft(T.S(96f));
            SGUI.Label(valueRect, SGUI.Ellipsize(value, T.Body, valueRect.width), T.Body, color ?? Palette.Text);
            y += T.S(24f);
        }

        // Place tab
        private float PlaceHeight() => T.S(760f);

        private void DrawPlace(Rect r)
        {
            var editing = Editing;
            int active = editing.ActiveModuleIndex;
            float y = r.y;

            Section(ref y, r, "Module to place");
            var thumbRect = new Rect(r.x, y, T.S(96f), T.S(74f));
            SGUI.RoundedOutline(thumbRect, Palette.Raised, Palette.Border, T.S(6f));
            var thumb = shell.Thumbnails.Get(active);
            if (thumb != null && Event.current.type == EventType.Repaint)
                GUI.DrawTexture(thumbRect.Inset(T.S(1f)), thumb, ScaleMode.ScaleAndCrop, false);
            else
                SGUI.Icon(thumbRect, IconId.Module, Palette.TextFaint);

            var info = new Rect(thumbRect.xMax + T.S(12f), y + T.S(4f), r.width - thumbRect.width - T.S(12f), T.S(20f));
            SGUI.Label(info, SGUI.Ellipsize(ModuleCatalog.GetDisplayName(active), T.BodyStrong, info.width), T.BodyStrong);
            info.y += T.S(20f);
            SGUI.Label(info, ModuleCatalog.GetCodeName(active), T.Mono, Palette.TextFaint);
            info.y += T.S(20f);
            var chain = ModuleCatalog.GetVariantChain(active);
            int index = 0;
            for (int i = 0; i < chain.Count; i++)
                if (chain[i] == active) index = i;
            SGUI.Label(info, chain.Count > 1 ? $"Variant {index + 1} of {chain.Count}" : "No variants", T.Small, Palette.TextDim);
            y += thumbRect.height + T.S(10f);

            // variant / surface buttons
            float bw = (r.width - T.S(8f) * 2f) / 3f;
            var b = new Rect(r.x, y, bw, T.ControlHeight);
            if (SGUI.Button(b, "Previous", ButtonKind.Secondary, chain.Count > 1, IconId.VariantPrev, "Previous variant  Z"))
                editing.PreviousVariant();
            b.x += bw + T.S(8f);
            if (SGUI.Button(b, "Next", ButtonKind.Secondary, chain.Count > 1, IconId.VariantNext, "Next variant  X"))
                editing.NextVariant();
            b.x += bw + T.S(8f);
            bool hasSurface = Modules.Lookup.OtherSurface[active] != active;
            if (SGUI.Button(b, "Surface", ButtonKind.Secondary, hasSurface, IconId.Surface, "Toggle surface  T"))
                editing.ToggleSurface();
            y += T.ControlHeight + T.S(22f);

            // orientation
            Section(ref y, r, "Rotation");
            int rotation = SGUI.Segmented(new Rect(r.x, y, r.width, T.ControlHeight), editing.ModuleRotation, new[] { "0°", "90°", "180°", "270°" }, true,
                                          new[] { "Rotate with R / Space", "Rotate with R / Space", "Rotate with R / Space", "Rotate with R / Space" });
            if (rotation != editing.ModuleRotation)
            {
                editing.ModuleRotation = rotation;
                TrackEditor.PlaySound(TrackEditor.SndRotate);
            }
            y += T.ControlHeight + T.S(22f);

            Section(ref y, r, "Height");
            int steps = editing.CursorHeightSteps;
            int newSteps = SGUI.Stepper(new Rect(r.x, y, T.S(150f), T.ControlHeight), steps, 0, editing.MaxHeightSteps,
                                        $"Level {steps}", true, "Raise / lower with PgUp / PgDn");
            if (newSteps != steps)
                editing.AdjustCursorHeight(newSteps - steps);
            SGUI.Label(new Rect(r.x + T.S(162f), y, r.width - T.S(162f), T.ControlHeight),
                       TrackEditor.UnlimitedMode ? "Unlimited mode" : $"0 – {EditorConstants.MaxElevationSteps}", T.Small, Palette.TextFaint);
            y += T.ControlHeight + T.S(26f);

            // how the module would join the track at the cursor
            Section(ref y, r, "Connections here");
            var fit = shell.Viewport.CurrentFit;
            if (fit == null || fit.Connects + fit.Jumps + fit.Steps + fit.Blocks == 0)
            {
                SGUI.Label(new Rect(r.x, y, r.width, T.S(34f)), "No track next to the cursor. Green markers show where a piece would join existing track.",
                           T.SmallWrapped, Palette.TextFaint);
                y += T.S(38f);
            }
            else
            {
                void FitLine(Color color, string text)
                {
                    SGUI.Rounded(new Rect(r.x, y + T.S(6f), T.S(8f), T.S(8f)), color, T.S(4f));
                    SGUI.Label(new Rect(r.x + T.S(16f), y, r.width - T.S(16f), T.S(20f)), text, T.Small, Palette.TextDim);
                    y += T.S(22f);
                }
                if (fit.Connects > 0) FitLine(Palette.Success, fit.Connects == 1 ? "Joins 1 piece" : $"Joins {fit.Connects} pieces");
                if (fit.Jumps > 0) FitLine(Palette.Info, "Jump landing");
                if (fit.Steps > 0) FitLine(Palette.Warning, fit.Steps == 1 ? "1 step in height" : $"{fit.Steps} steps in height");
                if (fit.Blocks > 0) FitLine(Palette.Error, fit.Blocks == 1 ? "Blocks 1 opening (no link point there)" : $"Blocks {fit.Blocks} openings (no link points there)");
                y += T.S(2f);
            }
            if (SGUI.Button(new Rect(r.x, y, r.width, T.ControlHeight), "Fit to neighbours", ButtonKind.Secondary, true, IconId.Link,
                            "Turn and raise the module so it joins the track around the cursor  A"))
                shell.Viewport.FitModuleToNeighbours();
            y += T.ControlHeight + T.S(22f);

            // cell under the cursor
            Section(ref y, r, "Cursor");
            var cursor = editing.CursorPosition;
            var track = TrackEditor.Track;
            cursor = new Vector2Int(Mathf.Clamp(cursor.x, 0, track.Width - 1), Mathf.Clamp(cursor.y, 0, track.Height - 1));
            var cell = track.GetCell(cursor);
            Property(ref y, r, "Cell", $"{cursor.x}, {cursor.y}");
            if (cell.Module != null)
            {
                var m = cell.Module;
                Property(ref y, r, "Module", ModuleCatalog.GetDisplayName(m.ModuleIndex));
                Property(ref y, r, "Code", ModuleCatalog.GetCodeName(m.ModuleIndex), Palette.TextDim);
                Property(ref y, r, "Orientation", $"{(m.Rotation % 4) * 90}°  ·  level {Mathf.RoundToInt(m.Elevation / EditorConstants.ElevationStep)}");
                Property(ref y, r, "Pickup", cell.HasPickup ? "Yes" : "No");
            }
            else
            {
                Property(ref y, r, "Module", "Empty", Palette.TextFaint);
            }
            y += T.S(6f);

            bool hasModule = cell.Module != null;
            var row = new Rect(r.x, y, bw, T.ControlHeight);
            if (SGUI.Button(row, "Pick", ButtonKind.Secondary, hasModule, IconId.ToolEyedropper, "Use this module's type, rotation and height  C"))
                shell.Viewport.PickModule(cursor);
            row.x += bw + T.S(8f);
            if (SGUI.Button(row, "Delete", ButtonKind.Secondary, hasModule, IconId.Trash, "Delete the module under the cursor  Del"))
                shell.Viewport.EraseAt(cursor);
            row.x += bw + T.S(8f);
            if (SGUI.Button(row, "Pickup", ButtonKind.Secondary, hasModule, IconId.ToolPickup, "Add or remove a pickup on this cell  P"))
                shell.Viewport.SetPickup(cursor, !cell.HasPickup, true);
        }

        // Track tab
        private float TrackHeight() => T.S(980f);

        private void DrawTrack(Rect r)
        {
            var track = TrackEditor.Track;
            float y = r.y;

            Section(ref y, r, "Track");
            SGUI.Label(new Rect(r.x, y, r.width, T.S(18f)), "Name", T.Small, Palette.TextFaint);
            y += T.S(20f);

            // the field edits a buffer; the rename is committed (as one undo step) on Enter or when focus leaves
            bool focused = SGUI.IsFocused(NameField);
            if (!focused)
            {
                if (nameFocused)
                    CommitName();
                nameBuffer = track.Name;
            }
            nameFocused = focused;

            var nameRect = new Rect(r.x, y, r.width, T.S(30f));
            if (SGUI.SubmitPressed(NameField))
            {
                CommitName();
                GUIUtility.keyboardControl = 0;
                Event.current.Use();
            }
            nameBuffer = SGUI.TextField(nameRect, nameBuffer, NameField, "Untitled", TdfDocument.MaxNameLength, null, TdfDocument.IsValidNameChar);
            y += T.S(38f);

            string file = TrackEditor.CurrentFilePath != null ? Path.GetFileName(TrackEditor.CurrentFilePath) : "Not saved yet";
            var fileRow = new Rect(r.x, y, r.width, T.S(22f));
            SGUI.Label(fileRow.TakeLeft(T.S(96f)), "File", T.Small, Palette.TextFaint);
            var fileValue = fileRow.CutLeft(T.S(96f)).CutRight(T.S(26f));
            SGUI.Label(fileValue, SGUI.Ellipsize(file, T.Body, fileValue.width), T.Body, TrackEditor.CurrentFilePath != null ? Palette.Text : Palette.TextFaint);
            if (TrackEditor.CurrentFilePath != null && SGUI.IconButton(fileRow.TakeRight(T.S(24f)), IconId.Folder, "Show in folder"))
                StudioShell.RevealFolder(Path.GetDirectoryName(TrackEditor.CurrentFilePath));
            y += T.S(24f);
            Property(ref y, r, "Status", track.IsDirty ? "Unsaved changes" : "Saved", track.IsDirty ? Palette.Warning : Palette.Success);
            y += T.S(14f);

            // grid size
            Section(ref y, r, "Grid size");
            GetModuleBounds(out var minPos, out var maxPos, out bool any);
            int limitMin = TrackEditor.UnlimitedMode ? 3 : EditorConstants.MinGridSize;
            int limitMax = TrackEditor.UnlimitedMode ? ushort.MaxValue : EditorConstants.MaxGridSize;
            int minW = Mathf.Max(any ? maxPos.x : 0, Mathf.Min(limitMin, track.Width));
            int minH = Mathf.Max(any ? maxPos.y : 0, Mathf.Min(limitMin, track.Height));
            int maxW = Mathf.Max(limitMax, track.Width);
            int maxH = Mathf.Max(limitMax, track.Height);

            if (pendingWidth < 0) pendingWidth = track.Width;
            if (pendingHeight < 0) pendingHeight = track.Height;

            float half = (r.width - T.S(10f)) * 0.5f;
            SGUI.Label(new Rect(r.x, y, half, T.S(18f)), "Width", T.Small, Palette.TextFaint);
            SGUI.Label(new Rect(r.x + half + T.S(10f), y, half, T.S(18f)), "Height", T.Small, Palette.TextFaint);
            y += T.S(20f);
            pendingWidth = SGUI.Stepper(new Rect(r.x, y, half, T.ControlHeight), pendingWidth, minW, maxW);
            pendingHeight = SGUI.Stepper(new Rect(r.x + half + T.S(10f), y, half, T.ControlHeight), pendingHeight, minH, maxH);
            y += T.ControlHeight + T.S(8f);

            bool sizeChanged = pendingWidth != track.Width || pendingHeight != track.Height;
            string limits = TrackEditor.UnlimitedMode ? "Unlimited mode" : $"{EditorConstants.MinGridSize}–{EditorConstants.MaxGridSize} cells";
            string hint = any ? $"{limits} · modules need at least {maxPos.x} × {maxPos.y}" : limits;
            SGUI.Label(new Rect(r.x, y, r.width, T.S(18f)), hint, T.Small, Palette.TextFaint);
            y += T.S(24f);

            if (sizeChanged)
            {
                var apply = new Rect(r.x, y, half, T.ControlHeight);
                if (SGUI.Button(apply, $"Apply {pendingWidth} × {pendingHeight}", ButtonKind.Primary))
                    ApplyResize(pendingWidth, pendingHeight);
                if (SGUI.Button(new Rect(r.x + half + T.S(10f), y, half, T.ControlHeight), "Reset"))
                {
                    pendingWidth = track.Width;
                    pendingHeight = track.Height;
                }
                y += T.ControlHeight + T.S(8f);
            }
            else
            {
                pendingWidth = track.Width;
                pendingHeight = track.Height;
            }
            y += T.S(14f);

            // shifting all modules
            Section(ref y, r, "Move all modules");
            float bs = T.ControlHeight;
            var pad = new Rect(r.x, y, bs * 3f + T.S(8f), bs * 2f + T.S(4f));
            var up = new Rect(pad.x + bs + T.S(4f), pad.y, bs, bs);
            var left = new Rect(pad.x, pad.y + bs + T.S(4f), bs, bs);
            var down = new Rect(up.x, left.y, bs, bs);
            var right = new Rect(up.xMax + T.S(4f), left.y, bs, bs);
            DirectionButton(up, IconId.ChevronUp, new Vector2Int(0, 1), "Move north", any, minPos, maxPos);
            DirectionButton(left, IconId.ChevronLeft, new Vector2Int(-1, 0), "Move west", any, minPos, maxPos);
            DirectionButton(down, IconId.ChevronDown, new Vector2Int(0, -1), "Move south", any, minPos, maxPos);
            DirectionButton(right, IconId.ChevronRight, new Vector2Int(1, 0), "Move east", any, minPos, maxPos);
            SGUI.Label(new Rect(pad.xMax + T.S(14f), pad.y, r.width - pad.width - T.S(14f), pad.height),
                       any ? "Shifts every module and pickup by one cell." : "Place modules first.", T.SmallWrapped, Palette.TextFaint);
            y += pad.height + T.S(22f);

            // overview
            Section(ref y, r, "Overview");
            var placements = track.GetAllModuleRootPlacements().ToList();
            int modules = placements.Count;
            int starts = placements.Count(x => x.ModuleIndex == (int)Modules.ID.TWM_START);
            int pickups = track.Cells.Count(x => x.HasPickup);
            int types = placements.Select(x => x.ModuleIndex).Distinct().Count();
            int cells = placements.Sum(x => x.TouchedCells.Count);
            Property(ref y, r, "Modules", modules == 0 ? "None" : $"{modules}  ·  {types} type{(types == 1 ? "" : "s")}  ·  {cells} cells");
            Property(ref y, r, "Start grids", starts.ToString(), starts == 1 ? (Color?)null : Palette.Warning);
            Property(ref y, r, "Pickups", TrackEditor.UnlimitedMode ? pickups.ToString() : $"{pickups} / {TrackValidator.MaxPickups}",
                     pickups > TrackValidator.MaxPickups ? Palette.Warning : (Color?)null);
            if (any)
            {
                Property(ref y, r, "Footprint", $"{maxPos.x - minPos.x} × {maxPos.y - minPos.y} cells (from {minPos.x}, {minPos.y})");
                int low = placements.Min(x => BlockOperations.LevelOf(x));
                int high = placements.Max(x => BlockOperations.LevelOf(x));
                Property(ref y, r, "Heights", low == high ? $"Level {low}" : $"Levels {low} – {high}");
            }

            var connections = ConnectivityService.Current(shell.TrackRevision);
            if (connections != null && modules > 0)
            {
                Property(ref y, r, "Connections", $"{connections.ConnectionCount} joined  ·  {connections.JumpCount} jumps",
                         null);
                Property(ref y, r, "Open ends", connections.OpenCount.ToString(), connections.OpenCount > 0 ? Palette.Warning : (Color?)null);
                Property(ref y, r, "Height steps", connections.StepCount.ToString(), connections.StepCount > 0 ? Palette.Warning : (Color?)null);
                Property(ref y, r, "Sections", connections.ComponentCount == 1 ? "1 (everything is joined)" : $"{connections.ComponentCount} separate parts",
                         connections.ComponentCount > 1 ? Palette.Warning : (Color?)null);
            }

            var check = shell.LastValidation;
            if (check != null)
            {
                int errors = check.Problems.Count(x => x.Severity == TrackProblemSeverity.Error);
                int warnings = check.Problems.Count(x => x.Severity == TrackProblemSeverity.Warning);
                string state = errors > 0 ? $"{errors} error{(errors == 1 ? "" : "s")}, {warnings} warning{(warnings == 1 ? "" : "s")}"
                             : warnings > 0 ? $"{warnings} warning{(warnings == 1 ? "" : "s")}" : "No problems";
                Property(ref y, r, "Check", shell.ValidationIsStale ? state + " (outdated)" : state,
                         errors > 0 ? Palette.Error : warnings > 0 ? Palette.Warning : Palette.Success);
                if (check.LapCloses)
                {
                    // export units: a cell is 1000 game units at regular scale
                    Property(ref y, r, "Lap", $"{check.LapLength / 1000f:0.#} cells long  ·  {(check.ReverseAvailable ? "reverse possible" : "no reverse")}");
                }
            }
            y += T.S(10f);

            var actions = new Rect(r.x, y, half, T.ControlHeight);
            if (SGUI.Button(actions, "Check", ButtonKind.Secondary, true, IconId.Check, "Check that the track forms a valid lap  F7"))
                shell.RunValidation(true);
            actions.x += half + T.S(10f);
            if (SGUI.Button(actions, "Export…", ButtonKind.Primary, true, IconId.Export, "Export to Re-Volt  Ctrl+E"))
                shell.ShowExportDialog();
        }

        private void CommitName()
        {
            if (nameBuffer != null && TdfDocument.SanitizeName(nameBuffer) != TrackEditor.Track.Name)
                TrackEditor.RenameTrack(nameBuffer);
        }

        private static void GetModuleBounds(out Vector2Int min, out Vector2Int max, out bool any)
        {
            min = new Vector2Int(int.MaxValue, int.MaxValue);
            max = new Vector2Int(int.MinValue, int.MinValue);
            any = false;
            foreach (var cell in TrackEditor.Track.Cells)
            {
                if (cell.Module == null)
                    continue;
                any = true;
                min = Vector2Int.Min(min, cell.Position);
                max = Vector2Int.Max(max, cell.Position + Vector2Int.one);
            }
            if (!any)
            {
                min = Vector2Int.zero;
                max = Vector2Int.zero;
            }
        }

        private void DirectionButton(Rect r, IconId icon, Vector2Int direction, string tooltip, bool any, Vector2Int minPos, Vector2Int maxPos)
        {
            var track = TrackEditor.Track;
            var size = new Vector2Int(track.Width, track.Height);
            bool possible = any && minPos.x + direction.x >= 0 && minPos.y + direction.y >= 0
                            && maxPos.x + direction.x <= size.x && maxPos.y + direction.y <= size.y;

            if (IconButtonOutlined(r, icon, possible ? tooltip : "No room to move further", possible))
            {
                TrackEditor.RecordChange("Move track", () =>
                {
                    TrackEditor.AdjustTrack(size, direction);
                    track.MarkDirty();
                });
                Editing.CursorPosition += direction;
                TrackEditor.PlaySound(TrackEditor.SndAdjust);
            }
        }

        private bool IconButtonOutlined(Rect r, IconId icon, string tooltip, bool enabled)
        {
            SGUI.RoundedOutline(r, Palette.Raised, Palette.Border);
            return SGUI.IconButton(r, icon, tooltip, false, enabled);
        }

        private void ApplyResize(int width, int height)
        {
            var track = TrackEditor.Track;
            GetModuleBounds(out _, out var maxPos, out bool any);
            if (any && (width < maxPos.x || height < maxPos.y))
            {
                TrackEditor.PlaySound(TrackEditor.SndWarning);
                return;
            }

            TrackEditor.RecordChange($"Resize to {width} × {height}", () =>
            {
                TrackEditor.SetTrackSize(width, height);
                track.MarkDirty();
            });
            Editing.CursorPosition = Editing.CursorPosition; // re-clamp into the new grid
            shell.Viewport.FrameTrack();
            TrackEditor.PlaySound(TrackEditor.SndAdjust);
        }

        // History tab
        private void DrawHistory(Rect body)
        {
            var history = TrackEditor.History;
            if (history == null)
                return;

            var footer = body.TakeBottom(T.S(52f));
            var listRect = body.CutBottom(footer.height);

            var undo = history.UndoEntries;
            var redo = history.RedoEntries;
            int rows = 1 + undo.Count + redo.Count;
            float rowHeight = T.S(30f);
            float contentHeight = rows * rowHeight + T.S(12f);
            int current = undo.Count;

            var content = SGUI.BeginScroll(listRect, ref scroll, contentHeight);
            for (int i = 0; i < rows; i++)
            {
                string label;
                if (i == 0)
                    label = TrackEditor.CurrentFilePath != null && undo.Count + redo.Count > 0 ? "Opened track" : "Start";
                else if (i <= undo.Count)
                    label = undo[i - 1].Label;
                else
                    label = redo[redo.Count - 1 - (i - undo.Count - 1)].Label;

                var row = new Rect(content.x + T.S(8f), content.y + T.S(6f) + i * rowHeight, content.width - T.S(16f), rowHeight);
                bool isCurrent = i == current;
                bool isFuture = i > current;

                if (SGUI.RowBehavior(row, !isCurrent, out bool hover, out _))
                {
                    history.JumpTo(i);
                    TrackEditor.PlaySound(TrackEditor.SndMenuMove);
                }
                if (row.yMax < 0f || row.y > listRect.height)
                    continue;

                if (isCurrent)
                    SGUI.Rounded(row, Palette.AccentSoft, T.S(4f));
                else if (hover)
                    SGUI.Rounded(row, Palette.Hover, T.S(4f));

                var dot = new Rect(row.x + T.S(10f), row.center.y - T.S(4f), T.S(8f), T.S(8f));
                SGUI.Rounded(dot, isCurrent ? Palette.Accent : isFuture ? Palette.BorderStrong : Palette.TextFaint, T.S(4f));
                if (i < rows - 1)
                    SGUI.VLine(dot.center.x - 0.5f, dot.yMax + T.S(3f), rowHeight - T.S(14f), Palette.Border);

                SGUI.Label(new Rect(row.x + T.S(28f), row.y, row.width - T.S(36f), row.height),
                           SGUI.Ellipsize(label, T.Body, row.width - T.S(36f)), isCurrent ? T.BodyStrong : T.Body,
                           isFuture ? Palette.TextFaint : Palette.Text);
            }
            SGUI.EndScroll(listRect, ref scroll, contentHeight);

            // footer
            SGUI.HLine(footer.x, footer.y, footer.width, Palette.Divider);
            float bw = (footer.width - T.Padding * 2f - T.S(8f)) * 0.5f;
            var b = new Rect(footer.x + T.Padding, footer.y + T.S(12f), bw, T.ControlHeight);
            if (SGUI.Button(b, "Undo", ButtonKind.Secondary, history.CanUndo, IconId.Undo, history.UndoLabel != null ? $"Undo {history.UndoLabel}  Ctrl+Z" : null))
                TrackEditor.Undo();
            b.x += bw + T.S(8f);
            if (SGUI.Button(b, "Redo", ButtonKind.Secondary, history.CanRedo, IconId.Redo, history.RedoLabel != null ? $"Redo {history.RedoLabel}  Ctrl+Y" : null))
                TrackEditor.Redo();
        }

        // Issues tab
        private static readonly string[] SeverityHeadings = { "Errors", "Warnings", "Notes" };

        private void DrawIssues(Rect body)
        {
            var result = shell.LastValidation;
            var footer = body.TakeBottom(T.S(58f));
            var listRect = body.CutBottom(footer.height);

            if (result == null)
            {
                var inner = listRect.Inset(T.Padding, T.S(12f));
                SGUI.Icon(new Rect(inner.x, inner.y + T.S(30f), inner.width, T.S(28f)), IconId.Check, Palette.TextFaint, T.S(26f));
                SGUI.Label(new Rect(inner.x, inner.y + T.S(64f), inner.width, T.S(20f)), "Not checked yet", T.BodyStrong, Palette.TextDim, TextAnchor.MiddleCenter);
                SGUI.Label(new Rect(inner.x, inner.y + T.S(88f), inner.width, T.S(60f)),
                           AppSettings.Data.AutoCheck
                               ? "The track is checked automatically a moment after each change: start grid, closed lap, connections, heights and export limits."
                               : "Check the track to find missing start grids, gaps in the lap, open ends and other problems before exporting.",
                           T.SmallWrapped, Palette.TextFaint, TextAnchor.UpperCenter);
            }
            else
            {
                // summary and filter
                int errors = result.Problems.Count(x => x.Severity == TrackProblemSeverity.Error);
                int warnings = result.Problems.Count(x => x.Severity == TrackProblemSeverity.Warning);
                int notes = result.Problems.Count(x => x.Severity == TrackProblemSeverity.Info);
                var filterRect = new Rect(listRect.x + T.Padding, listRect.y + T.S(10f), listRect.width - T.Padding * 2f, T.S(26f));
                issueFilter = SGUI.Segmented(filterRect, issueFilter,
                    new[] { $"All {result.Problems.Count}", $"Errors {errors}", $"Warn. {warnings}", $"Notes {notes}" }, true,
                    new[] { "Everything the check found", "Problems that stop the export", "Likely problems worth a look", "Information, often fine as it is" });

                var visible = result.Problems.Where(x => issueFilter == 0 || (int)x.Severity == issueFilter - 1).ToList();
                var scrollRect = new Rect(listRect.x, filterRect.yMax + T.S(8f), listRect.width, listRect.yMax - filterRect.yMax - T.S(8f));
                float width = scrollRect.width - T.Padding * 2f - T.S(8f);

                // measure
                float contentHeight = T.S(8f);
                if (shell.ValidationIsStale)
                    contentHeight += T.S(26f);
                if (result.Problems.Count == 0)
                    contentHeight += T.S(64f);
                TrackProblemSeverity? lastSeverity = null;
                foreach (var problem in visible)
                {
                    if (problem.Severity != lastSeverity)
                    {
                        contentHeight += T.S(28f);
                        lastSeverity = problem.Severity;
                    }
                    contentHeight += IssueHeight(problem, width) + T.S(8f);
                }

                var content = SGUI.BeginScroll(scrollRect, ref scroll, contentHeight);
                float x = content.x + T.Padding;
                float y = content.y + T.S(4f);
                if (shell.ValidationIsStale)
                {
                    SGUI.Label(new Rect(x, y, width, T.S(18f)), "The track changed since this check.", T.Small, Palette.TextFaint);
                    y += T.S(26f);
                }
                if (result.Problems.Count == 0)
                {
                    var ok = new Rect(x, y, width, T.S(56f));
                    SGUI.RoundedOutline(ok, Palette.Raised, Palette.Border, T.S(6f));
                    SGUI.Icon(new Rect(ok.x + T.S(10f), ok.y + T.S(10f), T.S(18f), T.S(18f)), IconId.Success, Palette.Success, T.S(16f));
                    SGUI.Label(new Rect(ok.x + T.S(36f), ok.y + T.S(8f), ok.width - T.S(44f), T.S(20f)), "Ready to export", T.SmallStrong, Palette.Success);
                    SGUI.Label(new Rect(ok.x + T.S(36f), ok.y + T.S(28f), ok.width - T.S(46f), T.S(24f)),
                               result.ReverseAvailable ? "The lap is valid in both directions and every piece is joined." : "The lap is valid.", T.SmallWrapped, Palette.TextDim);
                }

                lastSeverity = null;
                foreach (var problem in visible)
                {
                    if (problem.Severity != lastSeverity)
                    {
                        lastSeverity = problem.Severity;
                        int n = visible.Count(v => v.Severity == problem.Severity);
                        SGUI.SectionHeader(new Rect(x, y + T.S(6f), width, T.S(16f)), $"{SeverityHeadings[(int)problem.Severity]}  ·  {n}");
                        y += T.S(28f);
                    }
                    float h = IssueHeight(problem, width);
                    DrawIssueRow(new Rect(x, y, width, h), problem);
                    y += h + T.S(8f);
                }
                SGUI.EndScroll(scrollRect, ref scroll, contentHeight);
            }

            SGUI.HLine(footer.x, footer.y, footer.width, Palette.Divider);
            var b = new Rect(footer.x + T.Padding, footer.y + T.S(8f), footer.width - T.Padding * 2f, T.ControlHeight);
            if (SGUI.Button(b, result == null ? "Check track" : "Check again", ButtonKind.Secondary, true, IconId.Check, "F7"))
                shell.RunValidation(true);
            string status = AppSettings.Data.AutoCheck
                ? (result != null ? $"Checked automatically after changes · {result.Milliseconds:0} ms" : "Checked automatically after changes")
                : "Automatic checks are off (Track menu)";
            SGUI.Label(new Rect(b.x, b.yMax + T.S(2f), b.width, T.S(16f)), status, T.Caption, Palette.TextFaint, TextAnchor.MiddleCenter);
        }

        private float IssueHeight(TrackProblem problem, float width)
        {
            float text = width - T.S(46f);
            float h = T.S(30f) + SGUI.WrappedHeight(problem.Message ?? string.Empty, T.SmallWrapped, text);
            if (!string.IsNullOrEmpty(problem.Suggestion))
                h += T.S(4f) + SGUI.WrappedHeight("→ " + problem.Suggestion, T.SmallWrapped, text);
            if (problem.Cell.HasValue)
                h += T.S(20f);
            return Mathf.Max(T.S(48f), h + T.S(8f));
        }

        private void DrawIssueRow(Rect r, TrackProblem problem)
        {
            var (icon, color) = StudioShell.ProblemStyle(problem);
            bool navigable = problem.Cell.HasValue;
            bool clicked = false;
            bool hover = false;
            if (navigable)
                clicked = SGUI.ClickBehavior(r, true, out hover, out _);

            SGUI.RoundedOutline(r, hover ? Palette.Hover : Palette.Raised, Palette.Border, T.S(6f));
            SGUI.Fill(new Rect(r.x, r.y + T.S(6f), T.S(3f), r.height - T.S(12f)), color.WithAlpha(0.8f));
            SGUI.Icon(new Rect(r.x + T.S(12f), r.y + T.S(9f), T.S(18f), T.S(18f)), icon, color, T.S(16f));

            float textX = r.x + T.S(38f);
            float textWidth = r.width - T.S(46f);
            string title = problem.Title ?? (problem.Severity == TrackProblemSeverity.Error ? "Error" : problem.Severity == TrackProblemSeverity.Warning ? "Warning" : "Note");
            float sourceWidth = string.IsNullOrEmpty(problem.Source) ? 0f : SGUI.BadgeWidth(problem.Source);
            SGUI.Label(new Rect(textX, r.y + T.S(8f), textWidth - sourceWidth - T.S(6f), T.S(20f)),
                       SGUI.Ellipsize(title, T.SmallStrong, textWidth - sourceWidth - T.S(6f)), T.SmallStrong, color);
            if (sourceWidth > 0f)
                SGUI.Badge(new Rect(r.xMax - sourceWidth - T.S(8f), r.y + T.S(8f), sourceWidth, T.S(18f)), problem.Source, Palette.Chrome, Palette.TextFaint);

            float y = r.y + T.S(28f);
            float h = SGUI.WrappedHeight(problem.Message ?? string.Empty, T.SmallWrapped, textWidth);
            SGUI.Label(new Rect(textX, y, textWidth, h), problem.Message, T.SmallWrapped, Palette.TextDim);
            y += h;
            if (!string.IsNullOrEmpty(problem.Suggestion))
            {
                y += T.S(4f);
                string suggestion = "→ " + problem.Suggestion;
                float sh = SGUI.WrappedHeight(suggestion, T.SmallWrapped, textWidth);
                SGUI.Label(new Rect(textX, y, textWidth, sh), suggestion, T.SmallWrapped, Palette.TextFaint);
                y += sh;
            }
            if (navigable)
            {
                var cell = problem.Cell.Value;
                int related = problem.RelatedCells?.Count ?? 0;
                string where = related > 1 ? $"Cell {cell.x}, {cell.y}  ·  selects {related} blocks" : $"Cell {cell.x}, {cell.y}  ·  show";
                SGUI.Label(new Rect(textX, y + T.S(2f), textWidth, T.S(16f)), where, T.Caption, hover ? Palette.Accent : Palette.TextFaint);
                SGUI.Tooltip(r, "Show on the track");
            }
            if (clicked)
                shell.FocusProblem(problem);
        }
    }
}

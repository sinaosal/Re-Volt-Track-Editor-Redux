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

using Studio.Icons;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Studio
{
    /// <summary>Menu bar, toolbar, and status bar.</summary>
    public sealed partial class StudioShell
    {
        private static readonly string[] MenuTitles = { "File", "Edit", "View", "Track", "Help" };
        private readonly Rect[] menuTitleRects = new Rect[MenuTitles.Length];

        // Menu bar
        private void DrawMenuBar(Rect bar)
        {
            var T = Theme;
            SGUI.Fill(bar, Palette.Chrome);
            SGUI.HLine(bar.x, bar.yMax - 1f, bar.width, Palette.Border);

            var mark = new Rect(bar.x + T.S(10f), bar.y + (bar.height - T.S(18f)) * 0.5f, T.S(18f), T.S(18f)).Snap();
            DrawAppMark(T, mark);

            float x = mark.xMax + T.S(8f);
            for (int i = 0; i < MenuTitles.Length; i++)
            {
                float width = SGUI.TextWidth(MenuTitles[i], T.Body) + T.S(20f);
                var r = new Rect(x, bar.y + T.S(3f), width, bar.height - T.S(6f)).Snap();
                menuTitleRects[i] = r;
                x += width;

                bool open = menuBarIndex == i && menu != null;
                if (SGUI.ClickBehavior(r, true, out bool hover, out bool pressed) && menu == null)
                    OpenMenuBarMenu(i);

                if (open)
                    SGUI.Rounded(r, Palette.Pressed, T.S(4f));
                else if (hover)
                    SGUI.Rounded(r, Palette.Hover, T.S(4f));
                SGUI.Label(r, MenuTitles[i], T.Body, open || hover ? Palette.Text : Palette.TextDim, TextAnchor.MiddleCenter);
            }

            // document title in the middle
            var track = TrackEditor.Track;
            string name = string.IsNullOrEmpty(track.Name) ? "Untitled" : track.Name;
            string file = TrackEditor.CurrentFilePath != null ? Path.GetFileName(TrackEditor.CurrentFilePath) : "not saved";
            string title = $"{name}  —  {file}";
            float titleWidth = SGUI.TextWidth(title, T.Small);
            float center = bar.center.x;
            float available = Mathf.Max(0f, Mathf.Min(center - x, bar.xMax - T.S(140f) - center) * 2f - T.S(40f));
            if (available > T.S(80f))
            {
                string shown = SGUI.Ellipsize(title, T.Small, available);
                float shownWidth = Mathf.Min(titleWidth, available);
                var titleRect = new Rect(center - shownWidth * 0.5f, bar.y, shownWidth + T.S(2f), bar.height);
                SGUI.Label(titleRect, shown, T.Small, Palette.TextDim);
                if (track.IsDirty)
                {
                    float d = T.S(7f);
                    SGUI.Rounded(new Rect(titleRect.x - d - T.S(8f), bar.center.y - d * 0.5f, d, d), Palette.Accent, d * 0.5f);
                    SGUI.Tooltip(new Rect(titleRect.x - T.S(20f), bar.y, T.S(20f), bar.height), "Unsaved changes");
                }
            }

            // interface switch, right side
            float switchWidth = SGUI.TextWidth("Classic", T.Small) + T.S(40f);
            var classic = new Rect(bar.xMax - switchWidth - T.S(8f), bar.y + T.S(4f), switchWidth, bar.height - T.S(8f)).Snap();
            if (SGUI.ClickBehavior(classic, true, out bool chover, out _))
                Defer(() => TrackEditor.Instance.ToggleInterface());
            if (chover)
                SGUI.Rounded(classic, Palette.Hover, T.S(4f));
            SGUI.Icon(new Rect(classic.x + T.S(8f), classic.y, T.S(16f), classic.height), IconId.Layout, chover ? Palette.Text : Palette.TextFaint, T.S(14f));
            SGUI.Label(new Rect(classic.x + T.S(28f), classic.y, classic.width - T.S(28f), classic.height), "Classic", T.Small, chover ? Palette.Text : Palette.TextFaint);
            SGUI.Tooltip(classic, "Switch to the classic interface  F10");
        }

        private void OpenMenuBarMenu(int index)
        {
            var r = menuTitleRects[index];
            OpenMenu(BuildMenu(index), new Vector2(r.x, r.yMax + Theme.S(4f)), index);
        }

        /// <summary>While a menu bar menu is open: hovering another title switches menus, clicking a title closes.</summary>
        private void HandleMenuBarWhileOpen()
        {
            var e = Event.current;
            for (int i = 0; i < menuTitleRects.Length; i++)
            {
                if (!menuTitleRects[i].Contains(e.mousePosition))
                    continue;

                if (e.type == EventType.MouseDown)
                {
                    if (i == menuBarIndex)
                        CloseMenu();
                    else
                        OpenMenuBarMenu(i);
                    e.Use();
                }
                else if ((e.type == EventType.MouseMove || e.type == EventType.Repaint || e.type == EventType.MouseDrag) && i != menuBarIndex)
                {
                    OpenMenuBarMenu(i);
                }
                return;
            }

            // left/right arrows move between menus
            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.LeftArrow || e.keyCode == KeyCode.RightArrow))
            {
                int next = (menuBarIndex + (e.keyCode == KeyCode.RightArrow ? 1 : -1) + MenuTitles.Length) % MenuTitles.Length;
                OpenMenuBarMenu(next);
                e.Use();
            }
        }

        private List<MenuItem> BuildMenu(int index)
        {
            var c = Commands;
            var items = new List<MenuItem>();
            switch (index)
            {
                case 0: // File
                    items.Add(MenuItem.Command(c.Get("file.new")));
                    items.Add(MenuItem.Command(c.Get("file.open")));
                    items.Add(MenuItem.Sub("Open Recent", BuildRecentMenu(), IconId.Recent));
                    items.Add(MenuItem.Separator());
                    items.Add(MenuItem.Command(c.Get("file.save")));
                    items.Add(MenuItem.Command(c.Get("file.saveAs")));
                    items.Add(MenuItem.Separator());
                    items.Add(MenuItem.Command(c.Get("file.export")));
                    items.Add(MenuItem.Command(c.Get("help.folder")));
                    items.Add(MenuItem.Separator());
                    items.Add(MenuItem.Command(c.Get("app.preferences")));
                    items.Add(MenuItem.Separator());
                    items.Add(MenuItem.Command(c.Get("app.quit")));
                    break;

                case 1: // Edit
                    var history = TrackEditor.History;
                    items.Add(MenuItem.Command(c.Get("edit.undo"), history?.UndoLabel != null ? $"Undo {history.UndoLabel}" : "Undo"));
                    items.Add(MenuItem.Command(c.Get("edit.redo"), history?.RedoLabel != null ? $"Redo {history.RedoLabel}" : "Redo"));
                    items.Add(MenuItem.Separator());
                    items.Add(MenuItem.Command(c.Get("edit.cut")));
                    items.Add(MenuItem.Command(c.Get("edit.copy")));
                    items.Add(MenuItem.Command(c.Get("edit.paste")));
                    items.Add(MenuItem.Command(c.Get("edit.duplicate")));
                    items.Add(MenuItem.Separator());
                    items.Add(MenuItem.Sub("Select", new List<MenuItem>
                    {
                        MenuItem.Command(c.Get("edit.selectAll")),
                        MenuItem.Command(c.Get("edit.selectNone")),
                        MenuItem.Command(c.Get("edit.invertSelection")),
                        MenuItem.Separator(),
                        MenuItem.Command(c.Get("edit.selectConnected")),
                        MenuItem.Command(c.Get("edit.selectSameType")),
                    }, IconId.Cursor));
                    items.Add(MenuItem.Sub("Lock and hide", new List<MenuItem>
                    {
                        MenuItem.Command(c.Get("edit.lock")),
                        MenuItem.Command(c.Get("edit.unlockAll")),
                        MenuItem.Separator(),
                        MenuItem.Command(c.Get("edit.hide")),
                        MenuItem.Command(c.Get("edit.showAll")),
                    }, IconId.Lock));
                    items.Add(MenuItem.Separator());
                    items.Add(MenuItem.Command(c.Get("edit.place")));
                    items.Add(MenuItem.Command(c.Get("edit.pick")));
                    items.Add(MenuItem.Command(c.Get("edit.delete")));
                    items.Add(MenuItem.Command(c.Get("edit.pickup")));
                    items.Add(MenuItem.Separator());
                    items.Add(MenuItem.Command(c.Get("place.rotateCW")));
                    items.Add(MenuItem.Command(c.Get("place.rotateCCW")));
                    items.Add(MenuItem.Command(c.Get("place.raise")));
                    items.Add(MenuItem.Command(c.Get("place.lower")));
                    items.Add(MenuItem.Command(c.Get("place.prevVariant")));
                    items.Add(MenuItem.Command(c.Get("place.nextVariant")));
                    items.Add(MenuItem.Command(c.Get("place.surface")));
                    items.Add(MenuItem.Command(c.Get("place.fit")));
                    items.Add(MenuItem.Separator());
                    items.Add(MenuItem.Command(c.Get("view.palette")));
                    break;

                case 2: // View
                    items.Add(MenuItem.Command(c.Get("tool.place")));
                    items.Add(MenuItem.Command(c.Get("tool.pickups")));
                    items.Add(MenuItem.Command(c.Get("tool.erase")));
                    items.Add(MenuItem.Command(c.Get("tool.select")));
                    items.Add(MenuItem.Separator());
                    items.Add(MenuItem.Sub("Camera", new List<MenuItem>
                    {
                        MenuItem.Command(c.Get("view.perspective")),
                        MenuItem.Command(c.Get("view.top")),
                        MenuItem.Command(c.Get("view.front")),
                        MenuItem.Command(c.Get("view.side")),
                        MenuItem.Command(c.Get("view.ortho")),
                        MenuItem.Separator(),
                        MenuItem.Command(c.Get("view.orbitLeft")),
                        MenuItem.Command(c.Get("view.orbitRight")),
                        MenuItem.Command(c.Get("view.reset")),
                        MenuItem.Command(c.Get("view.zoomToPointer")),
                    }, IconId.ViewPerspective));
                    items.Add(MenuItem.Command(c.Get("view.frame")));
                    items.Add(MenuItem.Command(c.Get("view.frameAll")));
                    items.Add(MenuItem.Command(c.Get("view.focusCursor")));
                    items.Add(MenuItem.Separator());
                    items.Add(MenuItem.Command(c.Get("view.grid")));
                    items.Add(MenuItem.Command(c.Get("view.connections")));
                    items.Add(MenuItem.Command(c.Get("view.minimap")));
                    items.Add(MenuItem.Command(c.Get("view.pickups")));
                    items.Add(MenuItem.Command(c.Get("view.footprint")));
                    items.Add(MenuItem.Command(c.Get("view.follow")));
                    items.Add(MenuItem.Separator());
                    items.Add(MenuItem.Command(c.Get("view.palette")));
                    items.Add(MenuItem.Command(c.Get("view.outliner")));
                    items.Add(MenuItem.Command(c.Get("view.library")));
                    items.Add(MenuItem.Command(c.Get("view.inspector")));
                    items.Add(MenuItem.Command(c.Get("view.statusbar")));
                    items.Add(MenuItem.Separator());
                    items.Add(MenuItem.Command(c.Get("view.fullscreen")));
                    items.Add(MenuItem.Command(c.Get("view.classic")));
                    break;

                case 3: // Track
                    items.Add(MenuItem.Command(c.Get("track.check")));
                    items.Add(MenuItem.Command(c.Get("track.autoCheck")));
                    items.Add(MenuItem.Command(c.Get("file.export")));
                    items.Add(MenuItem.Separator());
                    items.Add(MenuItem.Command(c.Get("view.race")));
                    items.Add(MenuItem.Command(c.Get("race.generate"), Race.Data == null ? "Generate Race Data" : "Regenerate Race Data…"));
                    items.Add(MenuItem.Command(c.Get("race.add")));
                    items.Add(MenuItem.Separator());
                    items.Add(MenuItem.Command(c.Get("track.properties")));
                    items.Add(MenuItem.Command(c.Get("track.history")));
                    items.Add(MenuItem.Command(c.Get("track.issues")));
                    break;

                case 4: // Help
                    items.Add(MenuItem.Command(c.Get("help.shortcuts")));
                    items.Add(MenuItem.Command(c.Get("help.welcome")));
                    items.Add(MenuItem.Separator());
                    items.Add(MenuItem.Command(c.Get("help.about")));
                    break;
            }
            return items;
        }

        private List<MenuItem> BuildRecentMenu()
        {
            var items = new List<MenuItem>();
            foreach (var path in AppSettings.Data.RecentFiles)
            {
                bool exists = File.Exists(path);
                string p = path;
                items.Add(new MenuItem
                {
                    Label = Path.GetFileNameWithoutExtension(path) + (exists ? string.Empty : "  (missing)"),
                    Enabled = exists,
                    Icon = IconId.Track,
                    Action = () => OpenTrackFile(p)
                });
            }
            if (items.Count > 0)
            {
                items.Add(MenuItem.Separator());
                items.Add(new MenuItem { Label = "Clear Recent", Action = AppSettings.ClearRecentFiles });
            }
            return items;
        }

        // Toolbar
        private void DrawToolbar(Rect bar)
        {
            var T = Theme;
            SGUI.Fill(bar, Palette.Panel);
            SGUI.HLine(bar.x, bar.yMax - 1f, bar.width, Palette.Border);

            float size = T.S(30f);
            float y = bar.y + (bar.height - size) * 0.5f;
            float x = bar.x + T.S(10f);
            var editing = Editing;
            var tool = Viewport.Tool;

            bool Tool(IconId icon, string commandId, bool activeState)
            {
                var command = Commands.Get(commandId);
                string tip = command.BindingText.Length > 0 ? $"{command.Label}  {command.BindingText}" : command.Label;
                bool clicked = SGUI.IconButton(new Rect(x, y, size, size), icon, tip, activeState, command.Enabled);
                x += size + T.S(2f);
                if (clicked)
                    command.Invoke();
                return clicked;
            }

            void Divider()
            {
                x += T.S(6f);
                SGUI.VLine(x, bar.y + T.S(10f), bar.height - T.S(20f), Palette.Border);
                x += T.S(7f);
            }

            // workspace switch: track editing / race data
            var modeRect = new Rect(x, y, T.S(170f), size);
            int mode = SGUI.Segmented(modeRect, Race.Active ? 1 : 0, new[] { "Track", "Race data" }, true,
                                      new[] { "Build the track  F6", "AI nodes, zones, lights, objects, instances, force fields  F6" });
            if (mode != (Race.Active ? 1 : 0))
                SetRaceMode(mode == 1);
            x += modeRect.width + T.S(6f);
            Divider();

            if (Race.Active)
            {
                DrawRaceToolbar(bar, ref x, y, size);
                return;
            }

            // tools as a segmented group
            var toolGroup = new Rect(x - T.S(3f), y - T.S(3f), size * 4f + T.S(2f) * 3f + T.S(6f), size + T.S(6f));
            SGUI.Rounded(toolGroup, Palette.Chrome, T.S(7f));
            Tool(IconId.ToolPlace, "tool.place", tool == StudioTool.Place);
            Tool(IconId.ToolPickup, "tool.pickups", tool == StudioTool.Pickups);
            Tool(IconId.ToolErase, "tool.erase", tool == StudioTool.Erase);
            Tool(IconId.Cursor, "tool.select", tool == StudioTool.Select);
            x += T.S(3f);
            Divider();

            // placement
            Tool(IconId.RotateCCW, "place.rotateCCW", false);
            Tool(IconId.RotateCW, "place.rotateCW", false);
            x += T.S(4f);
            Tool(IconId.ElevationDown, "place.lower", false);
            var level = new Rect(x, y, T.S(34f), size);
            SGUI.Label(level, $"L{editing.CursorHeightSteps}", T.SmallStrong, Palette.TextDim, TextAnchor.MiddleCenter);
            SGUI.Tooltip(level, $"Placement height: level {editing.CursorHeightSteps}");
            x += level.width;
            Tool(IconId.ElevationUp, "place.raise", false);
            x += T.S(4f);
            Tool(IconId.VariantPrev, "place.prevVariant", false);
            Tool(IconId.VariantNext, "place.nextVariant", false);
            Tool(IconId.Surface, "place.surface", false);
            Tool(IconId.ToolEyedropper, "edit.pick", false);
            Tool(IconId.Link, "place.fit", false);
            Divider();

            Tool(IconId.Undo, "edit.undo", false);
            Tool(IconId.Redo, "edit.redo", false);
            Divider();

            Tool(IconId.Grid, "view.grid", AppSettings.Data.ShowGrid);
            Tool(Viewport.TopView ? IconId.ViewPerspective : IconId.ViewTop, "view.top", false);
            Tool(IconId.Frame, "view.frame", false);
            Tool(IconId.Command, "view.palette", false);

            // right side: check + export
            float exportWidth = SGUI.TextWidth("Export", T.BodyStrong) + T.S(48f);
            var export = new Rect(bar.xMax - exportWidth - T.S(10f), y, exportWidth, size).Snap();
            if (SGUI.Button(export, "Export", ButtonKind.Primary, true, IconId.Export, "Export to Re-Volt  " + Commands.Get("file.export").BindingText))
                ShowExportDialog();

            float checkWidth = SGUI.TextWidth("Check", T.Body) + T.S(48f);
            var check = new Rect(export.x - checkWidth - T.S(8f), y, checkWidth, size).Snap();
            if (SGUI.Button(check, "Check", ButtonKind.Secondary, true, IconId.Check, "Check the track for problems  " + Commands.Get("track.check").BindingText))
                RunValidation(true);

            // status of the last check next to the button
            if (LastValidation != null && check.x - x > T.S(40f))
            {
                int errors = LastValidation.Problems.Count(p => p.IsError);
                var (icon, color) = errors > 0 ? (IconId.Error, Palette.Error) : (IconId.Success, Palette.Success);
                if (ValidationIsStale)
                    color = Palette.TextFaint;
                var dot = new Rect(check.x - T.S(26f), y, T.S(20f), size);
                SGUI.Icon(dot, icon, color, T.S(16f));
                SGUI.Tooltip(dot, errors > 0 ? $"{errors} problem(s) found" + (ValidationIsStale ? " (track changed since)" : string.Empty)
                                             : "No problems found" + (ValidationIsStale ? " (track changed since)" : string.Empty));
            }
        }

        private void DrawRaceToolbar(Rect bar, ref float x, float y, float size)
        {
            var T = Theme;
            var data = Race.Data;

            string layerName = data == null ? "No race data" : $"Editing {RaceLayers.Name(Race.Layer).ToLowerInvariant()}";
            var label = new Rect(x, y, SGUI.TextWidth(layerName, T.BodyStrong) + T.S(30f), size);
            if (data != null)
                SGUI.Rounded(new Rect(label.x, label.center.y - T.S(4f), T.S(8f), T.S(8f)), RaceLayers.LayerColor(Race.Layer), T.S(4f));
            SGUI.Label(new Rect(label.x + (data != null ? T.S(16f) : 0f), label.y, label.width, label.height), layerName, T.BodyStrong,
                       data != null ? Palette.Text : Palette.TextDim);
            x += label.width;

            if (data == null)
            {
                var generate = new Rect(x, y, T.S(190f), size);
                if (SGUI.Button(generate, "Generate from track", ButtonKind.Primary, true, IconId.Lightning))
                    Race.Generate();
                return;
            }

            float buttonWidth = T.S(84f);
            if (SGUI.Button(new Rect(x, y, buttonWidth, size), "Add", ButtonKind.Secondary, true, IconId.Plus, "Add at the view centre, or after the selected node  Ins"))
                Race.AddElement();
            x += buttonWidth + T.S(6f);
            if (SGUI.Button(new Rect(x, y, buttonWidth, size), "Delete", ButtonKind.Ghost, Race.Selected >= 0, IconId.Trash, "Del"))
                Race.DeleteSelected();
            x += buttonWidth + T.S(6f);

            x += T.S(6f);
            SGUI.VLine(x, bar.y + T.S(10f), bar.height - T.S(20f), Palette.Border);
            x += T.S(13f);
            if (SGUI.IconButton(new Rect(x, y, size, size), IconId.Undo, "Undo  Ctrl+Z", false, TrackEditor.History.CanUndo)) TrackEditor.Undo();
            x += size + T.S(2f);
            if (SGUI.IconButton(new Rect(x, y, size, size), IconId.Redo, "Redo  Ctrl+Y", false, TrackEditor.History.CanRedo)) TrackEditor.Redo();
            x += size + T.S(2f);

            if (Race.IsStale)
            {
                x += T.S(14f);
                var warn = new Rect(x, y, T.S(250f), size);
                SGUI.Icon(warn.TakeLeft(T.S(18f)), IconId.Warning, Palette.Warning, T.S(15f));
                SGUI.Label(warn.CutLeft(T.S(24f)), "Track changed since generation", T.Small, Palette.Warning);
                SGUI.Tooltip(warn, "Regenerate in the panel on the left if the lap no longer fits");
            }

            // export stays reachable on the right
            float exportWidth = SGUI.TextWidth("Export", T.BodyStrong) + T.S(48f);
            var export = new Rect(bar.xMax - exportWidth - T.S(10f), y, exportWidth, size).Snap();
            if (SGUI.Button(export, "Export", ButtonKind.Primary, true, IconId.Export, "Export to Re-Volt"))
                ShowExportDialog();
        }

        // Status bar
        private void DrawStatusBar(Rect bar)
        {
            var T = Theme;
            SGUI.Fill(bar, Palette.Chrome);
            SGUI.HLine(bar.x, bar.y, bar.width, Palette.Border);

            var track = TrackEditor.Track;
            var editing = Editing;
            float gap = T.S(18f);
            float right = bar.xMax - T.S(12f);

            float Segment(string text, Color color, IconId? icon = null, string tooltip = null)
            {
                float width = SGUI.TextWidth(text, T.Small) + (icon.HasValue ? T.S(20f) : 0f);
                var r = new Rect(right - width, bar.y, width, bar.height);
                if (icon.HasValue)
                    SGUI.Icon(r.TakeLeft(T.S(14f)), icon.Value, color, T.S(13f));
                SGUI.Label(r.CutLeft(icon.HasValue ? T.S(20f) : 0f), text, T.Small, color);
                if (tooltip != null)
                    SGUI.Tooltip(r, tooltip);
                right -= width + gap;
                return width;
            }

            // save state
            if (track.IsDirty)
            {
                string autosave = TrackRecovery.LastError != null ? "autosave failed"
                                : TrackRecovery.LastAutosave.HasValue ? $"autosaved {TrackRecovery.LastAutosave.Value:HH:mm}" : null;
                Segment(autosave != null ? $"Unsaved changes · {autosave}" : "Unsaved changes",
                        TrackRecovery.LastError != null ? Palette.Error : Palette.Warning, IconId.Dot,
                        TrackRecovery.LastError ?? "Save with Ctrl+S. Unsaved work is autosaved for crash recovery.");
            }
            else
            {
                Segment("Saved", Palette.TextFaint, IconId.Check);
            }

            int pickups = track.Cells.Count(x => x.HasPickup);
            Segment(TrackEditor.UnlimitedMode ? $"{pickups} pickups" : $"{pickups}/{TrackValidator.MaxPickups} pickups",
                    pickups > TrackValidator.MaxPickups ? Palette.Warning : Palette.TextFaint, IconId.ToolPickup);
            Segment($"{track.GetAllModuleRootPlacements().Count()} modules", Palette.TextFaint, IconId.Module);

            var connections = ConnectivityService.Current(trackRevision);
            if (connections != null && connections.Placements.Count > 0)
            {
                int open = connections.OpenCount;
                int sections = connections.ComponentCount;
                string text = open == 0 && sections <= 1 ? "All joined" : sections > 1 ? $"{open} open · {sections} sections" : $"{open} open ends";
                Segment(text, open == 0 && sections <= 1 ? Palette.TextFaint : Palette.Warning, IconId.Link,
                        $"{connections.ConnectionCount} connections, {connections.StepCount} height steps, {open} open ends, {sections} separate section(s). Red markers in the viewport show open ends.");
            }

            if (Viewport.Tool == StudioTool.Select && !Viewport.Selection.IsEmpty)
                Segment($"{Viewport.Selection.Count} selected", Palette.Accent, IconId.Cursor);
            Segment($"{track.Width} × {track.Height}", Palette.TextFaint, IconId.Ruler, "Grid size");

            var cursor = editing.CursorPosition;
            Segment($"X {cursor.x}  Y {cursor.y}  L {editing.CursorHeightSteps}", Palette.TextDim, null, "Cursor cell and placement level");

            if (TrackEditor.UnlimitedMode)
                Segment("UNLIMITED", Palette.Warning, null, "Started with -unlimited: limits on grid size, height and pickups are lifted");

            // left: tool + hint
            var left = new Rect(bar.x + T.S(12f), bar.y, right - bar.x - T.S(12f), bar.height);
            IconId toolIcon = Viewport.Tool == StudioTool.Pickups ? IconId.ToolPickup
                            : Viewport.Tool == StudioTool.Erase ? IconId.ToolErase
                            : Viewport.Tool == StudioTool.Select ? IconId.Cursor
                            : IconId.ToolPlace;
            SGUI.Icon(left.TakeLeft(T.S(14f)), toolIcon, Palette.Accent, T.S(13f));
            string hint = Race.Active ? Race.Hint : Viewport.ToolHint;
            SGUI.Label(left.CutLeft(T.S(22f)), SGUI.Ellipsize(hint, T.Small, left.width - T.S(22f)), T.Small, Palette.TextDim);
        }
    }
}

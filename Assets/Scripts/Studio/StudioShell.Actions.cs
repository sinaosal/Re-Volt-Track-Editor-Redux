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
using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Studio
{
    /// <summary>Commands and document actions (new, open, save, export, checks).</summary>
    public sealed partial class StudioShell
    {
        private void RegisterCommands()
        {
            var c = Commands;
            var settings = AppSettings.Data;

            // File
            c.Add("file.new", "File", "New Track", NewTrack, KeyBinding.CtrlKey(Key.N), IconId.New);
            c.Add("file.open", "File", "Open Track…", ShowOpenDialog, KeyBinding.CtrlKey(Key.O), IconId.Open);
            c.Add("file.save", "File", "Save", () => SaveTrack(null), KeyBinding.CtrlKey(Key.S), IconId.Save);
            c.Add("file.saveAs", "File", "Save As…", () => OpenDialog(new SaveAsDialog(TrackEditor.Track.Name, null)), KeyBinding.CtrlShiftKey(Key.S), IconId.SaveAs);
            c.Add("file.export", "File", "Export to Re-Volt…", ShowExportDialog, KeyBinding.CtrlKey(Key.E), IconId.Export);
            c.Add("app.preferences", "File", "Preferences…", () => OpenDialog(new PreferencesDialog()), KeyBinding.CtrlKey(Key.Comma), IconId.Settings);
            c.Add("app.quit", "File", "Quit", () => TrackEditor.Instance.Quit(), KeyBinding.CtrlKey(Key.Q));

            // Edit
            c.Add("edit.undo", "Edit", "Undo", TrackEditor.Undo, KeyBinding.CtrlKey(Key.Z), IconId.Undo,
                  () => TrackEditor.History != null && TrackEditor.History.CanUndo);
            c.Add("edit.redo", "Edit", "Redo", TrackEditor.Redo, KeyBinding.CtrlKey(Key.Y), IconId.Redo,
                  () => TrackEditor.History != null && TrackEditor.History.CanRedo);
            c.AddFixed("edit.place", "Edit", "Place Module at Cursor", "Enter", () => { Editing.PlaceModule(); StudioViewport.NoteModuleUsed(Editing.ActiveModuleIndex); }, IconId.ToolPlace);
            c.AddFixed("edit.pick", "Edit", "Pick Module at Cursor", "C", () => Viewport.PickModule(Editing.CursorPosition), IconId.ToolEyedropper);
            c.AddFixed("edit.delete", "Edit", "Delete", "Del", () =>
            {
                if (SelectionActive)
                    Viewport.DeleteSelection();
                else
                    Viewport.EraseAt(Editing.CursorPosition);
            }, IconId.Trash);
            c.Add("edit.pickup", "Edit", "Toggle Pickup at Cursor", () =>
            {
                var cursor = Editing.CursorPosition;
                Viewport.SetPickup(cursor, !TrackEditor.Track.GetCell(cursor).HasPickup, true);
            }, new KeyBinding(Key.P), IconId.ToolPickup);
            c.AddFixed("edit.moveCursor", "Edit", "Move Cursor", "Arrow keys", null);

            // Selection and clipboard
            c.Add("edit.copy", "Edit", "Copy", Viewport.CopySelection, KeyBinding.CtrlKey(Key.C), IconId.Copy, () => SelectionActive,
                  description: "Copy the selected blocks");
            c.Add("edit.cut", "Edit", "Cut", Viewport.CutSelection, KeyBinding.CtrlKey(Key.X), null, () => SelectionActive,
                  description: "Copy the selected blocks and delete them");
            c.Add("edit.paste", "Edit", "Paste", Viewport.PasteClipboard, KeyBinding.CtrlKey(Key.V), IconId.Paste, () => !Viewport.Clipboard.IsEmpty && !Race.Active,
                  description: "Place copied blocks; click to place, R rotates, PgUp/PgDn height, Esc to finish");
            c.Add("edit.duplicate", "Edit", "Duplicate", Viewport.DuplicateSelection, KeyBinding.CtrlKey(Key.D), IconId.Copy, () => SelectionActive,
                  description: "Copy the selection and place the copy with the pointer");
            c.Add("edit.selectAll", "Select", "Select All", Viewport.SelectAll, KeyBinding.CtrlKey(Key.A), IconId.Cursor, () => !Race.Active);
            c.Add("edit.selectNone", "Select", "Deselect All", Viewport.SelectNone, KeyBinding.CtrlShiftKey(Key.A), null, () => !Viewport.Selection.IsEmpty);
            c.Add("edit.invertSelection", "Select", "Invert Selection", Viewport.InvertSelection, KeyBinding.CtrlShiftKey(Key.I), null, () => !Race.Active);
            c.Add("edit.selectConnected", "Select", "Select Connected Section", Viewport.SelectConnected, KeyBinding.CtrlKey(Key.L), IconId.Link, () => SelectionActive,
                  description: "Grow the selection to every block joined to it");
            c.Add("edit.selectSameType", "Select", "Select Same Module Type", Viewport.SelectSameType, KeyBinding.None, null, () => SelectionActive);
            c.Add("edit.hide", "Select", "Hide Selected", () => Viewport.SetHidden(Viewport.SelectedPlacements, true), new KeyBinding(Key.H), IconId.EyeOff, () => SelectionActive,
                  description: "Hide blocks in the viewport (they're still saved and exported)");
            c.Add("edit.showAll", "Select", "Show Hidden Blocks", () => Viewport.States.ShowAll(), new KeyBinding(Key.H, alt: true), IconId.Eye, () => Viewport.States.HiddenCount > 0);
            c.Add("edit.lock", "Select", "Lock Selected", () => Viewport.SetLocked(Viewport.SelectedPlacements, true), KeyBinding.None, IconId.Lock, () => SelectionActive,
                  description: "Protect blocks from changes in the Studio interface");
            c.Add("edit.unlockAll", "Select", "Unlock All Blocks", () => { Viewport.States.UnlockAll(); Toast("All blocks unlocked", IconId.LockOpen); }, KeyBinding.None, IconId.LockOpen,
                  () => Viewport.States.LockedCount > 0);

            // Placement
            // with blocks selected (or a paste in progress), placement actions edit those instead of the brush
            c.Add("place.rotateCW", "Placement", "Rotate Clockwise", () => RotateTarget(1), new KeyBinding(Key.R), IconId.RotateCW);
            c.Add("place.rotateCCW", "Placement", "Rotate Counter-clockwise", () => RotateTarget(-1), new KeyBinding(Key.R, shift: true), IconId.RotateCCW);
            c.AddFixed("place.raise", "Placement", "Raise", "PgUp", () => RaiseTarget(1),
                       IconId.ElevationUp, () => SelectionActive || Viewport.PasteActive || Editing.CursorHeightSteps < Editing.MaxHeightSteps);
            c.AddFixed("place.lower", "Placement", "Lower", "PgDn", () => RaiseTarget(-1),
                       IconId.ElevationDown, () => SelectionActive || Viewport.PasteActive || Editing.CursorHeightSteps > 0);
            c.AddFixed("place.prevVariant", "Placement", "Previous Variant", "Z", () => { if (SelectionActive) Viewport.ChangeSelectedVariant(false); else Editing.PreviousVariant(); },
                       IconId.VariantPrev);
            c.AddFixed("place.nextVariant", "Placement", "Next Variant", "X", () => { if (SelectionActive) Viewport.ChangeSelectedVariant(true); else Editing.NextVariant(); },
                       IconId.VariantNext);
            c.AddFixed("place.surface", "Placement", "Toggle Surface", "T", () => { if (SelectionActive) Viewport.ToggleSelectedSurface(); else Editing.ToggleSurface(); },
                       IconId.Surface, () => SelectionActive || Modules.Lookup.OtherSurface[Editing.ActiveModuleIndex] != Editing.ActiveModuleIndex);
            c.Add("place.fit", "Placement", "Fit Module to Neighbours", () =>
            {
                if (Viewport.Tool != StudioTool.Place)
                    Viewport.SetTool(StudioTool.Place);
                Viewport.FitModuleToNeighbours();
            }, new KeyBinding(Key.A), IconId.Link, () => !Race.Active,
                  description: "Turn and raise the module so it joins the track next to the cursor");

            // Tools
            c.Add("tool.place", "Tools", "Place Modules", () => { SetRaceMode(false); Viewport.SetTool(StudioTool.Place); }, new KeyBinding(Key.Digit1), IconId.ToolPlace,
                  null, () => Viewport.Tool == StudioTool.Place && !Race.Active);
            c.Add("tool.pickups", "Tools", "Place Pickups", () => Viewport.SetTool(StudioTool.Pickups), new KeyBinding(Key.Digit2), IconId.ToolPickup,
                  null, () => Viewport.Tool == StudioTool.Pickups);
            c.Add("tool.erase", "Tools", "Erase Modules", () => Viewport.SetTool(StudioTool.Erase), new KeyBinding(Key.Digit3), IconId.ToolErase,
                  null, () => Viewport.Tool == StudioTool.Erase);
            c.Add("tool.select", "Tools", "Select & Edit Blocks", SelectTool, new KeyBinding(Key.Digit4), IconId.Cursor,
                  null, () => Viewport.Tool == StudioTool.Select);

            // View
            c.Add("view.palette", "View", "Command Palette…", () => OpenDialog(new CommandPaletteDialog()), KeyBinding.CtrlShiftKey(Key.P), IconId.Command,
                  description: "Search and run any command");
            c.Add("view.top", "View", "Top View", Viewport.ToggleTopView, new KeyBinding(Key.V), IconId.ViewTop, null, () => Viewport.TopView);
            c.Add("view.perspective", "View", "Perspective View", () => Viewport.SetViewPreset(ViewPreset.Perspective), KeyBinding.None, IconId.ViewPerspective,
                  null, () => !Viewport.TopView && !Viewport.Orthographic);
            c.Add("view.front", "View", "Front View", () => Viewport.SetViewPreset(ViewPreset.Front), new KeyBinding(Key.Numpad1), null,
                  null, () => Viewport.ViewName == "Front", "Orthographic view from the south: shows heights along the track");
            c.Add("view.side", "View", "Side View", () => Viewport.SetViewPreset(ViewPreset.Side), KeyBinding.None, null,
                  null, () => Viewport.ViewName == "Side", "Orthographic view from the east");
            c.Add("view.ortho", "View", "Orthographic Projection", Viewport.ToggleOrthographic, new KeyBinding(Key.Numpad5), null, null, () => Viewport.Orthographic);
            c.Add("view.frame", "View", "Frame Selection", () => Viewport.FrameSelection(), new KeyBinding(Key.F), IconId.Frame,
                  description: "Frame the selected blocks, or the whole track if nothing is selected");
            c.Add("view.frameAll", "View", "Frame Track", () => Viewport.FrameTrack(), new KeyBinding(Key.Home), IconId.Frame);
            c.Add("view.reset", "View", "Reset View", Viewport.ResetView, KeyBinding.None, null, description: "Default angle and perspective, whole track in view");
            c.Add("view.connections", "View", "Show Connections", () => Toggle(ref settings.ShowConnections), KeyBinding.None, IconId.Link, null, () => settings.ShowConnections,
                  "Markers where pieces join (green), step in height (amber) or end (red)");
            c.Add("view.minimap", "View", "Show Minimap", () => Toggle(ref settings.ShowMinimap), KeyBinding.None, IconId.Map, null, () => settings.ShowMinimap);
            c.Add("view.zoomToPointer", "View", "Zoom Towards Pointer", () => Toggle(ref settings.ZoomToPointer), KeyBinding.None, null, null, () => settings.ZoomToPointer);
            c.Add("view.outliner", "View", "Outliner", ShowOutliner, KeyBinding.None, IconId.List, null, () => settings.LeftPanelVisible && settings.LeftPanelTab == 1);
            c.Add("view.focusCursor", "View", "Focus Cursor", Viewport.FocusCursor, new KeyBinding(Key.F, shift: true));
            c.AddFixed("view.orbitLeft", "View", "Orbit Left", "[", () => Viewport.Orbit90(1), IconId.OrbitLeft);
            c.AddFixed("view.orbitRight", "View", "Orbit Right", "]", () => Viewport.Orbit90(-1), IconId.OrbitRight);
            c.Add("view.grid", "View", "Show Grid", () => Toggle(ref settings.ShowGrid), new KeyBinding(Key.G), IconId.Grid, null, () => settings.ShowGrid);
            c.Add("view.pickups", "View", "Show Pickups", () => Toggle(ref settings.ShowPickups), KeyBinding.None, null, null, () => settings.ShowPickups);
            c.Add("view.footprint", "View", "Show Placement Footprint", () => Toggle(ref settings.ShowFootprint), KeyBinding.None, null, null, () => settings.ShowFootprint);
            c.Add("view.follow", "View", "Camera Follows Keyboard Cursor", () => Toggle(ref settings.CameraFollowsCursor), KeyBinding.None, null, null, () => settings.CameraFollowsCursor);
            c.Add("view.library", "View", "Modules Panel", () => Toggle(ref settings.LeftPanelVisible), KeyBinding.CtrlKey(Key.B), IconId.Module, null, () => settings.LeftPanelVisible);
            c.Add("view.inspector", "View", "Inspector", () => Toggle(ref settings.RightPanelVisible), KeyBinding.CtrlKey(Key.I), IconId.Layout, null, () => settings.RightPanelVisible);
            c.Add("view.statusbar", "View", "Status Bar", () => Toggle(ref settings.StatusBarVisible), KeyBinding.None, null, null, () => settings.StatusBarVisible);
            c.Add("view.fullscreen", "View", "Full Screen", () => Screen.fullScreen = !Screen.fullScreen, new KeyBinding(Key.F11), null, null, () => Screen.fullScreen);
            c.AddFixed("view.classic", "View", "Classic Interface", "F10", () => Defer(() => TrackEditor.Instance.ToggleInterface()), IconId.Layout);

            // Track
            c.Add("track.check", "Track", "Check Track", () => RunValidation(true), new KeyBinding(Key.F7), IconId.Check);
            c.Add("track.autoCheck", "Track", "Check Automatically", () => Toggle(ref settings.AutoCheck), KeyBinding.None, null, null, () => settings.AutoCheck,
                  "Re-check the track shortly after each change");
            c.Add("view.race", "Track", "Race Data Editing", () => SetRaceMode(!Race.Active), new KeyBinding(Key.F6), IconId.Track,
                  null, () => Race.Active, "AI nodes, position nodes, zones, lights, objects, instances and force fields");
            c.Add("race.generate", "Track", "Generate Race Data", () => { SetRaceMode(true); Race.Generate(); }, KeyBinding.None, IconId.Lightning);
            c.AddFixed("race.add", "Track", "Add Race Element", "Ins", () => Race.AddElement(), IconId.Plus, () => Race.Active && Race.Data != null);
            c.Add("track.properties", "Track", "Track Properties", () => ShowInspectorTab(InspectorTab.Track), KeyBinding.None, IconId.Ruler);
            c.Add("track.history", "Track", "History", () => ShowInspectorTab(InspectorTab.History), KeyBinding.CtrlKey(Key.H), IconId.History);
            c.Add("track.issues", "Track", "Issues", () => ShowInspectorTab(InspectorTab.Issues), KeyBinding.None, IconId.Warning);

            // Help
            c.Add("help.shortcuts", "Help", "Keyboard Shortcuts & Controls", () => OpenDialog(new ShortcutsDialog()), new KeyBinding(Key.F1), IconId.Keyboard);
            c.Add("help.welcome", "Help", "Welcome Screen", () => OpenDialog(new WelcomeDialog()), KeyBinding.None, IconId.Lightning);
            c.Add("help.folder", "Help", "Show Tracks Folder", () => RevealFolder(FileHelper.SaveDirectory), KeyBinding.None, IconId.Folder);
            c.Add("help.about", "Help", "About", () => OpenDialog(new AboutDialog()), KeyBinding.None, IconId.Info);
        }

        private bool SelectionActive => Viewport.Tool == StudioTool.Select && !Viewport.Selection.IsEmpty && !Race.Active;

        private void RotateTarget(int turns)
        {
            if (Viewport.PasteActive)
                Viewport.RotatePaste(turns);
            else if (SelectionActive)
                Viewport.RotateSelection(turns);
            else
                Editing.RotateModule(turns);
        }

        private void RaiseTarget(int levels)
        {
            if (Viewport.PasteActive)
                Viewport.ChangePasteLevel(levels);
            else if (SelectionActive)
                Viewport.RaiseSelection(levels);
            else
                Editing.AdjustCursorHeight(levels);
        }

        private void SelectTool()
        {
            Viewport.SetTool(StudioTool.Select);
            // select the block under the keyboard cursor, if any, so the inspector isn't empty
            if (Viewport.Selection.IsEmpty)
            {
                var module = TrackEditor.Track.GetCell(Editing.CursorPosition).Module;
                if (module != null)
                    Viewport.SelectedRoot = module.RootCell.Position;
            }
            if (AppSettings.Data.RightPanelVisible)
                inspector.SetTab(InspectorTab.Place);
        }

        private static void Toggle(ref bool value)
        {
            value = !value;
            AppSettings.Save();
        }

        private void ShowInspectorTab(InspectorTab tab)
        {
            AppSettings.Data.RightPanelVisible = true;
            inspector.SetTab(tab);
        }

        // Unsaved changes
        /// <summary>
        /// Asks Save / Don't Save / Cancel if the track has unsaved changes, then runs <paramref name="proceed"/>.
        /// </summary>
        public void ConfirmUnsavedChanges(Action proceed)
        {
            if (!TrackEditor.Track.IsDirty)
            {
                proceed();
                return;
            }

            Viewport.CancelInteraction();
            string name = string.IsNullOrEmpty(TrackEditor.Track.Name) ? "this track" : $"\"{TrackEditor.Track.Name}\"";
            OpenDialog(new ConfirmDialog("Save changes?",
                $"Do you want to save the changes to {name}? Your changes will be lost if you don't save them.",
                null, 0,
                new ConfirmDialog.Choice("Save", ButtonKind.Primary, () => SaveTrack(proceed)),
                new ConfirmDialog.Choice("Don't Save", ButtonKind.Secondary, proceed),
                new ConfirmDialog.Choice("Cancel", ButtonKind.Ghost, null)));
        }

        // Documents
        public void NewTrack()
        {
            ConfirmUnsavedChanges(() =>
            {
                TrackEditor.CreateNewTrack();
                Viewport.SetTool(StudioTool.Place);
                Viewport.FrameTrack(true);
                Toast("New track", IconId.New);
            });
        }

        public void ShowOpenDialog()
        {
            OpenDialog(new OpenTrackDialog());
        }

        /// <summary>Opens a track file, asking about unsaved changes first.</summary>
        public void OpenTrackFile(string path)
        {
            ConfirmUnsavedChanges(() => LoadTrackFile(path));
        }

        private void LoadTrackFile(string path)
        {
            var result = TrackEditor.LoadTrackFile(path);
            if (!result.Success)
            {
                TrackEditor.PlaySound(TrackEditor.SndWarning);
                OpenDialog(new MessageDialog("Couldn't open track",
                    $"{result.Error}\n\nThe track that was open before is unchanged.", MessageKind.Error));
                return;
            }

            Viewport.FrameTrack(true);
            Toast($"Opened {Path.GetFileName(path)}", IconId.Open);

            if (result.Warnings.Count > 0)
            {
                OpenDialog(new MessageDialog("Track opened with warnings",
                    string.Join("\n", result.Warnings.Select(w => "• " + w)) +
                    "\n\nThe file on disk is unchanged until you save.", MessageKind.Warning));
            }
        }

        /// <summary>
        /// Saves to the current file, or asks for a name if the track was never saved (or was renamed).
        /// </summary>
        public void SaveTrack(Action afterSave)
        {
            string path = TrackEditor.GetQuickSavePath();
            if (path == null)
            {
                OpenDialog(new SaveAsDialog(TrackEditor.Track.Name, afterSave));
                return;
            }
            WriteTrack(path, null, afterSave);
        }

        /// <summary>Saves under a new name into the tracks folder.</summary>
        public void SaveTrackAs(string name, Action afterSave)
        {
            string path = FileHelper.GetTrackSavePath(name);
            string previousName = TrackEditor.Track.Name;
            TrackEditor.Track.Name = name;
            WriteTrack(path, previousName, afterSave);
        }

        private void WriteTrack(string path, string previousName, Action afterSave)
        {
            try
            {
                FileHelper.EnsureSaveDirectoryExists();
                TrackEditor.SaveTrackFile(path);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Saving '{path}' failed: {ex}");
                if (previousName != null)
                    TrackEditor.Track.Name = previousName;
                TrackEditor.PlaySound(TrackEditor.SndWarning);
                OpenDialog(new MessageDialog("Couldn't save track",
                    $"{ex.Message}\n\nIf the file already existed, it was not changed.", MessageKind.Error));
                return;
            }

            Toast($"Saved {Path.GetFileName(path)}", IconId.Check);
            afterSave?.Invoke();
        }

        public void RestoreRecovery()
        {
            var result = TrackRecovery.RestorePending();
            if (!result.Success)
            {
                OpenDialog(new MessageDialog("Recovery failed", result.Error, MessageKind.Error));
                return;
            }
            Viewport.FrameTrack(true);
            Toast("Unsaved work recovered", IconId.Recover);
        }

        // Export and checks
        public void ShowExportDialog()
        {
            OpenDialog(new ExportDialog());
        }

        public void SetValidationResult(TrackValidator.Result result)
        {
            LastValidation = result;
            validationRevision = trackRevision;
        }

        public void RunValidation(bool showResults)
        {
            try
            {
                SetValidationResult(TrackValidator.Validate(TrackEditor.Track, TrackEditor.TrackUnit));
            }
            catch (Exception ex)
            {
                Debug.LogError($"Track check failed: {ex}");
                OpenDialog(new MessageDialog("Track check failed", ex.Message, MessageKind.Error));
                return;
            }

            if (!showResults)
                return;

            ShowInspectorTab(InspectorTab.Issues);
            int errors = LastValidation.Problems.Count(p => p.IsError);
            if (errors == 0)
            {
                Toast("No problems found", IconId.Success);
                TrackEditor.PlaySound(TrackEditor.SndAdjust);
            }
            else
            {
                Toast(errors == 1 ? "1 problem found" : $"{errors} problems found", IconId.Warning);
                TrackEditor.PlaySound(TrackEditor.SndWarning);
            }
        }

        /// <summary>Moves the cursor and camera to the cell a problem refers to.</summary>
        public void FocusProblem(TrackProblem problem)
        {
            ShowInspectorTab(InspectorTab.Issues);
            if (problem == null || !problem.Cell.HasValue)
                return;

            var cell = problem.Cell.Value;
            var track = TrackEditor.Track;
            if (cell.x < 0 || cell.y < 0 || cell.x >= track.Width || cell.y >= track.Height)
                return;

            // select the blocks involved so they can be fixed (or moved) right away
            var related = problem.RelatedCells?.Where(c => BlockSelection.RootPlacement(c) != null).ToList();
            if (related != null && related.Count > 0)
            {
                Viewport.SetTool(StudioTool.Select);
                Viewport.Selection.SetMany(related);
            }
            else
            {
                Viewport.SetTool(StudioTool.Place);
                Editing.CursorPosition = cell;
            }
            Viewport.FocusCell(cell);
            Viewport.Highlight(cell);
        }
    }
}

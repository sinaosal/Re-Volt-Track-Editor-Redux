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
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Document/session management: loading, saving, undo/redo, and dirty tracking.
/// Shared by the classic and the Studio interfaces.
/// </summary>
public partial class TrackEditor
{
    public struct LoadResult
    {
        public bool Success;
        public string Error;
        public List<string> Warnings;

        public static LoadResult Failed(string error) => new LoadResult { Success = false, Error = error, Warnings = new List<string>() };
    }

    /// <summary>Full path of the file the current track was loaded from / saved to. Null for new tracks.</summary>
    public static string CurrentFilePath { get; private set; }

    /// <summary>Undo/redo history of the current track.</summary>
    public static TrackHistory History { get; private set; }

    /// <summary>Raised whenever the track content changed (edits, undo/redo, load, new).</summary>
    public static event Action TrackChanged;

    /// <summary>Raised after a successful save, with the full path.</summary>
    public static event Action<string> TrackSaved;

    /// <summary>Raised after a successful load or new track.</summary>
    public static event Action TrackReplaced;

    /// <summary>
    /// When set, the interface uses this to ask the user about unsaved changes before
    /// <paramref name="proceed"/> runs (Studio interface). Otherwise the classic prompt is used.
    /// </summary>
    public static Action<Action> UnsavedChangesPrompt;

    private static TdfDocument savedState;

    // History
    private void InitSession()
    {
        History = new TrackHistory(() => Track.ToDocument(), ApplyDocument);
        History.Changed += OnHistoryChanged;
    }

    private static void OnHistoryChanged()
    {
        RefreshDirtyState();
        TrackChanged?.Invoke();
    }

    /// <summary>
    /// Runs <paramref name="change"/> as one undoable step and updates the dirty state.
    /// </summary>
    public static bool RecordChange(string label, Action change)
    {
        if (History == null)
        {
            change();
            Track.MarkDirty();
            TrackChanged?.Invoke();
            return true;
        }

        bool changed = History.Perform(label, change);
        if (History.IsGrouping)
        {
            // grouped edits (e.g. drag painting) only record when the group ends, but the UI updates live
            Track.MarkDirty();
            TrackChanged?.Invoke();
        }
        else if (!changed)
        {
            // the change callback may have marked the track dirty although nothing differs
            RefreshDirtyState();
        }
        return changed;
    }

    /// <summary>Ends a grouped change and corrects the dirty flag if the group changed nothing.</summary>
    public static bool EndChangeGroup()
    {
        if (History == null)
            return false;
        bool changed = History.EndGroup();
        if (!changed)
        {
            RefreshDirtyState();
            TrackChanged?.Invoke();
        }
        return changed;
    }

    public static void Undo()
    {
        if (History != null && History.Undo())
            PlaySound(SndRaise);
        else
            PlaySound(SndWarning);
    }

    public static void Redo()
    {
        if (History != null && History.Redo())
            PlaySound(SndLower);
        else
            PlaySound(SndWarning);
    }

    /// <summary>
    /// Compares the track to the last saved state, so undoing back to it clears the dirty flag.
    /// </summary>
    public static void RefreshDirtyState()
    {
        if (Track == null)
            return;

        bool dirty = savedState == null || !savedState.ContentEquals(Track.ToDocument());
        Track.SetDirty(dirty);
    }

    private static void ResetSession(string filePath)
    {
        CurrentFilePath = filePath;
        savedState = Track.ToDocument();
        Track.ClearDirtyFlag();

        if (History != null)
        {
            // keep the history memory bounded on very large (unlimited mode) grids
            long cells = (long)Track.Width * Track.Height;
            History.MaxDepth = (int)Math.Max(10L, Math.Min(250L, 8_000_000L / Math.Max(1L, cells)));
            History.Clear();
        }

        TrackReplaced?.Invoke();
        TrackChanged?.Invoke();
    }

    // Applying documents
    /// <summary>
    /// Validates a document against the loaded track unit and fixes what can be fixed.
    /// Invalid placements are removed, never silently: every change is reported as a warning.
    /// </summary>
    public static List<string> NormalizeDocument(TdfDocument doc)
    {
        var warnings = new List<string>(doc.Warnings);
        int moduleCount = TrackUnit.Modules.Count;

        int unknownModules = 0;
        int badRotations = 0;
        int outOfBounds = 0;
        int overlaps = 0;

        // owner[cellIndex] = root index of the placement covering that cell
        var owner = new int[doc.Cells.Length];
        for (int i = 0; i < owner.Length; i++)
            owner[i] = -1;

        for (int i = 0; i < doc.Cells.Length; i++)
        {
            var cell = doc.Cells[i];
            if (!cell.HasModule)
                continue;

            if (cell.ModuleId >= moduleCount)
            {
                unknownModules++;
                doc.Cells[i] = TdfCell.Empty;
                continue;
            }

            if (cell.Rotation > 3)
            {
                badRotations++;
                cell.Rotation = (ushort)(cell.Rotation % 4);
                doc.Cells[i] = cell;
            }

            var root = new Vector2Int(i % doc.Width, i / doc.Width);
            var footprint = GetFootprint(cell.ModuleId, root, cell.Rotation);

            bool inBounds = true;
            foreach (var pos in footprint)
            {
                if (!doc.InBounds(pos.x, pos.y))
                {
                    inBounds = false;
                    break;
                }
            }

            if (!inBounds)
            {
                outOfBounds++;
                doc.Cells[i] = TdfCell.Empty;
                continue;
            }

            // later placements replace earlier overlapping ones, like placing them in order would
            foreach (var pos in footprint)
            {
                int cellIndex = doc.IndexOf(pos.x, pos.y);
                int previousRoot = owner[cellIndex];
                if (previousRoot >= 0 && previousRoot != i && doc.Cells[previousRoot].HasModule)
                {
                    overlaps++;
                    RemovePlacement(doc, owner, previousRoot);
                }
            }

            foreach (var pos in footprint)
                owner[doc.IndexOf(pos.x, pos.y)] = i;
        }

        if (unknownModules > 0)
            warnings.Add($"{unknownModules} module(s) use IDs this track unit doesn't contain and were skipped.");
        if (badRotations > 0)
            warnings.Add($"{badRotations} module(s) had an invalid rotation and were corrected.");
        if (outOfBounds > 0)
            warnings.Add($"{outOfBounds} module(s) extended past the edge of the grid and were skipped.");
        if (overlaps > 0)
            warnings.Add($"{overlaps} overlapping module(s) were replaced by the module placed after them.");

        // styles of blocks that were removed above have nothing to apply to
        doc.Extras?.RemoveOrphans(doc);

        return warnings;
    }

    private static void RemovePlacement(TdfDocument doc, int[] owner, int rootIndex)
    {
        doc.Cells[rootIndex] = TdfCell.Empty;
        for (int i = 0; i < owner.Length; i++)
        {
            if (owner[i] == rootIndex)
                owner[i] = -1;
        }
    }

    /// <summary>
    /// Grid cells covered by a module placed at <paramref name="root"/> with <paramref name="rotation"/>.
    /// </summary>
    public static List<Vector2Int> GetFootprint(int moduleIndex, Vector2Int root, int rotation)
    {
        var module = TrackUnit.Modules[moduleIndex];
        var result = new List<Vector2Int>(module.Instances.Count);
        foreach (var instance in module.Instances)
            result.Add(root + instance.Position.Rotate(rotation));
        return result;
    }

    /// <summary>
    /// Makes the scene match <paramref name="doc"/>. Only modules that differ are destroyed and
    /// re-created, so undo/redo stays fast even on large tracks. The document must be normalized.
    /// </summary>
    public static void ApplyDocument(TdfDocument doc)
    {
        var editingMode = GetEditMode<TrackEditingMode>();

        // 1. remove placements that don't exist in the target (or don't fit the target size)
        var toDelete = new List<ModulePlacement>();
        foreach (var placement in Track.GetAllModuleRootPlacements())
        {
            var pos = placement.RootCell.Position;
            bool keep = doc.InBounds(pos.x, pos.y)
                        && SamePlacement(placement, doc.GetCell(pos.x, pos.y));
            if (!keep)
                toDelete.Add(placement);
        }
        foreach (var placement in toDelete)
            placement.Delete();

        // 2. resize
        if (Track.Width != doc.Width || Track.Height != doc.Height)
            SetTrackSize(doc.Width, doc.Height);

        // 3. create missing placements
        for (int i = 0; i < doc.Cells.Length; i++)
        {
            var data = doc.Cells[i];
            if (!data.HasModule)
                continue;

            var cell = Track.Cells[i];
            if (cell.Module != null && cell.Module.RootCell == cell && SamePlacement(cell.Module, data))
                continue;

            editingMode.PlaceModule(data.ModuleId, cell.Position, data.ElevationSteps * EditorConstants.ElevationStep, data.Rotation);
        }

        // 4. pickups and name
        for (int i = 0; i < doc.Pickups.Length; i++)
            Track.Cells[i].HasPickup = doc.Pickups[i];

        Track.Name = doc.Name;

        // 5. extras: support styles go onto their blocks, everything else onto the track
        var extras = doc.Extras ?? new TrackExtras();
        Track.Extras = extras.Clone();
        foreach (var placement in Track.GetAllModuleRootPlacements())
        {
            var pos = placement.RootCell.Position;
            SupportStyle? style = extras.Supports.TryGetValue((pos.x, pos.y), out var s) ? s : (SupportStyle?)null;
            if (!Nullable.Equals(placement.Support, style) || style.HasValue)
            {
                placement.Support = style;
                placement.ApplySupportVisual();
            }
        }
        RefreshDirtyState();
    }

    private static bool SamePlacement(ModulePlacement placement, TdfCell data)
    {
        if (!data.HasModule)
            return false;

        int steps = Mathf.RoundToInt(placement.Elevation / EditorConstants.ElevationStep);
        return placement.ModuleIndex == data.ModuleId
            && (((placement.Rotation % 4) + 4) % 4) == data.Rotation
            && steps == data.ElevationSteps;
    }

    // Loading and saving
    /// <summary>
    /// Loads a TDF file. The file is fully parsed and validated before anything in the editor
    /// changes, so a broken file can never destroy the track that is currently open.
    /// </summary>
    public static LoadResult LoadTrackFile(string path, bool addToRecentFiles = true)
    {
        TdfDocument doc;
        try
        {
            doc = TrackFiles.Load(path);
        }
        catch (TdfFormatException ex)
        {
            Debug.LogWarning($"LoadTrackFile: {path}: {ex}");
            return LoadResult.Failed(ex.Message);
        }
        catch (Exception ex)
        {
            Debug.LogError($"LoadTrackFile: {path}: {ex}");
            return LoadResult.Failed($"The track could not be read: {ex.Message}");
        }

        bool unlimitedSize = doc.Width > EditorConstants.MaxGridSize || doc.Height > EditorConstants.MaxGridSize;
        var warnings = NormalizeDocument(doc);
        if (unlimitedSize && !UnlimitedMode)
            warnings.Add($"This track is {doc.Width}x{doc.Height}, larger than the standard {EditorConstants.MaxGridSize}x{EditorConstants.MaxGridSize} limit. It may not export correctly for the original game.");

        try
        {
            Track.Clear();
            ApplyDocument(doc);
        }
        catch (Exception ex)
        {
            // should never happen for a normalized document; leave the editor in a usable state
            Debug.LogError($"LoadTrackFile: failed to build track: {ex}");
            Track.Clear();
            ResetSession(null);
            return LoadResult.Failed($"The track could not be built: {ex.Message}");
        }

        ResetSession(path);
        if (addToRecentFiles)
            AppSettings.AddRecentFile(path);
        TrackRecovery.Discard();
        TrackRecovery.ResetTimer();

        return new LoadResult { Success = true, Warnings = warnings };
    }

    /// <summary>
    /// Saves the current track to <paramref name="path"/> atomically. Throws on IO errors so the
    /// interface can tell the user; the existing file is left untouched in that case.
    /// </summary>
    public static void SaveTrackFile(string path)
    {
        Track.Name = TdfDocument.SanitizeName(Track.Name);
        Track.Save(path);

        CurrentFilePath = path;
        savedState = Track.ToDocument();
        Track.ClearDirtyFlag();

        AppSettings.AddRecentFile(path);
        TrackRecovery.Discard();
        TrackSaved?.Invoke(path);
        TrackChanged?.Invoke();
    }

    /// <summary>
    /// The file a "Save" (as opposed to "Save As") should write to, or null if the user must choose a name.
    /// Saving keeps writing to the opened file unless the track was renamed.
    /// </summary>
    public static string GetQuickSavePath()
    {
        if (CurrentFilePath == null || savedState == null)
            return null;
        if (savedState.Name != Track.Name)
            return null;
        return CurrentFilePath;
    }

    /// <summary>
    /// After restoring recovery data: associate the track with its original file (if it still exists)
    /// and compare against that file so the unsaved changes are flagged correctly.
    /// </summary>
    public static void AdoptRecoveredTrack(string originalPath)
    {
        CurrentFilePath = null;
        savedState = null;

        if (!string.IsNullOrEmpty(originalPath) && File.Exists(originalPath))
        {
            CurrentFilePath = originalPath;
            try
            {
                var original = TrackFiles.Load(originalPath);
                NormalizeDocument(original);
                savedState = original;
            }
            catch (Exception)
            {
                savedState = null;
            }
        }

        RefreshDirtyState();
        TrackChanged?.Invoke();
    }

    /// <summary>
    /// Renames the track as an undoable change.
    /// </summary>
    public static void RenameTrack(string newName)
    {
        newName = TdfDocument.SanitizeName(newName);
        if (newName == Track.Name)
            return;
        RecordChange("Rename track", () => Track.Name = newName);
    }

    /// <summary>
    /// Resets to an empty track without asking about unsaved changes.
    /// </summary>
    public static void CreateNewTrack()
    {
        Instance.NewTrackAction();
    }

    /// <summary>
    /// Runs <paramref name="action"/> after the user had the chance to save unsaved changes.
    /// </summary>
    public static void RunAfterSaveCheck(Action action)
    {
        Instance.AskToSaveBeforeAction(action);
    }

    public static string DescribeTrackFile(string path)
    {
        return path == null ? "Unsaved track" : Path.GetFileName(path);
    }
}

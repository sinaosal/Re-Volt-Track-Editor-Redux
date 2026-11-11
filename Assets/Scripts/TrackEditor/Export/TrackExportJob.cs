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
using ReVolt.TrackUnit;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

public enum TrackProblemKind
{
    NoGameFolder,
    MissingStartGrid,
    MultipleStartGrids,
    NoLoop,
    InvalidAIPath,
    ExportFailed,

    // warnings / information
    NoReverseVersion,
    TooManyPickups,
    OversizedTrack,
    EmptyTrack,
    RaceDataStale,
    RaceDataUnreadable,

    // from the connection check
    OpenEnd,
    HeightStep,
    DisconnectedSection,

    // export blockers found before anything is written
    MissingModel
}

public enum TrackProblemSeverity
{
    Error,
    Warning,
    Info
}

/// <summary>
/// Something wrong (or worth knowing) about a track, optionally tied to a grid cell
/// so the interface can take the user there.
/// </summary>
public class TrackProblem
{
    public TrackProblemKind Kind;
    public TrackProblemSeverity Severity;
    public string Message;
    public Vector2Int? Cell;

    /// <summary>Short heading for lists; falls back to the severity name.</summary>
    public string Title;
    /// <summary>What to do about it, if there is something to do.</summary>
    public string Suggestion;
    /// <summary>Which check found it: "Lap check", "Connections", "Export", "Race data".</summary>
    public string Source;
    /// <summary>Root cells of the modules involved (for selecting them).</summary>
    public List<Vector2Int> RelatedCells;

    // details the classic interface uses to pick its localized message
    public bool LastZoneIsPipe;
    public bool HasZoneSequence;

    public bool IsError => Severity == TrackProblemSeverity.Error;
}

/// <summary>
/// Runs the same checks as an export, without writing anything.
/// </summary>
public static class TrackValidator
{
    public const int MaxPickups = 40;

    public class Result
    {
        public readonly List<TrackProblem> Problems = new List<TrackProblem>();
        public float LapLength;
        public bool ReverseAvailable;
        public bool LapCloses;
        public ConnectivityReport Connections;
        public double Milliseconds;
        public bool IsExportable => !Problems.Any(x => x.IsError && x.Kind != TrackProblemKind.NoGameFolder);
    }

    public static Result Validate(EditorTrack track, TrackUnitFile unitFile)
    {
        PerfTimeLogger.Muted++;
        try
        {
            return ValidateQuietly(track, unitFile);
        }
        finally
        {
            PerfTimeLogger.Muted--;
        }
    }

    private static Result ValidateQuietly(EditorTrack track, TrackUnitFile unitFile)
    {
        var result = new Result();
        var watch = System.Diagnostics.Stopwatch.StartNew();

        if (!track.GetAllModuleRootPlacements().Any())
        {
            result.Problems.Add(new TrackProblem
            {
                Kind = TrackProblemKind.EmptyTrack,
                Severity = TrackProblemSeverity.Error,
                Title = "Empty track",
                Source = "Lap check",
                Message = "The track is empty.",
                Suggestion = "Place a start grid and build a closed loop back to it."
            });
            return result;
        }

        var exporter = new TrackExporter(track, unitFile);
        var problem = CheckLayout(exporter);
        if (problem != null)
        {
            result.Problems.Add(problem);
        }
        else
        {
            result.LapCloses = true;
            result.LapLength = exporter.TrackLength;

            var reverse = new TrackExporter(track, unitFile, true);
            reverse.Initialize();
            reverse.CreateAINodes();
            result.ReverseAvailable = reverse.TrackFormsLoop && reverse.AIIsValid;
            if (!result.ReverseAvailable)
            {
                result.Problems.Add(new TrackProblem
                {
                    Kind = TrackProblemKind.NoReverseVersion,
                    Severity = TrackProblemSeverity.Info,
                    Title = "No reversed version",
                    Source = "Lap check",
                    Message = "The track can't be driven in reverse, so no reversed version will be exported.",
                    Suggestion = "Usually a drop or jump that can't be driven backwards. Nothing to fix if that's intended."
                });
            }
        }

        int pickups = track.Cells.Count(x => x.HasPickup);
        if (pickups > MaxPickups)
        {
            result.Problems.Add(new TrackProblem
            {
                Kind = TrackProblemKind.TooManyPickups,
                Severity = TrackProblemSeverity.Warning,
                Title = "Too many pickups",
                Source = "Export",
                Message = $"{pickups} pickups placed. The original game supports at most {MaxPickups}.",
                Suggestion = "Remove pickups with the Pickups tool (2)."
            });
        }

        if (track.Width > EditorConstants.MaxGridSize || track.Height > EditorConstants.MaxGridSize)
        {
            result.Problems.Add(new TrackProblem
            {
                Kind = TrackProblemKind.OversizedTrack,
                Severity = TrackProblemSeverity.Warning,
                Title = "Oversized grid",
                Source = "Export",
                Message = $"The grid is {track.Width}x{track.Height}, larger than the standard {EditorConstants.MaxGridSize}x{EditorConstants.MaxGridSize}.",
                Suggestion = "Shrink the grid in the Track tab if the extra space isn't needed."
            });
        }

        CheckConnections(track, result, problem);
        CheckRaceData(track, result);

        if (FileHelper.GetCompiledLevelsPath() == null)
        {
            result.Problems.Add(new TrackProblem
            {
                Kind = TrackProblemKind.NoGameFolder,
                Severity = TrackProblemSeverity.Warning,
                Title = "Game folder not found",
                Source = "Export",
                Message = "No Re-Volt installation was found next to the editor, so the track can't be exported.",
                Suggestion = "Place the editor folder inside the game folder (Re-Volt\\editor)."
            });
        }

        // most important first, keeping the order of each check within a severity
        var ordered = result.Problems.Select((p, i) => (p, i)).OrderBy(x => (int)x.p.Severity).ThenBy(x => x.i).Select(x => x.p).ToList();
        result.Problems.Clear();
        result.Problems.AddRange(ordered);

        result.Milliseconds = watch.Elapsed.TotalMilliseconds;
        return result;
    }

    private static void CheckRaceData(EditorTrack track, Result result)
    {
        RaceData race;
        try
        {
            race = RaceData.FromTrack(track);
        }
        catch (Exception ex)
        {
            result.Problems.Add(new TrackProblem
            {
                Kind = TrackProblemKind.RaceDataUnreadable,
                Severity = TrackProblemSeverity.Warning,
                Title = "Race data unreadable",
                Source = "Race data",
                Message = $"The edited race data couldn't be read ({ex.Message}); generated data will be exported.",
                Suggestion = "Open Race data (F6) and generate it again."
            });
            return;
        }

        if (race != null && race.Enabled && race.IsStaleFor(track))
        {
            result.Problems.Add(new TrackProblem
            {
                Kind = TrackProblemKind.RaceDataStale,
                Severity = TrackProblemSeverity.Warning,
                Title = "Race data out of date",
                Source = "Race data",
                Message = "The track changed after the race data was edited. The edited AI nodes, zones, lights and objects will be exported as they are.",
                Suggestion = "Open Race data (F6) and regenerate if they no longer fit. Instances, force fields and models are kept."
            });
        }

        if (race != null && race.Enabled)
        {
            var missing = FindMissingModel(race);
            if (missing != null)
            {
                missing.Message = missing.Message.Replace(" Nothing was exported.", string.Empty);
                result.Problems.Add(missing);
            }
        }
    }

    /// <summary>A model used by edited race data that can't be found. Checked before export touches the level folder.</summary>
    public static TrackProblem FindMissingModel(RaceData overrides)
    {
        if (overrides == null)
            return null;
        foreach (var model in overrides.Models)
        {
            if (string.IsNullOrEmpty(model.SourcePath) || !System.IO.File.Exists(model.SourcePath))
            {
                return new TrackProblem
                {
                    Kind = TrackProblemKind.MissingModel,
                    Severity = TrackProblemSeverity.Error,
                    Title = "Model file missing",
                    Source = "Race data",
                    Message = $"The model {model.FileName} used by instances wasn't found at {model.SourcePath}. Nothing was exported.",
                    Suggestion = "Put the file back, or import it again in Race data, Instances layer."
                };
            }
        }
        return null;
    }

    /// <summary>
    /// Open ends, height steps and separate sections, from the link points of every placed module.
    /// <paramref name="layoutProblem"/> is the lap check's result (null when the lap closes).
    /// </summary>
    private static void CheckConnections(EditorTrack track, Result result, TrackProblem layoutProblem)
    {
        var placements = new List<ModulePlacement>();
        ConnectivityReport report;
        try
        {
            report = ConnectivityService.Analyze(track, placements);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Connection check skipped: {ex}");
            return;
        }
        result.Connections = report;
        if (placements.Count == 0)
            return;

        Vector2Int RootOf(int i) => placements[i].RootCell.Position;
        string Describe(int i) => ConnectivityService.DescribeModule(placements[i]);
        bool lapCloses = layoutProblem == null;
        Vector2Int? breakCell = layoutProblem?.Cell;

        // sections that don't touch the one with the start grid
        if (report.ComponentCount > 1 && report.MainComponent >= 0)
        {
            for (int c = 0; c < report.ComponentCount; c++)
            {
                if (c == report.MainComponent)
                    continue;
                var members = Enumerable.Range(0, placements.Count).Where(i => report.Component[i] == c).ToList();
                var cells = members.Select(RootOf).OrderBy(v => v.y).ThenBy(v => v.x).ToList();
                string other = report.HasStart ? "the track with the start grid" : "the largest part of the track";
                result.Problems.Add(new TrackProblem
                {
                    Kind = TrackProblemKind.DisconnectedSection,
                    Severity = TrackProblemSeverity.Warning,
                    Title = members.Count == 1 ? "Loose module" : $"Separate section of {members.Count} modules",
                    Source = "Connections",
                    Cell = cells[0],
                    RelatedCells = cells,
                    Message = members.Count == 1
                        ? $"{Describe(members[0])} doesn't connect to {other}."
                        : $"{members.Count} modules around {cells[0].x}, {cells[0].y} don't connect to {other}.",
                    Suggestion = "Join them to the lap, or delete them. Unconnected pieces are exported as scenery: the lap, the AI and position tracking don't use them. Ignore this if they're decoration."
                });
            }
        }

        // steps between pieces of the main section
        foreach (var joint in report.Joints)
        {
            if (joint.Kind != JointKind.Step || joint.Internal || report.Component[joint.A.Placement] != report.MainComponent)
                continue;
            int upper = joint.Upper.Placement, lower = joint.Lower.Placement;
            bool half = joint.Levels < 0.75f;
            result.Problems.Add(new TrackProblem
            {
                Kind = TrackProblemKind.HeightStep,
                Severity = lapCloses ? TrackProblemSeverity.Info : TrackProblemSeverity.Warning,
                Title = half ? "Banked edge meets level track" : $"Height step of {ConnectivityService.FormatLevels(joint.Levels)}",
                Source = "Connections",
                Cell = RootOf(lower),
                RelatedCells = new List<Vector2Int> { RootOf(upper), RootOf(lower) },
                Message = $"{Describe(upper)} is {ConnectivityService.FormatLevels(joint.Levels)} higher than {Describe(lower)} where they meet.",
                Suggestion = half
                    ? "Put a bank in/out piece between banked and level track."
                    : lapCloses
                        ? "The lap drops down here, so it can't be driven the other way. Use a ramp if that isn't intended."
                        : "The lap can only continue downhill over a step. Raise or lower one piece, or use a ramp."
            });
        }

        // open ends of the main section
        var open = report.Joints.Where(j => j.Kind == JointKind.Open && report.Component[j.A.Placement] == report.MainComponent).ToList();
        int listed = 0;
        for (int index = 0; index < open.Count; index++)
        {
            var joint = open[index];
            var cell = RootOf(joint.A.Placement);
            if (!lapCloses && breakCell.HasValue && breakCell.Value == cell)
                continue; // the lap check already points here
            if (listed == 12)
            {
                result.Problems.Add(new TrackProblem
                {
                    Kind = TrackProblemKind.OpenEnd,
                    Severity = TrackProblemSeverity.Info,
                    Title = "More open ends",
                    Source = "Connections",
                    Message = $"{open.Count - index} more open ends aren't listed. They're marked red in the viewport.",
                });
                break;
            }
            listed++;
            string facing = joint.A.HasFacing ? $" (towards {joint.A.FacingX}, {joint.A.FacingY})" : string.Empty;
            result.Problems.Add(new TrackProblem
            {
                Kind = TrackProblemKind.OpenEnd,
                Severity = lapCloses ? TrackProblemSeverity.Info : TrackProblemSeverity.Warning,
                Title = lapCloses ? "Unused opening" : "Track ends here",
                Source = "Connections",
                Cell = cell,
                RelatedCells = new List<Vector2Int> { cell },
                Message = $"{Describe(joint.A.Placement)} leads nowhere{facing}.",
                Suggestion = lapCloses
                    ? "The lap is closed, so this opening isn't part of it (for example the unused road of a crossroad or bridge). Cars leaving the lap here drive off the track."
                    : "Continue the track from here at the same height."
            });
        }
    }

    /// <summary>Edited race data to export instead of generated data, or null.</summary>
    public static RaceData GetExportOverrides(EditorTrack track)
    {
        try
        {
            var race = RaceData.FromTrack(track);
            return race != null && race.Enabled ? race : null;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Edited race data ignored: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Start grid, loop and AI checks. Runs <see cref="TrackExporter.Initialize"/> and
    /// <see cref="TrackExporter.CreateAINodes"/> on the exporter. Returns null if the layout is valid.
    /// </summary>
    public static TrackProblem CheckLayout(TrackExporter exporter)
    {
        exporter.Initialize();

        int startGridCount = exporter.GetStartGridCount();
        if (startGridCount != 1)
        {
            return new TrackProblem
            {
                Kind = startGridCount == 0 ? TrackProblemKind.MissingStartGrid : TrackProblemKind.MultipleStartGrids,
                Severity = TrackProblemSeverity.Error,
                Title = startGridCount == 0 ? "No start grid" : "Several start grids",
                Source = "Lap check",
                Message = startGridCount == 0 ? "The track has no start grid." : $"The track has {startGridCount} start grids. It needs exactly one.",
                Suggestion = startGridCount == 0 ? "Place a Start Grid module (first card in the module library)." : "Delete all but one start grid."
            };
        }

        if (!exporter.TrackFormsLoop)
        {
            Vector2Int? cell = null;
            if (exporter.ZoneSequence.Count > 0)
                cell = exporter.Zones[exporter.ZoneSequence.Last().ZoneID].CellCoordinate;

            return new TrackProblem
            {
                Kind = TrackProblemKind.NoLoop,
                Severity = TrackProblemSeverity.Error,
                Cell = cell,
                LastZoneIsPipe = exporter.LastZoneIsPipe,
                HasZoneSequence = exporter.ZoneSequence.Count > 0,
                Title = "Lap isn't closed",
                Source = "Lap check",
                Message = exporter.LastZoneIsPipe
                    ? "The track doesn't form a closed loop: a pipe isn't connected properly."
                    : "The track doesn't form a closed loop. Driving from the start grid, the track breaks off at the highlighted cell.",
                Suggestion = exporter.LastZoneIsPipe
                    ? "Pipes only join pipes at exactly the same height."
                    : "Connect the next piece at the same height, or use a ramp to change height."
            };
        }

        exporter.CreateAINodes();
        if (!exporter.AIIsValid)
        {
            return new TrackProblem
            {
                Kind = TrackProblemKind.InvalidAIPath,
                Severity = TrackProblemSeverity.Error,
                Cell = exporter.LastAICell,
                LastZoneIsPipe = exporter.LastZoneIsPipe,
                HasZoneSequence = exporter.ZoneSequence.Count > 0,
                Title = "Racing line can't be completed",
                Source = "Lap check",
                Message = "The racing line can't be completed at the highlighted cell.",
                Suggestion = "Check the connection there, for example a step in height. Replace steps with ramps."
            };
        }

        return null;
    }
}

/// <summary>
/// The export pipeline as a coroutine with progress reporting. Every step is guarded so a
/// failure (missing permissions, disk full, unexpected data) ends the job with an error
/// instead of leaving the interface stuck mid-export.
/// </summary>
public class TrackExportJob
{
    private readonly EditorTrack track;
    private readonly TrackUnitFile unitFile;
    private readonly float scale;

    public float Progress { get; private set; }
    public string Stage { get; private set; } = "Preparing";
    public bool IsDone { get; private set; }
    public bool Succeeded { get; private set; }
    public TrackProblem Problem { get; private set; }

    public string OutputFolder { get; private set; }
    /// <summary>Where the previous contents of the level folder were copied, or null if it was empty.</summary>
    public string BackupFolder { get; private set; }
    public bool ReverseExported { get; private set; }
    public float LapLength { get; private set; }
    public TimeSpan Duration { get; private set; }

    public TrackExportJob(EditorTrack track, TrackUnitFile unitFile, float scale)
    {
        this.track = track;
        this.unitFile = unitFile;
        this.scale = scale;
    }

    /// <summary>
    /// Runs the export. Yield-iterate it from a coroutine; check <see cref="Succeeded"/> afterwards.
    /// </summary>
    public IEnumerator Run()
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var inner = RunSteps();
        while (true)
        {
            bool moved;
            try
            {
                moved = inner.MoveNext();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Export failed: {ex}");
                Fail(new TrackProblem
                {
                    Kind = TrackProblemKind.ExportFailed,
                    Severity = TrackProblemSeverity.Error,
                    Message = $"Export failed during \"{Stage}\": {Unwrap(ex).Message}"
                });
                yield break;
            }

            if (!moved)
                break;
            yield return inner.Current;
        }

        Duration = stopwatch.Elapsed;
        if (!IsDone)
        {
            Succeeded = true;
            IsDone = true;
            Progress = 1f;
            Stage = "Finished";
        }
    }

    private void Fail(TrackProblem problem)
    {
        Problem = problem;
        Succeeded = false;
        IsDone = true;
    }

    private static Exception Unwrap(Exception ex)
    {
        while ((ex is AggregateException || ex is System.Reflection.TargetInvocationException) && ex.InnerException != null)
            ex = ex.InnerException;
        return ex;
    }

    private IEnumerator RunSteps()
    {
        var perfLogger = new PerfTimeLogger("Export:Total");
        Progress = 0f;

        // If there's no compiled levels folder, exit right here and now
        if (FileHelper.GetCompiledLevelsPath() == null)
        {
            Fail(new TrackProblem
            {
                Kind = TrackProblemKind.NoGameFolder,
                Severity = TrackProblemSeverity.Error,
                Message = "No Re-Volt installation was found. The editor folder must be inside the game folder (Re-Volt\\editor) to export."
            });
            yield break;
        }

        Stage = "Checking layout";
        var exporter = new TrackExporter(track, unitFile, scale)
        {
            Overrides = TrackValidator.GetExportOverrides(track)
        };
        var missingModel = TrackValidator.FindMissingModel(exporter.Overrides);
        if (missingModel != null)
        {
            Fail(missingModel);
            yield break;
        }
        var layoutProblem = TrackValidator.CheckLayout(exporter); // start grid, closed loop, racing line
        if (layoutProblem != null)
        {
            Fail(layoutProblem);
            yield break;
        }

        Progress = 0.1f;
        yield return null;

        LapLength = exporter.TrackLength;

        // Track is valid
        // Create reversed exporter
        var reverseExporter = new TrackExporter(track, unitFile, true, scale);
        reverseExporter.Initialize();
        reverseExporter.CreateAINodes();

        if (!reverseExporter.TrackFormsLoop || !reverseExporter.AIIsValid)
            reverseExporter = null; // can't export reverse, set to null
        ReverseExported = reverseExporter != null;

        // Keep a copy of what's in the level folder now: exporting replaces everything in it
        Stage = "Backing up the previous export";
        if (AppSettings.Data.BackupBeforeExport)
        {
            try
            {
                BackupFolder = ExportBackups.Backup(exporter.ExportPath, exporter.TrackFolderName);
            }
            catch (Exception ex)
            {
                Fail(new TrackProblem
                {
                    Kind = TrackProblemKind.ExportFailed,
                    Severity = TrackProblemSeverity.Error,
                    Title = "Backup failed",
                    Source = "Export",
                    Message = $"The existing level folder couldn't be backed up ({ex.Message}). Nothing was exported.",
                    Suggestion = "Free some disk space, or turn off backups in Preferences if you don't need them."
                });
                yield break;
            }
        }

        // Create and empty destination folder
        Stage = "Preparing folder";
        exporter.CreateTrackFolder();
        exporter.EmptyTrackFolderContents();
        reverseExporter?.CreateTrackFolder();
        OutputFolder = exporter.ExportPath;

        // Write the AI nodes
        Stage = "Writing AI nodes";
        exporter.WriteAINodes();
        reverseExporter?.WriteAINodes();

        Progress = 0.2f;
        yield return null;

        // Create world and collision file
        Stage = "Building world and collision";
        var worldTask = Task.Run(() =>
        {
            exporter.CreateWorld();
            exporter.WriteWorldFile();
        });
        var collisionTask = Task.Run(() =>
        {
            exporter.CreateCollision();
            exporter.WriteCollision();
        });

        while (!worldTask.IsCompleted || !collisionTask.IsCompleted)
        {
            //0.2-0.5
            Progress = 0.2f + (worldTask.IsCompleted ? 0.15f : 0f) + (collisionTask.IsCompleted ? 0.15f : 0f);
            yield return null;
        }

        // previously a failed task went unnoticed and a partial track was written
        if (worldTask.IsFaulted)
            throw worldTask.Exception;
        if (collisionTask.IsFaulted)
            throw collisionTask.Exception;

        // Save zones, pos nodes, etc
        Stage = "Writing track data";
        exporter.WritePosNodesFile();
        exporter.WriteZonesFile();

        reverseExporter?.WritePosNodesFile();
        reverseExporter?.WriteZonesFile();

        exporter.WriteLightsFile();
        exporter.WriteObjectsFile();

        Stage = "Writing instances and force fields";
        exporter.WriteCustomContent(reverseExporter);

        Progress = 0.7f;
        yield return null;

        // Finally, copy textures and write info file
        Stage = "Copying textures";
        exporter.CopyBitmaps();
        exporter.WriteInfoFile();

        perfLogger.Log("Total");
        Progress = 1f;
        yield return null;
    }
}

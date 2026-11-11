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
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// How the pieces of the open track connect, worked out from the link points of each module's zones
/// in the track unit file (see <see cref="TrackConnectivity"/>). The report is cached per track revision.
/// </summary>
public static class ConnectivityService
{
    private static TrackUnitFile shapesSource;
    private static ModuleShape[] shapes;

    private static ConnectivityReport cached;
    private static readonly List<ModulePlacement> cachedPlacements = new List<ModulePlacement>();
    private static int cachedRevision = int.MinValue;
    private static EditorTrack cachedTrack;

    /// <summary>Placements of the cached report, in report order.</summary>
    public static IReadOnlyList<ModulePlacement> Placements => cachedPlacements;

    public static bool IsPipe(int moduleIndex)
    {
        // the same pieces the exporter welds as pipes
        return moduleIndex == (int)Modules.ID.TWM_PIPE_2 || moduleIndex == (int)Modules.ID.TWM_PIPEC_2 || moduleIndex == (int)Modules.ID.TWM_PIPE_20_2;
    }

    public static ModuleShape ShapeOf(int moduleIndex)
    {
        var unit = TrackEditor.TrackUnit;
        if (unit == null || moduleIndex < 0 || moduleIndex >= unit.Modules.Count)
            return null;

        if (shapesSource != unit || shapes == null)
        {
            shapesSource = unit;
            shapes = new ModuleShape[unit.Modules.Count];
        }

        var shape = shapes[moduleIndex];
        if (shape != null)
            return shape;

        var module = unit.Modules[moduleIndex];
        shape = new ModuleShape { IsPipe = IsPipe(moduleIndex) };
        foreach (var instance in module.Instances)
            shape.Cells.Add((instance.Position.x, instance.Position.y));
        foreach (var zone in module.Zones)
            shape.Zones.Add((ToPoint(zone.LinkPositions[0]), ToPoint(zone.LinkPositions[1])));
        shapes[moduleIndex] = shape;
        return shape;
    }

    private static ConnectorPoint ToPoint(Vector3 v) => new ConnectorPoint(v.x, v.y, v.z);

    /// <summary>Game space (Y down, 1000 per cell) to the editor's world space (Y up, 1 per cell).</summary>
    public static Vector3 ToEditor(ConnectorPoint p) => new Vector3(p.X / TrackConnectivity.CellSize, -p.Y / TrackConnectivity.CellSize, p.Z / TrackConnectivity.CellSize);

    public static int LevelOf(ModulePlacement placement) => Mathf.RoundToInt(placement.Elevation / EditorConstants.ElevationStep);

    public static PlacedModule ToPlaced(ModulePlacement placement)
    {
        var root = placement.RootCell != null ? placement.RootCell.Position : placement.Position;
        return new PlacedModule(placement.ModuleIndex, root.x, root.y, ((placement.Rotation % 4) + 4) % 4, LevelOf(placement));
    }

    private static bool IsStart(int moduleIndex) => moduleIndex == (int)Modules.ID.TWM_START;

    /// <summary>Analyzes a track now. <paramref name="placements"/> receives the placements in report order.</summary>
    public static ConnectivityReport Analyze(EditorTrack track, List<ModulePlacement> placements)
    {
        placements.Clear();
        var placed = new List<PlacedModule>();
        foreach (var placement in track.GetAllModuleRootPlacements())
        {
            placements.Add(placement);
            placed.Add(ToPlaced(placement));
        }
        return TrackConnectivity.Analyze(placed, ShapeOf, IsStart);
    }

    /// <summary>The report for the open track, recomputed when <paramref name="revision"/> changes.</summary>
    public static ConnectivityReport Current(int revision)
    {
        var track = TrackEditor.Track;
        if (track == null || TrackEditor.TrackUnit == null)
            return null;
        if (cached == null || revision != cachedRevision || track != cachedTrack)
        {
            cached = Analyze(track, cachedPlacements);
            cachedRevision = revision;
            cachedTrack = track;
        }
        return cached;
    }

    /// <summary>Report index of a placement in the cached report, or -1.</summary>
    public static int IndexOf(ModulePlacement placement) => cachedPlacements.IndexOf(placement);

    public static string DescribeModule(ModulePlacement placement)
    {
        var root = placement.RootCell != null ? placement.RootCell.Position : placement.Position;
        return $"{ModuleCatalog.GetDisplayName(placement.ModuleIndex)} at {root.x}, {root.y}";
    }

    public static string FormatLevels(float levels)
    {
        if (levels < 0.75f)
            return "half a level";
        float rounded = Mathf.Round(levels * 2f) / 2f;
        return Mathf.Approximately(rounded, 1f) ? "1 level" : $"{rounded:0.#} levels";
    }
}

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

using System;
using System.Collections.Generic;

namespace ReVolt.Track
{
    /// <summary>A point in game units (1000 per grid cell, Y points down).</summary>
    public struct ConnectorPoint
    {
        public float X, Y, Z;

        public ConnectorPoint(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public override string ToString() => $"({X:0}, {Y:0}, {Z:0})";
    }

    /// <summary>
    /// What the connectivity check needs to know about a module definition: its footprint and the
    /// two link points of each drivable zone (from the track unit file). The exporter chains zones
    /// whose link points meet to build the lap, so these points are where pieces really connect.
    /// </summary>
    public sealed class ModuleShape
    {
        public readonly List<(int X, int Y)> Cells = new List<(int X, int Y)>();
        public readonly List<(ConnectorPoint A, ConnectorPoint B)> Zones = new List<(ConnectorPoint A, ConnectorPoint B)>();

        /// <summary>Pipe pieces only connect to other pipes at exactly the same height.</summary>
        public bool IsPipe;
    }

    /// <summary>A placed module: module id, root cell, quarter turns and height in elevation steps.</summary>
    public struct PlacedModule
    {
        public int ModuleIndex;
        public int X;
        public int Y;
        public int Rotation;
        public int Level;

        public PlacedModule(int moduleIndex, int x, int y, int rotation, int level)
        {
            ModuleIndex = moduleIndex;
            X = x;
            Y = y;
            Rotation = rotation;
            Level = level;
        }
    }

    public enum JointKind
    {
        /// <summary>Two link points meet at the same height.</summary>
        Connected,
        /// <summary>A launch point (jump exit) meets a lower piece; this is how jumps are built.</summary>
        Jump,
        /// <summary>Two link points meet at different heights. The lap check only continues downhill across a step.</summary>
        Step,
        /// <summary>A link point with nothing attached.</summary>
        Open
    }

    /// <summary>One end of a drivable zone, in game space.</summary>
    public sealed class ConnectorEnd
    {
        public int Placement;     // index into the analyzed placement list
        public int Zone;          // global zone index
        public int Link;          // 0 or 1
        public ConnectorPoint Position;
        public bool Launch;       // a jump exit: designed to drop onto anything lower
        public bool Pipe;

        /// <summary>The grid cell this end opens into, if it lies on the edge of its module.</summary>
        public bool HasFacing;
        public int FacingX, FacingY;
    }

    public sealed class Joint
    {
        public JointKind Kind;
        public ConnectorEnd A;
        public ConnectorEnd B;     // null for open ends

        /// <summary>Height difference in elevation steps (always positive; bank edges differ by half a step).</summary>
        public float Levels;

        /// <summary>Joint between two zones of the same module (e.g. inside a large corner).</summary>
        public bool Internal => B != null && A.Placement == B.Placement;

        /// <summary>For steps and jumps: the end on the higher side.</summary>
        public ConnectorEnd Upper => B == null || A.Position.Y <= B.Position.Y ? A : B;
        public ConnectorEnd Lower => B == null ? null : Upper == A ? B : A;
    }

    public sealed class ConnectivityReport
    {
        public readonly List<PlacedModule> Placements = new List<PlacedModule>();
        public readonly List<ConnectorEnd> Ends = new List<ConnectorEnd>();
        public readonly List<Joint> Joints = new List<Joint>();

        /// <summary>Which placement covers a cell.</summary>
        public readonly Dictionary<(int X, int Y), int> Occupancy = new Dictionary<(int X, int Y), int>();

        /// <summary>Connected section of each placement (pieces joined through any link, including steps).</summary>
        public int[] Component = Array.Empty<int>();
        public int[] ComponentSizes = Array.Empty<int>();
        public int ComponentCount;

        /// <summary>The section with the start grid, or the largest section if there is none. -1 when empty.</summary>
        public int MainComponent = -1;
        public bool HasStart;

        internal readonly Dictionary<long, List<ConnectorEnd>> EndsByPoint = new Dictionary<long, List<ConnectorEnd>>();

        public int ConnectionCount { get; internal set; }
        public int OpenCount { get; internal set; }
        public int StepCount { get; internal set; }
        public int JumpCount { get; internal set; }

        public IEnumerable<ConnectorEnd> EndsAt(ConnectorPoint p)
        {
            return EndsByPoint.TryGetValue(TrackConnectivity.PointKey(p), out var list) ? list : (IEnumerable<ConnectorEnd>)Array.Empty<ConnectorEnd>();
        }
    }

    /// <summary>How well a module would connect if placed somewhere (for previews and auto fitting).</summary>
    public sealed class PlacementFit
    {
        public enum MarkerKind { Connects, Jump, Step, Blocks, Open }

        public struct Marker
        {
            public ConnectorPoint Position;
            public MarkerKind Kind;
            public float Levels;
        }

        public int Rotation;
        public int Level;

        public int Connects;     // links that join existing track at the same height
        public int Jumps;
        public int Steps;        // links that meet existing track at a different height
        public int Blocks;       // existing openings this module would close off with a wall
        public int Open;         // links that lead nowhere (yet)

        public readonly List<Marker> Markers = new List<Marker>();

        public int Score => Connects * 4 + Jumps * 3 - Steps * 3 - Blocks * 3;
        public bool HasProblems => Steps > 0 || Blocks > 0;
    }

    /// <summary>
    /// Works out how placed modules connect, from the link points of their zones. This is the same
    /// rule the exporter uses to follow the lap (link points within a quarter unit horizontally;
    /// across a height difference only downhill, pipes only level), applied to every piece at once.
    /// </summary>
    public static class TrackConnectivity
    {
        public const float CellSize = 1000f;      // game units per grid cell
        public const float LevelHeight = 125f;    // game units per elevation step
        public const float LaunchHeight = 3000f;  // link points this far above their module launch cars (the jump exit is at 5000; bridge decks reach 2000)
        private const float LevelTolerance = 1f;

        public static (int X, int Y) RotateCell(int x, int y, int rotation)
        {
            switch (((rotation % 4) + 4) % 4)
            {
                case 1: return (y, -x);
                case 2: return (-x, -y);
                case 3: return (-y, x);
                default: return (x, y);
            }
        }

        /// <summary>Module space to game space, matching the exporter's module matrix.</summary>
        public static ConnectorPoint Transform(ConnectorPoint local, int x, int y, int rotation, int level)
        {
            float rx, rz;
            switch (((rotation % 4) + 4) % 4)
            {
                case 1: rx = local.Z; rz = -local.X; break;
                case 2: rx = -local.X; rz = -local.Z; break;
                case 3: rx = -local.Z; rz = local.X; break;
                default: rx = local.X; rz = local.Z; break;
            }
            return new ConnectorPoint(rx + x * CellSize, local.Y - level * LevelHeight, rz + y * CellSize);
        }

        public static List<(int X, int Y)> Footprint(ModuleShape shape, int x, int y, int rotation)
        {
            var result = new List<(int X, int Y)>(shape.Cells.Count);
            foreach (var c in shape.Cells)
            {
                var r = RotateCell(c.X, c.Y, rotation);
                result.Add((x + r.X, y + r.Y));
            }
            return result;
        }

        internal static long PointKey(ConnectorPoint p)
        {
            // link points sit on multiples of half a cell; a coarse key with an exact check afterwards
            long kx = (long)Math.Round(p.X / 50f);
            long kz = (long)Math.Round(p.Z / 50f);
            return (kx << 32) ^ (kz & 0xFFFFFFFFL);
        }

        private static bool SameSpot(ConnectorPoint a, ConnectorPoint b)
        {
            // the exporter's threshold
            return Math.Abs(a.X - b.X) < 0.25f && Math.Abs(a.Z - b.Z) < 0.25f;
        }

        /// <summary>The cell on the other side of the cell edge a link point lies on, if any.</summary>
        private static bool FindFacing(ConnectorPoint p, Func<int, int, bool> ownCell, out int fx, out int fy)
        {
            fx = fy = 0;
            double cx = p.X / CellSize;
            double cz = p.Z / CellSize;
            bool xHalf = Math.Abs(Math.Abs(cx - Math.Floor(cx)) - 0.5) < 0.01;
            bool zHalf = Math.Abs(Math.Abs(cz - Math.Floor(cz)) - 0.5) < 0.01;
            int rx = (int)Math.Round(cx);
            int rz = (int)Math.Round(cz);

            (int, int) a, b;
            if (xHalf && !zHalf)
            {
                a = ((int)Math.Floor(cx), rz);
                b = ((int)Math.Ceiling(cx), rz);
            }
            else if (zHalf && !xHalf)
            {
                a = (rx, (int)Math.Floor(cz));
                b = (rx, (int)Math.Ceiling(cz));
            }
            else
            {
                return false;
            }

            bool ownA = ownCell(a.Item1, a.Item2);
            bool ownB = ownCell(b.Item1, b.Item2);
            if (ownA == ownB)
                return false;
            (fx, fy) = ownA ? b : a;
            return true;
        }

        private static JointKind Classify(ConnectorEnd a, ConnectorEnd b, out float levels)
        {
            float dy = Math.Abs(a.Position.Y - b.Position.Y);
            levels = dy / LevelHeight;
            if (dy < LevelTolerance)
                return JointKind.Connected;
            if (a.Pipe && b.Pipe)
                return JointKind.Step;
            var upper = a.Position.Y <= b.Position.Y ? a : b;
            return upper.Launch ? JointKind.Jump : JointKind.Step;
        }

        public static ConnectivityReport Analyze(IReadOnlyList<PlacedModule> placements, Func<int, ModuleShape> shapeOf, Func<int, bool> isStart)
        {
            var report = new ConnectivityReport();
            report.Placements.AddRange(placements);

            // footprints
            var shapes = new ModuleShape[placements.Count];
            for (int i = 0; i < placements.Count; i++)
            {
                var p = placements[i];
                shapes[i] = shapeOf(p.ModuleIndex);
                if (shapes[i] == null)
                    continue;
                foreach (var cell in Footprint(shapes[i], p.X, p.Y, p.Rotation))
                    report.Occupancy[cell] = i;
            }

            // link points
            int zoneIndex = 0;
            for (int i = 0; i < placements.Count; i++)
            {
                var p = placements[i];
                var shape = shapes[i];
                if (shape == null)
                    continue;

                int owner = i;
                bool OwnCell(int x, int y) => report.Occupancy.TryGetValue((x, y), out int o) && o == owner;

                foreach (var zone in shape.Zones)
                {
                    for (int link = 0; link < 2; link++)
                    {
                        var local = link == 0 ? zone.A : zone.B;
                        var end = new ConnectorEnd
                        {
                            Placement = i,
                            Zone = zoneIndex,
                            Link = link,
                            Position = Transform(local, p.X, p.Y, p.Rotation, p.Level),
                            Launch = local.Y <= -LaunchHeight,
                            Pipe = shape.IsPipe
                        };
                        end.HasFacing = FindFacing(end.Position, OwnCell, out end.FacingX, out end.FacingY);
                        report.Ends.Add(end);

                        long key = PointKey(end.Position);
                        if (!report.EndsByPoint.TryGetValue(key, out var bucket))
                            report.EndsByPoint[key] = bucket = new List<ConnectorEnd>(2);
                        bucket.Add(end);
                    }
                    zoneIndex++;
                }
            }

            // pair link points that meet, closest heights first
            var paired = new HashSet<ConnectorEnd>();
            var candidates = new List<(ConnectorEnd A, ConnectorEnd B, float Dy)>();
            foreach (var bucket in report.EndsByPoint.Values)
            {
                if (bucket.Count < 2)
                    continue;
                candidates.Clear();
                for (int a = 0; a < bucket.Count; a++)
                {
                    for (int b = a + 1; b < bucket.Count; b++)
                    {
                        if (bucket[a].Zone == bucket[b].Zone || !SameSpot(bucket[a].Position, bucket[b].Position))
                            continue;
                        candidates.Add((bucket[a], bucket[b], Math.Abs(bucket[a].Position.Y - bucket[b].Position.Y)));
                    }
                }
                candidates.Sort((x, y) => x.Dy.CompareTo(y.Dy));
                foreach (var (a, b, _) in candidates)
                {
                    if (paired.Contains(a) || paired.Contains(b))
                        continue;
                    paired.Add(a);
                    paired.Add(b);
                    var kind = Classify(a, b, out float levels);
                    report.Joints.Add(new Joint { Kind = kind, A = a, B = b, Levels = levels });
                }
            }

            foreach (var end in report.Ends)
            {
                if (!paired.Contains(end))
                    report.Joints.Add(new Joint { Kind = JointKind.Open, A = end });
            }

            // sections
            var parent = new int[placements.Count];
            for (int i = 0; i < parent.Length; i++)
                parent[i] = i;
            int Find(int x)
            {
                while (parent[x] != x)
                {
                    parent[x] = parent[parent[x]];
                    x = parent[x];
                }
                return x;
            }

            foreach (var joint in report.Joints)
            {
                if (joint.B == null)
                    continue;
                int ra = Find(joint.A.Placement), rb = Find(joint.B.Placement);
                if (ra != rb)
                    parent[ra] = rb;

                if (joint.Internal)
                    continue;
                switch (joint.Kind)
                {
                    case JointKind.Connected: report.ConnectionCount++; break;
                    case JointKind.Jump: report.JumpCount++; break;
                    case JointKind.Step: report.StepCount++; break;
                }
            }
            foreach (var joint in report.Joints)
            {
                if (joint.B == null)
                    report.OpenCount++;
            }

            var ids = new Dictionary<int, int>();
            report.Component = new int[placements.Count];
            for (int i = 0; i < placements.Count; i++)
            {
                int root = Find(i);
                if (!ids.TryGetValue(root, out int id))
                    ids[root] = id = ids.Count;
                report.Component[i] = id;
            }
            report.ComponentCount = ids.Count;
            report.ComponentSizes = new int[ids.Count];
            foreach (int c in report.Component)
                report.ComponentSizes[c]++;

            for (int i = 0; i < placements.Count; i++)
            {
                if (isStart != null && isStart(placements[i].ModuleIndex))
                {
                    report.MainComponent = report.Component[i];
                    report.HasStart = true;
                    break;
                }
            }
            if (report.MainComponent < 0 && report.ComponentCount > 0)
            {
                int best = 0;
                for (int c = 1; c < report.ComponentCount; c++)
                {
                    if (report.ComponentSizes[c] > report.ComponentSizes[best])
                        best = c;
                }
                report.MainComponent = best;
            }

            return report;
        }

        /// <summary>
        /// How a module would connect to the existing track if placed at (x, y) with the given rotation
        /// and level. Modules it would replace (overlapping cells) are ignored, like placing does.
        /// </summary>
        public static PlacementFit Evaluate(ConnectivityReport existing, ModuleShape shape, int x, int y, int rotation, int level)
        {
            var fit = new PlacementFit { Rotation = ((rotation % 4) + 4) % 4, Level = level };
            if (shape == null)
                return fit;

            var footprint = new HashSet<(int X, int Y)>(Footprint(shape, x, y, rotation));
            var replaced = new HashSet<int>();
            foreach (var cell in footprint)
            {
                if (existing.Occupancy.TryGetValue(cell, out int p))
                    replaced.Add(p);
            }

            // link points of the new module, minus the ones joined inside it
            var ends = new List<(ConnectorPoint Pos, bool Launch)>();
            foreach (var zone in shape.Zones)
            {
                ends.Add((Transform(zone.A, x, y, rotation, level), zone.A.Y <= -LaunchHeight));
                ends.Add((Transform(zone.B, x, y, rotation, level), zone.B.Y <= -LaunchHeight));
            }
            var internalEnds = new HashSet<int>();
            for (int a = 0; a < ends.Count; a++)
            {
                for (int b = a + 1; b < ends.Count; b++)
                {
                    if (a / 2 != b / 2 && SameSpot(ends[a].Pos, ends[b].Pos) && Math.Abs(ends[a].Pos.Y - ends[b].Pos.Y) < LevelTolerance)
                    {
                        internalEnds.Add(a);
                        internalEnds.Add(b);
                    }
                }
            }

            var matchedExisting = new HashSet<ConnectorEnd>();
            for (int i = 0; i < ends.Count; i++)
            {
                if (internalEnds.Contains(i))
                    continue;
                var pos = ends[i].Pos;

                ConnectorEnd best = null;
                float bestDy = float.MaxValue;
                foreach (var other in existing.EndsAt(pos))
                {
                    if (replaced.Contains(other.Placement) || !SameSpot(other.Position, pos))
                        continue;
                    float dy = Math.Abs(other.Position.Y - pos.Y);
                    if (dy < bestDy)
                    {
                        best = other;
                        bestDy = dy;
                    }
                }

                if (best == null)
                {
                    fit.Open++;
                    fit.Markers.Add(new PlacementFit.Marker { Position = pos, Kind = PlacementFit.MarkerKind.Open });
                    continue;
                }

                matchedExisting.Add(best);
                var probe = new ConnectorEnd { Position = pos, Launch = ends[i].Launch, Pipe = shape.IsPipe };
                var kind = Classify(probe, best, out float levels);
                var marker = new PlacementFit.Marker { Position = pos, Levels = levels };
                switch (kind)
                {
                    case JointKind.Connected: fit.Connects++; marker.Kind = PlacementFit.MarkerKind.Connects; break;
                    case JointKind.Jump: fit.Jumps++; marker.Kind = PlacementFit.MarkerKind.Jump; break;
                    default: fit.Steps++; marker.Kind = PlacementFit.MarkerKind.Step; break;
                }
                fit.Markers.Add(marker);
            }

            // existing openings that lead into the new module but meet none of its link points
            foreach (var end in existing.Ends)
            {
                if (!end.HasFacing || replaced.Contains(end.Placement) || matchedExisting.Contains(end))
                    continue;
                if (!footprint.Contains((end.FacingX, end.FacingY)))
                    continue;
                // whatever this link joins in that cell today is replaced, so it would end at a wall
                fit.Blocks++;
                fit.Markers.Add(new PlacementFit.Marker { Position = end.Position, Kind = PlacementFit.MarkerKind.Blocks });
            }

            return fit;
        }

        /// <summary>
        /// Finds the rotation and level that connect a module best to the openings around (x, y).
        /// Returns null if no rotation or level does better than the current one (or nothing is nearby).
        /// </summary>
        public static PlacementFit FindBestFit(ConnectivityReport existing, ModuleShape shape, int x, int y,
                                               int currentRotation, int currentLevel, int maxLevel)
        {
            if (shape == null)
                return null;

            var current = Evaluate(existing, shape, x, y, currentRotation, currentLevel);
            PlacementFit best = current;

            for (int rotation = 0; rotation < 4; rotation++)
            {
                // levels at which one of this module's links would line up with a neighbouring opening
                var levels = new SortedSet<int> { currentLevel };
                var footprint = new HashSet<(int X, int Y)>(Footprint(shape, x, y, rotation));
                var replaced = new HashSet<int>();
                foreach (var cell in footprint)
                {
                    if (existing.Occupancy.TryGetValue(cell, out int occupant))
                        replaced.Add(occupant);
                }
                foreach (var end in existing.Ends)
                {
                    if (!end.HasFacing || replaced.Contains(end.Placement) || !footprint.Contains((end.FacingX, end.FacingY)))
                        continue;
                    foreach (var zone in shape.Zones)
                    {
                        foreach (var local in new[] { zone.A, zone.B })
                        {
                            var world = Transform(local, x, y, rotation, 0);
                            if (!SameSpot(world, end.Position))
                                continue;
                            int level = (int)Math.Round((world.Y - end.Position.Y) / LevelHeight);
                            if (level >= 0 && level <= maxLevel)
                                levels.Add(level);
                        }
                    }
                }

                foreach (int level in levels)
                {
                    if (rotation == current.Rotation && level == currentLevel)
                        continue;
                    var fit = Evaluate(existing, shape, x, y, rotation, level);
                    if (fit.Score > best.Score || (fit.Score == best.Score && best != current && IsCloser(fit, best, currentRotation, currentLevel)))
                        best = fit;
                }
            }

            return best == current || best.Score <= current.Score ? null : best;
        }

        private static bool IsCloser(PlacementFit a, PlacementFit b, int rotation, int level)
        {
            int Distance(PlacementFit f)
            {
                int turns = Math.Abs(f.Rotation - (((rotation % 4) + 4) % 4));
                turns = Math.Min(turns, 4 - turns);
                return turns * 100 + Math.Abs(f.Level - level);
            }
            return Distance(a) < Distance(b);
        }
    }
}

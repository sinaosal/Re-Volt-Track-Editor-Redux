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
    /// <summary>
    /// A placed block as plain data: everything the track file and its sidecar store about it.
    /// Used for moving, rotating, copying and pasting groups of blocks.
    /// </summary>
    public sealed class BlockSpec
    {
        public int ModuleIndex;
        public int X, Y;          // root cell
        public int Rotation;      // quarter turns, 0..3
        public int Level;         // elevation steps
        public SupportStyle? Support;
        public readonly List<(int X, int Y)> Pickups = new List<(int X, int Y)>(); // absolute cells

        public BlockSpec Clone()
        {
            var copy = new BlockSpec
            {
                ModuleIndex = ModuleIndex,
                X = X,
                Y = Y,
                Rotation = Rotation,
                Level = Level,
                Support = Support
            };
            copy.Pickups.AddRange(Pickups);
            return copy;
        }
    }

    /// <summary>Grid math for groups of blocks. Rotations are quarter turns clockwise seen from above.</summary>
    public static class BlockGroup
    {
        /// <summary>
        /// The cell a group turns around: the middle of its bounding box, rounded down to a whole cell
        /// so quarter turns always land on the grid.
        /// </summary>
        public static (int X, int Y) Pivot(IEnumerable<(int X, int Y)> cells)
        {
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            bool any = false;
            foreach (var c in cells)
            {
                any = true;
                minX = Math.Min(minX, c.X);
                minY = Math.Min(minY, c.Y);
                maxX = Math.Max(maxX, c.X);
                maxY = Math.Max(maxY, c.Y);
            }
            if (!any)
                return (0, 0);
            return ((int)Math.Floor((minX + maxX) / 2.0), (int)Math.Floor((minY + maxY) / 2.0));
        }

        public static (int X, int Y) RotateAbout((int X, int Y) cell, (int X, int Y) pivot, int turns)
        {
            var r = TrackConnectivity.RotateCell(cell.X - pivot.X, cell.Y - pivot.Y, turns);
            return (pivot.X + r.X, pivot.Y + r.Y);
        }

        /// <summary>The block after turning the whole group around <paramref name="pivot"/>.</summary>
        public static BlockSpec Rotate(BlockSpec block, (int X, int Y) pivot, int turns)
        {
            var result = block.Clone();
            (result.X, result.Y) = RotateAbout((block.X, block.Y), pivot, turns);
            result.Rotation = (((block.Rotation + turns) % 4) + 4) % 4;
            result.Pickups.Clear();
            foreach (var p in block.Pickups)
                result.Pickups.Add(RotateAbout(p, pivot, turns));
            return result;
        }

        public static BlockSpec Translate(BlockSpec block, int dx, int dy, int dLevel)
        {
            var result = block.Clone();
            result.X += dx;
            result.Y += dy;
            result.Level += dLevel;
            result.Pickups.Clear();
            foreach (var p in block.Pickups)
                result.Pickups.Add((p.X + dx, p.Y + dy));
            return result;
        }

        /// <summary>
        /// Checks that blocks fit: inside the grid, not overlapping each other, and not overlapping
        /// cells for which <paramref name="blocked"/> returns true. Returns the first problem, or null.
        /// </summary>
        public static string CheckFit(IEnumerable<BlockSpec> blocks, Func<int, IEnumerable<(int X, int Y)>> footprintOf,
                                      int width, int height, int maxLevel, Func<int, int, bool> blocked)
        {
            var used = new HashSet<(int X, int Y)>();
            foreach (var block in blocks)
            {
                if (block.Level < 0 || block.Level > maxLevel)
                    return "That's outside the height range.";
                foreach (var local in footprintOf(block.ModuleIndex))
                {
                    var r = TrackConnectivity.RotateCell(local.X, local.Y, block.Rotation);
                    var cell = (block.X + r.X, block.Y + r.Y);
                    if (cell.Item1 < 0 || cell.Item2 < 0 || cell.Item1 >= width || cell.Item2 >= height)
                        return "That's outside the grid.";
                    if (!used.Add(cell))
                        return "Blocks would overlap.";
                    if (blocked != null && blocked(cell.Item1, cell.Item2))
                        return "Other blocks are in the way.";
                }
            }
            return null;
        }
    }
}

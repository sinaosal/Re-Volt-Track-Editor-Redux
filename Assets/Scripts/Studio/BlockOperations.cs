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
using System.Linq;
using UnityEngine;

namespace Studio
{
    /// <summary>
    /// Edits to an already placed block (move, rotate, height, variant, support colors). Each is one
    /// undo step; the block keeps its support colors and the pickups on its cells.
    /// </summary>
    public static class BlockOperations
    {
        private static TrackEditingMode Editing => TrackEditor.GetEditMode<TrackEditingMode>();

        /// <summary>Lock state of the Studio session; locked blocks are never changed by these operations.</summary>
        public static BlockStates States;

        /// <summary>Why the last operation was refused or trimmed (for a toast), or null.</summary>
        public static string LastProblem { get; private set; }

        public static bool IsLocked(ModulePlacement p) => States != null && States.IsLocked(p);
        public static bool IsHidden(ModulePlacement p) => States != null && States.IsHidden(p);

        /// <summary>Replaces blocks one for one, keeping their lock and hidden state.</summary>
        private static List<ModulePlacement> ReplaceGroup(IList<ModulePlacement> blocks, IList<BlockSpec> specs, string label)
        {
            var wasLocked = blocks.Select(IsLocked).ToList();
            var wasHidden = blocks.Select(IsHidden).ToList();
            var result = ApplyGroup(blocks, specs, label, false);
            if (result != null)
                States?.Transfer(blocks, wasLocked, wasHidden, result);
            return result;
        }

        private static bool RefuseLocked(ModulePlacement p)
        {
            if (!IsLocked(p))
                return false;
            LastProblem = "That block is locked";
            TrackEditor.PlaySound(TrackEditor.SndWarning);
            return true;
        }

        /// <summary>Whether a block with these settings would fit, ignoring the block being edited.</summary>
        public static bool Fits(ModulePlacement self, int moduleIndex, Vector2Int root, int rotation)
        {
            var track = TrackEditor.Track;
            foreach (var cell in TrackEditor.GetFootprint(moduleIndex, root, rotation))
            {
                if (cell.x < 0 || cell.y < 0 || cell.x >= track.Width || cell.y >= track.Height)
                    return false;
                var other = track.GetCell(cell).Module;
                if (other != null && other != self)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Replaces <paramref name="placement"/> with a block using the given settings.
        /// Returns the new placement, or null if it doesn't fit.
        /// </summary>
        public static ModulePlacement Replace(ModulePlacement placement, int moduleIndex, Vector2Int root, int rotation, float elevation, string label)
        {
            LastProblem = null;
            if (RefuseLocked(placement))
                return null;
            rotation = ((rotation % 4) + 4) % 4;
            if (!Fits(placement, moduleIndex, root, rotation))
            {
                TrackEditor.PlaySound(TrackEditor.SndWarning);
                return null;
            }

            var track = TrackEditor.Track;
            var oldRoot = placement.RootCell.Position;
            var support = placement.Support;
            var pickups = placement.TouchedCells.Where(c => c.HasPickup).Select(c => c.Position).ToList();
            ModulePlacement result = null;

            TrackEditor.RecordChange(label, () =>
            {
                placement.Delete();
                Editing.PlaceModule(moduleIndex, root, elevation, rotation);

                result = track.GetCell(root).Module;
                result.Support = support;
                result.ApplySupportVisual();

                // pickups move with the block; ones that no longer land on it are dropped
                var shift = root - oldRoot;
                foreach (var cell in pickups)
                {
                    var target = cell + shift;
                    if (result.TouchedCells.Any(c => c.Position == target))
                        track.GetCell(target).HasPickup = true;
                }
                track.MarkDirty();
            });
            return result;
        }

        public static ModulePlacement Move(ModulePlacement p, Vector2Int newRoot)
            => Replace(p, p.ModuleIndex, newRoot, p.Rotation, p.Elevation, "Move block");

        public static ModulePlacement Rotate(ModulePlacement p, int turns)
            => Replace(p, p.ModuleIndex, p.RootCell.Position, p.Rotation + turns, p.Elevation, "Rotate block");

        public static ModulePlacement SetRotation(ModulePlacement p, int rotation)
            => Replace(p, p.ModuleIndex, p.RootCell.Position, rotation, p.Elevation, "Rotate block");

        public static ModulePlacement SetModule(ModulePlacement p, int moduleIndex, string label)
            => Replace(p, moduleIndex, p.RootCell.Position, p.Rotation, p.Elevation, label);

        public static ModulePlacement ChangeHeight(ModulePlacement p, int deltaSteps)
        {
            int steps = Mathf.RoundToInt(p.Elevation / EditorConstants.ElevationStep) + deltaSteps;
            if (steps < 0 || steps > Editing.MaxHeightSteps)
            {
                TrackEditor.PlaySound(TrackEditor.SndWarning);
                return null;
            }
            var result = Replace(p, p.ModuleIndex, p.RootCell.Position, p.Rotation, steps * EditorConstants.ElevationStep,
                                 deltaSteps > 0 ? "Raise block" : "Lower block");
            if (result != null)
                TrackEditor.PlaySound(deltaSteps > 0 ? TrackEditor.SndRaise : TrackEditor.SndLower);
            return result;
        }

        public static void SetSupport(ModulePlacement p, SupportStyle? style)
        {
            if (Nullable.Equals(p.Support, style) || RefuseLocked(p))
                return;
            TrackEditor.RecordChange(style.HasValue ? "Change support color" : "Reset support color", () =>
            {
                p.Support = style;
                p.ApplySupportVisual();
                TrackEditor.Track.MarkDirty();
            });
        }

        /// <summary>Applies a support style to several blocks as one undo step (locked blocks are skipped).</summary>
        public static void SetSupportGroup(IList<ModulePlacement> blocks, SupportStyle? style)
        {
            LastProblem = null;
            var changing = blocks.Where(p => !IsLocked(p) && !Nullable.Equals(p.Support, style)).ToList();
            if (changing.Count == 0)
            {
                if (blocks.Any(IsLocked))
                {
                    LastProblem = "Locked blocks can't be changed";
                    TrackEditor.PlaySound(TrackEditor.SndWarning);
                }
                return;
            }
            string what = changing.Count == 1 ? "support color" : "support colors";
            TrackEditor.RecordChange(style.HasValue ? $"Change {what}" : $"Reset {what}", () =>
            {
                foreach (var p in changing)
                {
                    p.Support = style;
                    p.ApplySupportVisual();
                }
                TrackEditor.Track.MarkDirty();
            });
        }

        /// <summary>Applies a support style to every raised block.</summary>
        public static int SetSupportOnAllRaised(SupportStyle? style)
        {
            var raised = TrackEditor.Track.GetAllModuleRootPlacements().Where(p => p.Elevation > 0f && !IsLocked(p)).ToList();
            TrackEditor.RecordChange("Change support colors", () =>
            {
                foreach (var p in raised)
                {
                    p.Support = style;
                    p.ApplySupportVisual();
                }
                TrackEditor.Track.MarkDirty();
            });
            return raised.Count;
        }

        public static void Delete(ModulePlacement p)
        {
            if (RefuseLocked(p))
                return;
            TrackEditor.RecordChange($"Delete {ModuleCatalog.GetDisplayName(p.ModuleIndex)}", () =>
            {
                p.Delete();
                TrackEditor.Track.MarkDirty();
            });
            TrackEditor.PlaySound(TrackEditor.SndDelete);
        }

        // Groups
        public static int LevelOf(ModulePlacement p) => Mathf.RoundToInt(p.Elevation / EditorConstants.ElevationStep);

        public static BlockSpec ToSpec(ModulePlacement p)
        {
            var root = p.RootCell.Position;
            var spec = new BlockSpec
            {
                ModuleIndex = p.ModuleIndex,
                X = root.x,
                Y = root.y,
                Rotation = ((p.Rotation % 4) + 4) % 4,
                Level = LevelOf(p),
                Support = p.Support
            };
            foreach (var cell in p.TouchedCells)
            {
                if (cell.HasPickup)
                    spec.Pickups.Add((cell.Position.x, cell.Position.y));
            }
            return spec;
        }

        public static IEnumerable<(int X, int Y)> LocalCells(int moduleIndex)
        {
            foreach (var instance in TrackEditor.TrackUnit.Modules[moduleIndex].Instances)
                yield return (instance.Position.x, instance.Position.y);
        }

        /// <summary>Cells a block spec covers.</summary>
        public static List<Vector2Int> Footprint(BlockSpec spec)
            => TrackEditor.GetFootprint(spec.ModuleIndex, new Vector2Int(spec.X, spec.Y), spec.Rotation);

        /// <summary>
        /// Checks whether specs can be placed. Blocks in <paramref name="removing"/> don't count as obstacles;
        /// other blocks do, unless <paramref name="replaceOthers"/> is set (then only locked and hidden ones
        /// do: nothing you can't see is replaced). Returns null if they fit, otherwise the reason.
        /// </summary>
        public static string CheckGroupFit(IEnumerable<BlockSpec> add, ICollection<ModulePlacement> removing, bool replaceOthers)
        {
            var track = TrackEditor.Track;
            return BlockGroup.CheckFit(add, LocalCells, track.Width, track.Height, Editing.MaxHeightSteps, (x, y) =>
            {
                var other = track.GetCell(x, y).Module;
                if (other == null || (removing != null && removing.Contains(other)))
                    return false;
                return !replaceOthers || IsLocked(other) || IsHidden(other);
            });
        }

        /// <summary>
        /// Removes <paramref name="remove"/> and places <paramref name="add"/> as one undo step. Blocks not being
        /// removed are left alone, unless <paramref name="replaceOthers"/> is set (like placing a module does).
        /// Returns the placed blocks, or null (with <see cref="LastProblem"/> set) if they don't fit.
        /// </summary>
        public static List<ModulePlacement> ApplyGroup(IList<ModulePlacement> remove, IList<BlockSpec> add, string label, bool replaceOthers)
        {
            LastProblem = null;
            var track = TrackEditor.Track;
            var removing = new HashSet<ModulePlacement>(remove);

            if (removing.Any(IsLocked))
            {
                LastProblem = "Locked blocks can't be changed";
                TrackEditor.PlaySound(TrackEditor.SndWarning);
                return null;
            }

            LastProblem = CheckGroupFit(add, removing, replaceOthers);
            if (LastProblem != null)
            {
                TrackEditor.PlaySound(TrackEditor.SndWarning);
                return null;
            }

            // pickups: copies must respect the limit
            var replacedByAdd = new HashSet<ModulePlacement>();
            if (replaceOthers)
            {
                foreach (var spec in add)
                {
                    foreach (var cell in Footprint(spec))
                    {
                        var other = track.GetCell(cell).Module;
                        if (other != null && !removing.Contains(other))
                            replacedByAdd.Add(other);
                    }
                }
            }
            int kept = track.Cells.Count(c => c.HasPickup && (c.Module == null || (!removing.Contains(c.Module) && !replacedByAdd.Contains(c.Module))));
            int budget = TrackEditor.UnlimitedMode ? int.MaxValue : Math.Max(0, TrackValidator.MaxPickups - kept);
            int droppedPickups = 0;

            var result = new List<ModulePlacement>(add.Count);
            TrackEditor.RecordChange(label, () =>
            {
                foreach (var p in removing)
                    p.Delete();

                foreach (var spec in add)
                {
                    var root = new Vector2Int(spec.X, spec.Y);
                    Editing.PlaceModule(spec.ModuleIndex, root, spec.Level * EditorConstants.ElevationStep, spec.Rotation);
                    var placed = track.GetCell(root).Module;
                    placed.Support = spec.Support;
                    placed.ApplySupportVisual();
                    result.Add(placed);
                }

                // pickups once every block is down, so a later block can't clear an earlier one's
                for (int i = 0; i < add.Count; i++)
                {
                    foreach (var cell in add[i].Pickups)
                    {
                        var pos = new Vector2Int(cell.X, cell.Y);
                        if (!result[i].TouchedCells.Any(c => c.Position == pos))
                            continue;
                        if (budget <= 0)
                        {
                            droppedPickups++;
                            continue;
                        }
                        track.GetCell(pos).HasPickup = true;
                        budget--;
                    }
                }
                track.MarkDirty();
            });

            if (droppedPickups > 0)
                LastProblem = $"{droppedPickups} pickup(s) left out: the game supports at most {TrackValidator.MaxPickups}";
            return result;
        }

        /// <summary>Moves blocks by whole cells and/or levels. Locked blocks can't move.</summary>
        public static List<ModulePlacement> MoveGroup(IList<ModulePlacement> blocks, Vector2Int delta, int deltaLevel)
        {
            if (blocks.Count == 0 || (delta == Vector2Int.zero && deltaLevel == 0))
                return null;
            var specs = blocks.Select(p => BlockGroup.Translate(ToSpec(p), delta.x, delta.y, deltaLevel)).ToList();
            string what = blocks.Count == 1 ? "block" : $"{blocks.Count} blocks";
            string label = delta == Vector2Int.zero ? (deltaLevel > 0 ? $"Raise {what}" : $"Lower {what}") : $"Move {what}";
            return ReplaceGroup(blocks, specs, label);
        }

        /// <summary>
        /// Turns blocks as a group around <paramref name="pivot"/>, normally the root cell of the block clicked
        /// last. A fixed whole cell keeps turns exact: four turns, or a turn and its opposite, end where they
        /// started (a pivot recomputed from the group's bounds would drift on even-sized groups).
        /// </summary>
        public static List<ModulePlacement> RotateGroup(IList<ModulePlacement> blocks, int turns, Vector2Int pivot)
        {
            if (blocks.Count == 0)
                return null;
            var specs = blocks.Select(p => BlockGroup.Rotate(ToSpec(p), (pivot.x, pivot.y), turns)).ToList();
            var result = ReplaceGroup(blocks, specs, blocks.Count == 1 ? "Rotate block" : $"Rotate {blocks.Count} blocks");
            if (result != null)
                TrackEditor.PlaySound(TrackEditor.SndRotate);
            return result;
        }

        public static void DeleteGroup(IList<ModulePlacement> blocks)
        {
            LastProblem = null;
            var deletable = blocks.Where(p => !IsLocked(p)).ToList();
            if (deletable.Count == 0)
            {
                if (blocks.Count > 0)
                    LastProblem = "Locked blocks can't be deleted";
                TrackEditor.PlaySound(TrackEditor.SndWarning);
                return;
            }
            TrackEditor.RecordChange(deletable.Count == 1 ? $"Delete {ModuleCatalog.GetDisplayName(deletable[0].ModuleIndex)}" : $"Delete {deletable.Count} blocks", () =>
            {
                foreach (var p in deletable)
                    p.Delete();
                TrackEditor.Track.MarkDirty();
            });
            if (deletable.Count < blocks.Count)
                LastProblem = "Locked blocks were left in place";
            TrackEditor.PlaySound(TrackEditor.SndDelete);
        }

        /// <summary>Changes the variant (or surface) of each block; blocks whose new variant doesn't fit stay as they are.</summary>
        public static int ChangeGroupModule(IList<ModulePlacement> blocks, Func<int, int> newModule, string label)
        {
            var remove = new List<ModulePlacement>();
            var add = new List<BlockSpec>();
            foreach (var p in blocks)
            {
                int target = newModule(p.ModuleIndex);
                if (target == p.ModuleIndex || IsLocked(p))
                    continue;
                var spec = ToSpec(p);
                spec.ModuleIndex = target;
                remove.Add(p);
                add.Add(spec);
                // drop it again if the new variant collides with something
                if (CheckGroupFit(add, remove, false) != null)
                {
                    remove.RemoveAt(remove.Count - 1);
                    add.RemoveAt(add.Count - 1);
                }
            }
            if (add.Count == 0)
            {
                TrackEditor.PlaySound(TrackEditor.SndWarning);
                return 0;
            }
            return ReplaceGroup(remove, add, label)?.Count ?? 0;
        }

        /// <summary>Preset support colors offered in the inspector (null = the game's default gradient).</summary>
        public static readonly (string Name, SupportStyle? Style)[] Presets =
        {
            ("Game default (blue gradient by height)", null),
            ("Concrete", new SupportStyle(0x9A9DA3, 0x3E4046)),
            ("Sandstone", new SupportStyle(0xD8B98A, 0x6E5434)),
            ("Brick", new SupportStyle(0xB5523B, 0x4A1E14)),
            ("Moss", new SupportStyle(0x6E9A4A, 0x24361A)),
            ("Ocean", new SupportStyle(0x3A8FD6, 0x0E2A4A)),
            ("Violet", new SupportStyle(0x8A5BD6, 0x2A1650)),
            ("Rose", new SupportStyle(0xE2457A, 0x4A1226)),
            ("Night", new SupportStyle(0x3A3F4A, 0x0E1014)),
            ("Ivory", new SupportStyle(0xF2EEE4, 0x8C877C)),
        };

        /// <summary>The game's default gradient, for display of blocks without custom colors.</summary>
        public static SupportStyle DefaultLook => new SupportStyle(ModulePlacement.ToRgb(EditorConstants.RootColorInGameMax),
                                                                   ModulePlacement.ToRgb(EditorConstants.RootColorInGameMin));
    }
}

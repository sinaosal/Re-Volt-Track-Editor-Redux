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
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Studio
{
    /// <summary>
    /// Inspector content for the blocks selected with the select tool: position, level and rotation as
    /// numbers, variants, support colors, connections and actions. Works on one block or a group.
    /// </summary>
    public sealed class BlockInspector
    {
        private const string TopField = "studio.block.supportTop";
        private const string BottomField = "studio.block.supportBottom";
        private const string XField = "studio.block.x";
        private const string YField = "studio.block.y";
        private const string LevelField = "studio.block.level";

        private readonly StudioShell shell;
        private StudioTheme T => shell.Theme;
        private StudioViewport Viewport => shell.Viewport;

        private string topBuffer, bottomBuffer;
        private bool topFocused, bottomFocused;
        private (Vector2Int root, int selection) bufferOwner = (new Vector2Int(int.MinValue, int.MinValue), -1);

        public BlockInspector(StudioShell shell)
        {
            this.shell = shell;
        }

        public float ContentHeight => T.S(1000f);

        public void Draw(Rect r)
        {
            var blocks = Viewport.SelectedPlacements;
            var p = Viewport.SelectedPlacement;
            if (blocks.Count == 0 || p == null)
            {
                DrawEmpty(r);
                return;
            }

            float y = r.y;
            y = blocks.Count == 1 ? DrawIdentity(r, y, p) : DrawGroupIdentity(r, y, blocks);
            y = DrawVariants(r, y, blocks, p);
            y = DrawTransform(r, y, blocks, p);
            y = DrawSupports(r, y, blocks, p);
            y = DrawConnections(r, y, blocks);
            y = DrawActions(r, y, blocks, p);

            SGUI.Label(new Rect(r.x, y, r.width, T.S(56f)),
                       "Drag blocks or the gizmo arrows to move them. Arrow keys move one cell, R rotates, Shift+click adds, Esc deselects.",
                       T.SmallWrapped, Palette.TextFaint);
        }

        private float Section(Rect r, float y, string title)
        {
            SGUI.SectionHeader(new Rect(r.x, y, r.width, T.S(16f)), title);
            return y + T.S(24f);
        }

        private float DrawIdentity(Rect r, float y, ModulePlacement p)
        {
            y = Section(r, y, BlockOperations.IsLocked(p) ? "Selected block  ·  locked" : "Selected block");
            var thumbRect = new Rect(r.x, y, T.S(96f), T.S(74f));
            SGUI.RoundedOutline(thumbRect, Palette.Raised, BlockOperations.IsLocked(p) ? Palette.Warning : Palette.Accent, T.S(6f));
            var thumb = shell.Thumbnails.Get(p.ModuleIndex);
            if (thumb != null && Event.current.type == EventType.Repaint)
                GUI.DrawTexture(thumbRect.Inset(T.S(1f)), thumb, ScaleMode.ScaleAndCrop, false);
            else
                SGUI.Icon(thumbRect, IconId.Module, Palette.TextFaint);

            var info = new Rect(thumbRect.xMax + T.S(12f), y + T.S(4f), r.width - thumbRect.width - T.S(12f), T.S(20f));
            SGUI.Label(info, SGUI.Ellipsize(ModuleCatalog.GetDisplayName(p.ModuleIndex), T.BodyStrong, info.width), T.BodyStrong);
            info.y += T.S(20f);
            SGUI.Label(info, ModuleCatalog.GetCodeName(p.ModuleIndex), T.Mono, Palette.TextFaint);
            info.y += T.S(20f);
            int pickups = p.TouchedCells.Count(c => c.HasPickup);
            SGUI.Label(info, $"{p.TouchedCells.Count} cell{(p.TouchedCells.Count == 1 ? "" : "s")}" + (pickups > 0 ? $"  ·  {pickups} pickup{(pickups == 1 ? "" : "s")}" : string.Empty),
                       T.Small, Palette.TextDim);
            return y + thumbRect.height + T.S(12f);
        }

        private float DrawGroupIdentity(Rect r, float y, List<ModulePlacement> blocks)
        {
            int locked = blocks.Count(BlockOperations.IsLocked);
            y = Section(r, y, locked > 0 ? $"{blocks.Count} blocks selected  ·  {locked} locked" : $"{blocks.Count} blocks selected");

            // the most used module types, as thumbnails
            var types = blocks.GroupBy(b => b.ModuleIndex).OrderByDescending(g => g.Count()).ToList();
            float size = T.S(44f);
            float gap = T.S(6f);
            int fit = Mathf.Max(1, Mathf.FloorToInt((r.width + gap) / (size + gap)));
            for (int i = 0; i < Mathf.Min(types.Count, fit); i++)
            {
                var cell = new Rect(r.x + i * (size + gap), y, size, size);
                SGUI.RoundedOutline(cell, Palette.Raised, Palette.Border, T.S(5f));
                var thumb = shell.Thumbnails.Get(types[i].Key);
                if (thumb != null && Event.current.type == EventType.Repaint)
                    GUI.DrawTexture(cell.Inset(T.S(1f)), thumb, ScaleMode.ScaleAndCrop, false);
                var badge = new Rect(cell.xMax - T.S(18f), cell.yMax - T.S(16f), T.S(18f), T.S(16f));
                SGUI.Rounded(badge, Palette.Chrome.WithAlpha(0.9f), T.S(4f));
                SGUI.Label(badge, types[i].Count().ToString(), T.Caption, Palette.Text, TextAnchor.MiddleCenter);
                SGUI.Tooltip(cell, $"{ModuleCatalog.GetDisplayName(types[i].Key)} × {types[i].Count()}");
            }
            y += size + T.S(6f);
            int pickups = blocks.Sum(b => b.TouchedCells.Count(c => c.HasPickup));
            SGUI.Label(new Rect(r.x, y, r.width, T.S(18f)),
                       $"{types.Count} module type{(types.Count == 1 ? "" : "s")}  ·  {blocks.Sum(b => b.TouchedCells.Count)} cells" + (pickups > 0 ? $"  ·  {pickups} pickups" : string.Empty),
                       T.Small, Palette.TextDim);
            return y + T.S(28f);
        }

        private float DrawVariants(Rect r, float y, List<ModulePlacement> blocks, ModulePlacement p)
        {
            float bw = (r.width - T.S(16f)) / 3f;
            bool anyVariants = blocks.Any(b => ModuleCatalog.GetVariantChain(b.ModuleIndex).Count > 1);
            bool anySurface = blocks.Any(b => Modules.Lookup.OtherSurface[b.ModuleIndex] != b.ModuleIndex);
            var b = new Rect(r.x, y, bw, T.ControlHeight);
            if (SGUI.Button(b, "Previous", ButtonKind.Secondary, anyVariants, IconId.VariantPrev, "Previous variant  Z"))
                Viewport.ChangeSelectedVariant(false);
            b.x += bw + T.S(8f);
            if (SGUI.Button(b, "Next", ButtonKind.Secondary, anyVariants, IconId.VariantNext, "Next variant  X"))
                Viewport.ChangeSelectedVariant(true);
            b.x += bw + T.S(8f);
            if (SGUI.Button(b, "Surface", ButtonKind.Secondary, anySurface, IconId.Surface, "Toggle surface  T"))
                Viewport.ToggleSelectedSurface();
            return y + T.ControlHeight + T.S(22f);
        }

        private float DrawTransform(Rect r, float y, List<ModulePlacement> blocks, ModulePlacement p)
        {
            bool single = blocks.Count == 1;
            y = Section(r, y, single ? "Position" : "Position (lowest corner of the group)");

            // the origin: the block's root cell, or the group's south-west corner
            Vector2Int origin;
            if (single)
                origin = p.RootCell.Position;
            else
            {
                origin = new Vector2Int(int.MaxValue, int.MaxValue);
                foreach (var block in blocks)
                    foreach (var cell in block.TouchedCells)
                        origin = Vector2Int.Min(origin, cell.Position);
            }

            float half = (r.width - T.S(10f)) * 0.5f;
            var xRow = new Rect(r.x, y, half, T.S(28f));
            var yRow = new Rect(r.x + half + T.S(10f), y, half, T.S(28f));
            SGUI.Label(xRow.TakeLeft(T.S(18f)), "X", T.SmallStrong, new Color(0.90f, 0.33f, 0.29f));
            SGUI.Label(yRow.TakeLeft(T.S(18f)), "Y", T.SmallStrong, new Color(0.30f, 0.62f, 0.91f));
            // typed values belong to this selection; a different selection drops an unfinished edit
            object owner = Viewport.Selection.Version;
            if (SGUI.IntField(xRow.CutLeft(T.S(18f)), XField, origin.x, out int newX, owner))
                Move(blocks, new Vector2Int(newX - origin.x, 0), 0);
            if (SGUI.IntField(yRow.CutLeft(T.S(18f)), YField, origin.y, out int newY, owner))
                Move(blocks, new Vector2Int(0, newY - origin.y), 0);
            y += T.S(36f);

            // nudge pad (fixed compass directions, unlike the arrow keys which follow the view)
            float bs = T.ControlHeight;
            var pad = new Rect(r.x, y, bs * 3f + T.S(8f), bs * 2f + T.S(4f));
            var up = new Rect(pad.x + bs + T.S(4f), pad.y, bs, bs);
            var left = new Rect(pad.x, pad.y + bs + T.S(4f), bs, bs);
            var down = new Rect(up.x, left.y, bs, bs);
            var right = new Rect(up.xMax + T.S(4f), left.y, bs, bs);
            if (Outlined(up, IconId.ChevronUp, "Move north")) Move(blocks, Vector2Int.up, 0);
            if (Outlined(left, IconId.ChevronLeft, "Move west")) Move(blocks, Vector2Int.left, 0);
            if (Outlined(down, IconId.ChevronDown, "Move south")) Move(blocks, Vector2Int.down, 0);
            if (Outlined(right, IconId.ChevronRight, "Move east")) Move(blocks, Vector2Int.right, 0);

            // level
            var editing = TrackEditor.GetEditMode<TrackEditingMode>();
            int level = single ? BlockOperations.LevelOf(p) : blocks.Min(BlockOperations.LevelOf);
            var levelRect = new Rect(pad.xMax + T.S(14f), pad.y, r.width - pad.width - T.S(14f), T.S(18f));
            SGUI.Label(levelRect, single ? "Level" : "Lowest level", T.Small, Palette.TextFaint);
            var levelField = new Rect(levelRect.x, levelRect.yMax + T.S(4f), T.S(64f), T.S(28f));
            if (SGUI.IntField(levelField, LevelField, level, out int newLevel, Viewport.Selection.Version))
                Move(blocks, Vector2Int.zero, Mathf.Clamp(newLevel, 0, editing.MaxHeightSteps) - level);
            var stepUp = new Rect(levelField.xMax + T.S(6f), levelField.y, T.S(28f), T.S(28f));
            if (Outlined(stepUp, IconId.ElevationUp, "Raise  PgUp")) Viewport.RaiseSelection(1);
            if (Outlined(new Rect(stepUp.xMax + T.S(4f), stepUp.y, stepUp.width, stepUp.height), IconId.ElevationDown, "Lower  PgDn")) Viewport.RaiseSelection(-1);
            y += pad.height + T.S(20f);

            // rotation
            y = Section(r, y, single ? "Rotation" : "Rotation (turns the group around its middle)");
            if (single)
            {
                int rotation = ((p.Rotation % 4) + 4) % 4;
                int newRotation = SGUI.Segmented(new Rect(r.x, y, r.width, T.ControlHeight), rotation, new[] { "0°", "90°", "180°", "270°" });
                if (newRotation != rotation)
                    Viewport.RotateSelection(newRotation - rotation);
            }
            else
            {
                float bw = (r.width - T.S(8f)) * 0.5f;
                if (SGUI.Button(new Rect(r.x, y, bw, T.ControlHeight), "Turn left", ButtonKind.Secondary, true, IconId.RotateCCW, "Shift+R"))
                    Viewport.RotateSelection(-1);
                if (SGUI.Button(new Rect(r.x + bw + T.S(8f), y, bw, T.ControlHeight), "Turn right", ButtonKind.Secondary, true, IconId.RotateCW, "R"))
                    Viewport.RotateSelection(1);
            }
            return y + T.ControlHeight + T.S(22f);
        }

        private void Move(List<ModulePlacement> blocks, Vector2Int delta, int levels)
        {
            if (delta == Vector2Int.zero && levels == 0)
                return;
            var moved = BlockOperations.MoveGroup(blocks, delta, levels);
            if (moved != null)
            {
                Viewport.Selection.SetMany(moved.Select(b => b.RootCell.Position).ToList());
                TrackEditor.PlaySound(TrackEditor.SndMenuMove);
            }
            else
            {
                shell.Toast(BlockOperations.LastProblem ?? "There's no room there", IconId.Warning);
            }
        }

        private bool Outlined(Rect r, IconId icon, string tooltip)
        {
            SGUI.RoundedOutline(r, Palette.Raised, Palette.Border);
            return SGUI.IconButton(r, icon, tooltip);
        }

        private void DrawEmpty(Rect r)
        {
            SGUI.Icon(new Rect(r.x, r.y + T.S(30f), r.width, T.S(28f)), IconId.Cursor, Palette.TextFaint, T.S(26f));
            SGUI.Label(new Rect(r.x, r.y + T.S(64f), r.width, T.S(20f)), "Nothing selected", T.BodyStrong, Palette.TextDim, TextAnchor.MiddleCenter);
            SGUI.Label(new Rect(r.x, r.y + T.S(88f), r.width, T.S(64f)),
                       "Click a block to select it. Shift+click or drag a box to select several. Ctrl+A selects everything, Ctrl+L the connected section.",
                       T.SmallWrapped, Palette.TextFaint, TextAnchor.UpperCenter);
        }

        private float DrawSupports(Rect r, float y, List<ModulePlacement> blocks, ModulePlacement p)
        {
            y = Section(r, y, blocks.Count == 1 ? "Support pillars" : "Support pillars (all selected)");

            if (blocks.All(b => b.Elevation <= 0f))
            {
                SGUI.Label(new Rect(r.x, y, r.width, T.S(32f)), "Pillars appear under raised blocks. Colors chosen here are kept if they're raised.",
                           T.SmallWrapped, Palette.TextFaint);
                y += T.S(36f);
            }

            // presets
            float swatch = T.S(30f);
            float gap = T.S(6f);
            int perRow = Mathf.Max(1, Mathf.FloorToInt((r.width + gap) / (swatch + gap)));
            var presets = BlockOperations.Presets;
            bool same = blocks.All(b => Nullable.Equals(b.Support, p.Support));
            for (int i = 0; i < presets.Length; i++)
            {
                int col = i % perRow;
                int row = i / perRow;
                var rect = new Rect(r.x + col * (swatch + gap), y + row * (swatch + gap), swatch, swatch);
                var style = presets[i].Style;
                bool current = same && Nullable.Equals(p.Support, style);

                if (SGUI.ClickBehavior(rect, true, out bool hover, out _))
                    BlockOperations.SetSupportGroup(blocks, style);
                SGUI.Tooltip(rect, presets[i].Name);
                DrawSwatch(rect, style ?? BlockOperations.DefaultLook, current, hover);
            }
            int rows = Mathf.CeilToInt(presets.Length / (float)perRow);
            y += rows * (swatch + gap) + T.S(8f);

            // custom colors (shown for the primary block, applied to all)
            var look = p.Support ?? BlockOperations.DefaultLook;
            var owner = (p.RootCell.Position, Viewport.Selection.Version);
            if (!bufferOwner.Equals(owner))
            {
                bufferOwner = owner;
                topBuffer = null;
                bottomBuffer = null;
            }

            y = HexRow(r, y, "Top", TopField, look.Top, ref topBuffer, ref topFocused, rgb => BlockOperations.SetSupportGroup(blocks, new SupportStyle(rgb, look.Bottom)));
            y = HexRow(r, y, "Bottom", BottomField, look.Bottom, ref bottomBuffer, ref bottomFocused, rgb => BlockOperations.SetSupportGroup(blocks, new SupportStyle(look.Top, rgb)));
            y += T.S(4f);

            if (SGUI.Button(new Rect(r.x, y, r.width, T.ControlHeight), "Apply to all raised blocks", ButtonKind.Ghost, true, IconId.Layout))
            {
                int count = BlockOperations.SetSupportOnAllRaised(p.Support);
                shell.Toast(count == 1 ? "Applied to 1 raised block" : $"Applied to {count} raised blocks", IconId.Check);
            }
            return y + T.ControlHeight + T.S(22f);
        }

        private float DrawConnections(Rect r, float y, List<ModulePlacement> blocks)
        {
            var report = ConnectivityService.Current(shell.TrackRevision);
            if (report == null)
                return y;
            y = Section(r, y, "Connections");

            var indices = new HashSet<int>(blocks.Select(ConnectivityService.IndexOf).Where(i => i >= 0));
            int joined = 0, steps = 0, open = 0, jumps = 0;
            foreach (var joint in report.Joints)
            {
                if (joint.Internal)
                    continue;
                bool a = indices.Contains(joint.A.Placement);
                bool b = joint.B != null && indices.Contains(joint.B.Placement);
                if (!a && !b)
                    continue;
                switch (joint.Kind)
                {
                    case JointKind.Connected: joined++; break;
                    case JointKind.Jump: jumps++; break;
                    case JointKind.Step: steps++; break;
                    case JointKind.Open: open++; break;
                }
            }
            bool detached = report.ComponentCount > 1 && indices.All(i => report.Component[i] != report.MainComponent);

            void Line(Color color, string text)
            {
                SGUI.Rounded(new Rect(r.x, y + T.S(6f), T.S(8f), T.S(8f)), color, T.S(4f));
                SGUI.Label(new Rect(r.x + T.S(16f), y, r.width - T.S(16f), T.S(20f)), text, T.Small, Palette.TextDim);
                y += T.S(22f);
            }
            Line(Palette.Success, joined == 1 ? "1 connection" : $"{joined} connections");
            if (jumps > 0) Line(Palette.Info, jumps == 1 ? "1 jump" : $"{jumps} jumps");
            if (steps > 0) Line(Palette.Warning, steps == 1 ? "1 step in height" : $"{steps} steps in height");
            Line(open > 0 ? Palette.Error : Palette.TextFaint, open == 1 ? "1 open end" : $"{open} open ends");
            if (detached)
                Line(Palette.Warning, report.HasStart ? "Not connected to the start grid's track" : "Separate from the largest section");

            y += T.S(4f);
            if (SGUI.Button(new Rect(r.x, y, r.width, T.ControlHeight), "Select connected section", ButtonKind.Ghost, true, IconId.Link, "Ctrl+L"))
                Viewport.SelectConnected();
            return y + T.ControlHeight + T.S(22f);
        }

        private float DrawActions(Rect r, float y, List<ModulePlacement> blocks, ModulePlacement p)
        {
            y = Section(r, y, "Actions");
            float half = (r.width - T.S(8f)) * 0.5f;
            bool locked = blocks.All(BlockOperations.IsLocked);

            if (SGUI.Button(new Rect(r.x, y, half, T.ControlHeight), "Duplicate", ButtonKind.Secondary, true, IconId.Copy, "Ctrl+D"))
                Viewport.DuplicateSelection();
            if (SGUI.Button(new Rect(r.x + half + T.S(8f), y, half, T.ControlHeight), "Copy", ButtonKind.Secondary, true, IconId.Copy, "Ctrl+C"))
                Viewport.CopySelection();
            y += T.ControlHeight + T.S(8f);

            if (SGUI.Button(new Rect(r.x, y, half, T.ControlHeight), locked ? "Unlock" : "Lock", ButtonKind.Secondary, true, locked ? IconId.LockOpen : IconId.Lock,
                            "Locked blocks can't be moved, replaced or deleted in this interface"))
                Viewport.SetLocked(blocks, !locked);
            if (SGUI.Button(new Rect(r.x + half + T.S(8f), y, half, T.ControlHeight), "Hide", ButtonKind.Secondary, true, IconId.EyeOff,
                            "Hide in the viewport (still saved and exported)  H"))
                Viewport.SetHidden(blocks, true);
            y += T.ControlHeight + T.S(8f);

            if (blocks.Count == 1 && SGUI.Button(new Rect(r.x, y, half, T.ControlHeight), "Use as brush", ButtonKind.Secondary, true, IconId.ToolEyedropper,
                                                 "Place more blocks like this one  C"))
                Viewport.PickModule(p.RootCell.Position);
            var deleteRect = blocks.Count == 1 ? new Rect(r.x + half + T.S(8f), y, half, T.ControlHeight) : new Rect(r.x, y, r.width, T.ControlHeight);
            if (SGUI.Button(deleteRect, blocks.Count == 1 ? "Delete" : $"Delete {blocks.Count} blocks", ButtonKind.Secondary, !locked, IconId.Trash, "Del"))
                Viewport.DeleteSelection();
            return y + T.ControlHeight + T.S(16f);
        }

        private float HexRow(Rect r, float y, string label, string fieldName, uint value, ref string buffer, ref bool wasFocused, Action<uint> commit)
        {
            var row = new Rect(r.x, y, r.width, T.S(30f));
            SGUI.Label(row.TakeLeft(T.S(64f)), label, T.Small, Palette.TextFaint);

            var chip = new Rect(row.x + T.S(64f), row.y + T.S(4f), T.S(22f), T.S(22f));
            SGUI.RoundedOutline(chip, ModulePlacement.ToColor(value), Palette.BorderStrong, T.S(4f));

            bool focused = SGUI.IsFocused(fieldName);
            if (!focused)
            {
                if (wasFocused && buffer != null && SupportStyle.TryParseHex(buffer, out uint typed) && typed != value)
                    commit(typed);
                buffer = SupportStyle.ToHex(value);
            }
            wasFocused = focused;

            var field = new Rect(chip.xMax + T.S(8f), row.y, T.S(110f), row.height);
            if (SGUI.SubmitPressed(fieldName))
            {
                if (SupportStyle.TryParseHex(buffer, out uint typed) && typed != value)
                    commit(typed);
                else if (!SupportStyle.TryParseHex(buffer, out _))
                    TrackEditor.PlaySound(TrackEditor.SndWarning);
                GUIUtility.keyboardControl = 0;
                Event.current.Use();
            }
            buffer = SGUI.TextField(field, buffer, fieldName, "#RRGGBB", 7, null, c => c == '#' || Uri.IsHexDigit(c));
            return y + row.height + T.S(6f);
        }

        private void DrawSwatch(Rect rect, SupportStyle style, bool selected, bool hover)
        {
            if (Event.current.type != EventType.Repaint)
                return;

            if (selected)
                SGUI.Rounded(rect.Inset(-T.S(3f)), Palette.Accent, T.S(8f));
            else if (hover)
                SGUI.Rounded(rect.Inset(-T.S(2f)), Palette.BorderStrong, T.S(7f));

            var inner = rect;
            SGUI.Rounded(inner, ModulePlacement.ToColor(style.Bottom), T.S(5f));
            // top color as the upper part, like a pillar seen from the side
            SGUI.Rounded(new Rect(inner.x, inner.y, inner.width, inner.height * 0.55f), ModulePlacement.ToColor(style.Top), T.S(5f));
            SGUI.Fill(new Rect(inner.x, inner.y + inner.height * 0.45f, inner.width, inner.height * 0.10f), ModulePlacement.ToColor(style.Top));
        }
    }
}

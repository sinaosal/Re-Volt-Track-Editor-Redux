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

using Dummiesman.DebugDraw;
using ReVolt.Track;
using Studio.Icons;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Studio
{
    /// <summary>World overlays (footprints, previews, connection markers) and in-viewport GUI.</summary>
    public sealed partial class StudioViewport
    {
        private static readonly Color AxisX = new Color(0.90f, 0.33f, 0.29f);
        private static readonly Color AxisY = new Color(0.24f, 0.72f, 0.55f);
        private static readonly Color AxisZ = new Color(0.30f, 0.62f, 0.91f);

        // World overlays (drawn through DebugDraw, which renders after the scene)
        private void DrawWorldOverlays()
        {
            var editing = Editing;
            var track = TrackEditor.Track;

            if (shell.Race.Active)
            {
                shell.Race.Draw();
                return;
            }

            DebugDraw.PushMatrix();
            DebugDraw.Identity();

            if (PasteActive)
            {
                DrawPastePreview();
            }
            else if (Tool == StudioTool.Place && editing.PreviewVisible)
            {
                if (AppSettings.Data.ShowFootprint)
                {
                    bool replaces = editing.PreviewReplacesModules();
                    var color = replaces ? Palette.FootprintReplace : Palette.FootprintPlace;
                    foreach (var cell in editing.GetPreviewFootprint())
                    {
                        if (InBounds(cell))
                            DrawCell(cell, editing.CursorElevation + 0.04f, color, true);
                    }
                }
                DrawFitMarkers(CurrentFit);
            }
            else if (Tool == StudioTool.Erase && HoverCell.HasValue && PointerInside)
            {
                var module = track.GetCell(HoverCell.Value).Module;
                if (module != null)
                {
                    var color = BlockOperations.IsLocked(module) ? Palette.Warning.WithAlpha(0.2f) : Palette.FootprintErase;
                    foreach (var cell in module.TouchedCells)
                        DrawCell(cell.Position, module.Elevation + 0.03f, color, true);
                }
            }
            else if (Tool == StudioTool.Select)
            {
                DrawSelection();
            }
            else if (Tool == StudioTool.Pickups && HoverCell.HasValue && PointerInside)
            {
                var cell = HoverCell.Value;
                var module = track.GetCell(cell).Module;
                DrawCell(cell, (module != null ? module.Elevation : 0f) + 0.03f,
                         module != null ? Palette.FootprintPickup : Palette.FootprintErase.WithAlpha(0.18f), true);
            }

            if (AppSettings.Data.ShowConnections && (Tool == StudioTool.Place || Tool == StudioTool.Select))
                DrawConnections();

            if (Time.unscaledTime < highlightUntil && InBounds(highlightCell))
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 8f);
                var module = track.GetCell(highlightCell).Module;
                float height = (module != null ? module.Elevation : 0f) + 0.05f;
                DrawCell(highlightCell, height, Palette.Error.WithAlpha(0.15f + 0.25f * pulse), true);
            }

            DebugDraw.PopMatrix();

            if (AppSettings.Data.ShowPickups || Tool == StudioTool.Pickups)
                DrawPickups();
        }

        private (Vector2Int delta, int levels, int version, int revision) previewKey;
        private bool previewFits;

        private void DrawSelection()
        {
            var track = TrackEditor.Track;
            var selected = SelectedPlacements;
            var primary = SelectedPlacement;

            // hover outline for other blocks
            if (drag == DragMode.None && HoverCell.HasValue && PointerInside && gizmoHover == GizmoAxis.None)
            {
                var hovered = track.GetCell(HoverCell.Value).Module;
                if (hovered != null && !Selection.Contains(hovered))
                {
                    foreach (var cell in hovered.TouchedCells)
                        DrawCell(cell.Position, hovered.Elevation + 0.03f, Palette.Accent.WithAlpha(0.10f), true);
                }
            }

            foreach (var block in selected)
            {
                var color = BlockOperations.IsLocked(block) ? Palette.Warning.WithAlpha(0.22f)
                          : block == primary ? Palette.Accent.WithAlpha(0.34f) : Palette.Accent.WithAlpha(0.22f);
                foreach (var cell in block.TouchedCells)
                    DrawCell(cell.Position, block.Elevation + 0.04f, color, true);
            }

            bool moving = (drag == DragMode.MoveSelection || drag == DragMode.Gizmo) && (moveDelta != Vector2Int.zero || moveLevelDelta != 0);
            if (moving && selected.Count > 0)
            {
                var key = (moveDelta, moveLevelDelta, Selection.Version, shell.TrackRevision);
                var specs = selected.Select(p => BlockGroup.Translate(BlockOperations.ToSpec(p), moveDelta.x, moveDelta.y, moveLevelDelta)).ToList();
                if (!previewKey.Equals(key))
                {
                    previewKey = key;
                    previewFits = !selected.Any(BlockOperations.IsLocked) && BlockOperations.CheckGroupFit(specs, selected, false) == null;
                }
                var color = previewFits ? Palette.Success.WithAlpha(0.32f) : Palette.FootprintErase;
                foreach (var spec in specs)
                {
                    float height = spec.Level * EditorConstants.ElevationStep + 0.06f;
                    foreach (var cell in BlockOperations.Footprint(spec))
                    {
                        if (InBounds(cell))
                            DrawCell(cell, height, color, true);
                    }
                }
            }
        }

        private void DrawPastePreview()
        {
            var specs = Paste.Resolve();
            bool clean = BlockOperations.CheckGroupFit(specs, null, false) == null;
            bool replacing = !clean && BlockOperations.CheckGroupFit(specs, null, true) == null;
            var color = clean ? Palette.Success.WithAlpha(0.30f) : replacing ? Palette.FootprintReplace : Palette.FootprintErase;
            foreach (var spec in specs)
            {
                float height = spec.Level * EditorConstants.ElevationStep + 0.06f;
                foreach (var cell in BlockOperations.Footprint(spec))
                {
                    if (InBounds(cell))
                        DrawCell(cell, height, color, true);
                }
            }
        }

        // Connections
        private void DrawConnections()
        {
            var report = ConnectivityService.Current(shell.TrackRevision);
            if (report == null)
                return;
            var placements = ConnectivityService.Placements;
            // on very large tracks only problems are marked (keeps the overlay within one draw batch)
            bool dense = report.Joints.Count > 2500;

            foreach (var joint in report.Joints)
            {
                if (joint.Internal || States.IsHidden(placements[joint.A.Placement]))
                    continue;
                if (dense && (joint.Kind == JointKind.Connected || joint.Kind == JointKind.Jump))
                    continue;
                var a = ConnectivityService.ToEditor(joint.A.Position) + Vector3.up * 0.02f;
                switch (joint.Kind)
                {
                    case JointKind.Connected:
                        Diamond(a, 0.045f, Palette.Success.WithAlpha(0.85f));
                        break;
                    case JointKind.Jump:
                        Diamond(a, 0.06f, Palette.Info);
                        break;
                    case JointKind.Step:
                        {
                            var b = ConnectivityService.ToEditor(joint.B.Position) + Vector3.up * 0.02f;
                            Diamond(a, 0.08f, Palette.Warning);
                            Diamond(b, 0.08f, Palette.Warning);
                            DebugDraw.Color = Palette.Warning;
                            DebugDraw.Begin(DrawType.LINES);
                            DebugDraw.Vertex(a);
                            DebugDraw.Vertex(b);
                            DebugDraw.End();
                        }
                        break;
                    case JointKind.Open:
                        {
                            Diamond(a, 0.09f, Palette.Error);
                            if (joint.A.HasFacing)
                            {
                                var outward = new Vector3(joint.A.FacingX - a.x, 0f, joint.A.FacingY - a.z).normalized;
                                DebugDraw.Color = Palette.Error;
                                DebugDraw.Begin(DrawType.LINES);
                                DebugDraw.Vertex(a);
                                DebugDraw.Vertex(a + outward * 0.28f);
                                DebugDraw.End();
                            }
                        }
                        break;
                }
            }
        }

        private void DrawFitMarkers(PlacementFit fit)
        {
            if (fit == null || !AppSettings.Data.ShowConnections)
                return;
            foreach (var marker in fit.Markers)
            {
                var p = ConnectivityService.ToEditor(marker.Position) + Vector3.up * 0.05f;
                switch (marker.Kind)
                {
                    case PlacementFit.MarkerKind.Connects:
                        Diamond(p, 0.12f, Palette.Success);
                        break;
                    case PlacementFit.MarkerKind.Jump:
                        Diamond(p, 0.1f, Palette.Info);
                        break;
                    case PlacementFit.MarkerKind.Step:
                        Diamond(p, 0.12f, Palette.Warning);
                        break;
                    case PlacementFit.MarkerKind.Blocks:
                        Cross(p, 0.14f, Palette.Error);
                        break;
                    default:
                        Diamond(p, 0.05f, Palette.TextFaint);
                        break;
                }
            }
        }

        private static void Diamond(Vector3 p, float s, Color color)
        {
            DebugDraw.Color = color.WithAlpha(color.a * 0.55f);
            DebugDraw.Begin(DrawType.QUADS);
            DebugDraw.Vertex(p + new Vector3(s, 0f, 0f));
            DebugDraw.Vertex(p + new Vector3(0f, 0f, s));
            DebugDraw.Vertex(p + new Vector3(-s, 0f, 0f));
            DebugDraw.Vertex(p + new Vector3(0f, 0f, -s));
            DebugDraw.End();

            DebugDraw.Color = color;
            DebugDraw.Begin(DrawType.LINES);
            DebugDraw.Vertex(p + new Vector3(s, 0f, 0f)); DebugDraw.Vertex(p + new Vector3(0f, 0f, s));
            DebugDraw.Vertex(p + new Vector3(0f, 0f, s)); DebugDraw.Vertex(p + new Vector3(-s, 0f, 0f));
            DebugDraw.Vertex(p + new Vector3(-s, 0f, 0f)); DebugDraw.Vertex(p + new Vector3(0f, 0f, -s));
            DebugDraw.Vertex(p + new Vector3(0f, 0f, -s)); DebugDraw.Vertex(p + new Vector3(s, 0f, 0f));
            // a vertical tick keeps markers visible from the side views
            DebugDraw.Vertex(p); DebugDraw.Vertex(p + new Vector3(0f, s * 1.6f, 0f));
            DebugDraw.End();
        }

        private static void Cross(Vector3 p, float s, Color color)
        {
            DebugDraw.Color = color;
            DebugDraw.Begin(DrawType.LINES);
            DebugDraw.Vertex(p + new Vector3(-s, 0f, -s)); DebugDraw.Vertex(p + new Vector3(s, 0f, s));
            DebugDraw.Vertex(p + new Vector3(-s, 0f, s)); DebugDraw.Vertex(p + new Vector3(s, 0f, -s));
            DebugDraw.End();
        }

        private static void DrawCell(Vector2Int cell, float height, Color fill, bool outline)
        {
            float h = 0.47f;
            var c = new Vector3(cell.x, height, cell.y);
            var a = c + new Vector3(-h, 0f, -h);
            var b = c + new Vector3(-h, 0f, h);
            var d = c + new Vector3(h, 0f, h);
            var e = c + new Vector3(h, 0f, -h);

            DebugDraw.Color = fill;
            DebugDraw.Begin(DrawType.QUADS);
            DebugDraw.Vertex(a);
            DebugDraw.Vertex(b);
            DebugDraw.Vertex(d);
            DebugDraw.Vertex(e);
            DebugDraw.End();

            if (outline)
            {
                DebugDraw.Color = new Color(fill.r, fill.g, fill.b, 1f);
                DebugDraw.Begin(DrawType.LINES);
                DebugDraw.Vertex(a); DebugDraw.Vertex(b);
                DebugDraw.Vertex(b); DebugDraw.Vertex(d);
                DebugDraw.Vertex(d); DebugDraw.Vertex(e);
                DebugDraw.Vertex(e); DebugDraw.Vertex(a);
                DebugDraw.End();
            }
        }

        private static void DrawPickups()
        {
            var texture = TrackEditor.GetEditMode<PickupEditingMode>().PickupTexture;
            if (texture == null)
                return;

            int frame = Mathf.FloorToInt((float)(Time.timeSinceLevelLoadAsDouble % 1d) * 8f);
            var uv = new Rect(frame * 0.0625f, 0f, 0.0625f, 0.0625f);
            float bob = Mathf.Sin(Time.time * 3f) * 0.04f;

            DebugDraw.PushMatrix();
            DebugDraw.Identity();
            DebugDraw.Color = Color.white;
            DebugDraw.BeginTexturedTransparent(DrawType.QUADS, texture);
            foreach (var cell in TrackEditor.Track.Cells)
            {
                if (!cell.HasPickup)
                    continue;

                float height = (cell.Module != null ? cell.Module.Elevation : 0f) + 0.55f + bob;
                var c = new Vector3(cell.Position.x, height, cell.Position.y);
                const float s = 0.32f;
                DebugDraw.Vertex(c + new Vector3(-s, 0f, -s), new Vector2(uv.x, uv.y));
                DebugDraw.Vertex(c + new Vector3(-s, 0f, s), new Vector2(uv.x, uv.yMax));
                DebugDraw.Vertex(c + new Vector3(s, 0f, s), new Vector2(uv.xMax, uv.yMax));
                DebugDraw.Vertex(c + new Vector3(s, 0f, -s), new Vector2(uv.xMax, uv.y));
            }
            DebugDraw.End();
            DebugDraw.PopMatrix();
        }

        // Move gizmo
        private bool ComputeGizmo(out Vector2 center, out Vector2[] tips, out float worldLength)
        {
            center = default;
            tips = null;
            worldLength = 0f;
            if (Tool != StudioTool.Select || PasteActive || shell.Race.Active)
                return false;
            var blocks = SelectedPlacements;
            if (blocks.Count == 0)
                return false;

            var min = new Vector2Int(int.MaxValue, int.MaxValue);
            var max = new Vector2Int(int.MinValue, int.MinValue);
            float top = 0f;
            foreach (var block in blocks)
            {
                foreach (var cell in block.TouchedCells)
                {
                    min = Vector2Int.Min(min, cell.Position);
                    max = Vector2Int.Max(max, cell.Position);
                }
                top = Mathf.Max(top, block.Elevation);
            }
            var c = new Vector3((min.x + max.x) * 0.5f, top + 0.06f, (min.y + max.y) * 0.5f);
            worldLength = Mathf.Clamp(distance * 0.13f, 0.5f, 6f);

            var cam = TrackEditor.Instance.Camera;
            var sc = cam.WorldToScreenPoint(c);
            if (sc.z <= 0f)
                return false;
            center = new Vector2(sc.x, Screen.height - sc.y);
            tips = new Vector2[3];
            var axes = new[] { Vector3.right, Vector3.forward, Vector3.up };
            for (int i = 0; i < 3; i++)
            {
                var st = cam.WorldToScreenPoint(c + axes[i] * worldLength);
                tips[i] = st.z <= 0f ? center : new Vector2(st.x, Screen.height - st.y);
            }
            return true;
        }

        private GizmoAxis HitGizmo(Vector2 gui)
        {
            if (!ComputeGizmo(out var center, out var tips, out _))
                return GizmoAxis.None;
            float tolerance = shell.Theme.S(9f);
            GizmoAxis best = GizmoAxis.None;
            float bestDistance = tolerance;
            for (int i = 0; i < 3; i++)
            {
                if ((tips[i] - center).magnitude < shell.Theme.S(12f))
                    continue; // the axis points at the camera; it can't be dragged meaningfully
                float d = DistanceToSegment(gui, center + (tips[i] - center) * 0.25f, tips[i]);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = i == 0 ? GizmoAxis.X : i == 1 ? GizmoAxis.Z : GizmoAxis.Y;
                }
            }
            return best;
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = ab.sqrMagnitude < 0.0001f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return (p - (a + ab * t)).magnitude;
        }

        private void UpdateGizmoDrag(Vector2 gui)
        {
            if (!ComputeGizmo(out var center, out var tips, out float worldLength))
                return;
            int index = gizmoAxis == GizmoAxis.X ? 0 : gizmoAxis == GizmoAxis.Z ? 1 : 2;
            var axis = tips[index] - center;
            float length = axis.magnitude;
            if (length < 4f)
                return;
            float units = Vector2.Dot(gui - gizmoDragStart, axis / length) / length * worldLength;
            if (gizmoAxis == GizmoAxis.Y)
            {
                moveDelta = Vector2Int.zero;
                moveLevelDelta = Mathf.RoundToInt(units / EditorConstants.ElevationStep);
            }
            else
            {
                int cells = Mathf.RoundToInt(units);
                moveDelta = gizmoAxis == GizmoAxis.X ? new Vector2Int(cells, 0) : new Vector2Int(0, cells);
                moveLevelDelta = 0;
            }
        }

        // GUI overlays
        /// <summary>
        /// Draws the in-viewport overlays (view label, view buttons, compass, minimap, gizmo, selection
        /// toolbar). Returns rects that should block viewport pointer input.
        /// </summary>
        public void DrawOverlay(List<Rect> blockingRects)
        {
            var T = shell.Theme;
            float pad = T.S(10f);

            // view label, top left
            string[] directions = { "North", "East", "South", "West" };
            int quadrant = ((Mathf.RoundToInt(yaw / 90f) % 4) + 4) % 4;
            string view = $"{ViewName} · facing {directions[quadrant]}";
            if (PasteActive)
                view = $"{Paste.Verb} {Paste.Count} block{(Paste.Count == 1 ? "" : "s")} · {Paste.Turns * 90}° · {(Paste.LevelOffset >= 0 ? "+" : "")}{Paste.LevelOffset} levels";

            float labelWidth = SGUI.TextWidth(view, T.Small) + T.S(20f);
            var label = new Rect(Rect.x + pad, Rect.y + pad, labelWidth, T.S(26f)).Snap();
            SGUI.Rounded(label, (PasteActive ? Palette.AccentSoft : Palette.Chrome).WithAlpha(0.9f));
            SGUI.Label(label, view, T.Small, PasteActive ? Palette.Text : Palette.TextDim, TextAnchor.MiddleCenter);
            blockingRects.Add(label);

            // view buttons, top right
            float button = T.S(28f);
            int count = 5;
            var bar = new Rect(Rect.xMax - pad - button * count - T.S(8f), Rect.y + pad, button * count + T.S(8f), button + T.S(8f)).Snap();
            SGUI.Rounded(bar, Palette.Chrome.WithAlpha(0.88f));
            var r = new Rect(bar.x + T.S(4f), bar.y + T.S(4f), button, button);
            if (SGUI.IconButton(r, IconId.OrbitLeft, "Orbit left  [")) Orbit90(1);
            r.x += button;
            if (SGUI.IconButton(r, IconId.OrbitRight, "Orbit right  ]")) Orbit90(-1);
            r.x += button;
            if (SGUI.IconButton(r, TopView ? IconId.ViewPerspective : IconId.ViewTop, TopView ? "Perspective view  V" : "Top view  V")) ToggleTopView();
            r.x += button;
            if (SGUI.IconButton(r, IconId.Frame, Tool == StudioTool.Select && !Selection.IsEmpty ? "Frame selection  F" : "Frame track  F")) FrameSelection();
            r.x += button;
            var viewMenuRect = r;
            if (SGUI.IconButton(r, IconId.ChevronDown, "View options"))
                shell.OpenViewMenu(new Vector2(viewMenuRect.xMax, viewMenuRect.yMax + T.S(6f)));
            blockingRects.Add(bar);

            // gizmo and selection toolbar
            if (ComputeGizmo(out var center, out var tips, out _))
            {
                DrawGizmo(center, tips);
                if (drag == DragMode.None)
                    DrawSelectionToolbar(center, blockingRects);
            }

            // placement fit chip
            if (Tool == StudioTool.Place && !PasteActive && !shell.Race.Active)
                DrawFitChip();

            // marquee
            if (drag == DragMode.Marquee)
            {
                var m = Rect.MinMaxRect(Mathf.Min(marqueeStart.x, marqueeEnd.x), Mathf.Min(marqueeStart.y, marqueeEnd.y),
                                        Mathf.Max(marqueeStart.x, marqueeEnd.x), Mathf.Max(marqueeStart.y, marqueeEnd.y));
                SGUI.Fill(m, Palette.Accent.WithAlpha(0.08f));
                SGUI.Fill(new Rect(m.x, m.y, m.width, 1f), Palette.AccentLine);
                SGUI.Fill(new Rect(m.x, m.yMax - 1f, m.width, 1f), Palette.AccentLine);
                SGUI.Fill(new Rect(m.x, m.y, 1f, m.height), Palette.AccentLine);
                SGUI.Fill(new Rect(m.xMax - 1f, m.y, 1f, m.height), Palette.AccentLine);
            }

            // minimap and compass, bottom left
            float compass = T.S(44f);
            float compassX = Rect.x + pad;
            if (AppSettings.Data.ShowMinimap && !shell.Race.Active)
            {
                var map = DrawMinimap(new Vector2(Rect.x + pad, Rect.yMax - pad));
                if (map.width > 0f)
                {
                    blockingRects.Add(map);
                    compassX = map.xMax + T.S(8f);
                }
            }
            var compassRect = new Rect(compassX, Rect.yMax - pad - compass, compass, compass).Snap();
            DrawCompass(compassRect);
        }

        private void DrawGizmo(Vector2 center, Vector2[] tips)
        {
            var T = shell.Theme;
            var colors = new[] { AxisX, AxisZ, AxisY };
            var axes = new[] { GizmoAxis.X, GizmoAxis.Z, GizmoAxis.Y };
            string[] names = { "X", "Z", "Y" };
            for (int i = 0; i < 3; i++)
            {
                if ((tips[i] - center).magnitude < T.S(12f))
                    continue;
                bool hot = gizmoHover == axes[i] || gizmoAxis == axes[i];
                var color = hot ? Color.Lerp(colors[i], Color.white, 0.35f) : colors[i];
                var start = center + (tips[i] - center) * 0.25f;
                SGUI.Line(start, tips[i], T.S(hot ? 4f : 3f), color);
                float dot = T.S(hot ? 13f : 11f);
                SGUI.Rounded(new Rect(tips[i].x - dot * 0.5f, tips[i].y - dot * 0.5f, dot, dot), color, dot * 0.5f);
                SGUI.Label(new Rect(tips[i].x - dot * 0.5f, tips[i].y - dot * 0.5f, dot, dot), names[i], T.Caption, Palette.Backdrop, TextAnchor.MiddleCenter);
            }
            float c = T.S(8f);
            SGUI.Rounded(new Rect(center.x - c * 0.5f, center.y - c * 0.5f, c, c), Palette.Text.WithAlpha(0.85f), c * 0.5f);

            if (drag == DragMode.Gizmo)
            {
                string text = gizmoAxis == GizmoAxis.Y ? $"{(moveLevelDelta >= 0 ? "+" : "")}{moveLevelDelta} levels"
                                                        : $"{(moveDelta.x + moveDelta.y >= 0 ? "+" : "")}{moveDelta.x + moveDelta.y} cells";
                float w = SGUI.TextWidth(text, T.SmallStrong) + T.S(16f);
                var chip = new Rect(center.x + T.S(14f), center.y + T.S(10f), w, T.S(22f)).Snap();
                SGUI.Rounded(chip, Palette.Overlay.WithAlpha(0.95f));
                SGUI.Label(chip, text, T.SmallStrong, Palette.Text, TextAnchor.MiddleCenter);
            }
        }

        private void DrawSelectionToolbar(Vector2 anchor, List<Rect> blockingRects)
        {
            var T = shell.Theme;
            var selected = SelectedPlacements;
            bool locked = selected.Count > 0 && selected.All(BlockOperations.IsLocked);
            string countText = selected.Count == 1 ? ModuleCatalog.GetDisplayName(selected[0].ModuleIndex) : $"{selected.Count} blocks";
            float button = T.S(26f);
            int buttons = 7;
            float textWidth = Mathf.Min(SGUI.TextWidth(countText, T.SmallStrong), T.S(140f)) + T.S(16f);
            float width = textWidth + button * buttons + T.S(8f);
            var bar = new Rect(anchor.x - width * 0.5f, anchor.y + T.S(34f), width, button + T.S(8f));
            bar.x = Mathf.Clamp(bar.x, Rect.x + T.S(8f), Rect.xMax - width - T.S(8f));
            bar.y = Mathf.Clamp(bar.y, Rect.y + T.S(48f), Rect.yMax - bar.height - T.S(8f));
            bar = bar.Snap();

            SGUI.Shadow(bar, T.S(6f));
            SGUI.RoundedOutline(bar, Palette.Overlay.WithAlpha(0.96f), Palette.BorderStrong, T.S(6f));
            SGUI.Label(new Rect(bar.x + T.S(10f), bar.y, textWidth - T.S(10f), bar.height),
                       SGUI.Ellipsize(countText, T.SmallStrong, textWidth - T.S(12f)), T.SmallStrong, Palette.TextDim);

            var r = new Rect(bar.x + textWidth, bar.y + T.S(4f), button, button);
            if (SGUI.IconButton(r, IconId.RotateCCW, "Rotate counter-clockwise  Shift+R")) RotateSelection(-1);
            r.x += button;
            if (SGUI.IconButton(r, IconId.RotateCW, "Rotate clockwise  R")) RotateSelection(1);
            r.x += button;
            if (SGUI.IconButton(r, IconId.ElevationUp, "Raise  PgUp")) RaiseSelection(1);
            r.x += button;
            if (SGUI.IconButton(r, IconId.ElevationDown, "Lower  PgDn")) RaiseSelection(-1);
            r.x += button;
            if (SGUI.IconButton(r, IconId.Copy, "Duplicate  Ctrl+D")) DuplicateSelection();
            r.x += button;
            if (SGUI.IconButton(r, locked ? IconId.Lock : IconId.LockOpen, locked ? "Unlock" : "Lock against editing", locked))
                SetLocked(selected, !locked);
            r.x += button;
            if (SGUI.IconButton(r, IconId.Trash, "Delete  Del")) DeleteSelection();

            blockingRects.Add(bar);
        }

        public void SetLocked(IList<ModulePlacement> blocks, bool locked)
        {
            foreach (var block in blocks)
                States.SetLocked(block, locked);
            shell.Toast(locked ? (blocks.Count == 1 ? "Block locked" : $"{blocks.Count} blocks locked") : "Unlocked", locked ? IconId.Lock : IconId.LockOpen);
        }

        public void SetHidden(IList<ModulePlacement> blocks, bool hidden)
        {
            foreach (var block in blocks)
            {
                States.SetHidden(block, hidden);
                if (hidden)
                    Selection.Remove(block.RootCell.Position);
            }
        }

        private void DrawFitChip()
        {
            var fit = CurrentFit;
            if (fit == null || fit.Connects + fit.Jumps + fit.Steps + fit.Blocks == 0)
                return;

            var T = shell.Theme;
            var parts = new List<string>();
            if (fit.Connects > 0) parts.Add(fit.Connects == 1 ? "connects 1" : $"connects {fit.Connects}");
            if (fit.Jumps > 0) parts.Add("jump");
            if (fit.Steps > 0) parts.Add(fit.Steps == 1 ? "1 height step" : $"{fit.Steps} height steps");
            if (fit.Blocks > 0) parts.Add(fit.Blocks == 1 ? "blocks 1 opening" : $"blocks {fit.Blocks} openings");
            string text = char.ToUpperInvariant(parts[0][0]) + string.Join(" · ", parts).Substring(1);
            if (fit.HasProblems)
                text += "  ·  A fits";

            Color color = fit.HasProblems ? Palette.Warning : Palette.Success;
            float width = SGUI.TextWidth(text, T.Small) + T.S(40f);
            var chip = new Rect(Rect.center.x - width * 0.5f, Rect.y + T.S(10f), width, T.S(26f)).Snap();
            SGUI.Rounded(chip, Palette.Chrome.WithAlpha(0.9f));
            SGUI.Icon(new Rect(chip.x + T.S(8f), chip.y, T.S(16f), chip.height), fit.HasProblems ? IconId.Warning : IconId.Link, color, T.S(14f));
            SGUI.Label(new Rect(chip.x + T.S(30f), chip.y, chip.width - T.S(36f), chip.height), text, T.Small, Palette.TextDim);
        }

        private void DrawCompass(Rect r)
        {
            if (Event.current.type != EventType.Repaint)
                return;

            var T = shell.Theme;
            SGUI.Rounded(r, Palette.Chrome.WithAlpha(0.82f), r.width * 0.5f);

            // north points along +Z; on screen it is rotated by the camera yaw
            float angle = -yaw * Mathf.Deg2Rad;
            var center = r.center;
            float radius = r.width * 0.32f;
            var north = center + new Vector2(Mathf.Sin(angle), -Mathf.Cos(angle)) * radius;

            float dot = T.S(6f);
            SGUI.Rounded(new Rect(north.x - dot * 0.5f, north.y - dot * 0.5f, dot, dot), Palette.Accent, dot * 0.5f);
            var labelPos = center + new Vector2(Mathf.Sin(angle), -Mathf.Cos(angle)) * (radius - T.S(10f));
            SGUI.Label(new Rect(labelPos.x - T.S(8f), labelPos.y - T.S(8f), T.S(16f), T.S(16f)), "N", T.Caption, Palette.Text, TextAnchor.MiddleCenter);
        }

        // Minimap
        private Texture2D minimapTexture;
        private (int revision, int selection, int hidden, int width, int height, bool select) minimapKey;

        private void UpdateMinimapTexture()
        {
            var track = TrackEditor.Track;
            int w = track.Width, h = track.Height;
            if (w <= 0 || h <= 0 || w * h > 512 * 512)
                return;

            if (minimapTexture == null || minimapTexture.width != w || minimapTexture.height != h)
            {
                if (minimapTexture != null)
                    UnityEngine.Object.Destroy(minimapTexture);
                minimapTexture = new Texture2D(w, h, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave
                };
                minimapKey = default;
            }

            bool selecting = Tool == StudioTool.Select;
            var key = (shell.TrackRevision, selecting ? Selection.Version : -1, States.HiddenCount, w, h, selecting);
            if (minimapKey.Equals(key))
                return;
            minimapKey = key;

            // pieces with open ends stand out
            var flagged = new HashSet<ModulePlacement>();
            var report = ConnectivityService.Current(shell.TrackRevision);
            if (report != null)
            {
                var placements = ConnectivityService.Placements;
                foreach (var joint in report.Joints)
                {
                    if (joint.Kind == JointKind.Open || joint.Kind == JointKind.Step)
                        flagged.Add(placements[joint.A.Placement]);
                }
            }
            var selected = selecting ? new HashSet<ModulePlacement>(SelectedPlacements) : new HashSet<ModulePlacement>();

            var low = new Color(0.33f, 0.36f, 0.42f, 1f);
            var high = new Color(0.86f, 0.88f, 0.92f, 1f);
            var pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    var module = track.GetCell(x, y).Module;
                    Color color;
                    if (module == null)
                        color = new Color(0f, 0f, 0f, 0f);
                    else
                    {
                        float level = module.Elevation / EditorConstants.ElevationStep;
                        color = Color.Lerp(low, high, Mathf.Clamp01(level / 16f));
                        if (module.ModuleIndex == (int)Modules.ID.TWM_START)
                            color = Palette.Accent;
                        else if (flagged.Contains(module))
                            color = Color.Lerp(color, Palette.Warning, 0.65f);
                        if (selected.Contains(module))
                            color = Palette.Info;
                        if (States.IsHidden(module))
                            color.a = 0.35f;
                    }
                    pixels[y * w + x] = color;
                }
            }
            minimapTexture.SetPixels32(pixels);
            minimapTexture.Apply(false);
        }

        /// <summary>Draws the minimap with its bottom left corner at <paramref name="bottomLeft"/>. Returns its rect.</summary>
        private Rect DrawMinimap(Vector2 bottomLeft)
        {
            var T = shell.Theme;
            var track = TrackEditor.Track;
            if (track == null || track.Width * track.Height > 512 * 512)
                return Rect.zero;

            if (Event.current.type == EventType.Repaint)
                UpdateMinimapTexture();

            float longest = T.S(150f);
            float aspect = track.Width / (float)Mathf.Max(1, track.Height);
            float mw = aspect >= 1f ? longest : longest * aspect;
            float mh = aspect >= 1f ? longest / aspect : longest;
            float inset = T.S(6f);
            var frame = new Rect(bottomLeft.x, bottomLeft.y - mh - inset * 2f, mw + inset * 2f, mh + inset * 2f).Snap();
            var map = new Rect(frame.x + inset, frame.y + inset, mw, mh);

            SGUI.RoundedOutline(frame, Palette.Chrome.WithAlpha(0.88f), Palette.Border, T.S(6f));
            SGUI.Fill(map, Palette.Viewport.WithAlpha(0.9f));
            if (minimapTexture != null && Event.current.type == EventType.Repaint)
                GUI.DrawTexture(map, minimapTexture, ScaleMode.StretchToFill, true);

            // camera target and view direction (north is up)
            var tp = new Vector2(map.x + (target.x + 0.5f) / track.Width * map.width, map.yMax - (target.z + 0.5f) / track.Height * map.height);
            tp.x = Mathf.Clamp(tp.x, map.x, map.xMax);
            tp.y = Mathf.Clamp(tp.y, map.y, map.yMax);
            float yawRad = yaw * Mathf.Deg2Rad;
            var dir = new Vector2(Mathf.Sin(yawRad), -Mathf.Cos(yawRad));
            SGUI.Line(tp, tp + dir * T.S(14f), T.S(2f), Palette.Text);
            float dot = T.S(7f);
            SGUI.Rounded(new Rect(tp.x - dot * 0.5f, tp.y - dot * 0.5f, dot, dot), Palette.Text, dot * 0.5f);

            // click or drag to move the view
            int id = GUIUtility.GetControlID(FocusType.Passive);
            var e = Event.current;
            bool interactive = !SGUI.InputBlocked && GUI.enabled;
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (interactive && e.button == 0 && map.Contains(e.mousePosition))
                    {
                        GUIUtility.hotControl = id;
                        MoveTargetToMap(map, e.mousePosition);
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id)
                    {
                        MoveTargetToMap(map, e.mousePosition);
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        e.Use();
                    }
                    break;
            }
            SGUI.Tooltip(frame, "Minimap · click to look there");
            return frame;
        }

        private void MoveTargetToMap(Rect map, Vector2 point)
        {
            var track = TrackEditor.Track;
            float x = (point.x - map.x) / map.width * track.Width - 0.5f;
            float z = (map.yMax - point.y) / map.height * track.Height - 0.5f;
            targetGoal = new Vector3(Mathf.Clamp(x, 0f, track.Width - 1), targetGoal.y, Mathf.Clamp(z, 0f, track.Height - 1));
        }
    }
}

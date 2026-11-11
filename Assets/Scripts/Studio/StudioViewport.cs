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
using UnityEngine.InputSystem;

namespace Studio
{
    public enum StudioTool
    {
        Place,
        Pickups,
        Erase,
        Select
    }

    public enum ViewPreset
    {
        Perspective,
        Top,
        Front,
        Side
    }

    /// <summary>
    /// The Studio 3D viewport: an orbit camera, mouse picking on the track grid, the place / pickup /
    /// erase / select tools, the move gizmo, pasting, and overlays (connections, minimap).
    /// The track itself is rendered by the existing scene.
    /// </summary>
    public sealed partial class StudioViewport
    {
        private enum DragMode
        {
            None,
            PendingRight, // right button down, not yet decided between orbit and context menu
            Orbit,
            Pan,
            Paint,
            Erase,
            Pickups,
            MoveSelection,
            Marquee,
            Gizmo
        }

        private enum GizmoAxis
        {
            None,
            X,
            Z,
            Y
        }

        private readonly StudioShell shell;

        public StudioTool Tool { get; private set; } = StudioTool.Place;

        // Selection
        public readonly BlockSelection Selection = new BlockSelection();
        public readonly BlockStates States = new BlockStates();
        public readonly BlockClipboard Clipboard = new BlockClipboard();

        /// <summary>The primary selected block's root (setting it selects only that block).</summary>
        public Vector2Int? SelectedRoot
        {
            get => Selection.Primary;
            set
            {
                if (value.HasValue)
                    Selection.Set(value.Value);
                else
                    Selection.Clear();
            }
        }

        /// <summary>The primary selected block, or null.</summary>
        public ModulePlacement SelectedPlacement => Selection.PrimaryPlacement;

        public List<ModulePlacement> SelectedPlacements => Selection.Placements;

        // block move (drag) preview
        private Vector2Int moveGrabCell;
        private Vector2Int moveDelta;
        private int moveLevelDelta;
        private Vector2Int? clickedRoot;
        private bool clickHadModifier;

        // marquee
        private Vector2 marqueeStart, marqueeEnd;
        private bool marqueeAdd, marqueeSubtract;

        // gizmo
        private GizmoAxis gizmoHover, gizmoAxis;
        private Vector2 gizmoDragStart;

        // paste
        public PasteSession Paste { get; private set; }
        public bool PasteActive => Paste != null;

        /// <summary>Viewport rect in GUI coordinates (origin top left).</summary>
        public Rect Rect;

        // camera state: current values ease towards the goals
        private Vector3 target, targetGoal;
        private float yaw, yawGoal;
        private float pitch = 56f, pitchGoal = 56f;
        private float distance = 9f, distanceGoal = 9f;
        private float topBlend, topBlendGoal;
        private bool orthographic;

        public bool TopView => topBlendGoal > 0.5f;
        public bool Orthographic => orthographic || topBlend >= 0.999f;

        /// <summary>The point the camera orbits around (editor space).</summary>
        public Vector3 TargetPoint => targetGoal;

        public void FocusPoint(Vector3 point)
        {
            targetGoal = point;
            distanceGoal = Mathf.Min(distanceGoal, 6f);
        }
        public float Yaw => yaw;

        // pointer
        public bool PointerInside { get; private set; }
        public Vector2Int? HoverCell { get; private set; }
        private Vector2 lastMouse;
        private Vector2 dragStart;
        private DragMode drag = DragMode.None;
        private Vector2Int lastPaintCell;
        private bool pickupPaintValue;
        private Vector2Int lastCursor;
        private bool cursorMovedByPointer;
        private Vector2Int? lastPointerCell;

        private const float MinPitch = 0f;
        private const float MaxPitch = 89f;
        private const float DefaultPitch = 56f;
        private const float MinDistance = 2.5f;
        private const float MaxDistance = 90f;

        private static TrackEditingMode Editing => TrackEditor.GetEditMode<TrackEditingMode>();

        public StudioViewport(StudioShell shell)
        {
            this.shell = shell;
        }

        // Tools
        public void SetTool(StudioTool tool)
        {
            if (drag != DragMode.None)
                EndDrag(false);
            if (tool != StudioTool.Select)
                EndPaste();
            Tool = tool;
        }

        public string ToolHint
        {
            get
            {
                if (PasteActive)
                    return $"Click to {Paste.Verb.ToLowerInvariant()} · R rotates · PgUp/PgDn height · Esc to finish";
                switch (Tool)
                {
                    case StudioTool.Pickups:
                        return "Click a module to add or remove a pickup";
                    case StudioTool.Erase:
                        return "Click or drag over modules to delete them";
                    case StudioTool.Select:
                        return "Click to select · Shift adds · Ctrl toggles · drag empty space to box-select · drag blocks or arrows to move";
                    default:
                        return "Click to place, drag to paint · A fits to neighbours · Alt+click picks · Right-drag orbits · Middle-drag pans";
                }
            }
        }

        // Camera control
        public void Activate()
        {
            var cam = TrackEditor.Instance.Camera;
            cam.orthographic = false;
            FrameTrack(true);
            lastCursor = Editing.CursorPosition;
        }

        public void FrameTrack(bool immediate = false)
        {
            var track = TrackEditor.Track;
            targetGoal = new Vector3((track.Width - 1) * 0.5f, 0f, (track.Height - 1) * 0.5f);
            distanceGoal = FitDistance(track.Width, track.Height);
            if (!TopView && pitchGoal > 5f)
                pitchGoal = Mathf.Max(pitchGoal, 30f);

            if (immediate)
                SnapCamera();
        }

        private float FitDistance(float width, float depth)
        {
            // fit the diagonal into the view
            float size = Mathf.Sqrt(width * width + depth * depth);
            float aspect = Rect.height > 0f ? Rect.width / Rect.height : 1.5f;
            float fit = size * 0.62f / Mathf.Tan(30f * Mathf.Deg2Rad) / Mathf.Clamp(aspect, 0.6f, 1.4f);
            return Mathf.Clamp(fit, MinDistance, MaxDistance);
        }

        /// <summary>Frames the selected blocks, or the whole track if nothing is selected.</summary>
        public void FrameSelection()
        {
            var blocks = Tool == StudioTool.Select ? SelectedPlacements : null;
            if (blocks == null || blocks.Count == 0)
            {
                FrameTrack();
                return;
            }

            var cells = blocks.SelectMany(p => p.TouchedCells).Select(c => c.Position).ToList();
            var min = cells.Aggregate(Vector2Int.Min);
            var max = cells.Aggregate(Vector2Int.Max);
            float height = blocks.Max(p => p.Elevation);
            targetGoal = new Vector3((min.x + max.x) * 0.5f, height * 0.5f, (min.y + max.y) * 0.5f);
            distanceGoal = Mathf.Max(FitDistance(max.x - min.x + 3, max.y - min.y + 3), 5f);
        }

        /// <summary>Default angle, perspective, whole track in view.</summary>
        public void ResetView()
        {
            orthographic = false;
            topBlendGoal = 0f;
            yawGoal = 0f;
            pitchGoal = DefaultPitch;
            FrameTrack();
        }

        public void SetViewPreset(ViewPreset preset)
        {
            switch (preset)
            {
                case ViewPreset.Top:
                    topBlendGoal = 1f;
                    break;
                case ViewPreset.Front:
                    topBlendGoal = 0f;
                    yawGoal = 0f;
                    pitchGoal = 0f;
                    orthographic = true;
                    break;
                case ViewPreset.Side:
                    topBlendGoal = 0f;
                    yawGoal = -90f;
                    pitchGoal = 0f;
                    orthographic = true;
                    break;
                default:
                    topBlendGoal = 0f;
                    orthographic = false;
                    if (pitchGoal < 20f)
                        pitchGoal = DefaultPitch;
                    break;
            }
            TrackEditor.PlaySound(TrackEditor.SndRotate);
        }

        public void ToggleOrthographic()
        {
            orthographic = !orthographic;
        }

        public string ViewName
        {
            get
            {
                if (TopView)
                    return "Top";
                if (pitchGoal < 1f && orthographic)
                {
                    int quadrant = ((Mathf.RoundToInt(yawGoal / 90f) % 4) + 4) % 4;
                    return quadrant == 0 ? "Front" : quadrant == 3 ? "Side" : quadrant == 2 ? "Back" : "Side (west)";
                }
                return orthographic ? "Orthographic" : "Perspective";
            }
        }

        public void FocusCursor()
        {
            var cursor = Editing.CursorPosition;
            targetGoal = new Vector3(cursor.x, Editing.CursorElevation, cursor.y);
            distanceGoal = Mathf.Min(distanceGoal, 9f);
        }

        private Vector2Int highlightCell;
        private float highlightUntil = -1f;

        /// <summary>Pulses a cell outline for a few seconds (e.g. where a track check found a gap).</summary>
        public void Highlight(Vector2Int cell)
        {
            highlightCell = cell;
            highlightUntil = Time.unscaledTime + 4f;
        }

        public void FocusCell(Vector2Int cell)
        {
            var module = InBounds(cell) ? TrackEditor.Track.GetCell(cell).Module : null;
            targetGoal = new Vector3(cell.x, module != null ? module.Elevation : 0f, cell.y);
            distanceGoal = Mathf.Min(distanceGoal, 10f);
        }

        /// <summary>Rotates the view by quarter turns, snapping to the grid axes.</summary>
        public void Orbit90(int turns)
        {
            float snapped = Mathf.Round(yawGoal / 90f) * 90f;
            yawGoal = snapped + 90f * turns;
            TrackEditor.PlaySound(TrackEditor.SndRotate);
        }

        public void ToggleTopView()
        {
            topBlendGoal = TopView ? 0f : 1f;
            if (!TopView && pitchGoal < 20f)
                pitchGoal = DefaultPitch;
        }

        private void SnapCamera()
        {
            target = targetGoal;
            yaw = yawGoal;
            pitch = pitchGoal;
            distance = distanceGoal;
            topBlend = topBlendGoal;
        }

        private void EaseCamera(float dt)
        {
            float k = 1f - Mathf.Exp(-dt * 14f);
            target = Vector3.Lerp(target, targetGoal, k);
            yaw = Mathf.LerpAngle(yaw, yawGoal, k);
            pitch = Mathf.Lerp(pitch, pitchGoal, k);
            distance = Mathf.Lerp(distance, distanceGoal, k);
            topBlend = Mathf.MoveTowards(topBlend, topBlendGoal, dt * 4f);
        }

        /// <summary>Positions the scene camera. Call from LateUpdate.</summary>
        public void ApplyCamera()
        {
            EaseCamera(Time.unscaledDeltaTime);

            var cam = TrackEditor.Instance.Camera;

            // restrict rendering to the viewport area between the panels
            float sw = Mathf.Max(1, Screen.width);
            float sh = Mathf.Max(1, Screen.height);
            cam.rect = new Rect(Rect.x / sw, 1f - Rect.yMax / sh, Rect.width / sw, Rect.height / sh);

            // blend the pitch towards straight down for the top view
            float effectivePitch = Mathf.Lerp(pitch, 89.9f, Mathf.SmoothStep(0f, 1f, topBlend));
            var rotation = Quaternion.Euler(effectivePitch, yaw, 0f);
            cam.transform.position = target - rotation * Vector3.forward * distance;
            cam.transform.rotation = rotation;

            bool ortho = Orthographic;
            cam.orthographic = ortho;
            if (ortho)
                cam.orthographicSize = distance * Mathf.Tan(30f * Mathf.Deg2Rad);

            // arrow keys move the cursor relative to the view
            Editing.CameraRotationTurns = Mathf.RoundToInt(yaw / 90f);
        }

        // Input
        /// <summary>
        /// Handles pointer input. <paramref name="pointerAllowed"/> is false while menus or dialogs
        /// are open, or when the pointer is over an overlay.
        /// </summary>
        public void Update(bool pointerAllowed)
        {
            var mouse = Mouse.current;
            var editing = Editing;
            if (mouse == null || editing == null)
                return;

            Vector2 screen = mouse.position.ReadValue();
            var gui = new Vector2(screen.x, Screen.height - screen.y);
            Vector2 delta = gui - lastMouse;
            lastMouse = gui;

            PointerInside = pointerAllowed && Rect.Contains(gui);

            // hover picking
            HoverCell = null;
            if (PointerInside || drag != DragMode.None)
                HoverCell = PickCell(screen);

            // the pointer moves the cursor only when it moves to another cell, so arrow key
            // movement isn't undone while the mouse rests over the viewport
            cursorMovedByPointer = false;
            if (Tool == StudioTool.Place && !shell.Race.Active && HoverCell.HasValue && (PointerInside || drag == DragMode.Paint))
            {
                if (HoverCell != lastPointerCell)
                {
                    editing.CursorPosition = HoverCell.Value;
                    cursorMovedByPointer = true;
                }
            }
            lastPointerCell = HoverCell;

            // the preview stays visible when the pointer leaves, so keyboard placement keeps working
            editing.PreviewVisible = Tool == StudioTool.Place && !shell.Race.Active && !PasteActive;

            // paste ghosts follow the pointer
            if (PasteActive)
            {
                if (shell.Race.Active)
                    EndPaste();
                else
                {
                    if (PointerInside && HoverCell.HasValue)
                        Paste.Anchor = HoverCell.Value;
                    Paste.UpdateGhosts();
                }
            }

            gizmoHover = drag == DragMode.None && PointerInside && !PasteActive ? HitGizmo(gui) : GizmoAxis.None;

            HandleButtons(mouse, gui, delta);
            if (shell.Race.Active)
                shell.Race.HandlePointer(mouse, screen, delta, PointerInside && (drag == DragMode.None || drag == DragMode.PendingRight));

            // follow the keyboard cursor (pointer moves never pull the camera)
            var cursor = editing.CursorPosition;
            if (cursor != lastCursor)
            {
                if (!cursorMovedByPointer && AppSettings.Data.CameraFollowsCursor && Tool == StudioTool.Place)
                    targetGoal = new Vector3(cursor.x, editing.CursorElevation, cursor.y);
                lastCursor = cursor;
            }

            // zoom, towards the point under the pointer
            if (PointerInside)
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                {
                    float steps = scroll / 120f;
                    if (Mathf.Abs(steps) < 0.01f)
                        steps = Mathf.Sign(scroll);
                    float before = distanceGoal;
                    distanceGoal = Mathf.Clamp(distanceGoal * Mathf.Pow(0.87f, steps), MinDistance, MaxDistance);
                    if (AppSettings.Data.ZoomToPointer && TryGroundPoint(screen, targetGoal.y, out var point))
                    {
                        float k = 1f - distanceGoal / before;
                        var shift = new Vector3(point.x - targetGoal.x, 0f, point.z - targetGoal.z) * k;
                        targetGoal += shift;
                    }
                }
            }

            DrawWorldOverlays();
        }

        private void HandleButtons(Mouse mouse, Vector2 gui, Vector2 delta)
        {
            var editing = Editing;

            // start drags only inside the viewport
            if (PointerInside && drag == DragMode.None)
            {
                if (mouse.rightButton.wasPressedThisFrame)
                {
                    drag = DragMode.PendingRight;
                    dragStart = gui;
                }
                else if (mouse.middleButton.wasPressedThisFrame)
                {
                    drag = DragMode.Pan;
                }
                else if (mouse.leftButton.wasPressedThisFrame && !shell.Race.Active)
                {
                    dragStart = gui;
                    BeginLeftDrag(editing, gui);
                }
            }

            switch (drag)
            {
                case DragMode.PendingRight:
                    if ((gui - dragStart).magnitude > shell.Theme.S(4f))
                        drag = DragMode.Orbit;
                    else if (!mouse.rightButton.isPressed)
                    {
                        drag = DragMode.None;
                        if (PasteActive)
                            EndPaste();
                        else if (shell.Race.Active)
                            shell.OpenRaceContextMenu(gui, GroundPoint(mouse.position.ReadValue()));
                        else
                            shell.OpenViewportContextMenu(gui, HoverCell);
                    }
                    break;

                case DragMode.Orbit:
                    yawGoal += delta.x * 0.3f;
                    pitchGoal = Mathf.Clamp(pitchGoal + delta.y * 0.25f, MinPitch, MaxPitch);
                    yaw = yawGoal;
                    pitch = pitchGoal;
                    if (!mouse.rightButton.isPressed)
                        drag = DragMode.None;
                    break;

                case DragMode.Pan:
                    {
                        // move the target in the ground plane so the grid follows the pointer
                        var cam = TrackEditor.Instance.Camera;
                        float worldPerPixel = 2f * distance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1f, Rect.height);
                        var right = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
                        var forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                        Vector3 move;
                        if (pitch < 10f && !TopView)
                            move = (-right * delta.x + Vector3.up * delta.y) * worldPerPixel; // side views pan up and down
                        else
                            move = (-right * delta.x + forward * delta.y) * worldPerPixel;
                        targetGoal += move;
                        target += move;
                        if (!mouse.middleButton.isPressed)
                            drag = DragMode.None;
                    }
                    break;

                case DragMode.Marquee:
                    marqueeEnd = gui;
                    if (!mouse.leftButton.isPressed)
                        EndDrag();
                    break;

                case DragMode.Gizmo:
                    UpdateGizmoDrag(gui);
                    if (!mouse.leftButton.isPressed)
                        EndDrag();
                    break;

                case DragMode.Paint:
                case DragMode.Erase:
                case DragMode.Pickups:
                case DragMode.MoveSelection:
                    if (!mouse.leftButton.isPressed)
                    {
                        EndDrag();
                        break;
                    }
                    ContinueLeftDrag(editing);
                    break;
            }
        }

        private void BeginLeftDrag(TrackEditingMode editing, Vector2 gui)
        {
            if (PasteActive)
            {
                CommitPaste();
                return;
            }

            if (Tool == StudioTool.Select && gizmoHover != GizmoAxis.None)
            {
                drag = DragMode.Gizmo;
                gizmoAxis = gizmoHover;
                gizmoDragStart = gui;
                moveDelta = Vector2Int.zero;
                moveLevelDelta = 0;
                return;
            }

            if (!HoverCell.HasValue)
            {
                if (Tool == StudioTool.Select)
                    BeginMarquee(gui);
                return;
            }
            var cell = HoverCell.Value;

            // Alt+click: pick the module under the pointer (eyedropper)
            if (Controls.IsAltHeld())
            {
                PickModule(cell);
                return;
            }

            switch (Tool)
            {
                case StudioTool.Place:
                    drag = DragMode.Paint;
                    TrackEditor.History.BeginGroup($"Place {ModuleCatalog.GetDisplayName(editing.ActiveModuleIndex)}");
                    editing.CursorPosition = cell;
                    editing.PlaceModule();
                    NoteModuleUsed(editing.ActiveModuleIndex);
                    lastPaintCell = editing.CursorPosition;
                    break;

                case StudioTool.Erase:
                    drag = DragMode.Erase;
                    TrackEditor.History.BeginGroup("Delete modules");
                    EraseAt(cell);
                    lastPaintCell = cell;
                    break;

                case StudioTool.Select:
                    {
                        var module = TrackEditor.Track.GetCell(cell).Module;
                        if (module == null)
                        {
                            BeginMarquee(gui);
                            return;
                        }
                        var root = module.RootCell.Position;
                        bool shift = Controls.IsShiftHeld();
                        bool ctrl = Controls.IsControlHeld();
                        clickedRoot = root;
                        clickHadModifier = shift || ctrl;

                        if (ctrl)
                        {
                            Selection.Toggle(root);
                            TrackEditor.PlaySound(TrackEditor.SndMenuMove);
                            return; // toggling never starts a move
                        }
                        if (!Selection.Contains(root))
                        {
                            if (shift)
                                Selection.Add(root);
                            else
                                Selection.Set(root);
                            TrackEditor.PlaySound(TrackEditor.SndMenuMove);
                        }
                        else
                        {
                            Selection.Add(root); // becomes the primary
                        }

                        drag = DragMode.MoveSelection;
                        moveGrabCell = cell;
                        moveDelta = Vector2Int.zero;
                        moveLevelDelta = 0;
                        lastPaintCell = cell;
                    }
                    break;

                case StudioTool.Pickups:
                    var trackCell = TrackEditor.Track.GetCell(cell);
                    if (trackCell.Module == null)
                    {
                        TrackEditor.PlaySound(TrackEditor.SndWarning);
                        shell.Toast("Pickups can only be placed on modules", IconId.Warning);
                        return;
                    }
                    drag = DragMode.Pickups;
                    pickupPaintValue = !trackCell.HasPickup;
                    TrackEditor.History.BeginGroup(pickupPaintValue ? "Add pickups" : "Remove pickups");
                    SetPickup(cell, pickupPaintValue, true);
                    lastPaintCell = cell;
                    break;
            }
        }

        private void BeginMarquee(Vector2 gui)
        {
            drag = DragMode.Marquee;
            marqueeStart = marqueeEnd = gui;
            marqueeAdd = Controls.IsShiftHeld();
            marqueeSubtract = Controls.IsControlHeld();
        }

        private void ContinueLeftDrag(TrackEditingMode editing)
        {
            if (!HoverCell.HasValue)
                return;
            var cell = HoverCell.Value;
            if (cell == lastPaintCell)
                return;

            switch (drag)
            {
                case DragMode.Paint:
                    // only single-cell modules paint; dragging a large module would keep replacing itself
                    var module = TrackEditor.TrackUnit.Modules[editing.ActiveModuleIndex];
                    if (module.Instances.Count == 1)
                    {
                        editing.CursorPosition = cell;
                        editing.PlaceModule();
                    }
                    break;
                case DragMode.Erase:
                    EraseAt(cell);
                    break;
                case DragMode.Pickups:
                    SetPickup(cell, pickupPaintValue, false);
                    break;
                case DragMode.MoveSelection:
                    moveDelta = cell - moveGrabCell;
                    break;
            }
            lastPaintCell = cell;
        }

        private void EndDrag(bool commit = true)
        {
            switch (drag)
            {
                case DragMode.Paint:
                case DragMode.Erase:
                case DragMode.Pickups:
                    TrackEditor.EndChangeGroup(); // already applied while painting
                    break;
                case DragMode.MoveSelection:
                    if (commit)
                    {
                        if (moveDelta != Vector2Int.zero)
                            CommitSelectionMove(moveDelta, 0);
                        else if (clickedRoot.HasValue && !clickHadModifier && Selection.Count > 1)
                            Selection.Set(clickedRoot.Value); // a plain click inside a group selects just that block
                    }
                    break;
                case DragMode.Marquee:
                    if (commit)
                        CommitMarquee();
                    break;
                case DragMode.Gizmo:
                    if (commit && (moveDelta != Vector2Int.zero || moveLevelDelta != 0))
                        CommitSelectionMove(moveDelta, moveLevelDelta);
                    break;
            }
            drag = DragMode.None;
            moveDelta = Vector2Int.zero;
            moveLevelDelta = 0;
            clickedRoot = null;
            gizmoAxis = GizmoAxis.None;
        }

        private void CommitSelectionMove(Vector2Int delta, int levels)
        {
            var moved = BlockOperations.MoveGroup(SelectedPlacements, delta, levels);
            if (moved != null)
            {
                Selection.SetMany(moved.Select(p => p.RootCell.Position));
                TrackEditor.PlaySound(levels > 0 ? TrackEditor.SndRaise : levels < 0 ? TrackEditor.SndLower : TrackEditor.SndPlaceModule);
            }
            else
            {
                ReportProblem("There's no room for the blocks there");
            }
        }

        private void CommitMarquee()
        {
            var rect = Rect.MinMaxRect(Mathf.Min(marqueeStart.x, marqueeEnd.x), Mathf.Min(marqueeStart.y, marqueeEnd.y),
                                       Mathf.Max(marqueeStart.x, marqueeEnd.x), Mathf.Max(marqueeStart.y, marqueeEnd.y));
            if (rect.width < 4f && rect.height < 4f)
            {
                // a click on empty space
                if (!marqueeAdd && !marqueeSubtract)
                    Selection.Clear();
                return;
            }

            var cam = TrackEditor.Instance.Camera;
            var inside = new List<Vector2Int>();
            foreach (var placement in TrackEditor.Track.GetAllModuleRootPlacements())
            {
                if (States.IsHidden(placement))
                    continue;
                // a block is inside when the middle of its footprint is
                var center = Vector3.zero;
                foreach (var cell in placement.TouchedCells)
                    center += new Vector3(cell.Position.x, placement.Elevation, cell.Position.y);
                center /= Mathf.Max(1, placement.TouchedCells.Count);
                var screen = cam.WorldToScreenPoint(center);
                if (screen.z <= 0f)
                    continue;
                if (rect.Contains(new Vector2(screen.x, Screen.height - screen.y)))
                    inside.Add(placement.RootCell.Position);
            }

            if (marqueeSubtract)
            {
                foreach (var root in inside)
                    Selection.Remove(root);
            }
            else if (marqueeAdd)
            {
                foreach (var root in inside)
                    Selection.Add(root);
            }
            else
            {
                Selection.SetMany(inside);
            }
            if (inside.Count > 0)
                TrackEditor.PlaySound(TrackEditor.SndMenuMove);
        }

        /// <summary>Ends any drag in progress (e.g. when a dialog opens). A pending block move is cancelled.</summary>
        public void CancelInteraction()
        {
            EndDrag(false);
        }

        public bool IsDragging => drag != DragMode.None;

        private void ReportProblem(string fallback)
        {
            shell.Toast(BlockOperations.LastProblem ?? fallback, IconId.Warning);
        }

        // Select tool keyboard
        /// <summary>
        /// Keys for the selected blocks. Called by the shell while the select tool is active and no
        /// dialog, menu or text field has focus.
        /// </summary>
        public void HandleSelectKeys()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || Controls.IsCommandModifierHeld() || drag != DragMode.None)
                return;

            if (PasteActive)
            {
                HandlePasteKeys(keyboard);
                return;
            }

            var selected = SelectedPlacements;
            if (selected.Count == 0)
                return;

            var delta = Vector2Int.zero;
            if (keyboard.upArrowKey.wasPressedThisFrame) delta = Vector2Int.up;
            else if (keyboard.downArrowKey.wasPressedThisFrame) delta = Vector2Int.down;
            else if (keyboard.leftArrowKey.wasPressedThisFrame) delta = Vector2Int.left;
            else if (keyboard.rightArrowKey.wasPressedThisFrame) delta = Vector2Int.right;

            if (delta != Vector2Int.zero)
            {
                // relative to the view, like the cursor
                var moved = BlockOperations.MoveGroup(selected, delta.Rotate(Editing.CameraRotationTurns), 0);
                if (moved != null)
                {
                    Selection.SetMany(moved.Select(p => p.RootCell.Position));
                    TrackEditor.PlaySound(TrackEditor.SndMenuMove);
                }
                else
                {
                    ReportProblem("There's no room to move there");
                }
                return;
            }

            if (keyboard.deleteKey.wasPressedThisFrame)
                DeleteSelection();
            else if (keyboard.pageUpKey.wasPressedThisFrame)
                RaiseSelection(1);
            else if (keyboard.pageDownKey.wasPressedThisFrame)
                RaiseSelection(-1);
            else if (keyboard.zKey.wasPressedThisFrame)
                ChangeSelectedVariant(false);
            else if (keyboard.xKey.wasPressedThisFrame)
                ChangeSelectedVariant(true);
            else if (keyboard.tKey.wasPressedThisFrame)
                ToggleSelectedSurface();
            else if (keyboard.cKey.wasPressedThisFrame && SelectedPlacement != null)
                PickModule(SelectedPlacement.RootCell.Position);
        }

        private void HandlePasteKeys(Keyboard keyboard)
        {
            if (keyboard.pageUpKey.wasPressedThisFrame)
                ChangePasteLevel(1);
            else if (keyboard.pageDownKey.wasPressedThisFrame)
                ChangePasteLevel(-1);
            else if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
                CommitPaste();
            else
            {
                var delta = Vector2Int.zero;
                if (keyboard.upArrowKey.wasPressedThisFrame) delta = Vector2Int.up;
                else if (keyboard.downArrowKey.wasPressedThisFrame) delta = Vector2Int.down;
                else if (keyboard.leftArrowKey.wasPressedThisFrame) delta = Vector2Int.left;
                else if (keyboard.rightArrowKey.wasPressedThisFrame) delta = Vector2Int.right;
                if (delta != Vector2Int.zero)
                    Paste.Anchor += delta.Rotate(Editing.CameraRotationTurns);
            }
        }

        // Selection commands
        public void DeleteSelection()
        {
            var selected = SelectedPlacements;
            if (selected.Count == 0)
                return;
            BlockOperations.DeleteGroup(selected);
            if (BlockOperations.LastProblem != null)
                shell.Toast(BlockOperations.LastProblem, IconId.Warning);
        }

        public void RaiseSelection(int levels)
        {
            var selected = SelectedPlacements;
            if (selected.Count == 0)
                return;
            var moved = BlockOperations.MoveGroup(selected, Vector2Int.zero, levels);
            if (moved != null)
            {
                Selection.SetMany(moved.Select(p => p.RootCell.Position));
                TrackEditor.PlaySound(levels > 0 ? TrackEditor.SndRaise : TrackEditor.SndLower);
            }
            else
            {
                ReportProblem("That's outside the height range");
            }
        }

        public void RotateSelection(int turns)
        {
            var selected = SelectedPlacements;
            if (selected.Count == 0)
                return;
            var rotated = BlockOperations.RotateGroup(selected, turns);
            if (rotated != null)
                Selection.SetMany(rotated.Select(p => p.RootCell.Position));
            else
                ReportProblem("There's no room to turn the blocks");
        }

        public void ChangeSelectedVariant(bool next)
        {
            var selected = SelectedPlacements;
            if (selected.Count == 0)
                return;
            int changed = BlockOperations.ChangeGroupModule(selected, id =>
            {
                var change = Modules.Lookup.Changes[id];
                return next ? change.NextVar : change.PreviousVar;
            }, selected.Count == 1 ? "Change block variant" : "Change variants");
            if (changed > 0)
            {
                RestoreSelectionAfterReplace(selected);
                TrackEditor.PlaySound(next ? TrackEditor.SndLower : TrackEditor.SndRaise);
            }
        }

        public void ToggleSelectedSurface()
        {
            var selected = SelectedPlacements;
            if (selected.Count == 0)
                return;
            int changed = BlockOperations.ChangeGroupModule(selected, id => Modules.Lookup.OtherSurface[id],
                                                            selected.Count == 1 ? "Toggle block surface" : "Toggle surfaces");
            if (changed > 0)
            {
                RestoreSelectionAfterReplace(selected);
                TrackEditor.PlaySound(TrackEditor.SndRaise);
            }
        }

        private void RestoreSelectionAfterReplace(List<ModulePlacement> previous)
        {
            // replaced blocks keep their root cells
            Selection.SetMany(previous.Select(p => p.RootCell.Position).ToList());
        }

        public void SelectAll()
        {
            Selection.SetMany(TrackEditor.Track.GetAllModuleRootPlacements().Where(p => !States.IsHidden(p)).Select(p => p.RootCell.Position).ToList());
            if (Tool != StudioTool.Select)
                SetTool(StudioTool.Select);
        }

        public void SelectNone() => Selection.Clear();

        public void InvertSelection()
        {
            var current = new HashSet<Vector2Int>(Selection.Roots);
            Selection.SetMany(TrackEditor.Track.GetAllModuleRootPlacements()
                .Select(p => p.RootCell.Position)
                .Where(r => !current.Contains(r))
                .ToList());
            if (Tool != StudioTool.Select)
                SetTool(StudioTool.Select);
        }

        /// <summary>Selects every block connected to the selected ones (the whole section).</summary>
        public void SelectConnected()
        {
            var report = ConnectivityService.Current(shell.TrackRevision);
            var placements = ConnectivityService.Placements;
            if (report == null || Selection.IsEmpty)
                return;
            var sections = new HashSet<int>();
            foreach (var p in SelectedPlacements)
            {
                int index = ConnectivityService.IndexOf(p);
                if (index >= 0)
                    sections.Add(report.Component[index]);
            }
            var roots = new List<Vector2Int>();
            for (int i = 0; i < placements.Count; i++)
            {
                if (sections.Contains(report.Component[i]))
                    roots.Add(placements[i].RootCell.Position);
            }
            Selection.SetMany(roots);
            TrackEditor.PlaySound(TrackEditor.SndMenuMove);
        }

        /// <summary>Selects every block of the same module types as the selected ones.</summary>
        public void SelectSameType()
        {
            var types = new HashSet<int>(SelectedPlacements.Select(p => p.ModuleIndex));
            if (types.Count == 0)
                return;
            Selection.SetMany(TrackEditor.Track.GetAllModuleRootPlacements()
                .Where(p => types.Contains(p.ModuleIndex))
                .Select(p => p.RootCell.Position)
                .ToList());
            TrackEditor.PlaySound(TrackEditor.SndMenuMove);
        }

        // Clipboard and paste
        public void CopySelection()
        {
            var selected = SelectedPlacements;
            if (selected.Count == 0)
            {
                TrackEditor.PlaySound(TrackEditor.SndWarning);
                return;
            }
            Clipboard.Copy(selected);
            shell.Toast(selected.Count == 1 ? "Copied 1 block" : $"Copied {selected.Count} blocks", IconId.Copy);
        }

        public void CutSelection()
        {
            var selected = SelectedPlacements;
            if (selected.Count == 0)
                return;
            if (selected.Any(BlockOperations.IsLocked))
            {
                shell.Toast("Locked blocks can't be cut", IconId.Warning);
                return;
            }
            Clipboard.Copy(selected);
            BlockOperations.DeleteGroup(selected);
            shell.Toast(selected.Count == 1 ? "Cut 1 block" : $"Cut {selected.Count} blocks", IconId.Copy);
        }

        public void PasteClipboard()
        {
            if (Clipboard.IsEmpty)
            {
                shell.Toast("Nothing to paste: copy blocks first (Ctrl+C)", IconId.Info);
                return;
            }
            var anchor = HoverCell ?? Editing.CursorPosition;
            StartPaste(Clipboard.Items, anchor, "Paste");
        }

        /// <summary>Copies the selection and lets the copy follow the pointer until it's placed.</summary>
        public void DuplicateSelection()
        {
            var selected = SelectedPlacements;
            if (selected.Count == 0)
            {
                TrackEditor.PlaySound(TrackEditor.SndWarning);
                return;
            }
            var items = BlockClipboard.Normalize(selected, out var anchor);
            StartPaste(items, anchor + Vector2Int.right, "Duplicate");
        }

        private void StartPaste(List<BlockSpec> items, Vector2Int anchor, string verb)
        {
            EndPaste();
            if (shell.Race.Active)
                shell.SetRaceMode(false);
            if (Tool != StudioTool.Select)
                SetTool(StudioTool.Select);
            Paste = new PasteSession(items, anchor, verb);
            Paste.UpdateGhosts();
            TrackEditor.PlaySound(TrackEditor.SndMenuOpen);
        }

        public void EndPaste()
        {
            if (Paste == null)
                return;
            Paste.Dispose();
            Paste = null;
        }

        public void RotatePaste(int turns)
        {
            if (!PasteActive)
                return;
            Paste.Rotate(turns);
            TrackEditor.PlaySound(TrackEditor.SndRotate);
        }

        public void ChangePasteLevel(int delta)
        {
            if (!PasteActive)
                return;
            if (Paste.ChangeLevel(delta, Editing.MaxHeightSteps))
                TrackEditor.PlaySound(delta > 0 ? TrackEditor.SndRaise : TrackEditor.SndLower);
            else
                TrackEditor.PlaySound(TrackEditor.SndWarning);
        }

        private void CommitPaste()
        {
            var specs = Paste.Resolve();
            string label = specs.Count == 1 ? $"{Paste.Verb} {ModuleCatalog.GetDisplayName(specs[0].ModuleIndex)}" : $"{Paste.Verb} {specs.Count} blocks";
            var placed = BlockOperations.ApplyGroup(new List<ModulePlacement>(), specs, label, true);
            if (placed == null)
            {
                ReportProblem("The blocks don't fit there");
                return;
            }
            Paste.Placements++;
            Selection.SetMany(placed.Select(p => p.RootCell.Position));
            TrackEditor.PlaySound(TrackEditor.SndPlaceModule);
            if (BlockOperations.LastProblem != null)
                shell.Toast(BlockOperations.LastProblem, IconId.Warning);

            // duplicates are placed once; pasting can repeat until Esc
            if (Paste.Verb == "Duplicate")
                EndPaste();
        }

        // Editing helpers shared with menus
        /// <summary>
        /// Eyedropper: takes module, rotation and height from the module at <paramref name="cell"/>.
        /// Reads the cell directly; moving the cursor there could be clamped to a different cell.
        /// </summary>
        public void PickModule(Vector2Int cell)
        {
            var track = TrackEditor.Track;
            if (!InBounds(cell) || track.GetCell(cell).Module == null)
            {
                TrackEditor.PlaySound(TrackEditor.SndWarning);
                return;
            }

            var placement = track.GetCell(cell).Module;
            var editing = Editing;
            editing.SetActiveModule(placement.ModuleIndex);
            editing.ModuleRotation = placement.Rotation;
            editing.CursorHeightSteps = Mathf.RoundToInt(placement.Elevation / EditorConstants.ElevationStep);
            editing.FlashCursor();
            TrackEditor.PlaySound(TrackEditor.SndAdjust);
            SetTool(StudioTool.Place);
        }

        public void EraseAt(Vector2Int cell)
        {
            var trackCell = TrackEditor.Track.GetCell(cell);
            if (trackCell.Module == null)
                return;

            var placement = trackCell.Module;
            if (BlockOperations.IsLocked(placement))
            {
                if (drag != DragMode.Erase || lastPaintCell != cell)
                    shell.Toast("That block is locked", IconId.Lock);
                return;
            }
            TrackEditor.RecordChange($"Delete {ModuleCatalog.GetDisplayName(placement.ModuleIndex)}", () =>
            {
                placement.Delete();
                TrackEditor.Track.MarkDirty();
            });
            TrackEditor.PlaySound(TrackEditor.SndDelete);
        }

        /// <summary>Sets or toggles a pickup, respecting the 40 pickup limit. Returns false if refused.</summary>
        public bool SetPickup(Vector2Int cell, bool value, bool reportErrors)
        {
            var track = TrackEditor.Track;
            var trackCell = track.GetCell(cell);
            if (trackCell.HasPickup == value)
                return true;

            if (value)
            {
                if (trackCell.Module == null)
                    return false;

                int count = track.Cells.Count(x => x.HasPickup);
                if (count >= TrackValidator.MaxPickups && !TrackEditor.UnlimitedMode)
                {
                    if (reportErrors)
                    {
                        TrackEditor.PlaySound(TrackEditor.SndWarning);
                        shell.Toast($"The game supports at most {TrackValidator.MaxPickups} pickups", IconId.Warning);
                    }
                    return false;
                }
            }

            TrackEditor.RecordChange(value ? "Add pickup" : "Remove pickup", () =>
            {
                trackCell.HasPickup = value;
                track.MarkDirty();
            });
            TrackEditor.PlaySound(TrackEditor.SndPlacePickup);
            return true;
        }

        /// <summary>Remembers a module for the library's Recent list.</summary>
        public static void NoteModuleUsed(int moduleIndex)
        {
            var recent = AppSettings.Data.RecentModules;
            if (recent.Count > 0 && recent[0] == moduleIndex)
                return;
            recent.Remove(moduleIndex);
            recent.Insert(0, moduleIndex);
            while (recent.Count > 16)
                recent.RemoveAt(recent.Count - 1);
            AppSettings.Save();
        }

        // Placement assistance
        private PlacementFit cachedFit;
        private (int module, Vector2Int cell, int rotation, int level, int revision) cachedFitKey;

        /// <summary>How the module at the cursor would connect (place tool), or null.</summary>
        public PlacementFit CurrentFit
        {
            get
            {
                var editing = Editing;
                if (Tool != StudioTool.Place || shell.Race.Active || editing == null)
                    return null;
                var key = (editing.ActiveModuleIndex, editing.CursorPosition, ((editing.ModuleRotation % 4) + 4) % 4, editing.CursorHeightSteps, shell.TrackRevision);
                if (cachedFit == null || !cachedFitKey.Equals(key))
                {
                    var report = ConnectivityService.Current(shell.TrackRevision);
                    var shape = ConnectivityService.ShapeOf(editing.ActiveModuleIndex);
                    cachedFit = report == null || shape == null ? null
                        : TrackConnectivity.Evaluate(report, shape, key.Item2.x, key.Item2.y, key.Item3, key.Item4);
                    cachedFitKey = key;
                }
                return cachedFit;
            }
        }

        /// <summary>Turns and raises the module to place so it joins the openings around the cursor.</summary>
        public void FitModuleToNeighbours()
        {
            var editing = Editing;
            var report = ConnectivityService.Current(shell.TrackRevision);
            var shape = ConnectivityService.ShapeOf(editing.ActiveModuleIndex);
            if (report == null || shape == null)
                return;

            var cursor = editing.CursorPosition;
            int rotation = ((editing.ModuleRotation % 4) + 4) % 4;
            var fit = TrackConnectivity.FindBestFit(report, shape, cursor.x, cursor.y, rotation, editing.CursorHeightSteps, editing.MaxHeightSteps);
            if (fit == null)
            {
                var current = CurrentFit;
                bool connected = current != null && current.Connects + current.Jumps > 0;
                shell.Toast(connected ? "Already the best fit here" : "No track to connect to next to the cursor", IconId.Info);
                TrackEditor.PlaySound(TrackEditor.SndWarning);
                return;
            }

            editing.ModuleRotation = fit.Rotation;
            if (fit.Level != editing.CursorHeightSteps)
                editing.AdjustCursorHeight(fit.Level - editing.CursorHeightSteps);
            TrackEditor.PlaySound(TrackEditor.SndRotate);
            string joins = fit.Connects + fit.Jumps == 1 ? "1 connection" : $"{fit.Connects + fit.Jumps} connections";
            shell.Toast($"Fitted: {fit.Rotation * 90}°, level {fit.Level} · {joins}", IconId.Link);
        }

        // Picking
        /// <summary>
        /// Finds the grid cell under a screen point. Raised modules are tested first (from the highest
        /// elevation down), so pointing at a raised piece selects it rather than the ground behind it.
        /// </summary>
        private Vector2Int? PickCell(Vector2 screenPoint)
        {
            var cam = TrackEditor.Instance.Camera;
            var track = TrackEditor.Track;
            var ray = cam.ScreenPointToRay(screenPoint);

            if (Tool == StudioTool.Place)
            {
                // placing happens at the cursor height, so pick on that plane
                var cell = IntersectCell(ray, Editing.CursorElevation);
                return cell.HasValue ? ClampToTrack(cell.Value) : (Vector2Int?)null;
            }

            var levels = new SortedSet<float>();
            foreach (var placement in track.GetAllModuleRootPlacements())
                levels.Add(placement.Elevation);

            foreach (float level in levels.Reverse())
            {
                var cell = IntersectCell(ray, level);
                if (!cell.HasValue)
                    continue;
                var c = cell.Value;
                if (!InBounds(c))
                    continue;
                var module = track.GetCell(c).Module;
                if (module != null && Mathf.Approximately(module.Elevation, level) && !States.IsHidden(module))
                    return c;
            }

            var ground = IntersectCell(ray, 0f);
            if (!ground.HasValue)
                return null;
            var g = ground.Value;
            if (!InBounds(g))
                return null;
            return g;
        }

        private static Vector2Int? IntersectCell(Ray ray, float height)
        {
            var plane = new Plane(Vector3.up, new Vector3(0f, height, 0f));
            if (!plane.Raycast(ray, out float enter))
                return null;
            var point = ray.GetPoint(enter);
            return new Vector2Int(Mathf.RoundToInt(point.x), Mathf.RoundToInt(point.z));
        }

        private static bool InBounds(Vector2Int cell)
        {
            var track = TrackEditor.Track;
            return cell.x >= 0 && cell.y >= 0 && cell.x < track.Width && cell.y < track.Height;
        }

        private static Vector2Int ClampToTrack(Vector2Int cell)
        {
            var track = TrackEditor.Track;
            return new Vector2Int(Mathf.Clamp(cell.x, 0, track.Width - 1), Mathf.Clamp(cell.y, 0, track.Height - 1));
        }

        /// <summary>Where a screen point hits the ground (editor space), or the view centre.</summary>
        public Vector3 GroundPoint(Vector2 screen)
        {
            return TryGroundPoint(screen, 0f, out var point) ? point : targetGoal;
        }

        private static bool TryGroundPoint(Vector2 screen, float height, out Vector3 point)
        {
            var ray = TrackEditor.Instance.Camera.ScreenPointToRay(screen);
            var plane = new Plane(Vector3.up, new Vector3(0f, height, 0f));
            if (plane.Raycast(ray, out float enter) && enter < 500f)
            {
                point = ray.GetPoint(enter);
                return true;
            }
            point = default;
            return false;
        }
    }
}

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
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Studio
{
    /// <summary>
    /// The race data workspace: generated AI nodes, position nodes, zones, lights and objects, plus
    /// instances and force fields, edited directly in the viewport. Data lives in the track's extras,
    /// so edits are undoable and saved with the track.
    /// </summary>
    public sealed class RaceEditor
    {
        private readonly StudioShell shell;

        public bool Active { get; private set; }
        public RaceLayer Layer { get; private set; } = RaceLayer.AINodes;
        public readonly HashSet<RaceLayer> Hidden = new HashSet<RaceLayer>();

        public int Selected = -1;
        public int SelectedPart;
        private RaceHandle? hover;

        // parsed data cache: re-parsed only when the stored section object changes (edit, undo, load)
        private RaceData cache;
        private object cacheSource;
        public string DataError { get; private set; }

        // stale check cache
        private int staleRevision = -1;
        private bool stale;

        // dragging
        private bool dragging;
        private bool moved;
        private float dragPlaneY;
        private Vector3 lastHit;

        public RaceEditor(StudioShell shell)
        {
            this.shell = shell;
        }

        private static EditorTrack Track => TrackEditor.Track;

        public RaceData Data
        {
            get
            {
                var section = Track.Extras?.GetSection(RaceData.SectionKey);
                if (!ReferenceEquals(section, cacheSource))
                {
                    cacheSource = section;
                    cache = null;
                    DataError = null;
                    if (section != null)
                    {
                        try
                        {
                            cache = RaceData.FromJson(section);
                        }
                        catch (Exception ex)
                        {
                            DataError = ex.Message;
                        }
                    }
                    if (cache == null || Selected >= RaceLayers.Count(cache, Layer))
                        Selected = -1;
                }
                return cache;
            }
        }

        public bool IsStale
        {
            get
            {
                var data = Data;
                if (data == null)
                    return false;
                if (staleRevision != shell.TrackRevision)
                {
                    staleRevision = shell.TrackRevision;
                    stale = data.IsStaleFor(Track);
                }
                return stale;
            }
        }

        public string Hint
        {
            get
            {
                if (Data == null)
                    return "Generate race data from the track to edit it";
                return $"Click a {RaceLayers.Singular(Layer)} to select it · drag to move (Shift: up/down) · Ins adds · Del deletes";
            }
        }

        // Mode and layers
        public void SetActive(bool active)
        {
            if (dragging)
                EndDrag(false);
            Active = active;
            Selected = -1;
        }

        public void SetLayer(RaceLayer layer)
        {
            if (dragging)
                EndDrag(false);
            Layer = layer;
            Hidden.Remove(layer);
            Selected = -1;
        }

        // Data operations
        /// <summary>
        /// Applies an edit to the race data as one undo step.
        /// </summary>
        public void Commit(string label, Action<RaceData> edit)
        {
            var data = Data;
            if (data == null)
                return;

            edit(data);
            TrackEditor.RecordChange(label, () => data.StoreInTrack(Track));

            // keep using the object we just stored instead of parsing it again
            cacheSource = Track.Extras.GetSection(RaceData.SectionKey);
            cache = data;
        }

        public void Generate()
        {
            var existing = Data;
            if (existing == null)
            {
                GenerateNow(null);
                return;
            }

            shell.OpenDialog(new ConfirmDialog("Regenerate race data?",
                "AI nodes, position nodes, zones, lights and objects are generated again from the current track; your edits to them are replaced. " +
                "Instances, force fields and imported models are kept. You can undo this.",
                null, 0,
                new ConfirmDialog.Choice("Regenerate", ButtonKind.Primary, () => GenerateNow(existing)),
                new ConfirmDialog.Choice("Cancel", ButtonKind.Secondary, null)));
        }

        private void GenerateNow(RaceData existing)
        {
            TrackProblem problem;
            RaceData data;
            try
            {
                data = RaceData.Generate(Track, TrackEditor.TrackUnit, out problem);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Race data generation failed: {ex}");
                shell.ShowMessage("Couldn't generate race data", ex.Message, true);
                return;
            }

            if (data == null)
            {
                TrackEditor.PlaySound(TrackEditor.SndWarning);
                shell.OpenDialog(new MessageDialog("Fix the track first",
                    $"{problem.Message}\n\nRace data is generated from a complete lap: one start grid and a closed loop.", MessageKind.Warning,
                    () => shell.FocusProblem(problem)));
                return;
            }

            if (existing != null)
            {
                data.Instances = existing.Instances;
                data.ForceFields = existing.ForceFields;
                data.Models.AddRange(existing.Models);
                data.Enabled = existing.Enabled;
            }

            TrackEditor.RecordChange(existing == null ? "Generate race data" : "Regenerate race data", () => data.StoreInTrack(Track));
            cacheSource = Track.Extras.GetSection(RaceData.SectionKey);
            cache = data;
            Selected = -1;
            staleRevision = -1;
            shell.Toast(existing == null ? "Race data generated" : "Race data regenerated", IconId.Success);
            TrackEditor.PlaySound(TrackEditor.SndAdjust);
        }

        public void Remove()
        {
            shell.OpenDialog(new ConfirmDialog("Remove race data?",
                "All edited race data, instances, force fields and imported model references are removed from this track. Export will use generated data again. You can undo this.",
                null, -1,
                new ConfirmDialog.Choice("Remove", ButtonKind.Danger, () =>
                {
                    TrackEditor.RecordChange("Remove race data", () => RaceData.RemoveFromTrack(Track));
                    Selected = -1;
                }),
                new ConfirmDialog.Choice("Cancel", ButtonKind.Secondary, null)));
        }

        public void AddElement(Vector3? editorPosition = null)
        {
            var data = Data;
            if (data == null)
                return;

            if (Layer == RaceLayer.Instances && data.Models.Count == 0 && data.Instances.Instances.Count == 0)
            {
                shell.ShowMessage("Import a model first", "Instances place .prm models. Import one in the Race Data panel on the left, then add instances of it.", false);
                return;
            }

            var position = editorPosition ?? shell.Viewport.TargetPoint;
            int index = -1;
            Commit($"Add {RaceLayers.Singular(Layer)}", d => index = RaceLayers.Add(d, Layer, position, Selected));
            Selected = index;
            SelectedPart = 0;
            TrackEditor.PlaySound(TrackEditor.SndPlaceModule);
        }

        public void DeleteSelected()
        {
            var data = Data;
            if (data == null || Selected < 0 || Selected >= RaceLayers.Count(data, Layer))
                return;
            int index = Selected;
            Commit($"Delete {RaceLayers.Singular(Layer)}", d => RaceLayers.Delete(d, Layer, index));
            Selected = -1;
            TrackEditor.PlaySound(TrackEditor.SndDelete);
        }

        // Pointer
        /// <summary>Handles the left mouse button in the viewport while the race workspace is active.</summary>
        public void HandlePointer(Mouse mouse, Vector2 screen, Vector2 guiDelta, bool inside)
        {
            var data = Data;
            var camera = TrackEditor.Instance.Camera;

            if (dragging)
            {
                if (!mouse.leftButton.isPressed || data == null || Selected < 0)
                {
                    EndDrag(true);
                    return;
                }

                Vector3 delta;
                if (Controls.IsShiftHeld())
                {
                    // vertical: screen movement converted to world units at the element's distance
                    var handle = RaceLayers.Position(data, Layer, Selected);
                    float distance = Vector3.Distance(camera.transform.position, handle);
                    float worldPerPixel = camera.orthographic
                        ? 2f * camera.orthographicSize / Mathf.Max(1f, camera.pixelHeight)
                        : 2f * distance * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1f, camera.pixelHeight);
                    delta = new Vector3(0f, -guiDelta.y * worldPerPixel, 0f);
                    dragPlaneY += delta.y;
                    lastHit.y = dragPlaneY;
                }
                else
                {
                    if (!PlaneHit(camera, screen, dragPlaneY, out var hit))
                        return;
                    delta = hit - lastHit;
                    lastHit = hit;
                }

                if (delta.sqrMagnitude > 1e-10f)
                {
                    RaceLayers.Translate(data, Layer, Selected, SelectedPart, delta);
                    moved = true;
                }
                return;
            }

            hover = null;
            if (!inside || data == null || Hidden.Contains(Layer))
                return;

            hover = Pick(data, camera, screen);
            if (mouse.leftButton.wasPressedThisFrame)
            {
                if (hover.HasValue)
                {
                    if (Selected != hover.Value.Index)
                        TrackEditor.PlaySound(TrackEditor.SndMenuMove);
                    Selected = hover.Value.Index;
                    SelectedPart = hover.Value.Part;
                    dragging = true;
                    moved = false;
                    dragPlaneY = hover.Value.Position.y;
                    PlaneHit(camera, screen, dragPlaneY, out lastHit);
                }
                else
                {
                    Selected = -1;
                }
            }
        }

        public void CancelInteraction()
        {
            if (dragging)
                EndDrag(false);
        }

        private void EndDrag(bool commit)
        {
            dragging = false;
            if (!moved)
                return;
            moved = false;

            if (commit)
            {
                if (Layer == RaceLayer.PosNodes)
                    Commit($"Move {RaceLayers.Singular(Layer)}", d => RaceLayers.RecalculatePosDistances(d.PosNodes));
                else
                    Commit($"Move {RaceLayers.Singular(Layer)}", d => { });
            }
            else
            {
                // drop the uncommitted move by re-reading the stored data
                cacheSource = null;
            }
        }

        private RaceHandle? Pick(RaceData data, Camera camera, Vector2 screen)
        {
            float radius = shell.Theme.S(14f);
            RaceHandle? best = null;
            float bestDistance = radius;
            foreach (var handle in RaceLayers.Handles(data, Layer))
            {
                var projected = camera.WorldToScreenPoint(handle.Position);
                if (projected.z <= 0f)
                    continue;
                float d = Vector2.Distance(screen, projected);
                // the ends of AI nodes win over their centre when both are close
                if (handle.Part != 0)
                    d -= 2f;
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = handle;
                }
            }
            return best;
        }

        private static bool PlaneHit(Camera camera, Vector2 screen, float height, out Vector3 hit)
        {
            var ray = camera.ScreenPointToRay(screen);
            var plane = new Plane(Vector3.up, new Vector3(0f, height, 0f));
            if (plane.Raycast(ray, out float enter))
            {
                hit = ray.GetPoint(enter);
                return true;
            }
            hit = Vector3.zero;
            return false;
        }

        // Keyboard
        public void HandleKeys()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || Controls.IsCommandModifierHeld() || dragging)
                return;

            if (keyboard.deleteKey.wasPressedThisFrame)
                DeleteSelected();
            else if (keyboard.insertKey.wasPressedThisFrame)
                AddElement();
        }

        // Drawing
        public void Draw()
        {
            var data = Data;
            if (data == null)
                return;

            foreach (var layer in RaceLayers.All)
            {
                if (layer == Layer || Hidden.Contains(layer))
                    continue;
                RaceLayers.Draw(data, layer, false, -1, 0);
            }
            // the active layer last, on top
            if (!Hidden.Contains(Layer))
                RaceLayers.Draw(data, Layer, true, Selected, SelectedPart);
        }

        /// <summary>Labels for the hovered and selected elements (GUI overlay).</summary>
        public void DrawLabels(StudioTheme T)
        {
            var data = Data;
            if (data == null || Event.current.type != EventType.Repaint)
                return;

            var camera = TrackEditor.Instance.Camera;
            void LabelAt(Vector3 world, string text, bool strong)
            {
                var p = camera.WorldToScreenPoint(world);
                if (p.z <= 0f)
                    return;
                var gui = new Vector2(p.x, Screen.height - p.y);
                float width = SGUI.TextWidth(text, T.SmallStrong) + T.S(12f);
                var r = new Rect(gui.x + T.S(10f), gui.y - T.S(24f), width, T.S(20f)).Snap();
                SGUI.Rounded(r, (strong ? Palette.Overlay : Palette.Chrome).WithAlpha(0.92f), T.S(4f));
                SGUI.Label(r, text, T.SmallStrong, strong ? Palette.Text : Palette.TextDim, TextAnchor.MiddleCenter);
            }

            int count = RaceLayers.Count(data, Layer);
            if (hover.HasValue && hover.Value.Index != Selected && hover.Value.Index < count)
                LabelAt(hover.Value.Position, $"{Capitalize(RaceLayers.Singular(Layer))} {hover.Value.Index}", false);
            if (Selected >= 0 && Selected < count)
                LabelAt(RaceLayers.Position(data, Layer, Selected), $"{Capitalize(RaceLayers.Singular(Layer))} {Selected}", true);
        }

        private static string Capitalize(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}

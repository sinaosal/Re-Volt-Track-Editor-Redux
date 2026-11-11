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

using ReVolt.TrackUnit.Unity;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class TrackEditingMode : EditorMode
{
    // Virtual properties
    public override string ModeName => Localization.Lookup(LocString.MODE_PLACEMODULES);
    public override string HelpPath => "HelpFiles/TrackEditingMode";

    // hierarchy stuff
    private GameObject placedPiecesParent;

    // cursor
    public Vector2Int CursorPosition
    {
        get
        {
            return editingCursor.Position;
        }
        set
        {
            editingCursor.Position = value;
            ClampCursor();
        }
    }

    public int CursorHeightSteps
    {
        get
        {
            return editingCursorHeightSteps;
        }
        set
        {
            editingCursorHeightSteps = value;
        }
    }

    private Cursor editingCursor = new Cursor() { Size = Vector3.one * 1.2f };
    private int editingCursorHeightSteps = 0;
    private float editingCursorHeight => EditorConstants.ElevationStep * editingCursorHeightSteps;
    private float cursorHighlightTime = 0f;

    // module stuff
    private List<GameObject> moduleObjects = new List<GameObject>();

    private int activeModuleIndex = (int)Modules.ID.TWM_START;
    private GameObject activeModule => moduleObjects[activeModuleIndex];

    private int moduleRotationTurns = 0;
    private float moduleRotation => moduleRotationTurns * 90f;

    // camera variables
    private int cameraRotationTurns = 0; // number of clockwise rotations of the camera
    private float cameraRotationTarget => cameraRotationTurns * 90f;
    private float cameraRotation = 0f;
    private float cameraHeight = 5.55f;
    private float cameraDist = 3.7f;

    // Studio integration
    /// <summary>When false, another controller (the Studio viewport) positions the camera.</summary>
    [NonSerialized] public bool ControlsCamera = true;

    /// <summary>Classic blinking module preview. The Studio interface uses a steady preview.</summary>
    [NonSerialized] public bool BlinkPreview = true;

    /// <summary>Shows the module preview and cursor box. Hidden e.g. while the pointer is outside the viewport.</summary>
    [NonSerialized] public bool PreviewVisible = true;

    /// <summary>Raised instead of rotating the classic camera when <see cref="ControlsCamera"/> is false.</summary>
    public event Action<int> CameraRotateRequested;

    /// <summary>Raised when the module to place, its rotation, or the cursor height changes.</summary>
    public event Action PlacementSettingsChanged;

    /// <summary>Set by the Studio interface: blocks locked against editing. Placing over or deleting them is refused.</summary>
    [NonSerialized] public Func<ModulePlacement, bool> IsProtected;

    /// <summary>Set by the Studio interface: blocks hidden in the viewport (display only).</summary>
    [NonSerialized] public Func<ModulePlacement, bool> IsHidden;

    /// <summary>Raised when an edit is refused because it would change a locked block.</summary>
    public event Action<string> EditRefused;

    private bool Protected(ModulePlacement placement) => placement != null && IsProtected != null && IsProtected(placement);

    /// <summary>A new inactive copy of a module's preview object (for ghosts); the caller destroys it.</summary>
    public GameObject CreateModuleGhost(int moduleIndex)
    {
        if (moduleIndex < 0 || moduleIndex >= moduleObjects.Count)
            return null;
        var ghost = Instantiate(moduleObjects[moduleIndex]);
        ghost.SetActive(false);
        return ghost;
    }

    public int ActiveModuleIndex => activeModuleIndex;

    public int ModuleRotation
    {
        get => moduleRotationTurns;
        set
        {
            moduleRotationTurns = ((value % 4) + 4) % 4;
            PlacementSettingsChanged?.Invoke();
        }
    }

    /// <summary>Number of clockwise 90 degree camera turns; arrow keys move the cursor relative to it.</summary>
    public int CameraRotationTurns
    {
        get => cameraRotationTurns;
        set => cameraRotationTurns = ((value % 4) + 4) % 4;
    }

    public float CursorElevation => editingCursorHeight;

    /// <summary>The classic orange cursor box. The Studio viewport draws its own footprint highlight.</summary>
    [NonSerialized] public bool DrawCursorBox = true;

    /// <summary>Flashes the cursor highlight, as after placing a module.</summary>
    public void FlashCursor()
    {
        cursorHighlightTime = 0.25f;
    }

    public int MaxHeightSteps => TrackEditor.UnlimitedMode ? ushort.MaxValue : EditorConstants.MaxElevationSteps;

    /// <summary>
    /// Cells the module preview currently covers.
    /// </summary>
    public List<Vector2Int> GetPreviewFootprint()
    {
        return TrackEditor.GetFootprint(activeModuleIndex, editingCursor.Position, moduleRotationTurns);
    }

    /// <summary>
    /// True if placing the preview would replace modules that are already on the track.
    /// </summary>
    public bool PreviewReplacesModules()
    {
        foreach (var _ in GetIntersectedModules())
            return true;
        return false;
    }

    // "Events"
    private void OnActiveModuleChange()
    {
        for (int i = 0; i < moduleObjects.Count; i++)
            moduleObjects[i].SetActive(i == activeModuleIndex);
        UpdateActiveModule();
    }

    // Public interface
    public void SetActiveModule(int id)
    {
        activeModuleIndex = id;
        OnActiveModuleChange();
        PlacementSettingsChanged?.Invoke();
    }

    public void PlaceModule(int moduleIndex, Vector2Int position, float elevation, int rotation)
    {
        // delete any objects in the way
        var module = TrackEditor.TrackUnit.Modules[moduleIndex];
        foreach (var instance in module.Instances)
        {
            var destCell = TrackEditor.Track.GetCell(position + instance.Position.Rotate(rotation));
            if (destCell.Module != null)
            {
                destCell.Module.Delete();
            }
        }

        // now place
        var placed = Instantiate(moduleObjects[moduleIndex]);
        placed.transform.SetParent(placedPiecesParent.transform, false);
        placed.GetComponent<ModuleInstance>().SetRootHeight(elevation);
        placed.SetActive(true);

        // add to track data
        var placement = new ModulePlacement
        {
            Object = placed,
            ModuleIndex = moduleIndex,
            Rotation = rotation,
            Position = position,
            Elevation = elevation,
            RootCell = TrackEditor.Track.GetCell(position)
        };
        placement.PositionObject();

        foreach (var instance in module.Instances)
        {
            var destCell = TrackEditor.Track.GetCell(position + instance.Position.Rotate(rotation));
            destCell.Module = placement;
            placement.TouchedCells.Add(destCell);
        }

        TrackEditor.Track.MarkDirty();
    }

    // Placement and editing helpers
    private IEnumerable<ModulePlacement> GetIntersectedModules()
    {
        var track = TrackEditor.Track;
        foreach (var instance in TrackEditor.TrackUnit.Modules[activeModuleIndex].Instances)
        {
            var pos = editingCursor.Position + instance.Position.Rotate(moduleRotationTurns);
            if (pos.x < 0 || pos.y < 0 || pos.x >= track.Width || pos.y >= track.Height)
                continue; // the cursor is re-clamped every frame; never throw in between
            var destCell = track.GetCell(pos);
            if (destCell.Module != null)
            {
                yield return destCell.Module;
            }
        }
    }

    public void AdjustCursorDown()
    {
        AdjustCursorPosition(Vector2Int.down);
    }

    public void AdjustCursorUp()
    {
        AdjustCursorPosition(Vector2Int.up);
    }

    public void AdjustCursorRight()
    {
        AdjustCursorPosition(Vector2Int.right);
    }

    public void AdjustCursorLeft()
    {
        AdjustCursorPosition(Vector2Int.left);
    }

    public void AdjustCursorPosition(Vector2Int positionChange)
    {
        TrackEditor.PlaySound(TrackEditor.SndMenuMove);
        CursorPosition += positionChange.Rotate(cameraRotationTurns);
    }

    public void AdjustCursorHeight(int heightChange)
    {
        int wantedHeight = editingCursorHeightSteps + heightChange;
        int maxHeight = (TrackEditor.UnlimitedMode) ? ushort.MaxValue : EditorConstants.MaxElevationSteps;

        if (wantedHeight < 0 || wantedHeight > maxHeight)
        {
            TrackEditor.PlaySound(TrackEditor.SndWarning);
        }
        else
        {
            TrackEditor.PlaySound((heightChange > 0) ? TrackEditor.SndRaise : TrackEditor.SndLower);
            editingCursorHeightSteps = wantedHeight;
            PlacementSettingsChanged?.Invoke();
        }
    }

    public void RotateCamera(int rotateAmount)
    {
        TrackEditor.PlaySound(TrackEditor.SndRotate);
        cameraRotationTurns += rotateAmount;
        cameraRotationTurns = (cameraRotationTurns < 0) ? 3 : cameraRotationTurns % 4;
    }

    public void RotateModule(int rotateAmount)
    {
        TrackEditor.PlaySound(TrackEditor.SndRotate);
        moduleRotationTurns += rotateAmount;
        moduleRotationTurns = (moduleRotationTurns < 0) ? 3 : moduleRotationTurns % 4;
        PlacementSettingsChanged?.Invoke();
    }

    public void NextVariant()
    {
        var change = Modules.Lookup.Changes[activeModuleIndex];
        if (change.NextVar != activeModuleIndex)
        {
            TrackEditor.PlaySound(TrackEditor.SndLower);
            SetActiveModule(change.NextVar);
        }
        else
        {
            TrackEditor.PlaySound(TrackEditor.SndWarning);
        }
    }

    public void PreviousVariant()
    {
        var change = Modules.Lookup.Changes[activeModuleIndex];
        if (change.PreviousVar != activeModuleIndex)
        {
            TrackEditor.PlaySound(TrackEditor.SndRaise);
            SetActiveModule(change.PreviousVar);
        }
        else
        {
            TrackEditor.PlaySound(TrackEditor.SndWarning);
        }
    }

    public void PlaceModule()
    {
        if (GetIntersectedModules().Any(Protected))
        {
            TrackEditor.PlaySound(TrackEditor.SndWarning);
            EditRefused?.Invoke("A locked or hidden block is in the way");
            return;
        }

        bool replacingPickups = GetIntersectedModules().Count(x => x.TouchedCells.Any(x => x.HasPickup)) != 0;
        if (replacingPickups)
            TrackEditor.PlaySound(TrackEditor.SndPlacePickup);

        TrackEditor.PlaySound(TrackEditor.SndPlaceModule);
        cursorHighlightTime = 0.25f;

        int moduleIndex = activeModuleIndex;
        var position = editingCursor.Position;
        float height = editingCursorHeight;
        int rotation = moduleRotationTurns;
        TrackEditor.RecordChange($"Place {ModuleCatalog.GetDisplayName(moduleIndex)}",
                                 () => PlaceModule(moduleIndex, position, height, rotation));
    }

    public void DeleteModule()
    {
        var cell = TrackEditor.Track.GetCell(editingCursor.Position);
        if (Protected(cell.Module))
        {
            TrackEditor.PlaySound(TrackEditor.SndWarning);
            EditRefused?.Invoke("That block is locked or hidden");
            return;
        }
        if (cell.Module != null)
        {
            bool replacingPickups = cell.Module.TouchedCells.Any(x => x.HasPickup);
            if (replacingPickups)
                TrackEditor.PlaySound(TrackEditor.SndPlacePickup);

            var placement = cell.Module;
            TrackEditor.RecordChange($"Delete {ModuleCatalog.GetDisplayName(placement.ModuleIndex)}", () =>
            {
                placement.Delete();
                TrackEditor.Track.MarkDirty();
            });
        }
        TrackEditor.PlaySound(TrackEditor.SndDelete);
    }

    public void CopyModule()
    {
        var cell = TrackEditor.Track.GetCell(editingCursor.Position);
        if (cell.Module != null)
        {
            SetActiveModule(cell.Module.ModuleIndex);
            moduleRotationTurns = cell.Module.Rotation;
            editingCursorHeightSteps = Mathf.RoundToInt(cell.Module.Elevation / EditorConstants.ElevationStep);
            PlacementSettingsChanged?.Invoke();

            TrackEditor.PlaySound(TrackEditor.SndAdjust);
            cursorHighlightTime = 0.25f;
        }
        else
        {
            TrackEditor.PlaySound(TrackEditor.SndWarning);
        }
        
    }

    public void ToggleSurface()
    {
        int newModule = Modules.Lookup.OtherSurface[activeModuleIndex];
        if(newModule != activeModuleIndex)
        {
            TrackEditor.PlaySound(TrackEditor.SndRaise);
            SetActiveModule(newModule);
        }
        else
        {
            TrackEditor.PlaySound(TrackEditor.SndWarning);
        }
    }

    // Other
    private void UpdateModuleFlash()
    {
        bool currentActive = PreviewVisible && (!BlinkPreview || (Time.timeSinceLevelLoadAsDouble % 1d) >= 0.5);
        activeModule.SetActive(currentActive);

        // set every object on the grid to visible
        foreach (var rootModulePlacement in TrackEditor.Track.GetAllModuleRootPlacements())
        {
            if (rootModulePlacement.Object != null)
                rootModulePlacement.Object.SetActive(IsHidden == null || !IsHidden(rootModulePlacement));
        }

        if (!PreviewVisible)
            return;

        // now hide all modules that we intersect
        var module = TrackEditor.TrackUnit.Modules[activeModuleIndex];
        foreach (var instance in module.Instances)
        {
            var destCell = TrackEditor.Track.GetCell(editingCursor.Position + instance.Position.Rotate(moduleRotationTurns));
            if (destCell.Module != null && destCell.Module.Object != null)
            {
                destCell.Module.Object.SetActive(!currentActive && (IsHidden == null || !IsHidden(destCell.Module)));
            }
        }
    }

    private void UpdateActiveModule()
    {
        var modInstance = activeModule.GetComponent<ModuleInstance>();
        modInstance.SetRootHeight(editingCursorHeight);

        activeModule.transform.position = new Vector3(editingCursor.Position.x, editingCursorHeight, editingCursor.Position.y);
        activeModule.transform.localEulerAngles = new Vector3(0f, moduleRotation, 0f);
    }

    private void UpdateCamera()
    {
        if (!ControlsCamera)
            return;

        cameraRotation = Mathf.MoveTowardsAngle(cameraRotation, cameraRotationTarget, 180f * Time.deltaTime);
        var cameraDir = Quaternion.Euler(0f, cameraRotation, 0f) * -Vector3.forward;

        var camera = TrackEditor.Instance.Camera;
        camera.transform.position = editingCursor.Position.ToVec3XZ()
                                    + (cameraDir * cameraDist)
                                    + (Vector3.up * cameraHeight)
                                    + (Vector3.up * Mathf.Max(0f, editingCursorHeight - EditorConstants.MaxElevation) * 1.25f);

        camera.transform.LookAt(editingCursor.Position.ToVec3XZ());
        camera.orthographic = false;
    }

    private void ClampCursor()
    {
        var moduleBounds = TrackEditor.TrackUnit.Modules[activeModuleIndex].CalculateModuleGridSize().Rotate(EditorConstants.ModuleBoundsPivot, moduleRotationTurns);
        moduleBounds.SetMinMax(moduleBounds.min + editingCursor.Position, moduleBounds.max + editingCursor.Position);

        int underStepX = -moduleBounds.xMin;
        int underStepY = -moduleBounds.yMin;
        int overStepX = moduleBounds.xMax - TrackEditor.Track.Width;
        int overStepY = moduleBounds.yMax - TrackEditor.Track.Height;

        editingCursor.Position.x += Mathf.Max(0, underStepX);
        editingCursor.Position.y += Mathf.Max(0, underStepY);
        editingCursor.Position.x -= Mathf.Max(0, overStepX);
        editingCursor.Position.y -= Mathf.Max(0, overStepY);
    }

    // Virtuals
    public override void Init()
    {
        base.Init();

        // clone modules for our use
        var cachedModulesParent = new GameObject("Cached Modules");
        cachedModulesParent.transform.SetParent(this.transform, false);

        placedPiecesParent = new GameObject("Placed Track Pieces");

        for (int i = 0; i < TrackEditor.ProcessedTrackUnit.UnityModules.Count; i++)
        {
            var moduleObj = Instantiate(TrackEditor.ProcessedTrackUnit.UnityModules[i]);
            moduleObj.GetComponent<ModuleInstance>().SetEditorConfig();
            moduleObj.transform.SetParent(cachedModulesParent.transform, false);
            moduleObjects.Add(moduleObj);
        }
        SetActiveModule((int)Modules.ID.TWM_START);
        CursorPosition = Vector2Int.zero;
    }

    public override void UpdateInput()
    {
        // MOVE CURSOR
        var movementDelta = Controls.DirectionalMovement.GetMovementDelta();
        if (movementDelta.sqrMagnitude > 0)
            AdjustCursorPosition(movementDelta);

        int heightChangeDelta = Controls.TrackEdit.GetHeightChangeDelta();
        if (heightChangeDelta != 0)
            AdjustCursorHeight(heightChangeDelta);

        // MODULE ROTATE
        int moduleRotationDelta = Controls.TrackEdit.GetModuleRotationDelta();
        if(moduleRotationDelta != 0)
            RotateModule(moduleRotationDelta);

        // MODULE STUFF
        if (Controls.TrackEdit.PlacePressed())
            PlaceModule();

        if (Controls.TrackEdit.DeletePressed())
            DeleteModule();

        if (Controls.TrackEdit.CopyPressed())
            CopyModule();

        if (Controls.TrackEdit.ToggleSurfacePressed())
            ToggleSurface();

        if (Controls.TrackEdit.PreviousVariantPressed())
            PreviousVariant();

        if (Controls.TrackEdit.NextVariantPressed())
            NextVariant();
        
        // rotate cam
        int cameraRotationDelta = Controls.TrackEdit.GetCameraRotationDelta();
        if (cameraRotationDelta != 0)
        {
            if (ControlsCamera)
                RotateCamera(cameraRotationDelta);
            else
                CameraRotateRequested?.Invoke(cameraRotationDelta);
        }

        base.UpdateInput();
    }

    public override void Update()
    {
        ClampCursor();
        UpdateActiveModule();
        UpdateModuleFlash();

        cursorHighlightTime = Mathf.Max(0f, cursorHighlightTime - Time.deltaTime);
        editingCursor.Color = (cursorHighlightTime > 0f) ? new Color(0.7f, 0.2f, 1f)
                                                         : new Color(0.7f, 0.2f, 0f);
        if (PreviewVisible && DrawCursorBox)
            editingCursor.Draw();

        base.Update();
    }

    public override void OnExitMode()
    {
        base.OnExitMode();

        // set every object on the grid to visible
        foreach (var rootModulePlacement in TrackEditor.Track.GetAllModuleRootPlacements())
        {
            if (rootModulePlacement.Object != null)
                rootModulePlacement.Object.SetActive(true);
        }
    }

    // Mono
    private void LateUpdate()
    {
        UpdateCamera();
    }
}
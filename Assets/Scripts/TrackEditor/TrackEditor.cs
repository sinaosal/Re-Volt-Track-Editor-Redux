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
using System.Globalization;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

public partial class TrackEditor : MonoBehaviour
{
    public static TrackEditor Instance { get; private set; }

    // scene references
    public Camera Camera;
    public Grid Grid;
    public TMP_Text ModeNameText;

    [Header("Images To Replace")]
    public Texture2D IconsTexture;
    public Texture2D ButtonsTexture;
    public Texture2D ClockTexture;
    public Texture2D Wire1Texture;
    public Texture2D Wire2Texture;

    [Header("UI")]
    public RectTransform UIContainer;
    public PauseMenu PauseMenu;
    public HelpMenu HelpMenu;

    [Header("Debug")]
    public TMP_Text DebugText;

    private int debugMode = -1;
    private int maxDebugMode = 1;

    // editor modes
    [Header("Edit Modes")]
    [SerializeField]
    private EditorMode[] editModes = new EditorMode[0];

    private int currentEditModeIndex = -1;
    private EditorMode currentEditMode => (currentEditModeIndex < 0) ? null : editModes[currentEditModeIndex];

    //
    public static EditorTrack Track { get; private set; }
    public static ReVolt.TrackUnit.TrackUnitFile TrackUnit { get; private set; }
    public static TrackUnitUnity ProcessedTrackUnit { get; private set; }

    // settings
    public static bool UnlimitedMode { get; private set; } = false;
    public static float? ExportScaleOverride { get; private set; } = null;

    /// <summary>A .tdf file passed on the command line (e.g. by dropping it onto the executable).</summary>
    public static string StartupFilePath { get; private set; } = null;
    private static InterfaceMode? interfaceOverride = null;

    // interface
    public static InterfaceMode ActiveInterface { get; private set; } = InterfaceMode.Classic;
    public static event Action<InterfaceMode> InterfaceChanged;
    private Studio.StudioShell studioShell;
    private bool quitConfirmed = false;

    // useful ui
    public static PromptMode Prompt { get; private set; }

    // audio
    private AudioSource audioSource;
    public static AudioClip SndPlaceModule { get; private set; }
    public static AudioClip SndChoice { get; private set; }
    public static AudioClip SndPlacePickup { get; private set; }
    public static AudioClip SndDelete { get; private set; }
    public static AudioClip SndRotate { get; private set; }
    public static AudioClip SndMenuMove { get; private set; }
    public static AudioClip SndWarning { get; private set; }
    public static AudioClip SndRaise { get; private set; }
    public static AudioClip SndLower { get; private set; }
    public static AudioClip SndAdjust { get; private set; }
    public static AudioClip SndMenuClose { get; private set; }
    public static AudioClip SndMenuOpen { get; private set; }
    public static AudioClip SndTypeText { get; private set; }
    public static AudioClip SndExportComplete { get; private set; }


    private void ReplaceImage(Texture2D image, string imagePath)
    {
        var newImage = TextureCache.Get(imagePath).ChromaKey(new Color32(0, 0, 0, 255));
        image.Resize(newImage.width, newImage.height);
        image.SetPixels32(newImage.GetPixels32());
        image.Apply(true);
    }

    private void FixClockTexture()
    {
        // the clock progress sprites have zero padding, sample a pixel from the empty clock
        // and fill the area behind the progress sprites
        var bgColor = ClockTexture.GetPixel((int)(0.25f * ClockTexture.width), (int)(0.825f * ClockTexture.height));
        int fillWidth = (int)(0.125f * ClockTexture.width);
        int fillHeight = (int)(0.036f * ClockTexture.height);
        int fillOrigin = (int)(0.516f * ClockTexture.width);
        for(int y=0; y < fillHeight; y++)
        {
            for(int x=fillOrigin; x < (fillOrigin+fillWidth); x++)
            {
                var srcPixel = (Color32)ClockTexture.GetPixel(x, ClockTexture.height - y - 1);
                if (srcPixel.a == 0)
                    srcPixel = bgColor;
                ClockTexture.SetPixel(x, ClockTexture.height - y - 1, srcPixel);
            }
        }
        ClockTexture.Apply(true);
    }

    private void ParseCommandLine()
    {
        string[] cmdLineArgs = Environment.GetCommandLineArgs();
        UnlimitedMode = Array.IndexOf(cmdLineArgs, "-unlimited") >= 0;

        if (Array.IndexOf(cmdLineArgs, "-classic") >= 0)
            interfaceOverride = InterfaceMode.Classic;
        else if (Array.IndexOf(cmdLineArgs, "-studio") >= 0)
            interfaceOverride = InterfaceMode.Studio;

        // a track file given as a plain argument (also what Windows passes when a file is dropped onto the exe)
        for (int i = 1; i < cmdLineArgs.Length; i++)
        {
            string arg = cmdLineArgs[i];
            if (arg.EndsWith(".tdf", StringComparison.OrdinalIgnoreCase) && File.Exists(arg))
            {
                StartupFilePath = Path.GetFullPath(arg);
                break;
            }
        }

        // process arguments with one parameter
        for (int i = 1; i < cmdLineArgs.Length - 1; i++)
        {
            if (cmdLineArgs[i] == "-exportscale"
               && float.TryParse(cmdLineArgs[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
            {
                ExportScaleOverride = f;
            }
        }
    }

    private void Init()
    {
        Instance = this;
        var perfTimer = System.Diagnostics.Stopwatch.StartNew();

        // handle args
        ParseCommandLine();

        // init filesystem
        FileHelper.Initialize();

        // user preferences and crash recovery
        AppSettings.Load();
        AppSettings.Changed += ApplyAudioSettings;
        ApplyAudioSettings();
        TrackRecovery.InitializeAtStartup();

        // update saved file list
        SavedFilesList.Update();

        // load audio
        audioSource = GetComponent<AudioSource>();
        SndPlaceModule = FileHelper.LoadSound("placemod");
        SndPlacePickup = FileHelper.LoadSound("placepup");
        SndDelete = FileHelper.LoadSound("delete");
        SndMenuMove = FileHelper.LoadSound("menumove");
        SndMenuOpen = FileHelper.LoadSound("menuin");
        SndMenuClose = FileHelper.LoadSound("menuout");
        SndRotate = FileHelper.LoadSound("rotate");
        SndDelete = FileHelper.LoadSound("delete");
        SndWarning = FileHelper.LoadSound("warning");
        SndRaise = FileHelper.LoadSound("raise");
        SndLower = FileHelper.LoadSound("lower");
        SndAdjust = FileHelper.LoadSound("enlrgred");
        SndChoice = FileHelper.LoadSound("choice");
        SndTypeText = FileHelper.LoadSound("typetext");
        SndExportComplete = FileHelper.LoadSound("exptcomp");

        // load images
        ReplaceImage(IconsTexture, "icons");
        ReplaceImage(ClockTexture, "clock");
        ReplaceImage(ButtonsTexture, "buttons");
        ReplaceImage(Wire1Texture, "spruewire1");
        ReplaceImage(Wire2Texture, "spruewire2");
        FixClockTexture();

        // load strings
        FileHelper.LoadStrings();

        // load trackunit and create Unity objects
        TrackUnit = FileHelper.LoadTrackUnit();

        if(TrackUnit == null)
        {
            throw new EditorSetupException(FileHelper.DescribeMissingEditorFiles());
        }

        ProcessedTrackUnit = new TrackUnitUnity(TrackUnit);
        ProcessedTrackUnit.CreateUnits();
        ProcessedTrackUnit.CreateModules();

        // set up UI safe area (mobile devices, notches etc.)
        var safeArea = Screen.safeArea;
        var screenSize = new Vector2Int(Screen.width, Screen.height);

        UIContainer.anchorMin = new Vector2(safeArea.x / screenSize.x, safeArea.y / screenSize.y);
        UIContainer.anchorMax = new Vector2(safeArea.xMax / screenSize.x, safeArea.yMax / screenSize.y);

        // set prompt reference
        Prompt = GetEditMode<PromptMode>();

        Debug.Log($"TrackEditor pre-mode initialize load time: {perfTimer.ElapsedMilliseconds}ms");

        // create track
        Track = new EditorTrack
        {
            AfterLoadCallback = OnTrackPostLoad
        };
        InitSession();

        //init edit modes
        foreach (var mode in editModes)
        {
            mode.gameObject.SetActive(false);
            mode.Init();
        }

        // create new track and set edit mode
        NewTrackAction();
        SetEditMode<TrackEditingMode>();

        // interface
        Application.wantsToQuit += OnWantsToQuit;
        InitInterface();

        Debug.Log($"TrackEditor fully initialized load time: {perfTimer.ElapsedMilliseconds}ms");
    }

    private void Awake()
    {
        try
        {
            Init();
        }
        catch(Exception ex)
        {
            if (ApplicationHelper.isEditor)
            {
                throw;
            }
            else
            {
                Destroy(this.gameObject);
                // setup problems are explained to the user; anything else keeps the details for bug reports
                FailScreen.FailMessage = ex is EditorSetupException ? ex.Message : ex.ToString();
                FailScreen.Show();
            }
        }
    }

    private void UpdateInput()
    {
        // the Studio interface owns menus, dialogs and shortcuts; it decides when the
        // viewport may receive keyboard/gamepad editing input
        if (ActiveInterface == InterfaceMode.Studio && studioShell != null)
        {
            if (studioShell.AllowsEditorInput)
                currentEditMode?.UpdateInput();
            return;
        }

        // classic undo/redo (Ctrl+Z, Ctrl+Y / Ctrl+Shift+Z) while placing modules
        if (currentEditMode is TrackEditingMode && !PauseMenu.gameObject.activeInHierarchy && !HelpMenu.gameObject.activeInHierarchy
            && Controls.IsControlHeld())
        {
            if (Controls.GetKeyDown(Key.Z))
            {
                if (Controls.IsShiftHeld()) Redo(); else Undo();
                return;
            }
            if (Controls.GetKeyDown(Key.Y))
            {
                Redo();
                return;
            }
        }

        // help and pause keys
        if(currentEditMode is TrackEditingMode)
        {
            if(Controls.PausePressed() && !PauseMenu.gameObject.activeInHierarchy && !HelpMenu.gameObject.activeInHierarchy)
            {
                PauseMenu.Open();
                return; // pause menu checks pause key and closes
            }
        }
        if (Controls.GetKeyDown(Key.F1) && !HelpMenu.gameObject.activeInHierarchy && !PauseMenu.gameObject.activeInHierarchy)
        {
            string helpPath = currentEditMode.HelpPath;
            if (helpPath != null)
            {
                HelpMenu.LoadHelpText(helpPath);
                HelpMenu.gameObject.SetActive(true);
                return; // help menu checks any key and closes
            }
        }

        // debug
        if (Controls.GetKeyDown(Key.F12))
        {
            debugMode++;
            if (debugMode > maxDebugMode)
                debugMode = -1;
            DebugText.gameObject.SetActive(debugMode >= 0);
        }

        // input handling
        if (HelpMenu.gameObject.activeInHierarchy)
        {
            if (Controls.AnyButtonPressed())
                HelpMenu.gameObject.SetActive(false);
        }
        else if (PauseMenu.gameObject.activeInHierarchy)
        {
            PauseMenu.UpdateInput();
        }
        else if(currentEditMode != null)
        {
            currentEditMode.UpdateInput();
        }
    }

    void Update()
    {
        // F10 switches between the Studio and the classic interface
        if (Controls.GetKeyDown(Key.F10) && (studioShell == null || !studioShell.IsTextInputActive))
        {
            ToggleInterface();
            return;
        }

        UpdateInput();
        TrackRecovery.Tick();

        // set mode name
        if (PauseMenu != null && PauseMenu.gameObject.activeInHierarchy)
        {
            ModeNameText.text = Localization.Lookup(LocString.MODE_MENU);
        }
        else if (currentEditMode != null)
        {
            ModeNameText.text = currentEditMode.ModeName;
        }
        else
        {
            ModeNameText.text = string.Empty;
        }

        // debug
        if (debugMode == 0)
        {
            if(Time.timeSinceLevelLoadAsDouble % 0.1f > ((Time.timeSinceLevelLoadAsDouble + Time.deltaTime) % 0.1f))
                DebugText.text = $"{Mathf.RoundToInt(1f / Time.smoothDeltaTime)}fps";
        }
        else if(debugMode == 1)
        {
            var editMode = GetEditMode<TrackEditingMode>();
            DebugText.text = $"Cursor Information\nX {editMode.CursorPosition.x}\nY {editMode.CursorPosition.y}\nHeight {editMode.CursorHeightSteps}";
        }
    }

    // Callbacks
    void OnTrackPostLoad()
    {
        // place the loaded modules
        var editingMode = GetEditMode<TrackEditingMode>();

        Grid.NumHorizontalSquares = Track.Width;
        Grid.NumVerticalSquares = Track.Height;

        foreach(var moduleRootPlacement in Track.GetAllModuleRootPlacements())
        {
            var cell = moduleRootPlacement.RootCell;
            editingMode.PlaceModule(moduleRootPlacement.ModuleIndex, cell.Position, cell.Module.Elevation, cell.Module.Rotation);
        }

        Track.ClearDirtyFlag();
    }

    // Helpers
    private void AskToSaveBeforeAction(Action action)
    {
        if (Track.IsDirty)
        {
            if (UnsavedChangesPrompt != null)
            {
                UnsavedChangesPrompt(action);
                return;
            }

            Prompt.InitQuestionPrompt(LocString.PROMPT_TRACK_NOT_SAVED, SetTrackEditingMode, () => { OpenSaveScreen(action); }, action);
            ShowPrompt();
        }
        else
        {
            action();
        }
    }

    // Public interface
    public static void ShowPrompt()
    {
        SetEditMode<PromptMode>();
    }

    public static void SetTrackSize(int width, int height)
    {
        var grid = Instance.Grid;

        Track.SetSize(width, height);
        grid.NumHorizontalSquares = Track.Width;
        grid.NumVerticalSquares = Track.Height;
        ReclampCursor();
    }

    /// <summary>The cursor must stay inside the grid after it shrinks (cell lookups outside it throw).</summary>
    private static void ReclampCursor()
    {
        var editing = GetEditMode<TrackEditingMode>();
        if (editing != null && Track.Width > 0 && Track.Height > 0)
            editing.CursorPosition = editing.CursorPosition;
    }

    public static void SetTrackSize(Vector2Int size)
    {
        SetTrackSize(size.x, size.y);
    }

    public static void AdjustTrack(Vector2Int newSize, Vector2Int shift)
    {
        var grid = Instance.Grid;

        Track.AdjustTrack(newSize, shift);
        grid.NumHorizontalSquares = Track.Width;
        grid.NumVerticalSquares = Track.Height;
        ReclampCursor();
    }

    private void QuitAction()
    {
        // the user saved or chose to discard their changes
        quitConfirmed = true;
        TrackRecovery.Discard();
        AppSettings.Save();

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    public void Quit()
    {
        AskToSaveBeforeAction(QuitAction);
    }

    /// <summary>
    /// Intercepts closing the window so unsaved work is never lost without asking.
    /// </summary>
    private bool OnWantsToQuit()
    {
        if (quitConfirmed || Track == null || !Track.IsDirty)
        {
            TrackRecovery.Discard();
            return true;
        }

        // protect the changes in case the process is killed while the question is open
        TrackRecovery.WriteNow();

        if (ActiveInterface == InterfaceMode.Classic)
            SetEditMode<TrackEditingMode>();
        Quit();
        return false;
    }

    private void NewTrackAction()
    {
        Track.Name = FileHelper.GetFirstAvailableDefaultTrackName();
        Track.Clear();
        Track.Extras = new ReVolt.Track.TrackExtras();
        SetTrackSize(16, 16);
        Track.ClearDirtyFlag();
        SetTrackEditingMode();
        GetEditMode<TrackEditingMode>().SetActiveModule((int)Modules.ID.TWM_START);

        // the user explicitly started over, so there is nothing to recover
        ResetSession(null);
        TrackRecovery.Discard();
        TrackRecovery.ResetTimer();
    }

    public void NewTrack()
    {
        AskToSaveBeforeAction(NewTrackAction);
    }

    public void Export()
    {
        SetEditMode<ExportMode>();
    }

    public void OpenAdjustScreen()
    {
        SetEditMode<AdjustMode>();
    }

    public void OpenSaveScreen(Action afterSaveCallback)
    {
        GetEditMode<SaveMode>().AfterSaveCallback = (str) => { afterSaveCallback(); };
        SetEditMode<SaveMode>();
    }

    public void OpenSaveScreen()
    {
        SetEditMode<SaveMode>();
    }

    private void LoadScreenAction()
    {
        SetEditMode<LoadMode>();
    }

    public void OpenLoadScreen()
    {
        AskToSaveBeforeAction(LoadScreenAction);
    }

    public void OpenLoadScreenNoAskSave()
    {
        LoadScreenAction();
    }

    public void SetTrackEditingMode()
    {
        SetEditMode<TrackEditingMode>();
    }

    public void SetPickupEditingMode()
    {
        SetEditMode<PickupEditingMode>();
    }

    public void SetModuleSelectMode()
    {
        SetEditMode<ModuleSelectMode>();
    }

    private static void ApplyAudioSettings()
    {
        var settings = AppSettings.Data;
        AudioListener.volume = settings.SoundsEnabled ? settings.SoundVolume : 0f;
    }

    public static void PlaySound(AudioClip clip)
    {
        if(Instance != null && Instance.audioSource != null && clip != null)
            Instance.audioSource.PlayOneShot(clip);
    }

    public static T GetEditMode<T>() where T : EditorMode
    {
        var type = typeof(T);
        int modeIndex = Array.FindIndex(Instance.editModes, x => x.GetType() == type);
        return (modeIndex < 0) ? null : (T)Instance.editModes[modeIndex];
    }

    public static void SetEditMode<T>() where T : EditorMode
    {
        var type = typeof(T);
        var modes = Instance.editModes;
        int modeIndex = Array.FindIndex(modes, x => x.GetType() == type);

        if (modeIndex < 0)
            throw new ArgumentException($"EditMode {type.Name} doesn't exist!");
        
        // exit old mode
        if(Instance.currentEditMode != null)
        {
            Instance.currentEditMode.OnExitMode();
        }

        // enter new mode
        Instance.currentEditModeIndex = modeIndex;
        Instance.currentEditMode.OnEnterMode();
    }
}

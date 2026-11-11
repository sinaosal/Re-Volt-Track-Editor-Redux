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

using System.Linq;
using UnityEngine;

/// <summary>
/// Switching between the classic (original editor look, keyboard/gamepad driven) and the
/// Studio (desktop, mouse and keyboard) interfaces. Both drive the same edit modes and track data.
/// </summary>
public partial class TrackEditor
{
    private GameObject classicFrame;

    public bool IsStudioActive => ActiveInterface == InterfaceMode.Studio && studioShell != null;

    private void InitInterface()
    {
        var frame = UIContainer != null ? UIContainer.Find("Main Border") : null;
        classicFrame = frame != null ? frame.gameObject : null;

        var mode = interfaceOverride ?? AppSettings.Interface;

        // the Studio interface needs a mouse; touch/gamepad devices keep the classic interface
        if (interfaceOverride == null && Application.platform == RuntimePlatform.Android)
            mode = InterfaceMode.Classic;

        SetInterface(mode, false);

        if (StartupFilePath != null)
        {
            var result = LoadTrackFile(StartupFilePath);
            if (!result.Success)
                ShowNotice("Couldn't open track", result.Error, true);
            else if (result.Warnings.Count > 0)
                ShowNotice("Track opened with warnings", string.Join("\n", result.Warnings), false);
        }

        // recovery data is only ever removed by an explicit choice; the Studio interface
        // offers it on its welcome screen
        if (ActiveInterface == InterfaceMode.Classic)
            OfferRecoveryClassic();
    }

    private void OfferRecoveryClassic()
    {
        var pending = TrackRecovery.FindPendingRecovery();
        if (pending == null)
            return;

        string question = $"The editor closed unexpectedly.\nRecover unsaved track \"{pending.Value.TrackName}\" from {pending.Value.SavedAt:t}?";
        Prompt.InitQuestionPrompt(question,
            () =>
            {
                var result = TrackRecovery.RestorePending();
                SetTrackEditingMode();
                if (!result.Success)
                    ShowNotice("Recovery failed", result.Error, true);
            },
            () =>
            {
                TrackRecovery.DiscardPending();
                SetTrackEditingMode();
            });
        ShowPrompt();
    }

    /// <summary>
    /// Shows a message in whichever interface is active.
    /// </summary>
    public static void ShowNotice(string title, string message, bool isError)
    {
        if (Instance.IsStudioActive)
        {
            Instance.studioShell.ShowMessage(title, message, isError);
            return;
        }

        Prompt.InitWarningPrompt($"{title}\n{message}", Instance.SetTrackEditingMode);
        ShowPrompt();
    }

    /// <summary>
    /// Whether switching interfaces right now is safe (no classic screen or Studio dialog is open).
    /// </summary>
    public bool CanSwitchInterface
    {
        get
        {
            if (ActiveInterface == InterfaceMode.Studio)
                return studioShell == null || !studioShell.HasModal;

            return currentEditMode is TrackEditingMode
                && !PauseMenu.gameObject.activeInHierarchy
                && !HelpMenu.gameObject.activeInHierarchy;
        }
    }

    public void ToggleInterface()
    {
        if (!CanSwitchInterface)
        {
            PlaySound(SndWarning);
            return;
        }

        SetInterface(ActiveInterface == InterfaceMode.Studio ? InterfaceMode.Classic : InterfaceMode.Studio, true);
        PlaySound(SndMenuOpen);
    }

    public void SetInterface(InterfaceMode mode, bool persist)
    {
        if (mode == InterfaceMode.Studio)
        {
            PauseMenu.gameObject.SetActive(false);
            HelpMenu.gameObject.SetActive(false);
            if (!(currentEditMode is TrackEditingMode))
                SetEditMode<TrackEditingMode>();

            if (studioShell == null)
                studioShell = gameObject.AddComponent<Studio.StudioShell>();

            if (classicFrame != null)
                classicFrame.SetActive(false);

            ActiveInterface = InterfaceMode.Studio;
            studioShell.Activate();
        }
        else
        {
            if (studioShell != null)
                studioShell.Deactivate();

            if (classicFrame != null)
                classicFrame.SetActive(true);

            UnsavedChangesPrompt = null;
            ActiveInterface = InterfaceMode.Classic;
        }

        if (persist)
            AppSettings.Interface = mode;

        InterfaceChanged?.Invoke(mode);
    }

    /// <summary>
    /// Finds a scene camera by name (the background camera that paints the clear color).
    /// </summary>
    public static Camera FindSceneCamera(string name)
    {
        return Resources.FindObjectsOfTypeAll<Camera>().FirstOrDefault(x => x.name == name && x.gameObject.scene.IsValid());
    }
}

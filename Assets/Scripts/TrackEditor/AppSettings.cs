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
using System.IO;
using System.Text;
using UnityEngine;

public enum InterfaceMode
{
    Studio,
    Classic
}

[Serializable]
public class ShortcutBindingData
{
    public string Action;
    public string Binding;
}

/// <summary>
/// Persisted user preferences. Serialized with JsonUtility, so only public fields of supported types.
/// New fields must have sensible defaults: older settings files simply won't contain them.
/// </summary>
[Serializable]
public class AppSettingsData
{
    public int Version = 1;

    // interface
    public string Interface = nameof(InterfaceMode.Studio);
    public bool ShowWelcome = true;
    public float UiScale = 0f; // 0 = automatic from screen DPI

    // workspace layout
    public float LeftPanelWidth = 264f;
    public float RightPanelWidth = 292f;
    public bool LeftPanelVisible = true;
    public bool RightPanelVisible = true;
    public bool StatusBarVisible = true;
    public string InspectorTab = "Track";

    // viewport
    public bool ShowGrid = true;
    public bool ShowPickups = true;
    public bool ShowFootprint = true;
    public bool CameraFollowsCursor = true;
    public bool ZoomToPointer = true;

    // audio
    public bool SoundsEnabled = true;
    public float SoundVolume = 0.8f;

    // safety
    public bool AutosaveEnabled = true;
    public int AutosaveIntervalSeconds = 120;

    // export
    public int LastExportScaleIndex = 1; // 0 = half, 1 = regular, 2 = double
    public bool BackupBeforeExport = true;
    public int ExportBackupsToKeep = 5;

    // checks
    public bool AutoCheck = true;
    public bool ShowConnections = true;
    public bool ShowMinimap = true;

    // module library
    public List<int> FavoriteModules = new List<int>();
    public List<int> RecentModules = new List<int>();
    public int LibraryCategory = 0;
    public int LibraryCardSize = 1; // 0 = small, 1 = medium, 2 = large
    public int LeftPanelTab = 0;    // 0 = modules, 1 = outliner

    public List<string> RecentFiles = new List<string>();
    public List<ShortcutBindingData> Shortcuts = new List<ShortcutBindingData>();
}

public static class AppSettings
{
    public const int MaxRecentFiles = 10;
    public static readonly int[] AutosaveIntervalChoices = { 30, 60, 120, 300, 600 };

    private static AppSettingsData data;
    private static bool loaded;

    public static AppSettingsData Data
    {
        get
        {
            if (!loaded)
                Load();
            return data;
        }
    }

    /// <summary>Raised after settings were changed through <see cref="Save"/>.</summary>
    public static event Action Changed;

    public static string DataDirectory => Application.persistentDataPath;
    private static string SettingsPath => Path.Combine(DataDirectory, "settings.json");

    public static InterfaceMode Interface
    {
        get => Data.Interface == nameof(InterfaceMode.Classic) ? InterfaceMode.Classic : InterfaceMode.Studio;
        set
        {
            Data.Interface = value.ToString();
            Save();
        }
    }

    public static void Load()
    {
        loaded = true;
        data = new AppSettingsData();

        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                JsonUtility.FromJsonOverwrite(json, data);
            }
        }
        catch (Exception ex)
        {
            // a broken settings file must never stop the editor from starting
            Debug.LogWarning($"AppSettings: could not read settings, using defaults: {ex.Message}");
            data = new AppSettingsData();
        }

        Sanitize();
    }

    private static void Sanitize()
    {
        if (data.RecentFiles == null)
            data.RecentFiles = new List<string>();
        if (data.Shortcuts == null)
            data.Shortcuts = new List<ShortcutBindingData>();
        if (data.FavoriteModules == null)
            data.FavoriteModules = new List<int>();
        if (data.RecentModules == null)
            data.RecentModules = new List<int>();
        data.ExportBackupsToKeep = Mathf.Clamp(data.ExportBackupsToKeep, 1, 50);
        data.LibraryCardSize = Mathf.Clamp(data.LibraryCardSize, 0, 2);
        data.LeftPanelTab = Mathf.Clamp(data.LeftPanelTab, 0, 1);

        data.AutosaveIntervalSeconds = Mathf.Clamp(data.AutosaveIntervalSeconds, 15, 3600);
        data.SoundVolume = Mathf.Clamp01(data.SoundVolume);
        data.LeftPanelWidth = Mathf.Clamp(data.LeftPanelWidth, 180f, 640f);
        data.RightPanelWidth = Mathf.Clamp(data.RightPanelWidth, 200f, 640f);
        data.LastExportScaleIndex = Mathf.Clamp(data.LastExportScaleIndex, 0, 2);
        if (data.UiScale != 0f)
            data.UiScale = Mathf.Clamp(data.UiScale, 0.75f, 3f);
    }

    public static void Save()
    {
        if (!loaded)
            Load();

        try
        {
            Directory.CreateDirectory(DataDirectory);
            var json = JsonUtility.ToJson(data, true);
            SafeFile.WriteAllBytesAtomic(SettingsPath, Encoding.UTF8.GetBytes(json));
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"AppSettings: could not save settings: {ex.Message}");
        }

        Changed?.Invoke();
    }

    // Recent files
    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception)
        {
            return path;
        }
    }

    private static bool SamePath(string a, string b)
    {
        var comparison = Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(NormalizePath(a), NormalizePath(b), comparison);
    }

    public static void AddRecentFile(string path)
    {
        if (string.IsNullOrEmpty(path))
            return;

        path = NormalizePath(path);
        Data.RecentFiles.RemoveAll(x => SamePath(x, path));
        Data.RecentFiles.Insert(0, path);
        if (Data.RecentFiles.Count > MaxRecentFiles)
            Data.RecentFiles.RemoveRange(MaxRecentFiles, Data.RecentFiles.Count - MaxRecentFiles);
        Save();
    }

    public static void RemoveRecentFile(string path)
    {
        if (Data.RecentFiles.RemoveAll(x => SamePath(x, path)) > 0)
            Save();
    }

    public static void ClearRecentFiles()
    {
        Data.RecentFiles.Clear();
        Save();
    }
}

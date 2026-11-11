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
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Autosave and crash recovery.
///
/// While the track has unsaved changes it is periodically written to a recovery file in the
/// user data folder (never into the TDF folder, so the original editor and the load list never see it).
/// The recovery file is removed when the track is saved, discarded, or the editor exits normally,
/// so a recovery file found at startup means the previous session ended unexpectedly.
/// </summary>
public static class TrackRecovery
{
    [Serializable]
    private class RecoveryInfo
    {
        public string TrackName;
        public string OriginalPath;
        public string SavedAtUtc;
    }

    public struct PendingRecovery
    {
        public string TrackName;
        public string OriginalPath;
        public DateTime SavedAt;
    }

    private static string RecoveryDirectory => Path.Combine(AppSettings.DataDirectory, "Recovery");
    private static string RecoveryTrackPath => Path.Combine(RecoveryDirectory, "recovery.tdf");
    private static string RecoveryInfoPath => Path.Combine(RecoveryDirectory, "recovery.json");

    // recovery data left behind by a previous session is moved here at startup, so this
    // session's autosaves can never overwrite it before the user decided what to do with it
    private static string PreviousTrackPath => Path.Combine(RecoveryDirectory, "previous-session.tdf");
    private static string PreviousInfoPath => Path.Combine(RecoveryDirectory, "previous-session.json");

    private static float nextAutosaveTime = -1f;
    private static TdfDocument lastAutosaved;

    /// <summary>Local time of the last successful autosave in this session, if any.</summary>
    public static DateTime? LastAutosave { get; private set; }

    /// <summary>Set if the last autosave attempt failed.</summary>
    public static string LastError { get; private set; }

    /// <summary>
    /// Call once at startup, before any autosave. Moves recovery data from an interrupted
    /// session aside so <see cref="FindPendingRecovery"/> can offer it.
    /// </summary>
    public static void InitializeAtStartup()
    {
        try
        {
            if (!File.Exists(RecoveryTrackPath))
                return;

            // an older, still undecided recovery is archived rather than overwritten
            if (File.Exists(PreviousTrackPath))
            {
                string stamp = File.GetLastWriteTime(PreviousTrackPath).ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                MoveReplacing(PreviousTrackPath, Path.Combine(RecoveryDirectory, $"archived-{stamp}.tdf"));
                if (File.Exists(TrackFiles.ExtrasPath(PreviousTrackPath)))
                    MoveReplacing(TrackFiles.ExtrasPath(PreviousTrackPath), Path.Combine(RecoveryDirectory, $"archived-{stamp}{TrackFiles.ExtrasExtension}"));
                Debug.Log($"TrackRecovery: archived older recovery data as archived-{stamp}.tdf");
            }

            MoveReplacing(RecoveryTrackPath, PreviousTrackPath);
            if (File.Exists(TrackFiles.ExtrasPath(RecoveryTrackPath)))
                MoveReplacing(TrackFiles.ExtrasPath(RecoveryTrackPath), TrackFiles.ExtrasPath(PreviousTrackPath));
            else if (File.Exists(TrackFiles.ExtrasPath(PreviousTrackPath)))
                File.Delete(TrackFiles.ExtrasPath(PreviousTrackPath));
            if (File.Exists(RecoveryInfoPath))
                MoveReplacing(RecoveryInfoPath, PreviousInfoPath);
            else if (File.Exists(PreviousInfoPath))
                File.Delete(PreviousInfoPath);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"TrackRecovery: could not prepare recovery data: {ex.Message}");
        }
    }

    private static void MoveReplacing(string from, string to)
    {
        if (File.Exists(to))
            File.Delete(to);
        File.Move(from, to);
    }

    /// <summary>
    /// Returns information about recovery data from an interrupted previous session, if any.
    /// </summary>
    public static PendingRecovery? FindPendingRecovery()
    {
        try
        {
            if (!File.Exists(PreviousTrackPath))
                return null;

            var info = new RecoveryInfo();
            if (File.Exists(PreviousInfoPath))
                JsonUtility.FromJsonOverwrite(File.ReadAllText(PreviousInfoPath), info);

            DateTime savedAt = File.GetLastWriteTime(PreviousTrackPath);
            if (!string.IsNullOrEmpty(info.SavedAtUtc) &&
                DateTime.TryParse(info.SavedAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            {
                savedAt = parsed.ToLocalTime();
            }

            string name = info.TrackName;
            if (string.IsNullOrEmpty(name))
            {
                try { name = TdfDocument.ReadName(PreviousTrackPath); }
                catch (Exception) { name = "Untitled"; }
            }

            return new PendingRecovery
            {
                TrackName = name,
                OriginalPath = string.IsNullOrEmpty(info.OriginalPath) ? null : info.OriginalPath,
                SavedAt = savedAt
            };
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"TrackRecovery: could not inspect recovery data: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Restores the track from the interrupted session. The track stays marked as modified and
    /// "Save" goes back to the file it originally came from.
    /// </summary>
    public static TrackEditor.LoadResult RestorePending()
    {
        var pending = FindPendingRecovery();
        if (pending == null)
            return TrackEditor.LoadResult.Failed("There is no recovery data.");

        var result = TrackEditor.LoadTrackFile(PreviousTrackPath, false);
        if (!result.Success)
            return result;

        TrackEditor.AdoptRecoveredTrack(pending.Value.OriginalPath);
        DiscardPending();

        // protect the recovered changes right away
        WriteNow();
        return result;
    }

    /// <summary>
    /// Deletes recovery data from the interrupted previous session.
    /// </summary>
    public static void DiscardPending()
    {
        try
        {
            if (File.Exists(PreviousTrackPath))
                TrackFiles.Delete(PreviousTrackPath);
            if (File.Exists(PreviousInfoPath))
                File.Delete(PreviousInfoPath);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"TrackRecovery: could not remove previous recovery data: {ex.Message}");
        }
    }

    /// <summary>
    /// Deletes this session's recovery data (the track was saved or deliberately discarded).
    /// </summary>
    public static void Discard()
    {
        lastAutosaved = null;
        try
        {
            if (File.Exists(RecoveryTrackPath))
                TrackFiles.Delete(RecoveryTrackPath);
            if (File.Exists(RecoveryInfoPath))
                File.Delete(RecoveryInfoPath);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"TrackRecovery: could not remove recovery data: {ex.Message}");
        }
    }

    /// <summary>
    /// Call every frame. Writes a recovery snapshot when autosave is due and the track changed.
    /// </summary>
    public static void Tick()
    {
        var track = TrackEditor.Track;
        if (track == null)
            return;

        var settings = AppSettings.Data;
        float now = Time.unscaledTime;

        if (!track.IsDirty)
        {
            // nothing to protect: the saved file is the latest state
            if (lastAutosaved != null)
                Discard();
            nextAutosaveTime = now + settings.AutosaveIntervalSeconds;
            return;
        }

        if (!settings.AutosaveEnabled)
            return;

        if (nextAutosaveTime < 0f)
            nextAutosaveTime = now + settings.AutosaveIntervalSeconds;

        if (now < nextAutosaveTime)
            return;

        nextAutosaveTime = now + settings.AutosaveIntervalSeconds;
        WriteNow();
    }

    /// <summary>
    /// Writes a recovery snapshot immediately if the track differs from the last one.
    /// </summary>
    public static void WriteNow()
    {
        var track = TrackEditor.Track;
        if (track == null || !track.IsDirty)
            return;

        var doc = track.ToDocument();
        if (lastAutosaved != null && lastAutosaved.ContentEquals(doc))
            return;

        try
        {
            Directory.CreateDirectory(RecoveryDirectory);
            TrackFiles.Save(doc, RecoveryTrackPath);

            var info = new RecoveryInfo
            {
                TrackName = doc.Name,
                OriginalPath = TrackEditor.CurrentFilePath,
                SavedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
            };
            SafeFile.WriteAllBytesAtomic(RecoveryInfoPath, Encoding.UTF8.GetBytes(JsonUtility.ToJson(info, true)));

            lastAutosaved = doc;
            LastAutosave = DateTime.Now;
            LastError = null;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Debug.LogWarning($"TrackRecovery: autosave failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Restarts the autosave timer, e.g. after loading a different track.
    /// </summary>
    public static void ResetTimer()
    {
        nextAutosaveTime = Time.unscaledTime + AppSettings.Data.AutosaveIntervalSeconds;
    }
}

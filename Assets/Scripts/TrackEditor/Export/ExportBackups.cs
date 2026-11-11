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

using System;
using System.IO;
using System.Linq;

/// <summary>
/// Exporting replaces everything in the level folder, including files added by hand (custom
/// textures, edited .inf files). Before that, the folder is copied to the user data folder
/// (not into the game's levels folder, where the game would try to load it as a level).
/// </summary>
public static class ExportBackups
{
    public static string RootDirectory => Path.Combine(AppSettings.DataDirectory, "ExportBackups");

    /// <summary>
    /// Copies <paramref name="levelFolder"/> to a new timestamped backup folder and prunes old backups.
    /// Returns the backup folder, or null if there was nothing to back up. Throws if copying fails.
    /// </summary>
    public static string Backup(string levelFolder, string trackFolderName)
    {
        if (string.IsNullOrEmpty(levelFolder) || !Directory.Exists(levelFolder))
            return null;
        if (!Directory.EnumerateFileSystemEntries(levelFolder).Any())
            return null;

        string trackRoot = Path.Combine(RootDirectory, SafeName(trackFolderName));
        string target = Path.Combine(trackRoot, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
        int suffix = 1;
        while (Directory.Exists(target))
            target = Path.Combine(trackRoot, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + "_" + suffix++);

        try
        {
            CopyDirectory(levelFolder, target);
        }
        catch
        {
            // don't leave a half copy that looks like a complete backup
            try { Directory.Delete(target, true); } catch { }
            throw;
        }

        Prune(trackRoot, AppSettings.Data.ExportBackupsToKeep);
        return target;
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), false);
        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
    }

    private static void Prune(string trackRoot, int keep)
    {
        try
        {
            var old = new DirectoryInfo(trackRoot).GetDirectories()
                .OrderByDescending(d => d.CreationTimeUtc)
                .Skip(Math.Max(1, keep))
                .ToList();
            foreach (var dir in old)
                dir.Delete(true);
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning($"Old export backups couldn't be removed: {ex.Message}");
        }
    }

    private static string SafeName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return "track";
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }
}

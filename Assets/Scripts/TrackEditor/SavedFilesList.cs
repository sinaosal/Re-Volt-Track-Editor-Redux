using ReVolt.Track;
using System;
using System.Collections.Generic;
using System.IO;

public static class SavedFilesList
{
    private static readonly List<SavedFile> savedFiles = new List<SavedFile>();
    public static IReadOnlyList<SavedFile> SavedFiles => savedFiles;

    public struct SavedFile
    {
        public string Name;
        public string Path;
        public DateTime ModifiedDate;

        /// <summary>Set if the file header couldn't be read. Such files are still listed so the user can delete them.</summary>
        public string Error;

        public bool IsValid => Error == null;

        /// <summary>Track name, or the file name for unreadable files.</summary>
        public string DisplayName => IsValid ? Name : $"{System.IO.Path.GetFileNameWithoutExtension(Path)} (unreadable)";

        public SavedFile(string path)
        {
            var fi = new FileInfo(path);
            ModifiedDate = fi.LastWriteTimeUtc;
            Path = path;
            Error = null;
            Name = string.Empty;

            try
            {
                Name = TdfDocument.ReadName(path);
            }
            catch (Exception ex)
            {
                // a single damaged file must not break the load screen (or editor startup)
                Error = ex is TdfFormatException ? ex.Message : $"The file could not be read: {ex.Message}";
                Name = System.IO.Path.GetFileNameWithoutExtension(path);
            }
        }
    }

    public static void Update()
    {
        HashSet<string> savedFilePaths;
        try
        {
            savedFilePaths = new HashSet<string>(FileHelper.GetSavedTrackFileList());
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning($"SavedFilesList: could not list saved tracks: {ex.Message}");
            savedFilePaths = new HashSet<string>();
        }

        bool sortList = false;

        // first, update and remove saved files
        for(int i=savedFiles.Count - 1; i >= 0; i--)
        {
            var savedFile = savedFiles[i];
            if (!savedFilePaths.Contains(savedFile.Path))
            {
                // saved file no longer exists
                savedFiles.RemoveAt(i);
            }
            else
            {
                // saved file still exists, update it if it's been modified
                var fileInfo = new FileInfo(savedFile.Path);
                if(fileInfo.LastWriteTimeUtc >= savedFile.ModifiedDate)
                {
                    savedFiles[i] = new SavedFile(savedFile.Path);
                    sortList |= savedFiles[i].Name != savedFile.Name;
                }
            }
            savedFilePaths.Remove(savedFile.Path);
        }

        // now add new ones
        sortList |= savedFilePaths.Count > 0;
        foreach (string newPath in savedFilePaths)
        {
            savedFiles.Add(new SavedFile(newPath));
        }

        // finally, sort if requested
        if (sortList)
        {
            savedFiles.Sort((s1, s2) => string.Compare(s1.Name, s2.Name, StringComparison.CurrentCultureIgnoreCase));
        }
    }
}

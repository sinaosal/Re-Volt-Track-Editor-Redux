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
using System.Text;

namespace ReVolt.Track
{
    /// <summary>
    /// Reads and writes a track design together with its extras sidecar ("!Name.TDF" + "!Name.tdx").
    /// </summary>
    public static class TrackFiles
    {
        /// <summary>
        /// Sidecar extension. Deliberately doesn't start with "TDF": Windows' 8.3 name matching would
        /// otherwise list sidecars in "*.TDF" searches.
        /// </summary>
        public const string ExtrasExtension = ".tdx";

        public static string ExtrasPath(string trackPath) => Path.ChangeExtension(trackPath, ExtrasExtension);

        /// <summary>
        /// Loads the track and, if present, its extras. A damaged sidecar never prevents loading
        /// the track; it is reported in <see cref="TdfDocument.Warnings"/> and left on disk unchanged.
        /// </summary>
        public static TdfDocument Load(string trackPath)
        {
            var doc = TdfDocument.Load(trackPath);
            string extrasPath = ExtrasPath(trackPath);
            if (!File.Exists(extrasPath))
                return doc;

            try
            {
                doc.Extras = TrackExtras.FromJson(File.ReadAllText(extrasPath, Encoding.UTF8), doc.Warnings);
                int orphans = doc.Extras.RemoveOrphans(doc);
                if (orphans > 0)
                    doc.Warnings.Add($"{orphans} support color(s) referred to blocks that no longer exist and were dropped.");
            }
            catch (Exception ex)
            {
                doc.Extras = new TrackExtras();
                doc.Warnings.Add($"The extra track data in {Path.GetFileName(extrasPath)} couldn't be read ({ex.Message}). " +
                                 "It was ignored; the file is kept as a .bak copy when you save.");
            }
            return doc;
        }

        /// <summary>
        /// Saves the track atomically, then its extras (or removes a sidecar that is no longer needed).
        /// An existing sidecar that can't be parsed is moved to ".tdx.bak" instead of being overwritten.
        /// </summary>
        public static void Save(TdfDocument doc, string trackPath)
        {
            doc.Save(trackPath);

            string extrasPath = ExtrasPath(trackPath);
            BackUpIfUnreadable(extrasPath);

            if (doc.Extras != null && !doc.Extras.IsEmpty)
                SafeFile.WriteAllBytesAtomic(extrasPath, Encoding.UTF8.GetBytes(doc.Extras.ToJson()));
            else if (File.Exists(extrasPath))
                File.Delete(extrasPath);
        }

        /// <summary>Deletes a track and its sidecar.</summary>
        public static void Delete(string trackPath)
        {
            File.Delete(trackPath);
            string extrasPath = ExtrasPath(trackPath);
            if (File.Exists(extrasPath))
                File.Delete(extrasPath);
        }

        private static void BackUpIfUnreadable(string extrasPath)
        {
            if (!File.Exists(extrasPath))
                return;

            try
            {
                TrackExtras.FromJson(File.ReadAllText(extrasPath, Encoding.UTF8));
            }
            catch (Exception)
            {
                string backup = extrasPath + ".bak";
                if (File.Exists(backup))
                    File.Delete(backup);
                File.Move(extrasPath, backup);
            }
        }
    }
}

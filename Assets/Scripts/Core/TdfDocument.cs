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
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ReVolt.Track
{
    /// <summary>
    /// One grid cell entry in a TDF file. Only the root cell of a module carries the module ID;
    /// every other cell is <see cref="TdfDocument.ModuleNull"/>.
    /// </summary>
    public struct TdfCell : IEquatable<TdfCell>
    {
        public ushort ModuleId;
        public ushort Rotation;
        public ushort ElevationSteps;

        public static readonly TdfCell Empty = new TdfCell(TdfDocument.ModuleNull, 0, 0);

        /// <summary>
        /// True if this cell is the root of a placed module.
        /// Module ID 0 (TWM_SPACE) is treated as empty, matching the original loader.
        /// </summary>
        public bool HasModule => ModuleId != TdfDocument.ModuleNull && ModuleId != TdfDocument.ModuleSpace;

        public TdfCell(ushort moduleId, ushort rotation, ushort elevationSteps)
        {
            ModuleId = moduleId;
            Rotation = rotation;
            ElevationSteps = elevationSteps;
        }

        public bool Equals(TdfCell other)
        {
            // empty cells compare equal regardless of their (meaningless) rotation/elevation
            if (!HasModule && !other.HasModule)
                return true;
            return ModuleId == other.ModuleId && Rotation == other.Rotation && ElevationSteps == other.ElevationSteps;
        }

        public override bool Equals(object obj) => obj is TdfCell other && Equals(other);
        public override int GetHashCode() => HasModule ? (ModuleId | (Rotation << 16) | (ElevationSteps << 18)) : -1;
    }

    /// <summary>
    /// Thrown when a TDF file cannot be read. The message is written for end users.
    /// </summary>
    public class TdfFormatException : Exception
    {
        public TdfFormatException(string message) : base(message) { }
        public TdfFormatException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// Unity-independent model and codec for Re-Volt Track Design Files (.TDF).
    ///
    /// Layout (little endian):
    ///   int32   magic 'TDF '
    ///   uint16  version (1 or 2)
    ///   padded string: uint8 length, chars, one zero byte, plus one more if length is odd
    ///   uint16  theme type (unused)
    ///   int32   sections used (unused)
    ///   uint16  width, uint16 height
    ///   width*height x { uint16 moduleId, uint16 rotation, uint16 elevationSteps }   (row major, x fastest)
    ///   version >= 2: uint16 pickupCount, pickupCount x { uint16 x, uint16 y }
    ///
    /// The writer is byte-for-byte identical to the editor's historical writer for ASCII names.
    /// The reader validates sizes before allocating so a corrupt file can never exhaust memory,
    /// and reports problems via <see cref="TdfFormatException"/> or <see cref="Warnings"/>.
    /// </summary>
    public sealed class TdfDocument
    {
        public const int Magic = 0x20464454; // 'TDF '
        public const ushort Version1 = 1;
        public const ushort Version2 = 2;
        public const ushort ModuleNull = 32767;
        public const ushort ModuleSpace = 0;

        /// <summary>Maximum name length the original editor's keyboard allowed.</summary>
        public const int MaxNameLength = 45;

        /// <summary>Bytes per cell record.</summary>
        private const int CellRecordSize = 6;

        public string Name = string.Empty;
        public ushort Version = Version2;
        public ushort ThemeType = 0;
        public int SectionsUsed = 1;

        public int Width { get; private set; }
        public int Height { get; private set; }
        public TdfCell[] Cells { get; private set; }
        public bool[] Pickups { get; private set; }

        /// <summary>Non-fatal problems found while reading (e.g. pickups outside the grid).</summary>
        public readonly List<string> Warnings = new List<string>();

        /// <summary>
        /// Editor data the TDF format can't store (kept in a sidecar file, see <see cref="TrackFiles"/>).
        /// Part of the document so undo, dirty tracking and autosave include it.
        /// </summary>
        public TrackExtras Extras = new TrackExtras();

        public TdfDocument(int width, int height)
        {
            if (width <= 0 || height <= 0 || width > ushort.MaxValue || height > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(width), $"Invalid track size {width}x{height}.");

            Width = width;
            Height = height;
            Cells = new TdfCell[width * height];
            Pickups = new bool[width * height];
            for (int i = 0; i < Cells.Length; i++)
                Cells[i] = TdfCell.Empty;
        }

        // Accessors
        public int IndexOf(int x, int y) => (y * Width) + x;
        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

        public TdfCell GetCell(int x, int y) => Cells[IndexOf(x, y)];
        public void SetCell(int x, int y, TdfCell cell) => Cells[IndexOf(x, y)] = cell;

        public bool GetPickup(int x, int y) => Pickups[IndexOf(x, y)];
        public void SetPickup(int x, int y, bool value) => Pickups[IndexOf(x, y)] = value;

        public int ModuleCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < Cells.Length; i++)
                    if (Cells[i].HasModule) count++;
                return count;
            }
        }

        public int PickupCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < Pickups.Length; i++)
                    if (Pickups[i]) count++;
                return count;
            }
        }

        /// <summary>
        /// Structural equality (name, size, modules, pickups). Used to detect whether
        /// the current editor state matches the last saved state.
        /// </summary>
        public bool ContentEquals(TdfDocument other)
        {
            if (other == null || other.Width != Width || other.Height != Height || other.Name != Name)
                return false;
            if (!(Extras ?? new TrackExtras()).ContentEquals(other.Extras))
                return false;
            for (int i = 0; i < Cells.Length; i++)
            {
                if (!Cells[i].Equals(other.Cells[i]) || Pickups[i] != other.Pickups[i])
                    return false;
            }
            return true;
        }

        public TdfDocument Clone()
        {
            var clone = new TdfDocument(Width, Height)
            {
                Name = Name,
                Version = Version,
                ThemeType = ThemeType,
                SectionsUsed = SectionsUsed
            };
            Array.Copy(Cells, clone.Cells, Cells.Length);
            Array.Copy(Pickups, clone.Pickups, Pickups.Length);
            clone.Extras = Extras != null ? Extras.Clone() : new TrackExtras();
            return clone;
        }

        // Names
        /// <summary>
        /// Restricts a track name to what the TDF format and the original editor can represent:
        /// printable ASCII, trimmed, at most <see cref="MaxNameLength"/> characters.
        /// Characters outside that range are replaced with '_' rather than dropped so the user can see them.
        /// </summary>
        public static string SanitizeName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return string.Empty;

            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                if (c >= 0x20 && c <= 0x7E)
                    sb.Append(c);
                else if (!char.IsControl(c))
                    sb.Append('_');
            }

            string result = sb.ToString().Trim();
            if (result.Length > MaxNameLength)
                result = result.Substring(0, MaxNameLength).TrimEnd();
            return result;
        }

        public static bool IsValidNameChar(char c) => c >= 0x20 && c <= 0x7E;

        // Reading
        public static TdfDocument Load(string path)
        {
            if (!File.Exists(path))
                throw new TdfFormatException($"The file '{Path.GetFileName(path)}' does not exist.");

            try
            {
                using (var stream = File.OpenRead(path))
                    return Read(stream);
            }
            catch (TdfFormatException)
            {
                throw;
            }
            catch (IOException ex)
            {
                throw new TdfFormatException($"The file '{Path.GetFileName(path)}' could not be read: {ex.Message}", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new TdfFormatException($"Access to '{Path.GetFileName(path)}' was denied.", ex);
            }
        }

        /// <summary>
        /// Reads only the header (name and version). Cheap; used for file listings.
        /// </summary>
        public static string ReadName(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var reader = new BinaryReader(stream))
            {
                ReadHeader(reader, out _);
                return ReadPaddedString(reader);
            }
        }

        public static TdfDocument Read(Stream stream)
        {
            using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
            {
                try
                {
                    return ReadInternal(reader);
                }
                catch (EndOfStreamException ex)
                {
                    throw new TdfFormatException("The track file is truncated (it ends unexpectedly).", ex);
                }
            }
        }

        private static void ReadHeader(BinaryReader reader, out ushort version)
        {
            int magic = reader.ReadInt32();
            if (magic != Magic)
                throw new TdfFormatException("This is not a Re-Volt track design file (wrong file signature).");

            version = reader.ReadUInt16();
            if (version != Version1 && version != Version2)
                throw new TdfFormatException($"Unsupported track file version {version}. Only versions 1 and 2 are supported.");
        }

        private static TdfDocument ReadInternal(BinaryReader reader)
        {
            ReadHeader(reader, out ushort version);
            string name = ReadPaddedString(reader);

            ushort themeType = reader.ReadUInt16();
            int sectionsUsed = reader.ReadInt32();

            int width = reader.ReadUInt16();
            int height = reader.ReadUInt16();

            if (width == 0 || height == 0)
                throw new TdfFormatException($"The track has an invalid size ({width}x{height}).");

            // make sure the file actually contains the cell data before allocating for it
            var stream = reader.BaseStream;
            if (stream.CanSeek)
            {
                long required = (long)width * height * CellRecordSize;
                long remaining = stream.Length - stream.Position;
                if (remaining < required)
                    throw new TdfFormatException($"The track file is truncated: a {width}x{height} grid needs {required} bytes of cell data but only {remaining} remain.");
            }

            var doc = new TdfDocument(width, height)
            {
                Name = name,
                Version = version,
                ThemeType = themeType,
                SectionsUsed = sectionsUsed
            };

            for (int i = 0; i < doc.Cells.Length; i++)
            {
                ushort moduleId = reader.ReadUInt16();
                ushort rotation = reader.ReadUInt16();
                ushort elevation = reader.ReadUInt16();
                doc.Cells[i] = new TdfCell(moduleId, rotation, elevation);
            }

            if (version >= Version2)
            {
                int pickupCount = reader.ReadUInt16();
                int outOfRange = 0;
                for (int i = 0; i < pickupCount; i++)
                {
                    int x = reader.ReadUInt16();
                    int y = reader.ReadUInt16();
                    if (doc.InBounds(x, y))
                        doc.SetPickup(x, y, true);
                    else
                        outOfRange++;
                }

                if (outOfRange > 0)
                    doc.Warnings.Add($"{outOfRange} pickup(s) were outside the {width}x{height} grid and were ignored.");
            }

            return doc;
        }

        /// <summary>
        /// Reads the TDF padded string. The length byte counts characters. Characters are decoded as UTF-8
        /// where the bytes form a valid sequence (this editor historically wrote names through a UTF-8
        /// BinaryWriter) and as Latin-1 otherwise, so neither kind of legacy file is misaligned.
        /// </summary>
        public static string ReadPaddedString(BinaryReader reader)
        {
            int len = reader.ReadByte();
            var builder = new StringBuilder(len);

            for (int i = 0; i < len; i++)
            {
                byte b0 = reader.ReadByte();
                if (b0 < 0x80)
                {
                    builder.Append((char)b0);
                    continue;
                }

                int extra = (b0 & 0xE0) == 0xC0 ? 1 : (b0 & 0xF0) == 0xE0 ? 2 : 0;
                if (extra == 0 || !TryReadUtf8Continuation(reader, b0, extra, builder))
                    builder.Append((char)b0); // Latin-1 fallback
            }

            reader.ReadByte(); // pad
            if ((len % 2) > 0)
                reader.ReadByte(); // pad

            return builder.ToString();
        }

        private static bool TryReadUtf8Continuation(BinaryReader reader, byte lead, int extra, StringBuilder builder)
        {
            var stream = reader.BaseStream;
            if (!stream.CanSeek)
                return false;

            long start = stream.Position;
            int codePoint = lead & (extra == 1 ? 0x1F : 0x0F);
            for (int i = 0; i < extra; i++)
            {
                if (stream.Position >= stream.Length)
                {
                    stream.Position = start;
                    return false;
                }

                byte b = reader.ReadByte();
                if ((b & 0xC0) != 0x80)
                {
                    stream.Position = start;
                    return false;
                }
                codePoint = (codePoint << 6) | (b & 0x3F);
            }

            // reject overlong encodings and surrogates; treat the lead byte as Latin-1 instead
            bool overlong = (extra == 1 && codePoint < 0x80) || (extra == 2 && codePoint < 0x800);
            if (overlong || (codePoint >= 0xD800 && codePoint <= 0xDFFF))
            {
                stream.Position = start;
                return false;
            }

            builder.Append((char)codePoint);
            return true;
        }

        // Writing
        /// <summary>
        /// Writes the document. The name is sanitized to printable ASCII so the file is always
        /// readable by the original editor and the game.
        /// </summary>
        public void Write(Stream stream)
        {
            using (var writer = new BinaryWriter(stream, Encoding.ASCII, true))
            {
                writer.Write(Magic);
                writer.Write(Version2);
                WritePaddedString(writer, SanitizeName(Name));

                writer.Write(ThemeType);
                writer.Write(SectionsUsed);

                writer.Write((ushort)Width);
                writer.Write((ushort)Height);

                for (int i = 0; i < Cells.Length; i++)
                {
                    var cell = Cells[i];
                    if (cell.HasModule)
                    {
                        writer.Write(cell.ModuleId);
                        writer.Write(cell.Rotation);
                        writer.Write(cell.ElevationSteps);
                    }
                    else
                    {
                        writer.Write(ModuleNull);
                        writer.Write((ushort)0);
                        writer.Write((ushort)0);
                    }
                }

                int pickupCount = Math.Min(PickupCount, ushort.MaxValue);
                writer.Write((ushort)pickupCount);
                int written = 0;
                for (int i = 0; i < Pickups.Length && written < pickupCount; i++)
                {
                    if (!Pickups[i])
                        continue;
                    writer.Write((ushort)(i % Width));
                    writer.Write((ushort)(i / Width));
                    written++;
                }
            }
        }

        public byte[] ToBytes()
        {
            using (var ms = new MemoryStream())
            {
                Write(ms);
                return ms.ToArray();
            }
        }

        private static void WritePaddedString(BinaryWriter writer, string str)
        {
            int len = Math.Min(byte.MaxValue, str.Length);
            writer.Write((byte)len);
            for (int i = 0; i < len; i++)
                writer.Write((byte)str[i]); // ASCII guaranteed by SanitizeName

            writer.Write((byte)0); // pad
            if ((len % 2) > 0)
                writer.Write((byte)0); // pad
        }

        /// <summary>
        /// Saves atomically: data is written to a temporary file next to the destination, flushed
        /// to disk, then swapped into place. If anything fails, the original file is left untouched.
        /// </summary>
        public void Save(string path)
        {
            byte[] data = ToBytes();
            SafeFile.WriteAllBytesAtomic(path, data);
        }
    }

    /// <summary>
    /// Crash-safe file writing helpers.
    /// </summary>
    public static class SafeFile
    {
        public static void WriteAllBytesAtomic(string path, byte[] data)
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            // ".tmp" extension so the temp file is never picked up by "*.TDF" listings
            string tempPath = path + ".tmp";
            try
            {
                using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    fs.Write(data, 0, data.Length);
                    fs.Flush(true);
                }

                if (File.Exists(path))
                {
                    try
                    {
                        File.Replace(tempPath, path, null);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        ReplaceFallback(tempPath, path);
                    }
                    catch (IOException)
                    {
                        // File.Replace is unsupported on some file systems (e.g. FAT, network shares)
                        ReplaceFallback(tempPath, path);
                    }
                }
                else
                {
                    File.Move(tempPath, path);
                }
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch (Exception)
                {
                    // best effort cleanup; the temp file is harmless
                }
            }
        }

        private static void ReplaceFallback(string tempPath, string path)
        {
            // keep the original until the new data is in place
            string backupPath = path + ".bak";
            if (File.Exists(backupPath))
                File.Delete(backupPath);
            File.Move(path, backupPath);
            try
            {
                File.Move(tempPath, path);
            }
            catch
            {
                File.Move(backupPath, path); // restore
                throw;
            }
            File.Delete(backupPath);
        }
    }
}

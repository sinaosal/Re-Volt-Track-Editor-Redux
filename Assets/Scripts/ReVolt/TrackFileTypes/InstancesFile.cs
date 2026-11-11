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
using UnityEngine;

namespace ReVolt.Track
{
    [Flags]
    public enum InstanceFlags
    {
        None = 0,
        /// <summary>Env-mapped with <see cref="Instance.EnvColor"/>.</summary>
        EnvMap = 1,
        /// <summary>Only drawn in reflections.</summary>
        MirrorHide = 2,
        NoMirror = 4,
        /// <summary>Ignores the level's .lit lights.</summary>
        NoFileLights = 8,
        /// <summary>The tint is only used when this is set; writers should always set it.</summary>
        SetModelRgb = 16,
        /// <summary>No collision with cars/objects. Note: the game also doesn't draw such instances.</summary>
        NoObjectCollision = 32,
        NoCameraCollision = 64
    }

    /// <summary>
    /// A placed model (.prm in the level folder) from a .fin file. 72 bytes on disk.
    /// </summary>
    public struct Instance : IBinSerializable
    {
        public const int MaxNameLength = 8;

        /// <summary>Model name (the .prm file name, matched on its first 8 characters, case-insensitive).</summary>
        public string Name;

        /// <summary>Signed tint per channel, -128..127 (0 = unchanged).</summary>
        public sbyte TintR, TintG, TintB;

        /// <summary>Environment map colour, 0xRRGGBB.</summary>
        public uint EnvColor;

        /// <summary>1 = always present; 0 = removed when the player turns instances off.</summary>
        public byte Priority;

        public InstanceFlags Flags;
        public float LodBias;
        public Vector3 Position;
        public Matrix4x4 Matrix;

        public static Instance Create(string name, Vector3 position)
        {
            return new Instance
            {
                Name = name,
                EnvColor = 0x808080,
                LodBias = 1024f,
                Flags = InstanceFlags.SetModelRgb,
                Position = position,
                Matrix = Matrix4x4.identity
            };
        }

        public void ReadBinary(BinaryReader reader)
        {
            byte[] nameBytes = reader.ReadBytes(9);
            int length = Array.IndexOf(nameBytes, (byte)0);
            Name = Encoding.ASCII.GetString(nameBytes, 0, length < 0 ? 9 : length);

            TintR = reader.ReadSByte();
            TintG = reader.ReadSByte();
            TintB = reader.ReadSByte();
            EnvColor = reader.ReadUInt32() & 0xFFFFFF;
            Priority = reader.ReadByte();
            Flags = (InstanceFlags)reader.ReadByte();
            reader.ReadBytes(2); // pad
            LodBias = reader.ReadSingle();
            Position = reader.ReadVector3();
            Matrix = reader.ReadMatrix3x3();
        }

        public void WriteBinary(BinaryWriter writer)
        {
            // upper-case like the game's own editor, at most 8 characters plus terminator
            var nameBytes = new byte[9];
            string upper = (Name ?? string.Empty).ToUpperInvariant();
            int length = Math.Min(MaxNameLength, upper.Length);
            Encoding.ASCII.GetBytes(upper, 0, length, nameBytes, 0);
            writer.Write(nameBytes);

            writer.Write(TintR);
            writer.Write(TintG);
            writer.Write(TintB);
            writer.Write(EnvColor & 0xFFFFFF);
            writer.Write(Priority);
            writer.Write((byte)(Flags | InstanceFlags.SetModelRgb));
            writer.Write((short)0); // pad
            writer.Write(LodBias);
            writer.WriteVector3(Position);
            writer.WriteMatrix3x3(Matrix);
        }
    }

    /// <summary>
    /// Instances (.fin). The game doesn't check the count against its 1024 slots, so the writer does.
    /// </summary>
    public class InstancesFile : IBinSerializable, ISaveLoad
    {
        public const int MaxInstances = 1024;

        public readonly List<Instance> Instances = new List<Instance>();

        public void Scale(float scale)
        {
            for (int i = 0; i < Instances.Count; i++)
            {
                var instance = Instances[i];
                instance.Position *= scale;
                // the model itself isn't rescaled by the game; scale it with the matrix instead
                instance.Matrix = Matrix4x4.Scale(Vector3.one * scale) * instance.Matrix;
                Instances[i] = instance;
            }
        }

        public void Save(string filepath)
        {
            using (var bw = new BinaryWriter(File.Open(filepath, FileMode.Create)))
                WriteBinary(bw);
        }

        public void Load(string filepath)
        {
            using (var br = new BinaryReader(File.OpenRead(filepath)))
                ReadBinary(br);
        }

        public void ReadBinary(BinaryReader reader)
        {
            Instances.Clear();
            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                var instance = new Instance();
                instance.ReadBinary(reader);
                Instances.Add(instance);
            }
        }

        public void WriteBinary(BinaryWriter writer)
        {
            if (Instances.Count > MaxInstances)
                throw new InvalidDataException($"A track can have at most {MaxInstances} instances (this one has {Instances.Count}).");

            writer.Write(Instances.Count);
            foreach (var instance in Instances)
                instance.WriteBinary(writer);
        }
    }
}

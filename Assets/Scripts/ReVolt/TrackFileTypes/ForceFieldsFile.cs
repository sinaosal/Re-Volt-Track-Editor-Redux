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

using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ReVolt.Track
{
    public enum ForceFieldType
    {
        Linear = 0,
        OrientationUp = 1,
        Velocity = 2,
        Wind = 4,
        Angular = 5,
        AngularVelocity = 6,
        OrientationForward = 7
    }

    public enum ForceFieldShape { Box = 0, Sphere = 1 }
    public enum ForceFieldDirection { Linear = 0, Radial = 1 }
    public enum ForceFieldDistribution { Uniform = 0, Gradient = 1 }
    public enum ForceFieldApply { Accelerate = 0, Force = 1 }

    /// <summary>
    /// A force field from a .fld file (100 bytes on disk). Uses RVGL's packed format, where the type
    /// word also holds the direction mode, shape, distribution and how the force is applied.
    /// </summary>
    public struct ForceField : IBinSerializable
    {
        public ForceFieldType Type;
        public ForceFieldDirection DirectionMode;
        public ForceFieldShape Shape;
        public ForceFieldDistribution Distribution;
        public ForceFieldApply Apply;

        public Vector3 Position;
        /// <summary>Box orientation (the R axis is also the gradient axis).</summary>
        public Matrix4x4 Matrix;
        /// <summary>Box half-extents along R, U, L.</summary>
        public Vector3 Size;
        /// <summary>World space direction (not rotated by the matrix); for radial fields, the centre offset.</summary>
        public Vector3 Direction;
        public float Magnitude;
        public float Damping;
        public float RadiusStart, RadiusEnd;
        public float MagnitudeStart, MagnitudeEnd;

        /// <summary>The game editor's defaults for a new field.</summary>
        public static ForceField Create(Vector3 position)
        {
            return new ForceField
            {
                Type = ForceFieldType.Linear,
                Shape = ForceFieldShape.Box,
                DirectionMode = ForceFieldDirection.Linear,
                Distribution = ForceFieldDistribution.Uniform,
                Apply = ForceFieldApply.Force,
                Position = position,
                Matrix = Matrix4x4.identity,
                Size = new Vector3(128f, 128f, 128f),
                Direction = new Vector3(0f, -1f, 0f),
                RadiusStart = 256f,
                RadiusEnd = 512f
            };
        }

        public int PackedType => ((int)Type & 0xFFFF)
                               | (((int)DirectionMode & 0xF) << 16)
                               | (((int)Shape & 0xF) << 20)
                               | (((int)Distribution & 0xF) << 24)
                               | (((int)Apply & 0xF) << 28);

        public void Unpack(int packed)
        {
            Type = (ForceFieldType)(packed & 0xFFFF);
            DirectionMode = (ForceFieldDirection)((packed >> 16) & 0xF);
            Shape = (ForceFieldShape)((packed >> 20) & 0xF);
            Distribution = (ForceFieldDistribution)((packed >> 24) & 0xF);
            Apply = (ForceFieldApply)((packed >> 28) & 0xF);
        }

        /// <summary>Converts a field from the original (legacy) format the way the game does on load.</summary>
        public void ConvertFromLegacy(int legacyType)
        {
            if (legacyType == 3)
            {
                // legacy spherical field
                Type = ForceFieldType.Linear;
                Shape = ForceFieldShape.Sphere;
                Distribution = ForceFieldDistribution.Gradient;
                DirectionMode = ForceFieldDirection.Radial;
                Apply = ForceFieldApply.Force;
                Direction = Vector3.zero;
            }
            else
            {
                Type = (ForceFieldType)legacyType;
                Shape = ForceFieldShape.Box;
                Distribution = ForceFieldDistribution.Uniform;
                DirectionMode = ForceFieldDirection.Linear;
                Apply = ForceFieldApply.Force;
            }
        }

        public void ReadBinary(BinaryReader reader)
        {
            Unpack(reader.ReadInt32());
            Position = reader.ReadVector3();
            Matrix = reader.ReadMatrix3x3();
            Size = reader.ReadVector3();
            Direction = reader.ReadVector3();
            Magnitude = reader.ReadSingle();
            Damping = reader.ReadSingle();
            RadiusStart = reader.ReadSingle();
            RadiusEnd = reader.ReadSingle();
            MagnitudeStart = reader.ReadSingle();
            MagnitudeEnd = reader.ReadSingle();
        }

        public void WriteBinary(BinaryWriter writer)
        {
            writer.Write(PackedType);
            writer.WriteVector3(Position);
            writer.WriteMatrix3x3(Matrix);
            writer.WriteVector3(Size);
            writer.WriteVector3(Direction);
            writer.Write(Magnitude);
            writer.Write(Damping);
            writer.Write(RadiusStart);
            writer.Write(RadiusEnd);
            writer.Write(MagnitudeStart);
            writer.Write(MagnitudeEnd);
        }
    }

    /// <summary>
    /// Force fields (.fld). Written in RVGL's packed format (trailer value 1); legacy files are converted on read.
    /// </summary>
    public class ForceFieldsFile : IBinSerializable, ISaveLoad
    {
        public const int MaxFields = 1024;
        private const int RecordSize = 100;

        public readonly List<ForceField> Fields = new List<ForceField>();

        public void Scale(float scale)
        {
            for (int i = 0; i < Fields.Count; i++)
            {
                var field = Fields[i];
                field.Position *= scale;
                field.Size *= scale;
                field.RadiusStart *= scale;
                field.RadiusEnd *= scale;
                if (field.DirectionMode == ForceFieldDirection.Radial)
                    field.Direction *= scale; // radial fields store a centre offset, not a direction
                Fields[i] = field;
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
            Fields.Clear();
            int count = reader.ReadInt32();
            var raw = new List<(ForceField field, int type)>();
            for (int i = 0; i < count; i++)
            {
                var field = new ForceField();
                field.ReadBinary(reader);
                raw.Add((field, field.PackedType));
            }

            // optional trailer: non-zero means the packed format
            int format = 0;
            var stream = reader.BaseStream;
            if (!stream.CanSeek || stream.Length - stream.Position >= 4)
            {
                try { format = reader.ReadInt32(); }
                catch (EndOfStreamException) { format = 0; }
            }

            foreach (var (field, type) in raw)
            {
                var f = field;
                if (format == 0)
                    f.ConvertFromLegacy(type & 0xFFFF);
                Fields.Add(f);
            }
        }

        public void WriteBinary(BinaryWriter writer)
        {
            if (Fields.Count > MaxFields)
                throw new InvalidDataException($"A track can have at most {MaxFields} force fields (this one has {Fields.Count}).");

            writer.Write(Fields.Count);
            foreach (var field in Fields)
                field.WriteBinary(writer);
            writer.Write(1); // packed format
        }
    }
}

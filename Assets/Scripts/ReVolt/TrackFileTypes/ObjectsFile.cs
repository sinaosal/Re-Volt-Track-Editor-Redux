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
    /// <summary>
    /// Object IDs understood by Re-Volt / RVGL (0..76). Note that 0 is a barrel, not "none".
    /// </summary>
    public enum ObjectType
    {
        Barrel = 0, Beachball = 1, Planet = 2, Plane = 3, Copter = 4, Dragon = 5, Water = 6, Trolley = 7, Boat = 8,
        Speedup = 9, Radar = 10, Balloon = 11, Horse = 12, Train = 13, Strobe = 14, Football = 15, SparkGenerator = 16,
        Spaceman = 17, Pickup = 30, Flap = 32, Laser = 33, Weebel = 36, ProbeLogo = 37, Clouds = 38, NameWheel = 39,
        Sprinkler = 40, SprinklerHose = 41, ObjectThrower = 42, Basketball = 43, TrackScreen = 44, Clock = 45, CarBox = 46,
        Stream = 47, Cup = 48, Sound3D = 49, Star = 50, Tumbleweed = 52, SmallScreen = 53, Lantern = 54, Skybox = 55,
        Slider = 56, Bottle = 57, Bucket = 58, Cone = 59, Can = 60, Lilo = 61, Rain = 63, Lightning = 64, ShipLight = 65,
        Packet = 66, AbcBlock = 67, WaterBox = 68, Ripple = 69, Flag = 70, Dolphin = 71, GardenFog = 72, FogBox = 73,
        Chopper = 74, Disco = 75, CustomAnimation = 76
    }

    public struct Object : IBinSerializable
    {
        public ObjectType Type;
        public int Flags1;
        public int Flags2;
        public int Flags3;
        public int Flags4;
        public Vector3 Position;
        public Matrix4x4 DirectionMatrix;

        public void ReadBinary(BinaryReader reader)
        {
            Type = (ObjectType)reader.ReadInt32();
            Flags1 = reader.ReadInt32();
            Flags2 = reader.ReadInt32();
            Flags3 = reader.ReadInt32();
            Flags4 = reader.ReadInt32();

            Position = reader.ReadVector3();

            Vector3 up = reader.ReadVector3();
            Vector3 forward = reader.ReadVector3();
            Vector3 sideways = Vector3.Cross(up, forward); // the game uses right = up x look

            DirectionMatrix = Matrix4x4.identity;
            DirectionMatrix.SetColumn(0, sideways);
            DirectionMatrix.SetColumn(1, up);
            DirectionMatrix.SetColumn(2, forward);
        }

        public void WriteBinary(BinaryWriter writer)
        {
            writer.Write((int)Type);
            writer.Write(Flags1);
            writer.Write(Flags2);
            writer.Write(Flags3);
            writer.Write(Flags4);

            writer.WriteVector3(Position);

            writer.WriteVector3(DirectionMatrix.GetColumn(1));
            writer.WriteVector3(DirectionMatrix.GetColumn(2));
        }
    }

    public class ObjectsFile : IBinSerializable, ISaveLoad
    {
        public readonly List<Object> Objects = new List<Object>();

        public void Scale(float scale)
        {
            for(int i=0; i < Objects.Count; i++)
            {
                var @object = Objects[i];
                @object.Position *= scale;

                // some types store distances in their flags
                int S(int value) => Mathf.RoundToInt(value * scale);
                switch (@object.Type)
                {
                    case ObjectType.Plane:
                        @object.Flags2 = S(@object.Flags2); // radius
                        break;
                    case ObjectType.Copter:
                        @object.Flags1 = S(@object.Flags1); // ranges and height offset
                        @object.Flags2 = S(@object.Flags2);
                        @object.Flags3 = S(@object.Flags3);
                        @object.Flags4 = S(@object.Flags4);
                        break;
                    case ObjectType.Speedup:
                        @object.Flags1 = S(@object.Flags1); // width
                        break;
                    case ObjectType.WaterBox:
                    case ObjectType.FogBox:
                        @object.Flags1 = S(@object.Flags1); // half sizes
                        @object.Flags2 = S(@object.Flags2);
                        @object.Flags3 = S(@object.Flags3);
                        break;
                }
                Objects[i] = @object;
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
            int objectsCount = reader.ReadInt32();
            for (int i = 0; i < objectsCount; i++)
            {
                var @object = new Object();
                @object.ReadBinary(reader);
                Objects.Add(@object);
            }
        }

        public void WriteBinary(BinaryWriter writer)
        {
            writer.Write(Objects.Count);
            foreach (var @object in Objects)
            {
                @object.WriteBinary(writer);
            }
        }

        // constructors
        public ObjectsFile(BinaryReader reader)
        {
            ReadBinary(reader);
        }

        public ObjectsFile(string filePath)
        {
            using var br = new BinaryReader(File.OpenRead(filePath));
            ReadBinary(br);
        }

        public ObjectsFile() { }
    }
}
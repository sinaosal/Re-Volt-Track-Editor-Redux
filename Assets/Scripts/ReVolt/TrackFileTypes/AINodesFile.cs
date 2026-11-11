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
using System.Linq;

namespace ReVolt.Track
{
    public enum AINodePriority
    {
        RacingLine,
        Pickup,
        Stairs,
        Bumpy,
        Slowdown25,
        SoftSuspension,
        JumpWall,
        IntroSlowdown,
        TurboLine,
        LongPickup,
        Shortcut,
        Longcut,
        BarrelBlock,
        OffThrottle,
        OffThrottlePetrol,
        Wilderness,
        Slowdown15,
        Slowdown20,
        Slowdown30
    }

    public struct AINodeEnd
    {
        public int Speed;
        public Vector3 Position;
    }

    public struct AINode : IBinSerializable
    {
        public AINodePriority Priority;
        public byte StartNode;

        public short Flags;

        public float RacingLine;
        public float FinishDist;
        public float OvertakingLine;

        public int RacingLineSpeed;
        public int CenterSpeed;

        public int[] PreviousLinkIDs;
        public int[] NextLinkIDs;

        public AINodeEnd RedEnd;
        public AINodeEnd GreenEnd;

        public void ReadBinary(BinaryReader reader)
        {
            Priority = (AINodePriority)reader.ReadByte();
            StartNode = reader.ReadByte();
            
            // wall flags are one byte the game writes twice (bit0 = wall left/green, bit1 = wall right/red)
            Flags = reader.ReadByte();
            reader.ReadByte();

            RacingLine = reader.ReadSingle();
            FinishDist = reader.ReadSingle();
            OvertakingLine = reader.ReadSingle();
            reader.ReadSingle(); // pad

            RacingLineSpeed = reader.ReadInt32();
            CenterSpeed = reader.ReadInt32();

            PreviousLinkIDs = new int[] { reader.ReadInt32(), reader.ReadInt32() };
            NextLinkIDs = new int[] { reader.ReadInt32(), reader.ReadInt32() };

            RedEnd = new AINodeEnd()
            {
                Speed = reader.ReadInt32(),
                Position = reader.ReadVector3()
            };

            GreenEnd = new AINodeEnd()
            {
                Speed = reader.ReadInt32(),
                Position = reader.ReadVector3()
            };
        }

        public void WriteBinary(BinaryWriter writer)
        {
            writer.Write((byte)Priority);
            writer.Write(StartNode);

            writer.Write((byte)Flags);
            writer.Write((byte)Flags);

            writer.Write(RacingLine);
            writer.Write(FinishDist);
            writer.Write(OvertakingLine);
            writer.Write(0f);

            writer.Write(RacingLineSpeed);
            writer.Write(CenterSpeed);

            writer.Write(PreviousLinkIDs[0]);
            writer.Write(PreviousLinkIDs[1]);
            writer.Write(NextLinkIDs[0]);
            writer.Write(NextLinkIDs[1]);

            writer.Write(RedEnd.Speed);
            writer.WriteVector3(RedEnd.Position);

            writer.Write(GreenEnd.Speed);
            writer.WriteVector3(GreenEnd.Position);
        }
    }

    public class AINodesFile : IBinSerializable, ISaveLoad
    {
        public readonly List<AINode> Nodes = new List<AINode>();
        public int StartNode;
        public float TotalDistance;

        public void Scale(float scale)
        {
            TotalDistance *= scale;
            for(int i=0; i < Nodes.Count; i++)
            {
                var node = Nodes[i];
                node.GreenEnd.Position *= scale;
                node.RedEnd.Position *= scale;
                node.FinishDist *= scale;
                Nodes[i] = node;
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
            int nodeCount = reader.ReadUInt16();
            reader.ReadUInt16(); // double forward link nodes count, recalculated on save

            for(int i=0; i < nodeCount; i++)
            {
                var node = new AINode();
                node.ReadBinary(reader);
                Nodes.Add(node);
            }

            StartNode = reader.ReadInt32();
            TotalDistance = reader.ReadSingle();
        }

        /// <summary>Packs links into the first slots (the game expects next[0] to be filled before next[1]).</summary>
        private static int[] TopSlot(int[] links)
        {
            var packed = new[] { -1, -1 };
            int k = 0;
            foreach (int link in links ?? new int[0])
            {
                if (link >= 0 && k < 2)
                    packed[k++] = link;
            }
            return packed;
        }

        public void WriteBinary(BinaryWriter writer)
        {
            for (int i = 0; i < Nodes.Count; i++)
            {
                var node = Nodes[i];
                node.PreviousLinkIDs = TopSlot(node.PreviousLinkIDs);
                node.NextLinkIDs = TopSlot(node.NextLinkIDs);
                Nodes[i] = node;
            }

            // the game allocates one extra node per branch (next[1] set); under-counting overruns its memory
            int doubleForwardLinkCount = Nodes.Count(x => x.NextLinkIDs[1] >= 0);
        
            writer.Write((ushort)Nodes.Count);
            writer.Write((ushort)doubleForwardLinkCount);

            for(int i=0; i < Nodes.Count; i++)
            {
                Nodes[i].WriteBinary(writer);
            }

            writer.Write(StartNode);
            writer.Write(TotalDistance);
        }

        // constructors
        public AINodesFile(BinaryReader reader)
        {
            ReadBinary(reader);
        }

        public AINodesFile(string filePath)
        {
            using var br = new BinaryReader(File.OpenRead(filePath));
            ReadBinary(br);
        }

        public AINodesFile() { }
    }
}

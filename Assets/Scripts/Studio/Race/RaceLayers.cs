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

using Dummiesman.DebugDraw;
using ReVolt.Track;
using Studio.Icons;
using System.Collections.Generic;
using UnityEngine;
using AINode = ReVolt.Track.AINode;
using TrackObject = ReVolt.Track.Object;
using Zone = ReVolt.Track.Zone;
using Instance = ReVolt.Track.Instance;

namespace Studio
{
    public enum RaceLayer
    {
        AINodes,
        PosNodes,
        Zones,
        Lights,
        Objects,
        Instances,
        ForceFields
    }

    /// <summary>A grabbable point of a race data element (AI nodes have a red and a green end).</summary>
    public struct RaceHandle
    {
        public int Index;
        public int Part; // 0 = whole element, 1 = red end, 2 = green end
        public Vector3 Position; // editor space
    }

    /// <summary>
    /// Editing operations on race data, per layer. Positions passed in and out are in editor space;
    /// the data itself stays in game units.
    /// </summary>
    public static class RaceLayers
    {
        public static readonly RaceLayer[] All =
        {
            RaceLayer.AINodes, RaceLayer.PosNodes, RaceLayer.Zones, RaceLayer.Lights,
            RaceLayer.Objects, RaceLayer.Instances, RaceLayer.ForceFields
        };

        public static string Name(RaceLayer layer)
        {
            switch (layer)
            {
                case RaceLayer.AINodes: return "AI nodes";
                case RaceLayer.PosNodes: return "Position nodes";
                case RaceLayer.Zones: return "Track zones";
                case RaceLayer.Lights: return "Lights";
                case RaceLayer.Instances: return "Instances";
                case RaceLayer.ForceFields: return "Force fields";
                default: return "Objects";
            }
        }

        public static string Singular(RaceLayer layer)
        {
            switch (layer)
            {
                case RaceLayer.AINodes: return "AI node";
                case RaceLayer.PosNodes: return "position node";
                case RaceLayer.Zones: return "zone";
                case RaceLayer.Lights: return "light";
                case RaceLayer.Instances: return "instance";
                case RaceLayer.ForceFields: return "force field";
                default: return "object";
            }
        }

        public static IconId Icon(RaceLayer layer)
        {
            switch (layer)
            {
                case RaceLayer.AINodes: return IconId.Track;
                case RaceLayer.PosNodes: return IconId.Pin;
                case RaceLayer.Zones: return IconId.Frame;
                case RaceLayer.Lights: return IconId.Lightning;
                case RaceLayer.Instances: return IconId.Module;
                case RaceLayer.ForceFields: return IconId.Resize;
                default: return IconId.ToolPickup;
            }
        }

        public static Color LayerColor(RaceLayer layer)
        {
            switch (layer)
            {
                case RaceLayer.AINodes: return new Color(0.55f, 0.72f, 1f);
                case RaceLayer.PosNodes: return new Color(0.98f, 0.74f, 0.25f);
                case RaceLayer.Zones: return new Color(0.3f, 0.86f, 0.95f);
                case RaceLayer.Lights: return new Color(1f, 0.93f, 0.55f);
                case RaceLayer.Instances: return new Color(0.74f, 0.6f, 1f);
                case RaceLayer.ForceFields: return new Color(0.4f, 0.95f, 0.7f);
                default: return Palette.Accent;
            }
        }

        public static readonly Color RedEnd = new Color(0.96f, 0.32f, 0.3f);
        public static readonly Color GreenEnd = new Color(0.32f, 0.9f, 0.42f);
        public static readonly Color RacingLine = new Color(1f, 0.85f, 0.2f);

        public static int Count(RaceData data, RaceLayer layer)
        {
            if (data == null)
                return 0;
            switch (layer)
            {
                case RaceLayer.AINodes: return data.AINodes.Nodes.Count;
                case RaceLayer.PosNodes: return data.PosNodes.Nodes.Count;
                case RaceLayer.Zones: return data.Zones.Zones.Count;
                case RaceLayer.Lights: return data.Lights.Lights.Count;
                case RaceLayer.Instances: return data.Instances.Instances.Count;
                case RaceLayer.ForceFields: return data.ForceFields.Fields.Count;
                default: return data.Objects.Objects.Count;
            }
        }

        // Handles
        public static List<RaceHandle> Handles(RaceData data, RaceLayer layer)
        {
            var result = new List<RaceHandle>();
            int count = Count(data, layer);
            for (int i = 0; i < count; i++)
            {
                if (layer == RaceLayer.AINodes)
                {
                    var n = data.AINodes.Nodes[i];
                    result.Add(new RaceHandle { Index = i, Part = 1, Position = RaceData.ToEditor(n.RedEnd.Position) });
                    result.Add(new RaceHandle { Index = i, Part = 2, Position = RaceData.ToEditor(n.GreenEnd.Position) });
                    result.Add(new RaceHandle { Index = i, Part = 0, Position = Center(n) });
                }
                else
                {
                    result.Add(new RaceHandle { Index = i, Part = 0, Position = Position(data, layer, i) });
                }
            }
            return result;
        }

        private static Vector3 Center(AINode n) => RaceData.ToEditor((n.RedEnd.Position + n.GreenEnd.Position) * 0.5f);

        /// <summary>Editor-space position of an element (its center).</summary>
        public static Vector3 Position(RaceData data, RaceLayer layer, int index)
        {
            switch (layer)
            {
                case RaceLayer.AINodes: return Center(data.AINodes.Nodes[index]);
                case RaceLayer.PosNodes: return RaceData.ToEditor(data.PosNodes.Nodes[index].Position);
                case RaceLayer.Zones: return RaceData.ToEditor(data.Zones.Zones[index].Position);
                case RaceLayer.Lights: return RaceData.ToEditor(data.Lights.Lights[index].Position);
                case RaceLayer.Instances: return RaceData.ToEditor(data.Instances.Instances[index].Position);
                case RaceLayer.ForceFields: return RaceData.ToEditor(data.ForceFields.Fields[index].Position);
                default: return RaceData.ToEditor(data.Objects.Objects[index].Position);
            }
        }

        /// <summary>Moves an element (or one end of an AI node) by an editor-space offset.</summary>
        public static void Translate(RaceData data, RaceLayer layer, int index, int part, Vector3 editorDelta)
        {
            var delta = RaceData.ToGame(editorDelta);
            switch (layer)
            {
                case RaceLayer.AINodes:
                    {
                        var n = data.AINodes.Nodes[index];
                        if (part != 2) n.RedEnd.Position += delta;
                        if (part != 1) n.GreenEnd.Position += delta;
                        data.AINodes.Nodes[index] = n;
                        break;
                    }
                case RaceLayer.PosNodes:
                    {
                        var n = data.PosNodes.Nodes[index];
                        n.Position += delta;
                        data.PosNodes.Nodes[index] = n;
                        break;
                    }
                case RaceLayer.Zones:
                    {
                        var z = data.Zones.Zones[index];
                        z.Position += delta;
                        data.Zones.Zones[index] = z;
                        break;
                    }
                case RaceLayer.Lights:
                    {
                        var l = data.Lights.Lights[index];
                        l.Position += delta;
                        data.Lights.Lights[index] = l;
                        break;
                    }
                case RaceLayer.Instances:
                    {
                        var i = data.Instances.Instances[index];
                        i.Position += delta;
                        data.Instances.Instances[index] = i;
                        break;
                    }
                case RaceLayer.ForceFields:
                    {
                        var f = data.ForceFields.Fields[index];
                        f.Position += delta;
                        data.ForceFields.Fields[index] = f;
                        break;
                    }
                default:
                    {
                        var o = data.Objects.Objects[index];
                        o.Position += delta;
                        data.Objects.Objects[index] = o;
                        break;
                    }
            }
        }

        /// <summary>
        /// Adds an element. Nodes are inserted after <paramref name="after"/> in the lap (between it and its
        /// successor); other elements are placed at <paramref name="editorPosition"/>. Returns the new index.
        /// </summary>
        public static int Add(RaceData data, RaceLayer layer, Vector3 editorPosition, int after)
        {
            var at = RaceData.ToGame(editorPosition);
            switch (layer)
            {
                case RaceLayer.AINodes: return InsertAINode(data.AINodes, after, at);
                case RaceLayer.PosNodes:
                    {
                        int index = InsertPosNode(data.PosNodes, after, at);
                        RecalculatePosDistances(data.PosNodes);
                        return index;
                    }
                case RaceLayer.Zones:
                    {
                        var size = data.Zones.Zones.Count > 0
                            ? data.Zones.Zones[after >= 0 && after < data.Zones.Zones.Count ? after : 0].Size
                            : new Vector3(RVConstants.SMALL_CUBE_HALF, RVConstants.SMALL_CUBE_HALF, RVConstants.SMALL_CUBE_HALF);
                        data.Zones.Zones.Add(new Zone { ID = data.Zones.Zones.Count, Position = at, Size = size, Matrix = Matrix4x4.identity });
                        return data.Zones.Zones.Count - 1;
                    }
                case RaceLayer.Lights:
                    {
                        // copy the look of the selected (or first) light so the new one is sensible
                        var template = data.Lights.Lights.Count > 0
                            ? data.Lights.Lights[after >= 0 && after < data.Lights.Lights.Count ? after : 0]
                            : new ReVolt.Track.Light
                            {
                                Reach = 512f,
                                Cone = 90f,
                                Speed = 1,
                                Color = new Color(128f, 128f, 128f),
                                DirectionMatrix = Matrix4x4.identity,
                                Flags = LightFlags.File | LightFlags.Fixed | LightFlags.Moving
                            };
                        template.Position = at;
                        data.Lights.Lights.Add(template);
                        return data.Lights.Lights.Count - 1;
                    }
                case RaceLayer.Instances:
                    {
                        // use the selected instance's model, or the first imported one
                        string name = data.Instances.Instances.Count > 0
                            ? data.Instances.Instances[after >= 0 && after < data.Instances.Instances.Count ? after : 0].Name
                            : data.Models.Count > 0 ? data.Models[0].InstanceName : string.Empty;
                        data.Instances.Instances.Add(Instance.Create(name, at));
                        return data.Instances.Instances.Count - 1;
                    }
                case RaceLayer.ForceFields:
                    data.ForceFields.Fields.Add(ForceField.Create(at));
                    return data.ForceFields.Fields.Count - 1;
                default:
                    data.Objects.Objects.Add(new TrackObject { Type = ObjectType.Pickup, Position = at, DirectionMatrix = Matrix4x4.identity });
                    return data.Objects.Objects.Count - 1;
            }
        }

        /// <summary>The imported model an instance uses, if any (matched like the game: first 8 characters, any case).</summary>
        public static RaceData.ModelFile? ModelFor(RaceData data, Instance instance)
        {
            foreach (var model in data.Models)
            {
                if (string.Equals(model.InstanceName, Truncate(instance.Name), System.StringComparison.OrdinalIgnoreCase))
                    return model;
            }
            return null;
        }

        private static string Truncate(string name)
        {
            name = name ?? string.Empty;
            return name.Length > Instance.MaxNameLength ? name.Substring(0, Instance.MaxNameLength) : name;
        }

        public static void Delete(RaceData data, RaceLayer layer, int index)
        {
            switch (layer)
            {
                case RaceLayer.AINodes:
                    DeleteAINode(data.AINodes, index);
                    break;
                case RaceLayer.PosNodes:
                    DeletePosNode(data.PosNodes, index);
                    RecalculatePosDistances(data.PosNodes);
                    break;
                case RaceLayer.Zones:
                    data.Zones.Zones.RemoveAt(index);
                    // zone IDs follow the lap order in the list
                    for (int i = 0; i < data.Zones.Zones.Count; i++)
                    {
                        var z = data.Zones.Zones[i];
                        z.ID = i;
                        data.Zones.Zones[i] = z;
                    }
                    break;
                case RaceLayer.Lights:
                    data.Lights.Lights.RemoveAt(index);
                    break;
                case RaceLayer.Instances:
                    data.Instances.Instances.RemoveAt(index);
                    break;
                case RaceLayer.ForceFields:
                    data.ForceFields.Fields.RemoveAt(index);
                    break;
                default:
                    data.Objects.Objects.RemoveAt(index);
                    break;
            }
        }

        // Linked node helpers
        private static void ReplaceLink(int[] links, int from, int to)
        {
            for (int i = 0; i < links.Length; i++)
            {
                if (links[i] == from)
                {
                    links[i] = to;
                    return;
                }
            }
        }

        private static void RemapAfterDelete(int[] links, int deleted)
        {
            for (int i = 0; i < links.Length; i++)
            {
                if (links[i] == deleted)
                    links[i] = -1;
                else if (links[i] > deleted)
                    links[i]--;
            }
        }

        private static int InsertAINode(AINodesFile file, int after, Vector3 at)
        {
            var nodes = file.Nodes;
            int index = nodes.Count;

            if (after < 0 || after >= nodes.Count)
            {
                // unconnected node at the given position, one car width wide
                var half = new Vector3(RVConstants.SMALL_CUBE_HALF * 0.6f, 0f, 0f);
                nodes.Add(new AINode
                {
                    RacingLine = 0.5f, OvertakingLine = 0.5f, RacingLineSpeed = 30, CenterSpeed = 30,
                    PreviousLinkIDs = new[] { -1, -1 }, NextLinkIDs = new[] { -1, -1 },
                    RedEnd = new AINodeEnd { Speed = 30, Position = at - half },
                    GreenEnd = new AINodeEnd { Speed = 30, Position = at + half }
                });
                return index;
            }

            var source = nodes[after];
            int next = source.NextLinkIDs[0];
            var node = source; // copies settings (priority, lines, speeds)
            node.PreviousLinkIDs = new[] { after, -1 };
            node.NextLinkIDs = new[] { next, -1 };
            if (next >= 0)
            {
                var n = nodes[next];
                node.RedEnd.Position = (source.RedEnd.Position + n.RedEnd.Position) * 0.5f;
                node.GreenEnd.Position = (source.GreenEnd.Position + n.GreenEnd.Position) * 0.5f;
                ReplaceLink(n.PreviousLinkIDs, after, index);
                nodes[next] = n;
            }
            else
            {
                var offset = new Vector3(0f, 0f, RVConstants.SMALL_CUBE_HALF);
                node.RedEnd.Position += offset;
                node.GreenEnd.Position += offset;
            }

            source.NextLinkIDs = (int[])source.NextLinkIDs.Clone();
            source.NextLinkIDs[0] = index;
            nodes[after] = source;
            nodes.Add(node);
            return index;
        }

        private static void DeleteAINode(AINodesFile file, int index)
        {
            var nodes = file.Nodes;
            var node = nodes[index];
            int prev = node.PreviousLinkIDs[0];
            int next = node.NextLinkIDs[0];

            // bridge the gap so the lap stays connected
            foreach (int p in node.PreviousLinkIDs)
            {
                if (p < 0 || p == index) continue;
                var n = nodes[p];
                n.NextLinkIDs = (int[])n.NextLinkIDs.Clone();
                ReplaceLink(n.NextLinkIDs, index, next);
                nodes[p] = n;
            }
            foreach (int x in node.NextLinkIDs)
            {
                if (x < 0 || x == index) continue;
                var n = nodes[x];
                n.PreviousLinkIDs = (int[])n.PreviousLinkIDs.Clone();
                ReplaceLink(n.PreviousLinkIDs, index, prev);
                nodes[x] = n;
            }

            nodes.RemoveAt(index);
            for (int i = 0; i < nodes.Count; i++)
            {
                var n = nodes[i];
                n.PreviousLinkIDs = (int[])n.PreviousLinkIDs.Clone();
                n.NextLinkIDs = (int[])n.NextLinkIDs.Clone();
                RemapAfterDelete(n.PreviousLinkIDs, index);
                RemapAfterDelete(n.NextLinkIDs, index);
                nodes[i] = n;
            }
            if (file.StartNode > index)
                file.StartNode--;
            else if (file.StartNode == index)
                file.StartNode = 0;
        }

        private static int InsertPosNode(POSNodesFile file, int after, Vector3 at)
        {
            var nodes = file.Nodes;
            int index = nodes.Count;
            var node = new POSNode
            {
                Position = at,
                PreviousNodeIDs = new[] { -1, -1, -1, -1 },
                NextNodeIDs = new[] { -1, -1, -1, -1 }
            };

            if (after >= 0 && after < nodes.Count)
            {
                var source = nodes[after];
                int next = source.NextNodeIDs[0];
                node.PreviousNodeIDs[0] = after;
                node.NextNodeIDs[0] = next;
                if (next >= 0)
                {
                    var n = nodes[next];
                    node.Position = (source.Position + n.Position) * 0.5f;
                    n.PreviousNodeIDs = (int[])n.PreviousNodeIDs.Clone();
                    ReplaceLink(n.PreviousNodeIDs, after, index);
                    nodes[next] = n;
                }
                source.NextNodeIDs = (int[])source.NextNodeIDs.Clone();
                source.NextNodeIDs[0] = index;
                nodes[after] = source;
            }

            nodes.Add(node);
            return index;
        }

        private static void DeletePosNode(POSNodesFile file, int index)
        {
            var nodes = file.Nodes;
            var node = nodes[index];
            int prev = node.PreviousNodeIDs[0];
            int next = node.NextNodeIDs[0];

            foreach (int p in node.PreviousNodeIDs)
            {
                if (p < 0 || p == index) continue;
                var n = nodes[p];
                n.NextNodeIDs = (int[])n.NextNodeIDs.Clone();
                ReplaceLink(n.NextNodeIDs, index, next);
                nodes[p] = n;
            }
            foreach (int x in node.NextNodeIDs)
            {
                if (x < 0 || x == index) continue;
                var n = nodes[x];
                n.PreviousNodeIDs = (int[])n.PreviousNodeIDs.Clone();
                ReplaceLink(n.PreviousNodeIDs, index, prev);
                nodes[x] = n;
            }

            nodes.RemoveAt(index);
            for (int i = 0; i < nodes.Count; i++)
            {
                var n = nodes[i];
                n.PreviousNodeIDs = (int[])n.PreviousNodeIDs.Clone();
                n.NextNodeIDs = (int[])n.NextNodeIDs.Clone();
                RemapAfterDelete(n.PreviousNodeIDs, index);
                RemapAfterDelete(n.NextNodeIDs, index);
                nodes[i] = n;
            }
            if (file.StartNodeIndex > index)
                file.StartNodeIndex--;
            else if (file.StartNodeIndex == index)
                file.StartNodeIndex = 0;
        }

        /// <summary>
        /// Recomputes each node's distance to the finish (and the lap length) by walking the lap from the
        /// start node, the way the exporter generates them. Returns false if the nodes don't form a single loop.
        /// </summary>
        public static bool RecalculatePosDistances(POSNodesFile file)
        {
            var nodes = file.Nodes;
            int count = nodes.Count;
            if (count < 2 || file.StartNodeIndex < 0 || file.StartNodeIndex >= count)
                return false;

            var order = new List<int>(count);
            var visited = new HashSet<int>();
            int current = file.StartNodeIndex;
            while (current >= 0 && current < count && visited.Add(current))
            {
                order.Add(current);
                current = nodes[current].NextNodeIDs[0];
            }
            if (current != file.StartNodeIndex || order.Count != count)
                return false;

            var cumulative = new float[count];
            float lap = 0f;
            for (int i = 0; i < order.Count; i++)
            {
                cumulative[i] = lap;
                lap += Vector3.Distance(nodes[order[i]].Position, nodes[order[(i + 1) % order.Count]].Position);
            }

            for (int i = 0; i < order.Count; i++)
            {
                var n = nodes[order[i]];
                n.Distance = i == 0 ? 0f : lap - cumulative[i];
                nodes[order[i]] = n;
            }
            file.TotalLength = lap;
            return true;
        }

        // Drawing
        public static void Draw(RaceData data, RaceLayer layer, bool active, int selected, int selectedPart)
        {
            if (data == null)
                return;

            float alpha = active ? 1f : 0.45f;
            var color = LayerColor(layer).WithAlpha(alpha);
            const float lift = 0.03f;
            var up = new Vector3(0f, lift, 0f);

            DebugDraw.PushMatrix();
            DebugDraw.Identity();

            switch (layer)
            {
                case RaceLayer.AINodes:
                    {
                        var nodes = data.AINodes.Nodes;
                        for (int i = 0; i < nodes.Count; i++)
                        {
                            var n = nodes[i];
                            var red = RaceData.ToEditor(n.RedEnd.Position) + up;
                            var green = RaceData.ToEditor(n.GreenEnd.Position) + up;
                            bool isSelected = active && i == selected;

                            Line(red, green, (isSelected ? Color.white : Color.Lerp(color, Color.white, 0.3f)).WithAlpha(alpha));
                            Marker(red, RedEnd.WithAlpha(alpha), isSelected && selectedPart != 2 ? 0.09f : 0.05f);
                            Marker(green, GreenEnd.WithAlpha(alpha), isSelected && selectedPart != 1 ? 0.09f : 0.05f);
                            Marker(Vector3.Lerp(green, red, n.RacingLine), RacingLine.WithAlpha(alpha), 0.04f);

                            foreach (int next in n.NextLinkIDs)
                            {
                                if (next < 0 || next >= nodes.Count) continue;
                                Arrow(Center(n) + up, Center(nodes[next]) + up, color);
                            }
                        }
                        break;
                    }
                case RaceLayer.PosNodes:
                    {
                        var nodes = data.PosNodes.Nodes;
                        for (int i = 0; i < nodes.Count; i++)
                        {
                            var p = RaceData.ToEditor(nodes[i].Position) + up;
                            bool isStart = i == data.PosNodes.StartNodeIndex;
                            Marker(p, (active && i == selected ? Color.white : color).WithAlpha(alpha), active && i == selected ? 0.1f : isStart ? 0.08f : 0.06f);
                            foreach (int next in nodes[i].NextNodeIDs)
                            {
                                if (next < 0 || next >= nodes.Count) continue;
                                Arrow(p, RaceData.ToEditor(nodes[next].Position) + up, color);
                            }
                        }
                        break;
                    }
                case RaceLayer.Zones:
                    {
                        var zones = data.Zones.Zones;
                        for (int i = 0; i < zones.Count; i++)
                        {
                            bool isSelected = active && i == selected;
                            Box(zones[i], (isSelected ? Color.white : color).WithAlpha(isSelected ? 1f : alpha * 0.8f));
                        }
                        break;
                    }
                case RaceLayer.Lights:
                    {
                        var lights = data.Lights.Lights;
                        for (int i = 0; i < lights.Count; i++)
                        {
                            var l = lights[i];
                            var p = RaceData.ToEditor(l.Position);
                            bool isSelected = active && i == selected;
                            Marker(p, (isSelected ? Color.white : color).WithAlpha(alpha), isSelected ? 0.12f : 0.07f);
                            if (isSelected)
                            {
                                DebugDraw.Color = color.WithAlpha(0.6f);
                                DebugDraw.DrawCircle(p, l.Reach / RVConstants.SMALL_CUBE_SIZE, 48);
                            }
                        }
                        break;
                    }
                case RaceLayer.Instances:
                    {
                        var instances = data.Instances.Instances;
                        var camera = TrackEditor.Instance.Camera;
                        for (int i = 0; i < instances.Count; i++)
                        {
                            var inst = instances[i];
                            bool isSelected = active && i == selected;
                            var model = ModelFor(data, inst);
                            Mesh mesh = null;
                            if (model.HasValue)
                                mesh = ModelPreviews.Get(model.Value.SourcePath, out _);

                            var matrix = ModelPreviews.EditorMatrix(inst.Position, inst.Matrix);
                            if (mesh != null)
                            {
                                Graphics.DrawMesh(mesh, matrix, ModelPreviews.Material, 0, camera);
                                if (isSelected)
                                    MeshBounds(mesh.bounds, matrix, Color.white);
                            }
                            Marker(RaceData.ToEditor(inst.Position), (isSelected ? Color.white : mesh == null ? Palette.Warning : color).WithAlpha(alpha),
                                   isSelected ? 0.12f : 0.07f);
                        }
                        break;
                    }
                case RaceLayer.ForceFields:
                    {
                        var fields = data.ForceFields.Fields;
                        for (int i = 0; i < fields.Count; i++)
                        {
                            var f = fields[i];
                            bool isSelected = active && i == selected;
                            var c = (isSelected ? Color.white : color).WithAlpha(isSelected ? 1f : alpha * 0.8f);
                            var center = RaceData.ToEditor(f.Position);
                            if (f.Shape == ForceFieldShape.Sphere)
                            {
                                DebugDraw.Color = c;
                                DebugDraw.DrawCircle(center, f.RadiusEnd / RVConstants.SMALL_CUBE_SIZE, 48);
                                DebugDraw.Color = c.WithAlpha(c.a * 0.5f);
                                DebugDraw.DrawCircle(center, f.RadiusStart / RVConstants.SMALL_CUBE_SIZE, 48);
                            }
                            else
                            {
                                Box(new Zone { Position = f.Position, Size = f.Size, Matrix = f.Matrix }, c);
                            }

                            // linear fields push along their (world space) direction
                            if (f.DirectionMode == ForceFieldDirection.Linear && f.Direction.sqrMagnitude > 1e-6f)
                            {
                                var dir = new Vector3(f.Direction.x, -f.Direction.y, f.Direction.z).normalized * 0.4f;
                                Arrow(center, center + dir, c);
                            }
                            Marker(center, c, isSelected ? 0.1f : 0.06f);
                        }
                        break;
                    }
                default:
                    {
                        var objects = data.Objects.Objects;
                        for (int i = 0; i < objects.Count; i++)
                        {
                            bool isSelected = active && i == selected;
                            Diamond(RaceData.ToEditor(objects[i].Position) + up, (isSelected ? Color.white : color).WithAlpha(alpha), isSelected ? 0.14f : 0.09f);
                        }
                        break;
                    }
            }

            DebugDraw.PopMatrix();
        }

        private static void Line(Vector3 a, Vector3 b, Color color)
        {
            DebugDraw.Color = color;
            DebugDraw.Begin(DrawType.LINES);
            DebugDraw.Vertex(a);
            DebugDraw.Vertex(b);
            DebugDraw.End();
        }

        private static void Marker(Vector3 p, Color color, float size)
        {
            DebugDraw.Color = color;
            DebugDraw.Begin(DrawType.LINES);
            DebugDraw.Vertex(p - Vector3.right * size); DebugDraw.Vertex(p + Vector3.right * size);
            DebugDraw.Vertex(p - Vector3.forward * size); DebugDraw.Vertex(p + Vector3.forward * size);
            DebugDraw.Vertex(p - Vector3.up * size); DebugDraw.Vertex(p + Vector3.up * size);
            DebugDraw.End();
            Diamond(p, color, size * 0.7f);
        }

        private static void Diamond(Vector3 p, Color color, float size)
        {
            DebugDraw.Color = color;
            DebugDraw.Begin(DrawType.LINES);
            var n = p + Vector3.forward * size;
            var e = p + Vector3.right * size;
            var s = p - Vector3.forward * size;
            var w = p - Vector3.right * size;
            DebugDraw.Vertex(n); DebugDraw.Vertex(e);
            DebugDraw.Vertex(e); DebugDraw.Vertex(s);
            DebugDraw.Vertex(s); DebugDraw.Vertex(w);
            DebugDraw.Vertex(w); DebugDraw.Vertex(n);
            DebugDraw.End();
        }

        private static void Arrow(Vector3 from, Vector3 to, Color color)
        {
            Line(from, to, color);
            var dir = to - from;
            if (dir.sqrMagnitude < 1e-6f)
                return;
            dir.Normalize();
            var side = Vector3.Cross(dir, Vector3.up).normalized * 0.05f;
            var mid = Vector3.Lerp(from, to, 0.55f);
            var back = mid - dir * 0.08f;
            Line(mid, back + side, color);
            Line(mid, back - side, color);
        }

        private static void MeshBounds(Bounds bounds, Matrix4x4 matrix, Color color)
        {
            var corners = new Vector3[8];
            int k = 0;
            for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                    for (int z = -1; z <= 1; z += 2)
                        corners[k++] = matrix.MultiplyPoint3x4(bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z)));
            BoxEdges(corners, color);
        }

        private static void BoxEdges(Vector3[] corners, Color color)
        {
            int[,] edges = { { 0, 1 }, { 2, 3 }, { 4, 5 }, { 6, 7 }, { 0, 2 }, { 1, 3 }, { 4, 6 }, { 5, 7 }, { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 } };
            DebugDraw.Color = color;
            DebugDraw.Begin(DrawType.LINES);
            for (int i = 0; i < edges.GetLength(0); i++)
            {
                DebugDraw.Vertex(corners[edges[i, 0]]);
                DebugDraw.Vertex(corners[edges[i, 1]]);
            }
            DebugDraw.End();
        }

        private static void Box(Zone zone, Color color)
        {
            var center = RaceData.ToEditor(zone.Position);
            var half = zone.Size / RVConstants.SMALL_CUBE_SIZE;
            var corners = new Vector3[8];
            int k = 0;
            for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                    for (int z = -1; z <= 1; z += 2)
                    {
                        // zone matrices are in game space; flip Y for the editor
                        var local = zone.Matrix.MultiplyVector(new Vector3(half.x * x, half.y * y, half.z * z));
                        corners[k++] = center + new Vector3(local.x, -local.y, local.z);
                    }

            int[,] edges = { { 0, 1 }, { 2, 3 }, { 4, 5 }, { 6, 7 }, { 0, 2 }, { 1, 3 }, { 4, 6 }, { 5, 7 }, { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 } };
            DebugDraw.Color = color;
            DebugDraw.Begin(DrawType.LINES);
            for (int i = 0; i < edges.GetLength(0); i++)
            {
                DebugDraw.Vertex(corners[edges[i, 0]]);
                DebugDraw.Vertex(corners[edges[i, 1]]);
            }
            DebugDraw.End();
        }
    }
}

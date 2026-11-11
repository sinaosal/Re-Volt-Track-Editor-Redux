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
using ReVolt.TrackUnit;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TrackObject = ReVolt.Track.Object;
using Zone = ReVolt.Track.Zone;
using AINode = ReVolt.Track.AINode;
using Instance = ReVolt.Track.Instance;

/// <summary>
/// Race data a track needs in the game besides its geometry: AI nodes, position nodes, track
/// zones, lights and objects. Normally generated at export time; once generated in the editor it
/// can be edited and is exported instead of the generated data.
///
/// Everything is in Re-Volt game units at regular scale (the export scale is applied when
/// writing). Stored as the "race" section of the track's extras sidecar.
/// </summary>
public sealed class RaceData
{
    public const string SectionKey = "race";

    /// <summary>Fingerprint of the layout (modules and pickups) the data was generated from.</summary>
    public string LayoutSignature;

    /// <summary>Use this data when exporting (otherwise the generated data is exported).</summary>
    public bool Enabled = true;

    public AINodesFile AINodes = new AINodesFile();
    public POSNodesFile PosNodes = new POSNodesFile();
    public ZonesFile Zones = new ZonesFile();
    public LightsFile Lights = new LightsFile();
    public ObjectsFile Objects = new ObjectsFile();
    public InstancesFile Instances = new InstancesFile();
    public ForceFieldsFile ForceFields = new ForceFieldsFile();

    /// <summary>A .prm model imported for instances; copied into the level folder on export.</summary>
    public struct ModelFile
    {
        /// <summary>File name in the level folder, e.g. "mytree.prm".</summary>
        public string FileName;
        /// <summary>Where it was imported from.</summary>
        public string SourcePath;

        /// <summary>The name instances use: the base name, which the game matches on its first 8 characters.</summary>
        public string InstanceName
        {
            get
            {
                string name = System.IO.Path.GetFileNameWithoutExtension(FileName ?? string.Empty);
                return name.Length > Instance.MaxNameLength ? name.Substring(0, Instance.MaxNameLength) : name;
            }
        }
    }

    public readonly List<ModelFile> Models = new List<ModelFile>();

    // keys of the race section this version doesn't know (kept unchanged)
    private Dictionary<string, object> unknown = new Dictionary<string, object>();

    // Generation
    /// <summary>
    /// Generates the race data the exporter would write for <paramref name="track"/>. Returns null and a
    /// problem if the layout can't produce it (no start grid, no closed lap, broken racing line).
    /// </summary>
    public static RaceData Generate(EditorTrack track, TrackUnitFile unitFile, out TrackProblem problem)
    {
        var exporter = new TrackExporter(track, unitFile);
        problem = TrackValidator.CheckLayout(exporter);
        if (problem != null)
            return null;

        return new RaceData
        {
            LayoutSignature = Signature(track),
            AINodes = exporter.BuildAINodesFile(),
            PosNodes = exporter.BuildPosNodesFile(),
            Zones = exporter.BuildZonesFile(),
            Lights = exporter.BuildLightsFile(),
            Objects = exporter.BuildObjectsFile()
        };
    }

    /// <summary>
    /// A fingerprint of everything generation depends on: grid size, modules and pickups.
    /// </summary>
    public static string Signature(EditorTrack track)
    {
        var doc = track.ToDocument();
        doc.Name = string.Empty;
        doc.Extras = new TrackExtras();
        byte[] bytes = doc.ToBytes();

        // FNV-1a 64
        ulong hash = 14695981039346656037UL;
        foreach (byte b in bytes)
        {
            hash ^= b;
            hash *= 1099511628211UL;
        }
        return hash.ToString("x16");
    }

    public bool IsStaleFor(EditorTrack track) => LayoutSignature != Signature(track);

    // Storage in the track extras
    public static RaceData FromTrack(EditorTrack track)
    {
        var section = track.Extras?.GetSection(SectionKey);
        return section == null ? null : FromJson(section);
    }

    public void StoreInTrack(EditorTrack track)
    {
        track.Extras.SetSection(SectionKey, ToJson());
    }

    public static void RemoveFromTrack(EditorTrack track)
    {
        track.Extras.SetSection(SectionKey, null);
    }

    public RaceData Clone() => FromJson(ToJson());

    // JSON
    public object ToJson()
    {
        var root = new Dictionary<string, object>
        {
            ["layout"] = LayoutSignature ?? string.Empty,
            ["enabled"] = Enabled,
            ["aiNodes"] = AINodesToJson(AINodes),
            ["posNodes"] = PosNodesToJson(PosNodes),
            ["zones"] = Zones.Zones.Select(ZoneToJson).Cast<object>().ToList(),
            ["lights"] = Lights.Lights.Select(LightToJson).Cast<object>().ToList(),
            ["objects"] = Objects.Objects.Select(ObjectToJson).Cast<object>().ToList(),
            ["instances"] = Instances.Instances.Select(InstanceToJson).Cast<object>().ToList(),
            ["forceFields"] = ForceFields.Fields.Select(FieldToJson).Cast<object>().ToList(),
            ["models"] = Models.Select(m => (object)new Dictionary<string, object> { ["file"] = m.FileName ?? string.Empty, ["source"] = m.SourcePath ?? string.Empty }).ToList()
        };
        foreach (var pair in unknown)
            root[pair.Key] = pair.Value;
        return root;
    }

    public static RaceData FromJson(object json)
    {
        var root = MiniJson.AsObject(json) ?? throw new FormatException("The race data section isn't an object.");
        var data = new RaceData
        {
            LayoutSignature = MiniJson.GetString(root, "layout", string.Empty),
            Enabled = MiniJson.GetBool(root, "enabled", true)
        };

        foreach (var pair in root)
        {
            switch (pair.Key)
            {
                case "layout":
                case "enabled":
                    break;
                case "aiNodes":
                    data.AINodes = AINodesFromJson(MiniJson.AsObject(pair.Value));
                    break;
                case "posNodes":
                    data.PosNodes = PosNodesFromJson(MiniJson.AsObject(pair.Value));
                    break;
                case "zones":
                    foreach (var item in MiniJson.AsArray(pair.Value) ?? new List<object>())
                        data.Zones.Zones.Add(ZoneFromJson(MiniJson.AsObject(item)));
                    break;
                case "lights":
                    foreach (var item in MiniJson.AsArray(pair.Value) ?? new List<object>())
                        data.Lights.Lights.Add(LightFromJson(MiniJson.AsObject(item)));
                    break;
                case "objects":
                    foreach (var item in MiniJson.AsArray(pair.Value) ?? new List<object>())
                        data.Objects.Objects.Add(ObjectFromJson(MiniJson.AsObject(item)));
                    break;
                case "instances":
                    foreach (var item in MiniJson.AsArray(pair.Value) ?? new List<object>())
                        data.Instances.Instances.Add(InstanceFromJson(MiniJson.AsObject(item)));
                    break;
                case "forceFields":
                    foreach (var item in MiniJson.AsArray(pair.Value) ?? new List<object>())
                        data.ForceFields.Fields.Add(FieldFromJson(MiniJson.AsObject(item)));
                    break;
                case "models":
                    foreach (var item in MiniJson.AsArray(pair.Value) ?? new List<object>())
                    {
                        var m = MiniJson.AsObject(item);
                        data.Models.Add(new ModelFile { FileName = MiniJson.GetString(m, "file", string.Empty), SourcePath = MiniJson.GetString(m, "source", string.Empty) });
                    }
                    break;
                default:
                    data.unknown[pair.Key] = pair.Value;
                    break;
            }
        }
        return data;
    }

    // value helpers
    private static List<object> Vec(Vector3 v) => new List<object> { (double)v.x, (double)v.y, (double)v.z };

    private static Vector3 Vec(object json)
    {
        var list = MiniJson.AsArray(json);
        if (list == null || list.Count < 3)
            return Vector3.zero;
        return new Vector3(Num(list[0]), Num(list[1]), Num(list[2]));
    }

    private static float Num(object value) => value is double d ? (float)d : 0f;

    private static List<object> Mat(Matrix4x4 m)
    {
        return new List<object>
        {
            (double)m.m00, (double)m.m01, (double)m.m02,
            (double)m.m10, (double)m.m11, (double)m.m12,
            (double)m.m20, (double)m.m21, (double)m.m22
        };
    }

    private static Matrix4x4 Mat(object json)
    {
        var list = MiniJson.AsArray(json);
        var m = Matrix4x4.identity;
        if (list == null || list.Count < 9)
            return m;
        m.m00 = Num(list[0]); m.m01 = Num(list[1]); m.m02 = Num(list[2]);
        m.m10 = Num(list[3]); m.m11 = Num(list[4]); m.m12 = Num(list[5]);
        m.m20 = Num(list[6]); m.m21 = Num(list[7]); m.m22 = Num(list[8]);
        return m;
    }

    private static List<object> Ints(int[] values) => (values ?? new int[0]).Select(v => (object)v).ToList();

    private static int[] Ints(object json, int length)
    {
        var result = Enumerable.Repeat(-1, length).ToArray();
        var list = MiniJson.AsArray(json);
        if (list != null)
        {
            for (int i = 0; i < Math.Min(length, list.Count); i++)
                result[i] = list[i] is double d ? (int)Math.Round(d) : -1;
        }
        return result;
    }

    // AI nodes
    private static object AINodesToJson(AINodesFile file)
    {
        return new Dictionary<string, object>
        {
            ["startNode"] = file.StartNode,
            ["totalDistance"] = (double)file.TotalDistance,
            ["nodes"] = file.Nodes.Select(n => (object)new Dictionary<string, object>
            {
                ["priority"] = (int)n.Priority,
                ["startNode"] = (int)n.StartNode,
                ["flags"] = (int)n.Flags,
                ["racingLine"] = (double)n.RacingLine,
                ["finishDist"] = (double)n.FinishDist,
                ["overtakingLine"] = (double)n.OvertakingLine,
                ["racingLineSpeed"] = n.RacingLineSpeed,
                ["centerSpeed"] = n.CenterSpeed,
                ["prev"] = Ints(n.PreviousLinkIDs),
                ["next"] = Ints(n.NextLinkIDs),
                ["red"] = new Dictionary<string, object> { ["speed"] = n.RedEnd.Speed, ["pos"] = Vec(n.RedEnd.Position) },
                ["green"] = new Dictionary<string, object> { ["speed"] = n.GreenEnd.Speed, ["pos"] = Vec(n.GreenEnd.Position) }
            }).ToList()
        };
    }

    private static AINodesFile AINodesFromJson(Dictionary<string, object> json)
    {
        var file = new AINodesFile
        {
            StartNode = MiniJson.GetInt(json, "startNode"),
            TotalDistance = (float)MiniJson.GetNumber(json, "totalDistance")
        };
        foreach (var item in MiniJson.AsArray(json?["nodes"]) ?? new List<object>())
        {
            var n = MiniJson.AsObject(item);
            var red = MiniJson.AsObject(n.TryGetValue("red", out var r) ? r : null);
            var green = MiniJson.AsObject(n.TryGetValue("green", out var g) ? g : null);
            file.Nodes.Add(new AINode
            {
                Priority = (AINodePriority)MiniJson.GetInt(n, "priority"),
                StartNode = (byte)MiniJson.GetInt(n, "startNode"),
                Flags = (short)MiniJson.GetInt(n, "flags"),
                RacingLine = (float)MiniJson.GetNumber(n, "racingLine", 0.5),
                FinishDist = (float)MiniJson.GetNumber(n, "finishDist"),
                OvertakingLine = (float)MiniJson.GetNumber(n, "overtakingLine", 0.5),
                RacingLineSpeed = MiniJson.GetInt(n, "racingLineSpeed", 30),
                CenterSpeed = MiniJson.GetInt(n, "centerSpeed", 30),
                PreviousLinkIDs = Ints(n.TryGetValue("prev", out var p) ? p : null, 2),
                NextLinkIDs = Ints(n.TryGetValue("next", out var x) ? x : null, 2),
                RedEnd = new AINodeEnd { Speed = MiniJson.GetInt(red, "speed", 30), Position = Vec(red != null && red.TryGetValue("pos", out var rp) ? rp : null) },
                GreenEnd = new AINodeEnd { Speed = MiniJson.GetInt(green, "speed", 30), Position = Vec(green != null && green.TryGetValue("pos", out var gp) ? gp : null) }
            });
        }
        return file;
    }

    // Position nodes
    private static object PosNodesToJson(POSNodesFile file)
    {
        return new Dictionary<string, object>
        {
            ["startNode"] = file.StartNodeIndex,
            ["totalLength"] = (double)file.TotalLength,
            ["nodes"] = file.Nodes.Select(n => (object)new Dictionary<string, object>
            {
                ["pos"] = Vec(n.Position),
                ["distance"] = (double)n.Distance,
                ["prev"] = Ints(n.PreviousNodeIDs),
                ["next"] = Ints(n.NextNodeIDs)
            }).ToList()
        };
    }

    private static POSNodesFile PosNodesFromJson(Dictionary<string, object> json)
    {
        var file = new POSNodesFile
        {
            StartNodeIndex = MiniJson.GetInt(json, "startNode"),
            TotalLength = (float)MiniJson.GetNumber(json, "totalLength")
        };
        foreach (var item in MiniJson.AsArray(json?["nodes"]) ?? new List<object>())
        {
            var n = MiniJson.AsObject(item);
            file.Nodes.Add(new POSNode
            {
                Position = Vec(n.TryGetValue("pos", out var p) ? p : null),
                Distance = (float)MiniJson.GetNumber(n, "distance"),
                PreviousNodeIDs = Ints(n.TryGetValue("prev", out var pr) ? pr : null, 4),
                NextNodeIDs = Ints(n.TryGetValue("next", out var nx) ? nx : null, 4)
            });
        }
        return file;
    }

    // Zones
    private static Dictionary<string, object> ZoneToJson(Zone z)
    {
        return new Dictionary<string, object>
        {
            ["id"] = z.ID,
            ["pos"] = Vec(z.Position),
            ["size"] = Vec(z.Size),
            ["matrix"] = Mat(z.Matrix)
        };
    }

    private static Zone ZoneFromJson(Dictionary<string, object> json)
    {
        return new Zone
        {
            ID = MiniJson.GetInt(json, "id"),
            Position = Vec(json.TryGetValue("pos", out var p) ? p : null),
            Size = Vec(json.TryGetValue("size", out var s) ? s : null),
            Matrix = Mat(json.TryGetValue("matrix", out var m) ? m : null)
        };
    }

    // Lights
    private static Dictionary<string, object> LightToJson(ReVolt.Track.Light l)
    {
        return new Dictionary<string, object>
        {
            ["pos"] = Vec(l.Position),
            ["reach"] = (double)l.Reach,
            ["matrix"] = Mat(l.DirectionMatrix),
            ["cone"] = (double)l.Cone,
            ["color"] = new List<object> { (double)l.Color.r, (double)l.Color.g, (double)l.Color.b },
            ["flags"] = (int)l.Flags,
            ["type"] = (int)l.Type,
            ["speed"] = (int)l.Speed
        };
    }

    private static ReVolt.Track.Light LightFromJson(Dictionary<string, object> json)
    {
        var c = Vec(json.TryGetValue("color", out var col) ? col : null);
        return new ReVolt.Track.Light
        {
            Position = Vec(json.TryGetValue("pos", out var p) ? p : null),
            Reach = (float)MiniJson.GetNumber(json, "reach", 500),
            DirectionMatrix = Mat(json.TryGetValue("matrix", out var m) ? m : null),
            Cone = (float)MiniJson.GetNumber(json, "cone"),
            Color = new Color(c.x, c.y, c.z, 1f),
            Flags = (LightFlags)MiniJson.GetInt(json, "flags"),
            Type = (byte)MiniJson.GetInt(json, "type"),
            Speed = (byte)MiniJson.GetInt(json, "speed")
        };
    }

    // Objects
    private static Dictionary<string, object> ObjectToJson(TrackObject o)
    {
        return new Dictionary<string, object>
        {
            ["type"] = (int)o.Type,
            ["flags"] = new List<object> { o.Flags1, o.Flags2, o.Flags3, o.Flags4 },
            ["pos"] = Vec(o.Position),
            ["matrix"] = Mat(o.DirectionMatrix)
        };
    }

    private static TrackObject ObjectFromJson(Dictionary<string, object> json)
    {
        var flags = Ints(json.TryGetValue("flags", out var f) ? f : null, 4).Select(v => v < 0 ? 0 : v).ToArray();
        return new TrackObject
        {
            Type = (ObjectType)MiniJson.GetInt(json, "type"),
            Flags1 = flags[0],
            Flags2 = flags[1],
            Flags3 = flags[2],
            Flags4 = flags[3],
            Position = Vec(json.TryGetValue("pos", out var p) ? p : null),
            DirectionMatrix = Mat(json.TryGetValue("matrix", out var m) ? m : null)
        };
    }

    // Instances
    private static Dictionary<string, object> InstanceToJson(Instance i)
    {
        return new Dictionary<string, object>
        {
            ["name"] = i.Name ?? string.Empty,
            ["tint"] = new List<object> { (int)i.TintR, (int)i.TintG, (int)i.TintB },
            ["env"] = SupportStyle.ToHex(i.EnvColor),
            ["priority"] = (int)i.Priority,
            ["flags"] = (int)i.Flags,
            ["lodBias"] = (double)i.LodBias,
            ["pos"] = Vec(i.Position),
            ["matrix"] = Mat(i.Matrix)
        };
    }

    private static Instance InstanceFromJson(Dictionary<string, object> json)
    {
        var tint = Ints(json.TryGetValue("tint", out var t) ? t : null, 3);
        SupportStyle.TryParseHex(MiniJson.GetString(json, "env", "#808080"), out uint env);
        return new Instance
        {
            Name = MiniJson.GetString(json, "name", string.Empty),
            TintR = (sbyte)Mathf.Clamp(tint[0] < -128 ? 0 : tint[0], -128, 127),
            TintG = (sbyte)Mathf.Clamp(tint[1] < -128 ? 0 : tint[1], -128, 127),
            TintB = (sbyte)Mathf.Clamp(tint[2] < -128 ? 0 : tint[2], -128, 127),
            EnvColor = env,
            Priority = (byte)MiniJson.GetInt(json, "priority"),
            Flags = (InstanceFlags)MiniJson.GetInt(json, "flags", (int)InstanceFlags.SetModelRgb),
            LodBias = (float)MiniJson.GetNumber(json, "lodBias", 1024),
            Position = Vec(json.TryGetValue("pos", out var p) ? p : null),
            Matrix = Mat(json.TryGetValue("matrix", out var m) ? m : null)
        };
    }

    // Force fields
    private static Dictionary<string, object> FieldToJson(ForceField f)
    {
        return new Dictionary<string, object>
        {
            ["type"] = f.PackedType,
            ["pos"] = Vec(f.Position),
            ["matrix"] = Mat(f.Matrix),
            ["size"] = Vec(f.Size),
            ["dir"] = Vec(f.Direction),
            ["mag"] = (double)f.Magnitude,
            ["damping"] = (double)f.Damping,
            ["radius"] = new List<object> { (double)f.RadiusStart, (double)f.RadiusEnd },
            ["gradient"] = new List<object> { (double)f.MagnitudeStart, (double)f.MagnitudeEnd }
        };
    }

    private static ForceField FieldFromJson(Dictionary<string, object> json)
    {
        var radius = MiniJson.AsArray(json.TryGetValue("radius", out var r) ? r : null);
        var gradient = MiniJson.AsArray(json.TryGetValue("gradient", out var g) ? g : null);
        var field = new ForceField
        {
            Position = Vec(json.TryGetValue("pos", out var p) ? p : null),
            Matrix = Mat(json.TryGetValue("matrix", out var m) ? m : null),
            Size = Vec(json.TryGetValue("size", out var s) ? s : null),
            Direction = Vec(json.TryGetValue("dir", out var d) ? d : null),
            Magnitude = (float)MiniJson.GetNumber(json, "mag"),
            Damping = (float)MiniJson.GetNumber(json, "damping"),
            RadiusStart = radius != null && radius.Count > 0 ? Num(radius[0]) : 256f,
            RadiusEnd = radius != null && radius.Count > 1 ? Num(radius[1]) : 512f,
            MagnitudeStart = gradient != null && gradient.Count > 0 ? Num(gradient[0]) : 0f,
            MagnitudeEnd = gradient != null && gradient.Count > 1 ? Num(gradient[1]) : 0f
        };
        field.Unpack(MiniJson.GetInt(json, "type"));
        return field;
    }

    // Deep copies for the exporter (Scale() modifies files in place)
    public static AINodesFile CloneAINodes(AINodesFile file) => AINodesFromJson((Dictionary<string, object>)AINodesToJson(file));
    public static POSNodesFile ClonePosNodes(POSNodesFile file) => PosNodesFromJson((Dictionary<string, object>)PosNodesToJson(file));

    public static ZonesFile CloneZones(ZonesFile file)
    {
        var clone = new ZonesFile();
        clone.Zones.AddRange(file.Zones);
        return clone;
    }

    public static LightsFile CloneLights(LightsFile file)
    {
        var clone = new LightsFile();
        clone.Lights.AddRange(file.Lights);
        return clone;
    }

    public static ObjectsFile CloneObjects(ObjectsFile file)
    {
        var clone = new ObjectsFile();
        clone.Objects.AddRange(file.Objects);
        return clone;
    }

    public static InstancesFile CloneInstances(InstancesFile file)
    {
        var clone = new InstancesFile();
        clone.Instances.AddRange(file.Instances);
        return clone;
    }

    public static ForceFieldsFile CloneForceFields(ForceFieldsFile file)
    {
        var clone = new ForceFieldsFile();
        clone.Fields.AddRange(file.Fields);
        return clone;
    }

    // Coordinates
    /// <summary>Game units (Y down) to editor world units (one unit per grid cell, Y up).</summary>
    public static Vector3 ToEditor(Vector3 game) => new Vector3(game.x, -game.y, game.z) / RVConstants.SMALL_CUBE_SIZE;

    /// <summary>Editor world units to game units.</summary>
    public static Vector3 ToGame(Vector3 editor) => new Vector3(editor.x, -editor.y, editor.z) * RVConstants.SMALL_CUBE_SIZE;
}

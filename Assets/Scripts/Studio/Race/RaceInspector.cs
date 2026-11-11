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
using Studio.Icons;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using AINode = ReVolt.Track.AINode;
using Instance = ReVolt.Track.Instance;
using TrackObject = ReVolt.Track.Object;
using Zone = ReVolt.Track.Zone;

namespace Studio
{
    /// <summary>
    /// Property editor for the selected race data element. Values are shown in game units
    /// (as in the game's own editor; note that Y points down).
    /// </summary>
    public sealed class RaceInspector
    {
        private readonly StudioShell shell;
        private Rect area;
        private float y;
        private float lastHeight = 600f;

        private StudioTheme T => shell.Theme;
        private RaceEditor Race => shell.Race;

        private static readonly string[] PriorityNames =
        {
            "Racing line", "Pickup route", "Stairs", "Bumpy", "Slow down 25 mph", "Soft suspension", "Jump wall",
            "Title screen slow down", "Turbo line", "Long pickup route", "Shortcut", "Long cut", "Barrel block",
            "Off throttle", "Petrol throttle", "Wilderness", "Slow down 15 mph", "Slow down 20 mph", "Slow down 30 mph"
        };

        private static readonly string[] LightTypes = { "Omni", "Omni normal", "Spot", "Spot normal", "Shadow box" };

        public RaceInspector(StudioShell shell)
        {
            this.shell = shell;
        }

        public float ContentHeight => Mathf.Max(T.S(300f), lastHeight + T.S(40f));

        public void Draw(Rect r)
        {
            area = r;
            y = r.y;

            if (sliderGroupOpen && GUIUtility.hotControl == 0)
            {
                sliderGroupOpen = false;
                TrackEditor.EndChangeGroup();
            }

            var data = Race.Data;
            if (data == null)
            {
                Empty(IconId.Track, "No race data", "Generate race data in the panel on the left to edit it here.");
                return;
            }

            int count = RaceLayers.Count(data, Race.Layer);
            int index = Race.Selected;
            if (index < 0 || index >= count)
            {
                Empty(RaceLayers.Icon(Race.Layer), $"No {RaceLayers.Singular(Race.Layer)} selected",
                      $"Click one in the viewport, or add one. {count} in this track.");
                if (SGUI.Button(new Rect(r.x, y, r.width, T.ControlHeight), $"Add {RaceLayers.Singular(Race.Layer)}", ButtonKind.Secondary, true, IconId.Plus))
                    Race.AddElement();
                lastHeight = y + T.ControlHeight - r.y;
                return;
            }

            // header
            string title = $"{char.ToUpperInvariant(RaceLayers.Singular(Race.Layer)[0])}{RaceLayers.Singular(Race.Layer).Substring(1)} {index}";
            SGUI.Label(new Rect(r.x, y, r.width, T.S(22f)), title, T.Heading);
            y += T.S(30f);

            float third = (r.width - T.S(16f)) / 3f;
            if (SGUI.Button(new Rect(r.x, y, third, T.ControlHeight), "Add", ButtonKind.Secondary, true, IconId.Plus,
                            Race.Layer == RaceLayer.AINodes || Race.Layer == RaceLayer.PosNodes ? "Insert after this one  Ins" : "Add another  Ins"))
                Race.AddElement(RaceLayers.Position(data, Race.Layer, index) + new Vector3(0.25f, 0f, 0.25f));
            if (SGUI.Button(new Rect(r.x + third + T.S(8f), y, third, T.ControlHeight), "Focus", ButtonKind.Secondary, true, IconId.Frame))
                shell.Viewport.FocusPoint(RaceLayers.Position(data, Race.Layer, index));
            if (SGUI.Button(new Rect(r.x + (third + T.S(8f)) * 2f, y, third, T.ControlHeight), "Delete", ButtonKind.Secondary, true, IconId.Trash, "Del"))
            {
                Race.DeleteSelected();
                return;
            }
            y += T.ControlHeight + T.S(18f);

            switch (Race.Layer)
            {
                case RaceLayer.AINodes: DrawAINode(data, index); break;
                case RaceLayer.PosNodes: DrawPosNode(data, index); break;
                case RaceLayer.Zones: DrawZone(data, index); break;
                case RaceLayer.Lights: DrawLight(data, index); break;
                case RaceLayer.Objects: DrawObject(data, index); break;
                case RaceLayer.Instances: DrawInstance(data, index); break;
                case RaceLayer.ForceFields: DrawField(data, index); break;
            }

            lastHeight = y - r.y;
        }

        private void Empty(IconId icon, string title, string text)
        {
            SGUI.Icon(new Rect(area.x, y + T.S(20f), area.width, T.S(28f)), icon, Palette.TextFaint, T.S(26f));
            SGUI.Label(new Rect(area.x, y + T.S(54f), area.width, T.S(20f)), title, T.BodyStrong, Palette.TextDim, TextAnchor.MiddleCenter);
            SGUI.Label(new Rect(area.x, y + T.S(78f), area.width, T.S(44f)), text, T.SmallWrapped, Palette.TextFaint, TextAnchor.UpperCenter);
            y += T.S(132f);
        }

        // Row helpers
        private void Section(string title)
        {
            y += T.S(6f);
            SGUI.SectionHeader(new Rect(area.x, y, area.width, T.S(16f)), title);
            y += T.S(22f);
        }

        private Rect Row(string label, float height = -1f)
        {
            if (height < 0f)
                height = T.S(30f);
            var row = new Rect(area.x, y, area.width, height);
            y += height + T.S(6f);
            return SGUI.LabeledRow(row, label, T.S(104f));
        }

        private void Note(string text)
        {
            float h = SGUI.WrappedHeight(text, T.SmallWrapped, area.width);
            SGUI.Label(new Rect(area.x, y, area.width, h), text, T.SmallWrapped, Palette.TextFaint);
            y += h + T.S(8f);
        }

        private string Key(string field) => $"race.{Race.Layer}.{Race.Selected}.{field}";

        private bool Float(string label, float value, string field, out float result)
        {
            return SGUI.FloatField(Row(label), Key(field), value, out result);
        }

        private bool Int(string label, int value, string field, out int result)
        {
            return SGUI.IntField(Row(label), Key(field), value, out result);
        }

        private bool Vec3(string label, Vector3 value, string field, out Vector3 result)
        {
            result = value;
            var r = Row(label);
            float w = (r.width - T.S(8f)) / 3f;
            bool changed = false;
            if (SGUI.FloatField(new Rect(r.x, r.y, w, r.height), Key(field + ".x"), value.x, out float x)) { result.x = x; changed = true; }
            if (SGUI.FloatField(new Rect(r.x + w + T.S(4f), r.y, w, r.height), Key(field + ".y"), value.y, out float yy)) { result.y = yy; changed = true; }
            if (SGUI.FloatField(new Rect(r.x + (w + T.S(4f)) * 2f, r.y, w, r.height), Key(field + ".z"), value.z, out float z)) { result.z = z; changed = true; }
            return changed;
        }

        private float Slider(string label, float value, float min, float max)
        {
            var r = Row(label);
            float result = SGUI.Slider(r.CutRight(T.S(50f)), value, min, max);
            SGUI.Label(r.TakeRight(T.S(44f)), result.ToString("0.00"), T.Small, Palette.TextDim, TextAnchor.MiddleRight);
            return result;
        }

        private bool sliderGroupOpen;

        /// <summary>
        /// A slider whose whole drag becomes one undo step: the first change opens a history group,
        /// releasing the mouse closes it (see the check at the start of <see cref="Draw"/>).
        /// </summary>
        private void SliderEdit(string label, string undoLabel, float value, float min, float max, Action<RaceData, float> apply)
        {
            float v = Slider(label, value, min, max);
            if (Mathf.Approximately(v, value))
                return;

            if (!sliderGroupOpen && GUIUtility.hotControl != 0 && TrackEditor.History != null)
            {
                TrackEditor.History.BeginGroup(undoLabel);
                sliderGroupOpen = true;
            }
            Race.Commit(undoLabel, d => apply(d, v));
        }

        private bool Toggle(string label, bool value, out bool result)
        {
            var r = new Rect(area.x, y, area.width, T.S(24f));
            y += T.S(28f);
            result = SGUI.Toggle(r, value, label);
            return result != value;
        }

        /// <summary>A dropdown button; the chosen option arrives later through <paramref name="choose"/>.</summary>
        private void Dropdown(string label, string current, IList<string> options, Action<int> choose)
        {
            var r = Row(label);
            if (SGUI.Button(r, string.Empty, ButtonKind.Secondary))
            {
                var items = new List<MenuItem>();
                for (int i = 0; i < options.Count; i++)
                {
                    int index = i;
                    if (string.IsNullOrEmpty(options[i]))
                        continue;
                    items.Add(new MenuItem { Label = options[i], Checked = options[i] == current, IsCheckable = true, Action = () => choose(index) });
                }
                var anchor = SGUI.LocalToTopLevel(new Vector2(r.x, r.yMax + T.S(2f)));
                shell.OpenMenu(items, anchor, -1, r.width);
            }
            SGUI.Label(r.Inset(T.S(10f), 0f), current, T.Body, Palette.Text);
            SGUI.Icon(r.TakeRight(T.S(26f)), IconId.ChevronDown, Palette.TextDim, T.S(12f));
        }

        private static float Yaw(Matrix4x4 m)
        {
            var look = m.GetColumn(2);
            return Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg;
        }

        private static Matrix4x4 FromYaw(float degrees) => Matrix4x4.Rotate(Quaternion.Euler(0f, degrees, 0f));

        // AI nodes
        private void DrawAINode(RaceData data, int i)
        {
            var n = data.AINodes.Nodes[i];

            Section("Route");
            int priority = (int)n.Priority;
            Dropdown("Priority", priority >= 0 && priority < PriorityNames.Length ? PriorityNames[priority] : $"Unknown ({priority})", PriorityNames,
                     choice => Race.Commit("Change AI priority", d => Edit(d, i, (ref AINode x) => x.Priority = (AINodePriority)choice)));
            Note("The priority applies to the link leading into this node (used when the AI picks between two routes).");

            SliderEdit("Racing line", "Change racing line", n.RacingLine, 0f, 1f,
                       (d, v) => Edit(d, i, (ref AINode x) => x.RacingLine = v));
            SliderEdit("Overtaking line", "Change overtaking line", n.OvertakingLine, 0f, 1f,
                       (d, v) => Edit(d, i, (ref AINode x) => x.OvertakingLine = v));
            Note("0 is the green end, 1 the red end (the yellow marker shows the racing line).");

            Section("Walls");
            if (Toggle("Wall on the green (left) side", (n.Flags & 1) != 0, out bool left))
                Race.Commit("Change AI wall", d => Edit(d, i, (ref AINode x) => x.Flags = (short)(left ? x.Flags | 1 : x.Flags & ~1)));
            if (Toggle("Wall on the red (right) side", (n.Flags & 2) != 0, out bool right))
                Race.Commit("Change AI wall", d => Edit(d, i, (ref AINode x) => x.Flags = (short)(right ? x.Flags | 2 : x.Flags & ~2)));

            Section("Ends (game units)");
            if (Vec3("Red end", n.RedEnd.Position, "red", out var red))
                Race.Commit("Move AI node end", d => Edit(d, i, (ref AINode x) => x.RedEnd.Position = red));
            if (Vec3("Green end", n.GreenEnd.Position, "green", out var green))
                Race.Commit("Move AI node end", d => Edit(d, i, (ref AINode x) => x.GreenEnd.Position = green));

            Section("Links");
            LinkRow("Next", n.NextLinkIDs, data.AINodes.Nodes.Count, "next", links => Race.Commit("Change AI links", d => Edit(d, i, (ref AINode x) => x.NextLinkIDs = links)));
            LinkRow("Previous", n.PreviousLinkIDs, data.AINodes.Nodes.Count, "prev", links => Race.Commit("Change AI links", d => Edit(d, i, (ref AINode x) => x.PreviousLinkIDs = links)));
            Note("Node numbers, -1 for none. A second next link creates a branch.");
        }

        private delegate void Mutate<T>(ref T value);

        private static void Edit(RaceData d, int i, Mutate<AINode> mutate)
        {
            var n = d.AINodes.Nodes[i];
            n.PreviousLinkIDs = (int[])n.PreviousLinkIDs.Clone();
            n.NextLinkIDs = (int[])n.NextLinkIDs.Clone();
            mutate(ref n);
            d.AINodes.Nodes[i] = n;
        }

        private void LinkRow(string label, int[] links, int count, string field, Action<int[]> commit)
        {
            var r = Row(label);
            float w = (r.width - T.S(4f) * (links.Length - 1)) / links.Length;
            for (int k = 0; k < links.Length; k++)
            {
                var cell = new Rect(r.x + (w + T.S(4f)) * k, r.y, w, r.height);
                if (SGUI.IntField(cell, Key($"{field}{k}"), links[k], out int value))
                {
                    if (value < -1 || value >= count)
                    {
                        TrackEditor.PlaySound(TrackEditor.SndWarning);
                        shell.Toast($"Links must be -1 or 0..{count - 1}", IconId.Warning);
                        continue;
                    }
                    var copy = (int[])links.Clone();
                    copy[k] = value;
                    commit(copy);
                }
            }
        }

        // Position nodes
        private void DrawPosNode(RaceData data, int i)
        {
            var n = data.PosNodes.Nodes[i];
            Section("Position (game units)");
            if (Vec3("Position", n.Position, "pos", out var pos))
                Race.Commit("Move position node", d =>
                {
                    var x = d.PosNodes.Nodes[i];
                    x.Position = pos;
                    d.PosNodes.Nodes[i] = x;
                    RaceLayers.RecalculatePosDistances(d.PosNodes);
                });

            var info = Row("To finish");
            SGUI.Label(info, $"{n.Distance:0} units  ({n.Distance / 200f:0.0} m)", T.Body, Palette.TextDim);

            bool isStart = data.PosNodes.StartNodeIndex == i;
            if (Toggle("Start / finish node", isStart, out bool start) && start)
                Race.Commit("Set start node", d =>
                {
                    d.PosNodes.StartNodeIndex = i;
                    RaceLayers.RecalculatePosDistances(d.PosNodes);
                });

            Section("Links");
            LinkRow("Next", n.NextNodeIDs, data.PosNodes.Nodes.Count, "next", links => Race.Commit("Change position links", d =>
            {
                var x = d.PosNodes.Nodes[i];
                x.NextNodeIDs = links;
                d.PosNodes.Nodes[i] = x;
                RaceLayers.RecalculatePosDistances(d.PosNodes);
            }));
            LinkRow("Previous", n.PreviousNodeIDs, data.PosNodes.Nodes.Count, "prev", links => Race.Commit("Change position links", d =>
            {
                var x = d.PosNodes.Nodes[i];
                x.PreviousNodeIDs = links;
                d.PosNodes.Nodes[i] = x;
            }));

            var lap = Row("Lap length");
            SGUI.Label(lap, $"{data.PosNodes.TotalLength:0} units  ({data.PosNodes.TotalLength / 200f:0} m)", T.Body, Palette.TextDim);
            Note("Distances to the finish are recalculated along the lap whenever nodes change.");
        }

        // Zones
        private void DrawZone(RaceData data, int i)
        {
            var z = data.Zones.Zones[i];
            Section("Zone");
            if (Int("Order (ID)", z.ID, "id", out int id))
                Race.Commit("Change zone order", d => { var x = d.Zones.Zones[i]; x.ID = Mathf.Max(0, id); d.Zones.Zones[i] = x; });
            Note("Cars must pass zones in ID order (0, 1, 2, …) to count laps. Several boxes can share an ID.");

            Section("Box (game units)");
            if (Vec3("Centre", z.Position, "pos", out var pos))
                Race.Commit("Move zone", d => { var x = d.Zones.Zones[i]; x.Position = pos; d.Zones.Zones[i] = x; });
            if (Vec3("Half size", z.Size, "size", out var size))
                Race.Commit("Resize zone", d => { var x = d.Zones.Zones[i]; x.Size = Vector3.Max(size, Vector3.one * 32f); d.Zones.Zones[i] = x; });
            if (Float("Rotation (°)", Yaw(z.Matrix), "yaw", out float yaw))
                Race.Commit("Rotate zone", d => { var x = d.Zones.Zones[i]; x.Matrix = FromYaw(yaw); d.Zones.Zones[i] = x; });
        }

        // Lights
        private void DrawLight(RaceData data, int i)
        {
            var l = data.Lights.Lights[i];
            void EditLight(string label, Mutate<ReVolt.Track.Light> mutate) =>
                Race.Commit(label, d => { var x = d.Lights.Lights[i]; mutate(ref x); d.Lights.Lights[i] = x; });

            Section("Light");
            Dropdown("Type", l.Type < LightTypes.Length ? LightTypes[l.Type] : $"Type {l.Type}", LightTypes,
                     choice => EditLight("Change light type", (ref ReVolt.Track.Light x) => x.Type = (byte)choice));
            if (Vec3("Position", l.Position, "pos", out var pos))
                EditLight("Move light", (ref ReVolt.Track.Light x) => x.Position = pos);

            if (l.Type == 4)
            {
                if (Vec3("Box half size", new Vector3(l.Color.r, l.Color.g, l.Color.b), "box", out var box))
                    EditLight("Resize shadow box", (ref ReVolt.Track.Light x) =>
                    {
                        box = Vector3.Max(box, Vector3.one * 32f);
                        x.Color = new Color(box.x, box.y, box.z, 1f);
                        x.Reach = box.magnitude;
                    });
                Note("Shadow boxes darken cars and objects inside them; the amount comes from the level settings.");
                return;
            }

            if (Float("Reach", l.Reach, "reach", out float reach))
                EditLight("Change light reach", (ref ReVolt.Track.Light x) => x.Reach = Mathf.Max(1f, reach));
            if (l.Type == 2 || l.Type == 3)
            {
                if (Float("Cone", l.Cone, "cone", out float cone))
                    EditLight("Change light cone", (ref ReVolt.Track.Light x) => x.Cone = Mathf.Clamp(cone, 1f, 180f));
                Note("1–180. Not an angle: 90 lights about 60°, 180 a hemisphere.");
            }

            Section("Colour");
            var swatch = Row("Preview", T.S(24f));
            SGUI.RoundedOutline(swatch.TakeLeft(T.S(60f)), new Color(Mathf.Clamp01(l.Color.r / 255f), Mathf.Clamp01(l.Color.g / 255f), Mathf.Clamp01(l.Color.b / 255f)),
                                Palette.BorderStrong, T.S(4f));
            if (Vec3("Red Green Blue", new Vector3(l.Color.r, l.Color.g, l.Color.b), "rgb", out var rgb))
                EditLight("Change light colour", (ref ReVolt.Track.Light x) =>
                    x.Color = new Color(Mathf.Clamp(rgb.x, -1024f, 1024f), Mathf.Clamp(rgb.y, -1024f, 1024f), Mathf.Clamp(rgb.z, -1024f, 1024f), 1f));
            Note("0–255 per channel, more for brighter; negative values darken.");

            Section("Behaviour");
            if (Toggle("Light the track", (l.Flags & LightFlags.Fixed) != 0, out bool world))
                EditLight("Change light", (ref ReVolt.Track.Light x) => x.Flags = world ? x.Flags | LightFlags.Fixed : x.Flags & ~LightFlags.Fixed);
            if (Toggle("Light cars and objects", (l.Flags & LightFlags.Moving) != 0, out bool moving))
                EditLight("Change light", (ref ReVolt.Track.Light x) => x.Flags = moving ? x.Flags | LightFlags.Moving : x.Flags & ~LightFlags.Moving);
            if (Toggle("Flicker", (l.Flags & LightFlags.Flicker) != 0, out bool flicker))
                EditLight("Change light", (ref ReVolt.Track.Light x) => x.Flags = flicker ? x.Flags | LightFlags.Flicker : x.Flags & ~LightFlags.Flicker);
            if ((l.Flags & LightFlags.Flicker) != 0 && Int("Flicker speed", l.Speed, "speed", out int speed))
                EditLight("Change flicker speed", (ref ReVolt.Track.Light x) => x.Speed = (byte)Mathf.Clamp(speed, 1, 255));
            if ((l.Flags & (LightFlags.Fixed | LightFlags.Moving)) == 0)
                Note("A light that lights neither the track nor cars has no effect.");
        }

        // Objects
        private static readonly ObjectType[] ObjectTypes = ((ObjectType[])Enum.GetValues(typeof(ObjectType))).OrderBy(t => t.ToString()).ToArray();

        private void DrawObject(RaceData data, int i)
        {
            var o = data.Objects.Objects[i];
            void EditObject(string label, Mutate<TrackObject> mutate) =>
                Race.Commit(label, d => { var x = d.Objects.Objects[i]; mutate(ref x); d.Objects.Objects[i] = x; });

            Section("Object");
            var names = ObjectTypes.Select(t => ObjectName(t)).ToList();
            Dropdown("Type", Enum.IsDefined(typeof(ObjectType), o.Type) ? ObjectName(o.Type) : $"ID {(int)o.Type}", names,
                     choice => EditObject("Change object type", (ref TrackObject x) => x.Type = ObjectTypes[choice]));
            if (Vec3("Position", o.Position, "pos", out var pos))
                EditObject("Move object", (ref TrackObject x) => x.Position = pos);
            if (Float("Rotation (°)", Yaw(o.DirectionMatrix), "yaw", out float yaw))
                EditObject("Rotate object", (ref TrackObject x) => x.DirectionMatrix = FromYaw(yaw));

            Section("Settings");
            var labels = FlagLabels(o.Type);
            if (labels[0] != null && Int(labels[0], o.Flags1, "f1", out int f1)) EditObject("Change object", (ref TrackObject x) => x.Flags1 = f1);
            if (labels[1] != null && Int(labels[1], o.Flags2, "f2", out int f2)) EditObject("Change object", (ref TrackObject x) => x.Flags2 = f2);
            if (labels[2] != null && Int(labels[2], o.Flags3, "f3", out int f3)) EditObject("Change object", (ref TrackObject x) => x.Flags3 = f3);
            if (labels[3] != null && Int(labels[3], o.Flags4, "f4", out int f4)) EditObject("Change object", (ref TrackObject x) => x.Flags4 = f4);
            if (labels.All(l => l == null))
                Note("This object has no settings.");
        }

        private static string ObjectName(ObjectType type)
        {
            string name = type.ToString();
            var sb = new System.Text.StringBuilder();
            for (int k = 0; k < name.Length; k++)
            {
                if (k > 0 && char.IsUpper(name[k]) && !char.IsUpper(name[k - 1]))
                    sb.Append(' ');
                sb.Append(k > 0 && char.IsUpper(name[k]) ? char.ToLowerInvariant(name[k]) : name[k]);
            }
            return sb.ToString();
        }

        /// <summary>Meaning of the four object flags per type (null = unused).</summary>
        private static string[] FlagLabels(ObjectType type)
        {
            switch (type)
            {
                case ObjectType.Barrel: return new[] { "Spin speed", null, null, null };
                case ObjectType.Planet: return new[] { "Planet (0–11)", "Orbits planet", "Orbit speed", "Spin speed" };
                case ObjectType.Plane: return new[] { "Speed", "Radius", "Bank", null };
                case ObjectType.Copter: return new[] { "X range (×10)", "Y range (×10)", "Z range (×10)", "Y offset (×50)" };
                case ObjectType.Boat: return new[] { "Sail 0 / Tug 1", null, null, null };
                case ObjectType.Speedup: return new[] { "Width (×5)", "Low speed mph", "High speed mph", "Change time s" };
                case ObjectType.Strobe: return new[] { "Type (0–2)", "Sequence num", "Sequence count", null };
                case ObjectType.SparkGenerator: return new[] { "Spark type", "Speed (×10)", "Speed variance", "Frequency" };
                case ObjectType.Laser: return new[] { "Width", "Random width", "Hits objects 0/1", null };
                case ObjectType.Sprinkler: return new[] { "ID", null, null, null };
                case ObjectType.SprinklerHose: return new[] { "Sprinkler ID", null, null, null };
                case ObjectType.ObjectThrower: return new[] { "ID", "Object type", "Speed", "Reuse 0/1" };
                case ObjectType.CarBox: return new[] { "Car ID", null, null, null };
                case ObjectType.Stream: return new[] { "Model (0–5)", null, null, null };
                case ObjectType.Cup: return new[] { "Bronze 0 … Platinum 3", null, null, null };
                case ObjectType.Sound3D: return new[] { "Sound (0–47)", "Range (×0.1)", "Mode 0/1/2", "Start time" };
                case ObjectType.Star: return new[] { "Practice star 0/1", null, null, null };
                case ObjectType.Skybox: return new[] { "Skybox (0–4)", null, null, null };
                case ObjectType.Slider: return new[] { "ID (0–1)", null, null, null };
                case ObjectType.Bottle: return new[] { "Starts at rest 0/1", null, null, null };
                case ObjectType.WaterBox:
                case ObjectType.FogBox: return new[] { "Half size X", "Half size Y", "Half size Z", null };
                case ObjectType.Ripple: return new[] { "Model (0–5)", null, null, null };
                case ObjectType.CustomAnimation: return new[] { "Animation slot", "Start offset (×0.1 s)", "Hide in time trial", "Trigger ID" };
                default: return new string[4];
            }
        }

        // Instances
        private void DrawInstance(RaceData data, int i)
        {
            var inst = data.Instances.Instances[i];
            void EditInstance(string label, Mutate<Instance> mutate) =>
                Race.Commit(label, d => { var x = d.Instances.Instances[i]; mutate(ref x); d.Instances.Instances[i] = x; });

            Section("Model");
            var modelNames = data.Models.Select(m => m.InstanceName).ToList();
            var model = RaceLayers.ModelFor(data, inst);
            Dropdown("Model", string.IsNullOrEmpty(inst.Name) ? "None" : inst.Name, modelNames,
                     choice => EditInstance("Change instance model", (ref Instance x) => x.Name = data.Models[choice].InstanceName));
            if (!model.HasValue)
                Note("No imported model has this name; the game will skip this instance. Import the .prm in the panel on the left.");
            else
                Note($"{model.Value.FileName} is copied into the level folder on export.");

            Section("Placement (game units)");
            if (Vec3("Position", inst.Position, "pos", out var pos))
                EditInstance("Move instance", (ref Instance x) => x.Position = pos);
            if (Float("Rotation (°)", Yaw(inst.Matrix), "yaw", out float yaw))
                EditInstance("Rotate instance", (ref Instance x) => x.Matrix = FromYaw(yaw));

            Section("Look");
            if (Vec3("Tint R G B", new Vector3(inst.TintR, inst.TintG, inst.TintB), "tint", out var tint))
                EditInstance("Change instance tint", (ref Instance x) =>
                {
                    x.TintR = (sbyte)Mathf.Clamp(Mathf.RoundToInt(tint.x), -128, 127);
                    x.TintG = (sbyte)Mathf.Clamp(Mathf.RoundToInt(tint.y), -128, 127);
                    x.TintB = (sbyte)Mathf.Clamp(Mathf.RoundToInt(tint.z), -128, 127);
                });
            Note("-128 (black) to 127 (about twice as bright); 0 leaves the model's colours unchanged.");
            if (Toggle("Environment map", (inst.Flags & InstanceFlags.EnvMap) != 0, out bool env))
                EditInstance("Change instance", (ref Instance x) => x.Flags = env ? x.Flags | InstanceFlags.EnvMap : x.Flags & ~InstanceFlags.EnvMap);
            if (Toggle("Ignore the track's lights", (inst.Flags & InstanceFlags.NoFileLights) != 0, out bool noLights))
                EditInstance("Change instance", (ref Instance x) => x.Flags = noLights ? x.Flags | InstanceFlags.NoFileLights : x.Flags & ~InstanceFlags.NoFileLights);
            if (Toggle("Hide in reflections", (inst.Flags & InstanceFlags.NoMirror) != 0, out bool noMirror))
                EditInstance("Change instance", (ref Instance x) => x.Flags = noMirror ? x.Flags | InstanceFlags.NoMirror : x.Flags & ~InstanceFlags.NoMirror);

            Section("Behaviour");
            if (Toggle("Always present (even with instances off)", inst.Priority != 0, out bool priority))
                EditInstance("Change instance", (ref Instance x) => x.Priority = (byte)(priority ? 1 : 0));
            if (Toggle("Cameras pass through", (inst.Flags & InstanceFlags.NoCameraCollision) != 0, out bool noCamera))
                EditInstance("Change instance", (ref Instance x) => x.Flags = noCamera ? x.Flags | InstanceFlags.NoCameraCollision : x.Flags & ~InstanceFlags.NoCameraCollision);
            if (Toggle("Cars pass through (also hides it in the game)", (inst.Flags & InstanceFlags.NoObjectCollision) != 0, out bool noObjects))
                EditInstance("Change instance", (ref Instance x) => x.Flags = noObjects ? x.Flags | InstanceFlags.NoObjectCollision : x.Flags & ~InstanceFlags.NoObjectCollision);
        }

        // Force fields
        private static readonly ForceFieldType[] FieldTypes = (ForceFieldType[])Enum.GetValues(typeof(ForceFieldType));

        private void DrawField(RaceData data, int i)
        {
            var f = data.ForceFields.Fields[i];
            void EditField(string label, Mutate<ForceField> mutate) =>
                Race.Commit(label, d => { var x = d.ForceFields.Fields[i]; mutate(ref x); d.ForceFields.Fields[i] = x; });

            Section("Field");
            Dropdown("Type", FieldName(f.Type), FieldTypes.Select(FieldName).ToList(),
                     choice => EditField("Change field type", (ref ForceField x) => x.Type = FieldTypes[choice]));
            if (f.Type == ForceFieldType.Wind)
                Note("Wind ignores the box, direction and strength: the game blows randomly up to this field's magnitude.");
            if (f.Type == ForceFieldType.Velocity || f.Type == ForceFieldType.AngularVelocity)
                Note("In the game this behaves like a constant push rather than a target speed.");

            int shape = SGUI.Segmented(Row("Shape"), (int)f.Shape, new[] { "Box", "Sphere" });
            if (shape != (int)f.Shape)
                EditField("Change field shape", (ref ForceField x) => x.Shape = (ForceFieldShape)shape);
            int dirMode = SGUI.Segmented(Row("Direction"), (int)f.DirectionMode, new[] { "Fixed", "Radial" });
            if (dirMode != (int)f.DirectionMode)
                EditField("Change field direction", (ref ForceField x) => x.DirectionMode = (ForceFieldDirection)dirMode);
            int distribution = SGUI.Segmented(Row("Strength"), (int)f.Distribution, new[] { "Uniform", "Gradient" });
            if (distribution != (int)f.Distribution)
                EditField("Change field strength", (ref ForceField x) => x.Distribution = (ForceFieldDistribution)distribution);
            int apply = SGUI.Segmented(Row("Affects"), (int)f.Apply, new[] { "All equally", "By mass" });
            if (apply != (int)f.Apply)
                EditField("Change field", (ref ForceField x) => x.Apply = (ForceFieldApply)apply);

            Section("Shape (game units)");
            if (Vec3("Position", f.Position, "pos", out var pos))
                EditField("Move field", (ref ForceField x) => x.Position = pos);
            if (f.Shape == ForceFieldShape.Box)
            {
                if (Vec3("Half size", f.Size, "size", out var size))
                    EditField("Resize field", (ref ForceField x) => x.Size = Vector3.Max(size, Vector3.one));
                if (Float("Rotation (°)", Yaw(f.Matrix), "yaw", out float yaw))
                    EditField("Rotate field", (ref ForceField x) => x.Matrix = FromYaw(yaw));
            }
            else
            {
                if (Float("Inner radius", f.RadiusStart, "r0", out float r0))
                    EditField("Resize field", (ref ForceField x) => x.RadiusStart = Mathf.Clamp(r0, 32f, x.RadiusEnd));
                if (Float("Outer radius", f.RadiusEnd, "r1", out float r1))
                    EditField("Resize field", (ref ForceField x) => x.RadiusEnd = Mathf.Clamp(r1, x.RadiusStart, 4096f));
            }

            Section("Force");
            if (Vec3(f.DirectionMode == ForceFieldDirection.Radial ? "Centre offset" : "Direction", f.Direction, "dir", out var dir))
                EditField("Change field direction", (ref ForceField x) => x.Direction = dir);
            if (f.DirectionMode == ForceFieldDirection.Linear)
                Note("World direction (not rotated with the box). Y points down: (0, -1, 0) pushes up.");
            if (f.Distribution == ForceFieldDistribution.Uniform)
            {
                if (Float("Magnitude", f.Magnitude, "mag", out float mag))
                    EditField("Change field strength", (ref ForceField x) => x.Magnitude = mag);
            }
            else
            {
                if (Float("Start magnitude", f.MagnitudeStart, "mag0", out float m0))
                    EditField("Change field strength", (ref ForceField x) => x.MagnitudeStart = m0);
                if (Float("End magnitude", f.MagnitudeEnd, "mag1", out float m1))
                    EditField("Change field strength", (ref ForceField x) => x.MagnitudeEnd = m1);
            }
            if (Float("Damping", f.Damping, "damp", out float damping))
                EditField("Change field damping", (ref ForceField x) => x.Damping = damping);
            Note("Gravity is 2200 units/s². For anti-gravity use Affects: all equally, magnitude 2200, direction (0, -1, 0).");
        }

        private static string FieldName(ForceFieldType type)
        {
            switch (type)
            {
                case ForceFieldType.Linear: return "Push";
                case ForceFieldType.OrientationUp: return "Turn upright";
                case ForceFieldType.OrientationForward: return "Turn forward";
                case ForceFieldType.Velocity: return "Velocity";
                case ForceFieldType.Wind: return "Wind";
                case ForceFieldType.Angular: return "Spin";
                case ForceFieldType.AngularVelocity: return "Spin velocity";
                default: return type.ToString();
            }
        }
    }
}

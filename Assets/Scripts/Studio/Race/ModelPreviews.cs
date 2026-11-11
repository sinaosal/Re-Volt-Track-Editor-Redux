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
using UnityEngine;

namespace Studio
{
    /// <summary>
    /// Loads Re-Volt .prm models for display in the viewport (first mesh only, the one the game draws
    /// for instances). Textures belong to the level, so models are shown with their vertex colours,
    /// shaded by face direction.
    /// </summary>
    public static class ModelPreviews
    {
        private struct Entry
        {
            public Mesh Mesh;
            public string Error;
            public DateTime Modified;
        }

        private static readonly Dictionary<string, Entry> cache = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private static Material material;

        public static Material Material
        {
            get
            {
                if (material == null)
                {
                    var shader = Shader.Find("Unlit/Color VC");
                    material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                }
                return material;
            }
        }

        /// <summary>Returns the mesh for a .prm file, or null with an error message.</summary>
        public static Mesh Get(string path, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                error = "The model file is missing.";
                return null;
            }

            var modified = File.GetLastWriteTimeUtc(path);
            if (cache.TryGetValue(path, out var entry) && entry.Modified == modified)
            {
                error = entry.Error;
                return entry.Mesh;
            }

            if (entry.Mesh != null)
                UnityEngine.Object.Destroy(entry.Mesh);

            entry = new Entry { Modified = modified };
            try
            {
                entry.Mesh = Load(path);
            }
            catch (Exception ex)
            {
                entry.Error = $"Couldn't read the model: {ex.Message}";
            }
            cache[path] = entry;
            error = entry.Error;
            return entry.Mesh;
        }

        /// <summary>
        /// Reads the first mesh of a .prm. Vertices stay in game units; Y is flipped to the editor's up
        /// axis (which also reverses the winding, so triangles are emitted in reverse order).
        /// </summary>
        private static Mesh Load(string path)
        {
            using (var reader = new BinaryReader(File.OpenRead(path)))
            {
                int polyCount = reader.ReadUInt16();
                int vertCount = reader.ReadUInt16();
                if (vertCount > 32767)
                    throw new InvalidDataException("too many vertices");

                const long polySize = 60, vertSize = 24;
                long needed = polyCount * polySize + vertCount * vertSize;
                if (reader.BaseStream.Length - reader.BaseStream.Position < needed)
                    throw new InvalidDataException("the file is truncated");

                var polys = new List<(int[] indices, Color32[] colors, bool quad)>(polyCount);
                for (int i = 0; i < polyCount; i++)
                {
                    short type = reader.ReadInt16();
                    reader.ReadInt16(); // texture page
                    var indices = new int[] { reader.ReadInt16(), reader.ReadInt16(), reader.ReadInt16(), reader.ReadInt16() };
                    var colors = new Color32[4];
                    for (int c = 0; c < 4; c++)
                    {
                        uint bgra = reader.ReadUInt32();
                        colors[c] = new Color32((byte)(bgra >> 16), (byte)(bgra >> 8), (byte)bgra, 255);
                    }
                    reader.ReadBytes(32); // uv
                    polys.Add((indices, colors, (type & 1) != 0));
                }

                var positions = new Vector3[vertCount];
                for (int i = 0; i < vertCount; i++)
                {
                    positions[i] = new Vector3(reader.ReadSingle(), -reader.ReadSingle(), reader.ReadSingle());
                    reader.ReadBytes(12); // normal
                }

                // unshared vertices so every face gets its own colour and shading
                var vertices = new List<Vector3>();
                var colorsOut = new List<Color32>();
                var triangles = new List<int>();
                var light = new Vector3(0.35f, 0.85f, 0.4f).normalized;

                void Triangle(int a, int b, int c, Color32 ca, Color32 cb, Color32 cc)
                {
                    if (a < 0 || b < 0 || c < 0 || a >= vertCount || b >= vertCount || c >= vertCount)
                        return;
                    var pa = positions[a];
                    var pb = positions[b];
                    var pc = positions[c];
                    var normal = Vector3.Cross(pb - pa, pc - pa).normalized;
                    float shade = 0.55f + 0.45f * Mathf.Abs(Vector3.Dot(normal, light));
                    int start = vertices.Count;
                    vertices.Add(pa); vertices.Add(pb); vertices.Add(pc);
                    colorsOut.Add(Shade(ca, shade)); colorsOut.Add(Shade(cb, shade)); colorsOut.Add(Shade(cc, shade));
                    triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
                }

                foreach (var (indices, colors, quad) in polys)
                {
                    // reversed order compensates for the Y flip
                    Triangle(indices[2], indices[1], indices[0], colors[2], colors[1], colors[0]);
                    if (quad)
                        Triangle(indices[3], indices[2], indices[0], colors[3], colors[2], colors[0]);
                }

                var mesh = new Mesh { name = Path.GetFileName(path), hideFlags = HideFlags.HideAndDontSave };
                if (vertices.Count > 65000)
                    mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.SetVertices(vertices);
                mesh.SetColors(colorsOut);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        private static Color32 Shade(Color32 c, float shade)
        {
            return new Color32((byte)(c.r * shade), (byte)(c.g * shade), (byte)(c.b * shade), 255);
        }

        /// <summary>Editor-space transform for an instance (game position and R/U/L matrix).</summary>
        public static Matrix4x4 EditorMatrix(Vector3 gamePosition, Matrix4x4 gameRotation)
        {
            var flip = Matrix4x4.Scale(new Vector3(1f, -1f, 1f));
            var rotation = flip * gameRotation * flip;
            rotation.m33 = 1f;
            return Matrix4x4.Translate(RaceData.ToEditor(gamePosition))
                   * rotation
                   * Matrix4x4.Scale(Vector3.one / RVConstants.SMALL_CUBE_SIZE);
        }

        public static void Clear()
        {
            foreach (var entry in cache.Values)
            {
                if (entry.Mesh != null)
                    UnityEngine.Object.Destroy(entry.Mesh);
            }
            cache.Clear();
        }
    }
}


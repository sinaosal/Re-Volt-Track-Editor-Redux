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

using Studio.Icons;
using System.Collections.Generic;
using UnityEngine;

namespace Studio
{
    /// <summary>
    /// Texture cache for the procedural icon set. Icons are rasterized white at the exact pixel
    /// size they are drawn at and tinted at draw time, so they are sharp at every UI scale.
    /// </summary>
    public static class StudioIcons
    {
        private static readonly Dictionary<int, Texture2D> cache = new Dictionary<int, Texture2D>();

        public static Texture2D Get(IconId id, int sizePx)
        {
            sizePx = Mathf.Clamp(sizePx, 8, 128);
            int key = ((int)id << 8) | sizePx;
            if (cache.TryGetValue(key, out var tex) && tex != null)
                return tex;

            byte[] rgba = IconRasterizer.Render(id, sizePx);
            tex = new Texture2D(sizePx, sizePx, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            tex.LoadRawTextureData(rgba);
            tex.Apply(false, true);
            cache[key] = tex;
            return tex;
        }

        /// <summary>
        /// Draws an icon centered in <paramref name="rect"/> at <paramref name="size"/> pixels, tinted.
        /// </summary>
        public static void Draw(Rect rect, IconId id, float size, Color color)
        {
            if (Event.current.type != EventType.Repaint)
                return;

            int px = Mathf.RoundToInt(size);
            var tex = Get(id, px);
            var iconRect = new Rect(Mathf.Round(rect.center.x - px * 0.5f), Mathf.Round(rect.center.y - px * 0.5f), px, px);

            var old = GUI.color;
            GUI.color = old * color;
            GUI.DrawTexture(iconRect, tex, ScaleMode.StretchToFill, true);
            GUI.color = old;
        }

        public static void Clear()
        {
            foreach (var tex in cache.Values)
            {
                if (tex != null)
                    Object.Destroy(tex);
            }
            cache.Clear();
        }
    }
}

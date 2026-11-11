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
using UnityEngine;

namespace Studio
{
    /// <summary>
    /// Colors of the Studio interface. A neutral graphite base with a single accent: the rose
    /// of the original editor's frame, so the Studio interface still feels like Re-Volt's editor.
    /// </summary>
    public static class Palette
    {
        static Color Hex(uint rgb, float a = 1f) => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, a);

        // surfaces, darkest to lightest
        public static readonly Color Backdrop = Hex(0x111214);
        public static readonly Color Viewport = Hex(0x17181B);
        public static readonly Color Chrome = Hex(0x1B1C20);    // menu bar, status bar
        public static readonly Color Panel = Hex(0x1F2024);     // side panels, toolbar
        public static readonly Color Raised = Hex(0x26282D);    // cards, inputs
        public static readonly Color Hover = Hex(0x2E3036);
        public static readonly Color Pressed = Hex(0x35373E);
        public static readonly Color Overlay = Hex(0x25272C);   // menus, dialogs, tooltips

        public static readonly Color Border = Hex(0x2D2F35);
        public static readonly Color BorderStrong = Hex(0x3A3D44);
        public static readonly Color Divider = Hex(0x26282D);

        public static readonly Color Text = Hex(0xE7E8EB);
        public static readonly Color TextDim = Hex(0xA3A7B0);
        public static readonly Color TextFaint = Hex(0x6C717B);
        public static readonly Color TextOnAccent = Hex(0xFFFFFF);

        public static readonly Color Accent = Hex(0xE2457A);
        public static readonly Color AccentHover = Hex(0xEA5C8B);
        public static readonly Color AccentPressed = Hex(0xC93A6A);
        public static readonly Color AccentSoft = Hex(0xE2457A, 0.16f);
        public static readonly Color AccentLine = Hex(0xE2457A, 0.55f);

        public static readonly Color Success = Hex(0x3DB88B);
        public static readonly Color Warning = Hex(0xE9A93A);
        public static readonly Color Error = Hex(0xE5534B);
        public static readonly Color Info = Hex(0x4D9DE8);

        public static readonly Color Scrim = new Color(0.04f, 0.04f, 0.05f, 0.62f);
        public static readonly Color Shadow = new Color(0f, 0f, 0f, 0.35f);

        // viewport overlays
        public static readonly Color Grid = new Color(0.36f, 0.38f, 0.43f, 1f);
        public static readonly Color FootprintPlace = Hex(0xE2457A, 0.28f);
        public static readonly Color FootprintReplace = Hex(0xE9A93A, 0.30f);
        public static readonly Color FootprintErase = Hex(0xE5534B, 0.32f);
        public static readonly Color FootprintPickup = Hex(0x4DD0E1, 0.30f);

        public static Color WithAlpha(this Color c, float a) => new Color(c.r, c.g, c.b, a);
    }

    /// <summary>
    /// Fonts, scaled metrics, and styles. All sizes are design pixels at 100% scale; use
    /// <see cref="S"/> / <see cref="Px"/> to convert so the interface stays crisp at any DPI.
    /// </summary>
    public sealed class StudioTheme
    {
        public float Scale { get; private set; } = 1f;

        public Font Regular { get; private set; }
        public Font SemiBold { get; private set; }

        public GUISkin Skin { get; private set; }

        // text styles
        public GUIStyle Body, BodyStrong, Small, SmallStrong, Caption, Title, Heading, Mono, Shortcut, Wrapped, SmallWrapped;

        // inputs
        public GUIStyle TextField, SearchField;

        // metrics
        public float MenuBarHeight => S(30f);
        public float ToolbarHeight => S(40f);
        public float StatusBarHeight => S(26f);
        public float RowHeight => S(28f);
        public float ControlHeight => S(28f);
        public float Spacing => S(8f);
        public float Padding => S(12f);
        public float Radius => S(5f);
        public float IconSize => S(16f);

        private readonly Dictionary<long, GUIStyle> shapeCache = new Dictionary<long, GUIStyle>();
        private readonly List<Texture2D> ownedTextures = new List<Texture2D>();
        private bool initialized;

        public float S(float value) => value * Scale;
        public int Px(float value) => Mathf.Max(1, Mathf.RoundToInt(value * Scale));

        public static float DetectScale()
        {
            float dpi = Screen.dpi;
            float scale;
            if (dpi > 0f)
                scale = dpi / 96f;
            else
                scale = Screen.height >= 1900 ? 1.5f : 1f;

            // snap to quarter steps so fonts and shapes stay pixel aligned
            return Mathf.Clamp(Mathf.Round(scale * 4f) / 4f, 1f, 2.5f);
        }

        /// <summary>
        /// (Re)builds styles if the scale changed. Must be called from OnGUI (GUI.skin is only valid there).
        /// </summary>
        public void Ensure(float scale)
        {
            if (initialized && Mathf.Approximately(scale, Scale))
                return;

            Scale = scale;
            Build();
            initialized = true;
        }

        private void Build()
        {
            if (Regular == null)
            {
                Regular = Resources.Load<Font>("StudioFonts/Inter-Regular");
                SemiBold = Resources.Load<Font>("StudioFonts/Inter-SemiBold");

                // fall back to the built-in font rather than failing to draw any text
                if (Regular == null)
                    Regular = Resources.GetBuiltinResource<Font>("Arial.ttf");
                if (SemiBold == null)
                    SemiBold = Regular;
            }

            foreach (var tex in ownedTextures)
                Object.Destroy(tex);
            ownedTextures.Clear();
            shapeCache.Clear();

            Body = MakeText(Regular, 13f, Palette.Text);
            BodyStrong = MakeText(SemiBold, 13f, Palette.Text);
            Small = MakeText(Regular, 12f, Palette.TextDim);
            SmallStrong = MakeText(SemiBold, 12f, Palette.Text);
            Caption = MakeText(SemiBold, 10.5f, Palette.TextFaint);
            Title = MakeText(SemiBold, 17f, Palette.Text);
            Heading = MakeText(SemiBold, 14f, Palette.Text);
            Mono = MakeText(Regular, 11.5f, Palette.TextDim);
            Shortcut = MakeText(Regular, 11.5f, Palette.TextFaint);
            Shortcut.alignment = TextAnchor.MiddleRight;

            Wrapped = MakeText(Regular, 13f, Palette.TextDim);
            Wrapped.wordWrap = true;
            Wrapped.alignment = TextAnchor.UpperLeft;
            SmallWrapped = MakeText(Regular, 12f, Palette.TextDim);
            SmallWrapped.wordWrap = true;
            SmallWrapped.alignment = TextAnchor.UpperLeft;

            TextField = MakeText(Regular, 13f, Palette.Text);
            TextField.padding = new RectOffset(Px(9f), Px(9f), 0, 0);
            TextField.normal.background = RoundedTexture(Radius, Palette.Raised, Palette.Border, Px(1f));
            TextField.hover.background = RoundedTexture(Radius, Palette.Raised, Palette.BorderStrong, Px(1f));
            TextField.focused.background = RoundedTexture(Radius, Palette.Raised, Palette.Accent, Px(1f));
            TextField.active.background = TextField.focused.background;
            TextField.hover.textColor = Palette.Text;
            TextField.focused.textColor = Palette.Text;
            TextField.active.textColor = Palette.Text;
            TextField.onNormal.textColor = Palette.Text;
            TextField.onFocused.textColor = Palette.Text;
            TextField.onActive.textColor = Palette.Text;
            int b = Mathf.RoundToInt(Radius) + 2; // must match RoundedTexture's corner size
            TextField.border = new RectOffset(b, b, b, b);

            SearchField = new GUIStyle(TextField);
            SearchField.padding = new RectOffset(Px(30f), Px(28f), 0, 0);

            Skin = Object.Instantiate(GUI.skin);
            Skin.font = Regular;
            Skin.label = Body;
            Skin.textField = TextField;
            Skin.settings.cursorColor = Palette.Text;
            Skin.settings.selectionColor = Palette.Accent.WithAlpha(0.38f);
            Skin.settings.cursorFlashSpeed = 1f;
            Skin.settings.doubleClickSelectsWord = true;
        }

        private GUIStyle MakeText(Font font, float size, Color color)
        {
            var style = new GUIStyle
            {
                font = font,
                fontSize = Mathf.RoundToInt(size * Scale),
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                wordWrap = false,
                richText = false,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0)
            };
            style.normal.textColor = color;
            return style;
        }

        // Shapes
        /// <summary>
        /// A white, anti-aliased rounded rectangle as a 9-slice style. Tint it with GUI.backgroundColor.
        /// Generated procedurally so it doesn't depend on IMGUI's rounded rect shader being in the build.
        /// </summary>
        public GUIStyle RoundedShape(float radius)
        {
            int r = Mathf.Max(0, Mathf.RoundToInt(radius));
            long key = r;
            if (shapeCache.TryGetValue(key, out var style))
                return style;

            style = new GUIStyle();
            style.normal.background = RoundedTexture(r, Color.white, Color.clear, 0);
            int border = r + 2; // see RoundedTexture: corners occupy r+2 pixels, one straight pixel in the middle
            style.border = new RectOffset(border, border, border, border);
            shapeCache[key] = style;
            return style;
        }

        /// <summary>
        /// Rounded rectangle texture with baked fill and border colors, laid out for 9-slicing
        /// with a border of radius+2 pixels.
        /// </summary>
        public Texture2D RoundedTexture(float radius, Color fill, Color border, int borderWidth)
        {
            int r = Mathf.Max(0, Mathf.RoundToInt(radius));
            int size = (r + 2) * 2 + 1;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            var pixels = new Color32[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // signed distance to a rounded box filling the texture
                    float px = Mathf.Abs(x + 0.5f - half);
                    float py = Mathf.Abs(y + 0.5f - half);
                    float qx = px - (half - r);
                    float qy = py - (half - r);
                    float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
                    float inside = Mathf.Min(Mathf.Max(qx, qy), 0f);
                    float dist = outside + inside - r; // < 0 inside

                    float coverage = Mathf.Clamp01(0.5f - dist);
                    Color c = fill;
                    if (borderWidth > 0)
                    {
                        float borderBlend = Mathf.Clamp01(0.5f - (-dist - borderWidth)); // 1 within the border band
                        c = Color.Lerp(fill, border, borderBlend);
                    }
                    c.a *= coverage;
                    pixels[y * size + x] = c;
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            ownedTextures.Add(tex);
            return tex;
        }

        public void Dispose()
        {
            foreach (var tex in ownedTextures)
                Object.Destroy(tex);
            ownedTextures.Clear();
            shapeCache.Clear();
            if (Skin != null)
                Object.Destroy(Skin);
            initialized = false;
        }
    }
}

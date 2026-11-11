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
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Studio
{
    public enum ButtonKind
    {
        Primary,
        Secondary,
        Ghost,
        Danger
    }

    /// <summary>
    /// Immediate mode widgets for the Studio interface, drawn with explicit rects.
    ///
    /// Every widget calls GUIUtility.GetControlID exactly once per call, whatever its state, so
    /// control IDs stay stable between IMGUI's layout, input and repaint passes.
    /// </summary>
    public static class SGUI
    {
        public static StudioTheme T;

        /// <summary>
        /// Set while drawing a layer that sits underneath an open dialog or menu: widgets keep their
        /// normal look but don't react to the pointer.
        /// </summary>
        public static bool InputBlocked;

        private static readonly Stack<Rect> clipStack = new Stack<Rect>();

        // tooltips
        private static string tooltipCandidate;
        private static Rect tooltipCandidateRect;
        private static string tooltipShown;
        private static float tooltipHoverStart;

        private static bool IsRepaint => Event.current.type == EventType.Repaint;

        // Geometry helpers
        public static bool PointerIn(Rect r)
        {
            if (InputBlocked)
                return false;
            var mouse = Event.current.mousePosition;
            if (clipStack.Count > 0 && !clipStack.Peek().Contains(mouse))
                return false;
            return r.Contains(mouse);
        }

        public static Rect Inset(this Rect r, float amount) => new Rect(r.x + amount, r.y + amount, r.width - amount * 2f, r.height - amount * 2f);
        public static Rect Inset(this Rect r, float x, float y) => new Rect(r.x + x, r.y + y, r.width - x * 2f, r.height - y * 2f);
        public static Rect TakeLeft(this Rect r, float width) => new Rect(r.x, r.y, width, r.height);
        public static Rect TakeRight(this Rect r, float width) => new Rect(r.xMax - width, r.y, width, r.height);
        public static Rect TakeTop(this Rect r, float height) => new Rect(r.x, r.y, r.width, height);
        public static Rect TakeBottom(this Rect r, float height) => new Rect(r.x, r.yMax - height, r.width, height);
        public static Rect CutLeft(this Rect r, float width) => new Rect(r.x + width, r.y, r.width - width, r.height);
        public static Rect CutRight(this Rect r, float width) => new Rect(r.x, r.y, r.width - width, r.height);
        public static Rect CutTop(this Rect r, float height) => new Rect(r.x, r.y + height, r.width, r.height - height);
        public static Rect CutBottom(this Rect r, float height) => new Rect(r.x, r.y, r.width, r.height - height);
        public static Rect Snap(this Rect r) => new Rect(Mathf.Round(r.x), Mathf.Round(r.y), Mathf.Round(r.width), Mathf.Round(r.height));

        // Drawing
        public static void Fill(Rect r, Color color)
        {
            if (!IsRepaint || color.a <= 0f)
                return;
            var old = GUI.color;
            GUI.color = old * color;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        /// <summary>A straight line of the given width between two GUI points.</summary>
        public static void Line(Vector2 a, Vector2 b, float width, Color color)
        {
            if (!IsRepaint || color.a <= 0f)
                return;
            var d = b - a;
            float length = d.magnitude;
            if (length < 0.5f)
                return;
            float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            var matrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, a);
            Fill(new Rect(a.x, a.y - width * 0.5f, length, width), color);
            GUI.matrix = matrix;
        }

        public static void Rounded(Rect r, Color color, float radius = -1f)
        {
            if (!IsRepaint || color.a <= 0f)
                return;
            if (radius < 0f)
                radius = T.Radius;
            radius = Mathf.Min(radius, Mathf.Floor(Mathf.Min(r.width, r.height) * 0.5f));

            var style = T.RoundedShape(radius);
            int border = style.border.left;
            if (r.width < border * 2f || r.height < border * 2f)
            {
                // too small for 9-slicing (the corners would overlap): stretch the whole shape instead
                var oldColor = GUI.color;
                GUI.color = oldColor * color;
                GUI.DrawTexture(r, style.normal.background, ScaleMode.StretchToFill, true);
                GUI.color = oldColor;
                return;
            }

            var oldBg = GUI.backgroundColor;
            GUI.backgroundColor = color;
            style.Draw(r, false, false, false, false);
            GUI.backgroundColor = oldBg;
        }

        public static void RoundedOutline(Rect r, Color fill, Color border, float radius = -1f, float width = -1f)
        {
            if (!IsRepaint)
                return;
            if (radius < 0f)
                radius = T.Radius;
            if (width < 0f)
                width = Mathf.Max(1f, Mathf.Round(T.S(1f)));
            Rounded(r, border, radius);
            Rounded(r.Inset(width), fill, Mathf.Max(0f, radius - width));
        }

        public static void HLine(float x, float y, float width, Color color)
        {
            Fill(new Rect(x, y, width, Mathf.Max(1f, Mathf.Round(T.S(1f)))), color);
        }

        public static void VLine(float x, float y, float height, Color color)
        {
            Fill(new Rect(x, y, Mathf.Max(1f, Mathf.Round(T.S(1f))), height), color);
        }

        public static void Label(Rect r, string text, GUIStyle style, Color? color = null, TextAnchor? alignment = null)
        {
            if (!IsRepaint || string.IsNullOrEmpty(text))
                return;

            var oldColor = style.normal.textColor;
            var oldAlign = style.alignment;
            if (color.HasValue)
                style.normal.textColor = color.Value;
            if (alignment.HasValue)
                style.alignment = alignment.Value;

            style.Draw(r, text, false, false, false, false);

            style.normal.textColor = oldColor;
            style.alignment = oldAlign;
        }

        public static float TextWidth(string text, GUIStyle style)
        {
            if (string.IsNullOrEmpty(text))
                return 0f;
            return style.CalcSize(new GUIContent(text)).x;
        }

        public static float WrappedHeight(string text, GUIStyle style, float width)
        {
            if (string.IsNullOrEmpty(text))
                return 0f;
            return style.CalcHeight(new GUIContent(text), width);
        }

        /// <summary>Shortens text with an ellipsis so it fits <paramref name="width"/>.</summary>
        public static string Ellipsize(string text, GUIStyle style, float width)
        {
            if (string.IsNullOrEmpty(text) || TextWidth(text, style) <= width)
                return text;

            int lo = 0, hi = text.Length;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (TextWidth(text.Substring(0, mid) + "…", style) <= width)
                    lo = mid;
                else
                    hi = mid - 1;
            }
            return lo <= 0 ? "…" : text.Substring(0, lo).TrimEnd() + "…";
        }

        public static void Icon(Rect r, IconId icon, Color color, float size = -1f)
        {
            StudioIcons.Draw(r, icon, size < 0f ? T.IconSize : size, color);
        }

        public static void Shadow(Rect r, float spread = -1f)
        {
            if (!IsRepaint)
                return;
            if (spread < 0f)
                spread = T.S(10f);
            // a few stacked translucent rounded rects make a soft shadow
            for (int i = 3; i >= 1; i--)
            {
                float grow = spread * i / 3f;
                Rounded(new Rect(r.x - grow, r.y - grow * 0.5f, r.width + grow * 2f, r.height + grow * 2f),
                        new Color(0f, 0f, 0f, 0.07f), T.Radius + grow);
            }
        }

        // Behaviour
        /// <summary>
        /// Standard press-and-release button behaviour. Returns true when clicked.
        /// </summary>
        public static bool ClickBehavior(Rect r, bool enabled, out bool hover, out bool pressed)
        {
            int id = GUIUtility.GetControlID(FocusType.Passive);
            var e = Event.current;
            bool interactive = enabled && GUI.enabled && !InputBlocked;
            hover = interactive && PointerIn(r);
            bool clicked = false;

            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (hover && e.button == 0)
                    {
                        GUIUtility.hotControl = id;
                        GUIUtility.keyboardControl = 0; // clicking anything ends text editing
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id)
                        e.Use();
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        e.Use();
                        clicked = interactive && r.Contains(e.mousePosition);
                    }
                    break;
            }

            pressed = GUIUtility.hotControl == id && hover;
            return clicked;
        }

        /// <summary>
        /// Like <see cref="ClickBehavior"/> but also reports double clicks (for list rows).
        /// </summary>
        public static bool RowBehavior(Rect r, bool enabled, out bool hover, out bool doubleClicked)
        {
            int id = GUIUtility.GetControlID(FocusType.Passive);
            var e = Event.current;
            bool interactive = enabled && GUI.enabled && !InputBlocked;
            hover = interactive && PointerIn(r);
            doubleClicked = false;

            if (e.GetTypeForControl(id) == EventType.MouseDown && hover && e.button == 0)
            {
                GUIUtility.keyboardControl = 0;
                doubleClicked = e.clickCount >= 2;
                e.Use();
                return true;
            }
            return false;
        }

        // Buttons
        public static bool Button(Rect r, string text, ButtonKind kind = ButtonKind.Secondary, bool enabled = true, IconId? icon = null, string tooltip = null)
        {
            bool clicked = ClickBehavior(r, enabled, out bool hover, out bool pressed);
            if (tooltip != null)
                Tooltip(r, tooltip);

            if (IsRepaint)
            {
                Color bg, fg;
                switch (kind)
                {
                    case ButtonKind.Primary:
                        bg = pressed ? Palette.AccentPressed : hover ? Palette.AccentHover : Palette.Accent;
                        fg = Palette.TextOnAccent;
                        break;
                    case ButtonKind.Danger:
                        bg = pressed ? Palette.Error * 0.85f : hover ? Palette.Error : Palette.Error.WithAlpha(0.88f);
                        bg.a = 1f;
                        fg = Palette.TextOnAccent;
                        break;
                    case ButtonKind.Ghost:
                        bg = pressed ? Palette.Pressed : hover ? Palette.Hover : Color.clear;
                        fg = Palette.Text;
                        break;
                    default:
                        bg = pressed ? Palette.Pressed : hover ? Palette.Hover : Palette.Raised;
                        fg = Palette.Text;
                        break;
                }

                if (!enabled)
                {
                    bg = kind == ButtonKind.Ghost ? Color.clear : Palette.Raised;
                    fg = Palette.TextFaint;
                }

                if (kind == ButtonKind.Secondary && enabled)
                    RoundedOutline(r, bg, hover ? Palette.BorderStrong : Palette.Border);
                else
                    Rounded(r, bg);

                var style = kind == ButtonKind.Primary || kind == ButtonKind.Danger ? T.BodyStrong : T.Body;
                if (icon.HasValue)
                {
                    float textWidth = TextWidth(text, style);
                    float iconSize = T.IconSize;
                    float gap = string.IsNullOrEmpty(text) ? 0f : T.S(7f);
                    float total = iconSize + gap + textWidth;
                    float x = Mathf.Round(r.center.x - total * 0.5f);
                    Icon(new Rect(x, r.y, iconSize, r.height), icon.Value, fg);
                    Label(new Rect(x + iconSize + gap, r.y, textWidth + 2f, r.height), text, style, fg);
                }
                else
                {
                    Label(r, text, style, fg, TextAnchor.MiddleCenter);
                }
            }
            return clicked;
        }

        /// <summary>
        /// Square icon button. <paramref name="active"/> renders it as a selected toggle.
        /// </summary>
        public static bool IconButton(Rect r, IconId icon, string tooltip, bool active = false, bool enabled = true, Color? tint = null)
        {
            bool clicked = ClickBehavior(r, enabled, out bool hover, out bool pressed);
            if (tooltip != null)
                Tooltip(r, tooltip);

            if (IsRepaint)
            {
                if (active)
                    Rounded(r, pressed ? Palette.AccentPressed.WithAlpha(0.35f) : Palette.AccentSoft);
                else if (pressed)
                    Rounded(r, Palette.Pressed);
                else if (hover)
                    Rounded(r, Palette.Hover);

                Color color = !enabled ? Palette.TextFaint.WithAlpha(0.6f)
                            : active ? Palette.Accent
                            : tint ?? (hover ? Palette.Text : Palette.TextDim);
                Icon(r, icon, color);
            }
            return clicked;
        }

        /// <summary>A checkbox with a label. Returns the new value.</summary>
        public static bool Toggle(Rect r, bool value, string label, bool enabled = true)
        {
            if (ClickBehavior(r, enabled, out bool hover, out bool pressed))
                value = !value;

            if (IsRepaint)
            {
                float box = T.S(16f);
                var boxRect = new Rect(r.x, Mathf.Round(r.center.y - box * 0.5f), box, box);
                if (value)
                {
                    Rounded(boxRect, enabled ? (hover ? Palette.AccentHover : Palette.Accent) : Palette.TextFaint, T.S(4f));
                    Icon(boxRect, IconId.Check, Palette.TextOnAccent, T.S(12f));
                }
                else
                {
                    RoundedOutline(boxRect, Palette.Raised, hover ? Palette.TextFaint : Palette.BorderStrong, T.S(4f));
                }
                Label(r.CutLeft(box + T.S(9f)), label, T.Body, enabled ? Palette.Text : Palette.TextFaint);
            }
            return value;
        }

        /// <summary>A row of mutually exclusive options. Returns the selected index.</summary>
        public static int Segmented(Rect r, int selected, string[] options, bool enabled = true, string[] tooltips = null)
        {
            if (IsRepaint)
                RoundedOutline(r, Palette.Raised, Palette.Border);

            float segmentWidth = r.width / options.Length;
            float inset = T.S(2f);
            for (int i = 0; i < options.Length; i++)
            {
                var seg = new Rect(r.x + segmentWidth * i, r.y, segmentWidth, r.height);
                if (ClickBehavior(seg, enabled, out bool hover, out bool pressed))
                    selected = i;
                if (tooltips != null && i < tooltips.Length)
                    Tooltip(seg, tooltips[i]);

                if (IsRepaint)
                {
                    var inner = seg.Inset(inset);
                    if (i == selected)
                        Rounded(inner, enabled ? Palette.Pressed : Palette.Hover, T.Radius - inset);
                    else if (hover)
                        Rounded(inner, Palette.Hover, T.Radius - inset);

                    Color fg = !enabled ? Palette.TextFaint : i == selected ? Palette.Text : Palette.TextDim;
                    Label(seg, options[i], i == selected ? T.SmallStrong : T.Small, fg, TextAnchor.MiddleCenter);
                }
            }
            return selected;
        }

        /// <summary>Horizontal slider. Returns the new value.</summary>
        public static float Slider(Rect r, float value, float min, float max, bool enabled = true)
        {
            int id = GUIUtility.GetControlID(FocusType.Passive);
            var e = Event.current;
            bool interactive = enabled && GUI.enabled && !InputBlocked;
            bool hover = interactive && PointerIn(r);

            float thumb = T.S(14f);
            float trackX = r.x + thumb * 0.5f;
            float trackW = r.width - thumb;

            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (hover && e.button == 0)
                    {
                        GUIUtility.hotControl = id;
                        GUIUtility.keyboardControl = 0;
                        value = Mathf.Lerp(min, max, Mathf.Clamp01((e.mousePosition.x - trackX) / trackW));
                        GUI.changed = true;
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id)
                    {
                        value = Mathf.Lerp(min, max, Mathf.Clamp01((e.mousePosition.x - trackX) / trackW));
                        GUI.changed = true;
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        e.Use();
                    }
                    break;
            }

            if (IsRepaint)
            {
                float t = Mathf.InverseLerp(min, max, value);
                float trackH = T.S(4f);
                var track = new Rect(trackX, Mathf.Round(r.center.y - trackH * 0.5f), trackW, trackH);
                Rounded(track, Palette.Pressed, trackH * 0.5f);
                Rounded(new Rect(track.x, track.y, track.width * t, track.height), enabled ? Palette.Accent : Palette.TextFaint, trackH * 0.5f);

                bool active = GUIUtility.hotControl == id;
                var thumbRect = new Rect(Mathf.Round(trackX + trackW * t - thumb * 0.5f), Mathf.Round(r.center.y - thumb * 0.5f), thumb, thumb);
                Rounded(thumbRect, active || hover ? Color.white : Palette.Text, thumb * 0.5f);
            }
            return value;
        }

        /// <summary>[-] value [+] integer stepper. Returns the new value.</summary>
        public static int Stepper(Rect r, int value, int min, int max, string text = null, bool enabled = true, string tooltip = null)
        {
            float button = r.height;
            if (IsRepaint)
                RoundedOutline(r, Palette.Raised, Palette.Border);

            var minus = r.TakeLeft(button);
            var plus = r.TakeRight(button);
            if (IconButton(minus.Inset(T.S(2f)), IconId.Minus, null, false, enabled && value > min))
                value--;
            if (IconButton(plus.Inset(T.S(2f)), IconId.Plus, null, false, enabled && value < max))
                value++;

            var middle = new Rect(minus.xMax, r.y, r.width - button * 2f, r.height);
            Label(middle, text ?? value.ToString(), T.Body, enabled ? Palette.Text : Palette.TextFaint, TextAnchor.MiddleCenter);
            if (tooltip != null)
                Tooltip(middle, tooltip);

            // scroll wheel over the value
            var e = Event.current;
            if (enabled && e.type == EventType.ScrollWheel && PointerIn(r))
            {
                value += e.delta.y < 0f ? 1 : -1;
                e.Use();
            }

            return Mathf.Clamp(value, min, max);
        }

        // Text input
        /// <summary>
        /// Single line text field with placeholder text. <paramref name="charFilter"/> rejects typed
        /// characters before the field sees them.
        /// </summary>
        public static string TextField(Rect r, string value, string controlName, string placeholder = null, int maxLength = 256,
                                       GUIStyle style = null, Func<char, bool> charFilter = null)
        {
            style = style ?? T.TextField;
            var e = Event.current;
            bool focused = GUI.GetNameOfFocusedControl() == controlName;

            if (focused && charFilter != null && e.type == EventType.KeyDown && e.character != '\0' && !char.IsControl(e.character) && !charFilter(e.character))
                e.Use();

            bool oldEnabled = GUI.enabled;
            if (InputBlocked && e.type != EventType.Repaint)
                GUI.enabled = false;

            GUI.SetNextControlName(controlName);
            string result = GUI.TextField(r, value ?? string.Empty, maxLength, style);

            GUI.enabled = oldEnabled;

            if (string.IsNullOrEmpty(result) && !string.IsNullOrEmpty(placeholder) && !focused)
            {
                var placeholderRect = new Rect(r.x + style.padding.left, r.y, r.width - style.padding.horizontal, r.height);
                Label(placeholderRect, placeholder, T.Body, Palette.TextFaint);
            }
            return result;
        }

        public static string SearchField(Rect r, string value, string controlName, string placeholder)
        {
            // the clear button's behaviour runs before the text field, which would otherwise take the click
            var clear = new Rect(r.xMax - r.height, r.y, r.height, r.height).Inset(T.S(4f));
            bool hasText = !string.IsNullOrEmpty(value);
            bool cleared = ClickBehavior(clear, hasText, out bool clearHover, out _);

            string result = TextField(r, cleared ? string.Empty : value, controlName, placeholder, 128, T.SearchField);
            Icon(new Rect(r.x + T.S(6f), r.y, T.S(18f), r.height), IconId.Search, Palette.TextFaint, T.S(14f));

            if (hasText)
            {
                if (clearHover)
                    Rounded(clear, Palette.Hover);
                Icon(clear, IconId.Close, clearHover ? Palette.Text : Palette.TextFaint, T.S(12f));
            }
            return result;
        }

        public static bool IsFocused(string controlName) => GUI.GetNameOfFocusedControl() == controlName;

        // Numeric fields: edit a text buffer, commit on Enter or when focus leaves
        private static readonly Dictionary<string, string> numberBuffers = new Dictionary<string, string>();
        private static readonly HashSet<string> focusedNumberFields = new HashSet<string>();
        private static readonly Dictionary<string, int> numberFieldFrames = new Dictionary<string, int>();
        private static readonly Dictionary<string, object> numberFieldOwners = new Dictionary<string, object>();

        /// <summary>
        /// A number field. Returns true (with the parsed value in <paramref name="result"/>) once, when the
        /// user commits a changed value with Enter or by leaving the field.
        /// A typed value belongs to what the field showed while it was typed: if the field wasn't drawn last
        /// frame, or <paramref name="owner"/> changed (another selection), the unfinished edit is dropped
        /// instead of being applied to whatever the field shows now.
        /// </summary>
        public static bool FloatField(Rect r, string controlName, float value, out float result, string format = "0.###", object owner = null)
        {
            result = value;
            int frame = Time.frameCount;
            bool skippedFrames = numberFieldFrames.TryGetValue(controlName, out int lastFrame) && frame - lastFrame > 1;
            bool ownerChanged = numberFieldOwners.TryGetValue(controlName, out object lastOwner) && !Equals(lastOwner, owner);
            if (skippedFrames || ownerChanged)
            {
                focusedNumberFields.Remove(controlName);
                numberBuffers.Remove(controlName);
                if (IsFocused(controlName))
                    GUIUtility.keyboardControl = 0;
            }
            numberFieldFrames[controlName] = frame;
            numberFieldOwners[controlName] = owner;

            string shown = value.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
            bool focused = IsFocused(controlName);
            bool committed = false;
            string buffer;

            if (!focused)
            {
                if (focusedNumberFields.Remove(controlName) && numberBuffers.TryGetValue(controlName, out buffer)
                    && TryParseNumber(buffer, out float typed) && buffer != shown)
                {
                    result = typed;
                    committed = true;
                }
                numberBuffers[controlName] = shown;
            }
            else
            {
                focusedNumberFields.Add(controlName);
            }

            if (SubmitPressed(controlName))
            {
                if (numberBuffers.TryGetValue(controlName, out buffer) && TryParseNumber(buffer, out float typed) && buffer != shown)
                {
                    result = typed;
                    committed = true;
                }
                focusedNumberFields.Remove(controlName);
                GUIUtility.keyboardControl = 0;
                Event.current.Use();
            }

            numberBuffers.TryGetValue(controlName, out buffer);
            numberBuffers[controlName] = TextField(r, buffer ?? shown, controlName, null, 32, null,
                                                   c => char.IsDigit(c) || c == '.' || c == '-' || c == 'e' || c == 'E' || c == '+');
            return committed;
        }

        public static bool IntField(Rect r, string controlName, int value, out int result, object owner = null)
        {
            result = value;
            if (!FloatField(r, controlName, value, out float typed, "0", owner))
                return false;
            result = Mathf.RoundToInt(typed);
            return result != value;
        }

        private static bool TryParseNumber(string text, out float value)
        {
            return float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value)
                   && !float.IsNaN(value) && !float.IsInfinity(value);
        }

        /// <summary>A labelled row: label on the left, returns the field rect on the right.</summary>
        public static Rect LabeledRow(Rect row, string label, float labelWidth)
        {
            Label(row.TakeLeft(labelWidth), label, T.Small, Palette.TextFaint);
            return row.CutLeft(labelWidth);
        }

        /// <summary>True if Enter was pressed while the named field had focus.</summary>
        public static bool SubmitPressed(string controlName)
        {
            var e = Event.current;
            return e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                && GUI.GetNameOfFocusedControl() == controlName;
        }

        // Decorations
        public static void SectionHeader(Rect r, string text)
        {
            Label(r, text.ToUpperInvariant(), T.Caption, Palette.TextFaint);
        }

        public static void Badge(Rect r, string text, Color background, Color foreground)
        {
            Rounded(r, background, r.height * 0.5f);
            Label(r, text, T.SmallStrong, foreground, TextAnchor.MiddleCenter);
        }

        public static float BadgeWidth(string text) => TextWidth(text, T.SmallStrong) + T.S(14f);

        /// <summary>A keyboard key cap, e.g. for shortcut lists.</summary>
        public static float KeyCaps(Rect r, string binding, bool alignRight = true)
        {
            if (string.IsNullOrEmpty(binding))
                return 0f;

            string[] parts = binding.Split('+');
            float h = Mathf.Min(r.height, T.S(20f));
            float gap = T.S(3f);
            float total = 0f;
            var widths = new float[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                widths[i] = Mathf.Max(h, TextWidth(parts[i], T.Mono) + T.S(10f));
                total += widths[i] + (i > 0 ? gap : 0f);
            }

            float x = alignRight ? r.xMax - total : r.x;
            float y = Mathf.Round(r.center.y - h * 0.5f);
            for (int i = 0; i < parts.Length; i++)
            {
                var cap = new Rect(x, y, widths[i], h);
                RoundedOutline(cap, Palette.Raised, Palette.BorderStrong, T.S(4f));
                Label(cap, parts[i], T.Mono, Palette.TextDim, TextAnchor.MiddleCenter);
                x += widths[i] + gap;
            }
            return total;
        }

        public static void ProgressBar(Rect r, float t, Color? color = null)
        {
            Rounded(r, Palette.Pressed, r.height * 0.5f);
            if (t > 0f)
                Rounded(new Rect(r.x, r.y, Mathf.Max(r.height, r.width * Mathf.Clamp01(t)), r.height), color ?? Palette.Accent, r.height * 0.5f);
        }

        // Scrolling
        /// <summary>
        /// Starts a clipped, vertically scrolling region. Draw content in the returned rect
        /// (local coordinates, already offset by the scroll position), then call <see cref="EndScroll"/>.
        /// </summary>
        public static Rect BeginScroll(Rect view, ref float offset, float contentHeight)
        {
            float max = Mathf.Max(0f, contentHeight - view.height);
            var e = Event.current;
            if (e.type == EventType.ScrollWheel && PointerIn(view) && max > 0f)
            {
                offset += e.delta.y * T.S(14f);
                e.Use();
            }
            offset = Mathf.Clamp(offset, 0f, max);

            GUI.BeginGroup(view);
            var local = new Rect(0f, 0f, view.width, view.height);
            if (clipStack.Count > 0)
            {
                // intersect with the parent clip, translated into this group's coordinates
                var parent = clipStack.Peek();
                parent.position -= view.position;
                local = Intersect(local, parent);
            }
            clipStack.Push(local);

            float scrollbar = max > 0f ? T.S(10f) : 0f;
            return new Rect(0f, -offset, view.width - scrollbar, contentHeight);
        }

        public static void EndScroll(Rect view, ref float offset, float contentHeight)
        {
            clipStack.Pop();
            GUI.EndGroup();

            float max = Mathf.Max(0f, contentHeight - view.height);
            int id = GUIUtility.GetControlID(FocusType.Passive);
            if (max <= 0f)
                return;

            float trackWidth = T.S(10f);
            var track = new Rect(view.xMax - trackWidth, view.y, trackWidth, view.height);
            float thumbHeight = Mathf.Max(T.S(28f), view.height * (view.height / contentHeight));
            float thumbY = view.y + (view.height - thumbHeight) * (offset / max);
            var thumb = new Rect(track.x + T.S(2f), thumbY + T.S(2f), trackWidth - T.S(4f), thumbHeight - T.S(4f));

            var e = Event.current;
            bool hover = PointerIn(track);
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (hover && e.button == 0)
                    {
                        GUIUtility.hotControl = id;
                        if (!thumb.Contains(e.mousePosition))
                        {
                            // jump to the clicked position
                            float t = Mathf.Clamp01((e.mousePosition.y - view.y - thumbHeight * 0.5f) / (view.height - thumbHeight));
                            offset = t * max;
                        }
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id)
                    {
                        offset = Mathf.Clamp(offset + e.delta.y * (max / Mathf.Max(1f, view.height - thumbHeight)), 0f, max);
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        e.Use();
                    }
                    break;
                case EventType.Repaint:
                    bool active = GUIUtility.hotControl == id;
                    Rounded(thumb, active ? Palette.TextFaint : hover ? Palette.BorderStrong.WithAlpha(1f) : Palette.BorderStrong.WithAlpha(0.8f), thumb.width * 0.5f);
                    break;
            }
        }

        /// <summary>Scrolls so the given content range is visible.</summary>
        public static void ScrollIntoView(ref float offset, float itemY, float itemHeight, float viewHeight)
        {
            if (itemY < offset)
                offset = itemY;
            else if (itemY + itemHeight > offset + viewHeight)
                offset = itemY + itemHeight - viewHeight;
        }

        private static Rect Intersect(Rect a, Rect b)
        {
            float x = Mathf.Max(a.x, b.x);
            float y = Mathf.Max(a.y, b.y);
            float xMax = Mathf.Min(a.xMax, b.xMax);
            float yMax = Mathf.Min(a.yMax, b.yMax);
            return new Rect(x, y, Mathf.Max(0f, xMax - x), Mathf.Max(0f, yMax - y));
        }

        private static Vector2 topLevelOffset;

        public static void ResetFrameState()
        {
            clipStack.Clear();
            InputBlocked = false;
            // where top level GUI coordinates sit in "screen" space (non-zero in the editor's game view)
            topLevelOffset = GUIUtility.GUIToScreenPoint(Vector2.zero);
        }

        /// <summary>Converts a point inside groups/scroll areas to top level GUI coordinates (for popups).</summary>
        public static Vector2 LocalToTopLevel(Vector2 local) => GUIUtility.GUIToScreenPoint(local) - topLevelOffset;

        // Tooltips
        /// <summary>Registers a tooltip for <paramref name="r"/>; shown after a short hover.</summary>
        public static void Tooltip(Rect r, string text)
        {
            if (!IsRepaint || string.IsNullOrEmpty(text) || GUIUtility.hotControl != 0)
                return;
            if (PointerIn(r))
            {
                tooltipCandidate = text;
                // store in screen space so it can be drawn outside groups
                var topLeft = GUIUtility.GUIToScreenPoint(r.position);
                tooltipCandidateRect = new Rect(topLeft, r.size);
            }
        }

        public static void BeginTooltipFrame()
        {
            if (IsRepaint)
                tooltipCandidate = null;
        }

        /// <summary>Draws the hovered tooltip (call last in OnGUI, outside any group).</summary>
        public static void DrawTooltip()
        {
            if (!IsRepaint)
                return;

            if (tooltipCandidate != tooltipShown)
            {
                tooltipShown = tooltipCandidate;
                tooltipHoverStart = Time.unscaledTime;
            }

            if (tooltipShown == null || Time.unscaledTime - tooltipHoverStart < 0.45f)
                return;

            var style = T.Small;
            float maxWidth = T.S(320f);
            var content = new GUIContent(tooltipShown);
            float width = Mathf.Min(maxWidth, style.CalcSize(content).x) + T.S(16f);
            float height = T.S(26f);
            bool wraps = width - T.S(16f) >= maxWidth;
            if (wraps)
                height = T.SmallWrapped.CalcHeight(content, width - T.S(16f)) + T.S(12f);

            var anchor = new Rect(GUIUtility.ScreenToGUIPoint(tooltipCandidateRect.position), tooltipCandidateRect.size);
            float x = Mathf.Clamp(anchor.center.x - width * 0.5f, T.S(4f), Screen.width - width - T.S(4f));
            float y = anchor.yMax + T.S(6f);
            if (y + height > Screen.height - T.S(4f))
                y = anchor.y - height - T.S(6f);

            var rect = new Rect(Mathf.Round(x), Mathf.Round(y), Mathf.Round(width), Mathf.Round(height));
            Shadow(rect, T.S(6f));
            RoundedOutline(rect, Palette.Overlay, Palette.BorderStrong);
            if (wraps)
                Label(rect.Inset(T.S(8f), T.S(6f)), tooltipShown, T.SmallWrapped, Palette.Text);
            else
                Label(rect, tooltipShown, T.Small, Palette.Text, TextAnchor.MiddleCenter);
        }
    }
}

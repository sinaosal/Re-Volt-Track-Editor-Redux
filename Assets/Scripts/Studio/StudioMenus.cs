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
    public sealed class MenuItem
    {
        public string Label;
        public string Shortcut;
        public Action Action;
        public bool Enabled = true;
        public bool Checked;
        public bool IsCheckable;
        public bool IsSeparator;
        public IconId? Icon;
        public List<MenuItem> Submenu;

        public static MenuItem Separator() => new MenuItem { IsSeparator = true };

        public static MenuItem Command(StudioCommand command, string labelOverride = null)
        {
            return new MenuItem
            {
                Label = labelOverride ?? command.Label,
                Shortcut = command.BindingText,
                Action = () => command.Invoke(),
                Enabled = command.Enabled,
                Checked = command.Checked,
                IsCheckable = command.IsChecked != null,
                Icon = command.Icon
            };
        }

        public static MenuItem Sub(string label, List<MenuItem> items, IconId? icon = null)
        {
            return new MenuItem { Label = label, Submenu = items, Enabled = items.Count > 0, Icon = icon };
        }
    }

    /// <summary>
    /// A dropdown or context menu with optional one-level submenus.
    /// </summary>
    public sealed class PopupMenu
    {
        public readonly List<MenuItem> Items;
        public Rect Rect;
        private PopupMenu child;
        private int childIndex = -1;
        private int keyboardIndex = -1;

        public PopupMenu(List<MenuItem> items)
        {
            Items = items;
        }

        private static float ItemHeight(StudioTheme T) => T.S(28f);
        private static float SeparatorHeight(StudioTheme T) => T.S(9f);

        /// <summary>Sizes the menu and positions it at <paramref name="anchor"/>, kept on screen.</summary>
        public void Layout(StudioTheme T, Vector2 anchor, float minWidth = 0f, Rect? avoid = null)
        {
            float labelWidth = 0f, shortcutWidth = 0f;
            float height = T.S(8f);
            foreach (var item in Items)
            {
                if (item.IsSeparator)
                {
                    height += SeparatorHeight(T);
                    continue;
                }
                labelWidth = Mathf.Max(labelWidth, SGUI.TextWidth(item.Label, T.Body));
                if (!string.IsNullOrEmpty(item.Shortcut))
                    shortcutWidth = Mathf.Max(shortcutWidth, SGUI.TextWidth(item.Shortcut, T.Shortcut));
                height += ItemHeight(T);
            }

            float width = T.S(36f) + labelWidth + (shortcutWidth > 0f ? T.S(36f) + shortcutWidth : T.S(24f)) + T.S(14f);
            width = Mathf.Max(width, minWidth, T.S(180f));

            float x = anchor.x, y = anchor.y;
            if (x + width > Screen.width - T.S(4f))
                x = avoid.HasValue ? avoid.Value.x - width : Screen.width - width - T.S(4f);
            if (y + height > Screen.height - T.S(4f))
                y = Mathf.Max(T.S(4f), Screen.height - height - T.S(4f));

            Rect = new Rect(Mathf.Round(Mathf.Max(T.S(4f), x)), Mathf.Round(y), Mathf.Round(width), Mathf.Round(height));
        }

        /// <summary>True if the point is inside this menu or its open submenu.</summary>
        public bool Contains(Vector2 point)
        {
            return Rect.Contains(point) || (child != null && child.Contains(point));
        }

        /// <summary>
        /// Draws and handles the menu. Returns true when an item was chosen (the caller closes the menu).
        /// </summary>
        public bool Draw(StudioTheme T)
        {
            var e = Event.current;
            HandleKeyboard(e);

            SGUI.Shadow(Rect);
            SGUI.RoundedOutline(Rect, Palette.Overlay, Palette.BorderStrong, T.S(7f));

            bool chosen = false;
            float y = Rect.y + T.S(4f);
            for (int i = 0; i < Items.Count; i++)
            {
                var item = Items[i];
                if (item.IsSeparator)
                {
                    SGUI.HLine(Rect.x + T.S(8f), Mathf.Round(y + SeparatorHeight(T) * 0.5f), Rect.width - T.S(16f), Palette.Border);
                    y += SeparatorHeight(T);
                    continue;
                }

                var row = new Rect(Rect.x + T.S(4f), y, Rect.width - T.S(8f), ItemHeight(T));
                y += ItemHeight(T);

                bool clicked = SGUI.ClickBehavior(row, item.Enabled, out bool hover, out bool pressed);
                bool highlighted = item.Enabled && (hover || keyboardIndex == i || (childIndex == i && child != null));

                if (hover)
                    keyboardIndex = i;

                if (highlighted && e.type == EventType.Repaint)
                    SGUI.Rounded(row, pressed ? Palette.AccentPressed : Palette.Accent.WithAlpha(0.9f), T.S(4f));

                Color fg = !item.Enabled ? Palette.TextFaint : highlighted ? Palette.TextOnAccent : Palette.Text;
                var iconRect = new Rect(row.x + T.S(4f), row.y, T.S(22f), row.height);
                if (item.IsCheckable)
                {
                    if (item.Checked)
                        SGUI.Icon(iconRect, IconId.Check, fg, T.S(14f));
                }
                else if (item.Icon.HasValue)
                {
                    SGUI.Icon(iconRect, item.Icon.Value, highlighted ? fg : Palette.TextDim, T.S(15f));
                }

                SGUI.Label(new Rect(row.x + T.S(30f), row.y, row.width - T.S(30f), row.height), item.Label, T.Body, fg);

                if (item.Submenu != null)
                {
                    SGUI.Icon(row.TakeRight(T.S(22f)), IconId.ChevronRight, fg, T.S(12f));
                    if ((hover || (keyboardIndex == i && child == null && e.type == EventType.KeyDown)) && item.Enabled && childIndex != i)
                        OpenChild(T, i, row);
                }
                else if (!string.IsNullOrEmpty(item.Shortcut))
                {
                    SGUI.Label(new Rect(row.x, row.y, row.width - T.S(10f), row.height), item.Shortcut, T.Shortcut,
                               highlighted ? Palette.TextOnAccent.WithAlpha(0.85f) : Palette.TextFaint);
                }

                if (hover && item.Submenu == null && child != null)
                {
                    child = null;
                    childIndex = -1;
                }

                if (clicked && item.Enabled)
                {
                    if (item.Submenu != null)
                    {
                        OpenChild(T, i, row);
                    }
                    else
                    {
                        Activate(item);
                        chosen = true;
                    }
                }
            }

            if (child != null)
            {
                if (child.Draw(T))
                    chosen = true;
                if (child.PendingClose)
                    PendingClose = true;
            }

            return chosen;
        }

        private void OpenChild(StudioTheme T, int index, Rect row)
        {
            childIndex = index;
            child = new PopupMenu(Items[index].Submenu);
            child.Layout(T, new Vector2(Rect.xMax - T.S(2f), row.y - T.S(4f)), 0f, Rect);
        }

        private static void Activate(MenuItem item)
        {
            try
            {
                item.Action?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Menu action '{item.Label}' failed: {ex}");
            }
        }

        private void HandleKeyboard(Event e)
        {
            if (e.type != EventType.KeyDown || child != null)
                return;

            switch (e.keyCode)
            {
                case KeyCode.DownArrow:
                    keyboardIndex = NextSelectable(keyboardIndex, 1);
                    e.Use();
                    break;
                case KeyCode.UpArrow:
                    keyboardIndex = NextSelectable(keyboardIndex, -1);
                    e.Use();
                    break;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    if (keyboardIndex >= 0 && keyboardIndex < Items.Count && Items[keyboardIndex].Enabled && Items[keyboardIndex].Submenu == null)
                    {
                        Activate(Items[keyboardIndex]);
                        PendingClose = true;
                    }
                    e.Use();
                    break;
            }
        }

        /// <summary>Set when the menu chose an item from the keyboard.</summary>
        public bool PendingClose;

        private int NextSelectable(int from, int direction)
        {
            int count = Items.Count;
            for (int step = 1; step <= count; step++)
            {
                int index = ((from + direction * step) % count + count) % count;
                if (!Items[index].IsSeparator && Items[index].Enabled)
                    return index;
            }
            return from;
        }
    }
}

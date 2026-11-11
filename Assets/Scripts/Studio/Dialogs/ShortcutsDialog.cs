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
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Studio
{
    /// <summary>
    /// Lists every action with its shortcut; rebindable shortcuts can be changed by clicking them.
    /// Also documents the fixed mouse, keyboard, and gamepad controls.
    /// </summary>
    public sealed class ShortcutsDialog : StudioDialog
    {
        private const string SearchName = "studio.shortcuts.search";

        private static readonly (string Action, string Keys)[] MouseControls =
        {
            ("Place module / paint", "Left mouse"),
            ("Pick module under pointer", "Alt+Left mouse"),
            ("Orbit camera", "Right drag"),
            ("Context menu", "Right click"),
            ("Pan camera", "Middle drag"),
            ("Zoom (towards the pointer)", "Wheel"),
            ("Select block (select tool)", "Left click"),
            ("Add to selection", "Shift+Left click"),
            ("Toggle in selection", "Ctrl+Left click"),
            ("Box select", "Left drag on empty space"),
            ("Move selection", "Left drag a selected block"),
            ("Move along one axis / change level", "Drag a gizmo arrow"),
            ("Place pasted blocks", "Left click (Esc or right click ends)"),
            ("Look somewhere else", "Click the minimap"),
        };

        private static readonly (string Action, string Keys)[] GamepadControls =
        {
            ("Move cursor", "D-Pad / Left stick"),
            ("Place module", "A"),
            ("Delete module", "B"),
            ("Raise / lower", "Y / X"),
            ("Rotate module", "Triggers"),
            ("Variants", "Bumpers"),
            ("Orbit camera", "Right stick"),
        };

        private string search = string.Empty;
        private float scroll;
        private StudioCommand capturing;
        private int captureEndedFrame = -1;
        private string notice;
        private bool noticeIsError;

        public ShortcutsDialog()
        {
            Title = "Keyboard Shortcuts & Controls";
            TitleIcon = IconId.Keyboard;
        }

        public override float Width => T.S(620f);
        public override float BodyHeight(float width) => T.S(500f);

        public override void Update()
        {
            if (capturing == null || !(Keyboard.current is Keyboard keyboard))
                return;

            foreach (var key in keyboard.allKeys)
            {
                if (key == null || !key.wasPressedThisFrame || KeyBinding.IsModifierKey(key.keyCode))
                    continue;

                var code = KeyBinding.LogicalKey(key);
                captureEndedFrame = Time.frameCount;

                if (code == Key.Escape)
                {
                    capturing = null;
                    notice = null;
                    return;
                }

                if (code == Key.Backspace && !Controls.IsCommandModifierHeld())
                {
                    capturing.Binding = KeyBinding.None;
                    Shell.Commands.SaveBindings();
                    notice = $"\"{capturing.Label}\" no longer has a shortcut.";
                    noticeIsError = false;
                    capturing = null;
                    return;
                }

                var binding = new KeyBinding(code, Controls.IsControlHeld(), Controls.IsShiftHeld(), Controls.IsAltHeld());
                if (!binding.HasModifier && KeyBinding.IsReservedEditingKey(code))
                {
                    notice = $"{binding} is used by the editing controls. Add Ctrl or Alt to use it.";
                    noticeIsError = true;
                    return; // keep capturing
                }

                var conflict = Shell.Commands.FindByBinding(binding, capturing);
                if (conflict != null)
                {
                    conflict.Binding = KeyBinding.None;
                    notice = $"{binding} moved from \"{conflict.Label}\" to \"{capturing.Label}\".";
                    noticeIsError = false;
                }
                else
                {
                    notice = $"\"{capturing.Label}\" is now {binding}.";
                    noticeIsError = false;
                }

                capturing.Binding = binding;
                Shell.Commands.SaveBindings();
                capturing = null;
                return;
            }
        }

        /// <summary>True while waiting for a key press (the shell suspends shortcuts meanwhile).</summary>
        public bool IsCapturing => capturing != null;

        public override void Cancel()
        {
            // the key that ended a capture must not also close the dialog
            if (capturing != null || captureEndedFrame == Time.frameCount)
                return;
            Close();
        }

        public override bool Submit()
        {
            if (capturing != null || captureEndedFrame == Time.frameCount)
                return true;
            return false;
        }

        private bool Matches(string text)
        {
            return string.IsNullOrWhiteSpace(search) || text.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public override void DrawBody(Rect rect)
        {
            var searchRect = rect.TakeTop(T.S(32f));
            search = SGUI.SearchField(searchRect, search, SearchName, "Search actions or keys");
            if (FrameCount == 0)
                GUI.FocusControl(SearchName);

            var noticeRect = new Rect(rect.x, searchRect.yMax + T.S(6f), rect.width, T.S(20f));
            if (capturing != null)
            {
                SGUI.Icon(noticeRect.TakeLeft(T.S(16f)), IconId.Keyboard, Palette.Accent, T.S(14f));
                SGUI.Label(noticeRect.CutLeft(T.S(22f)), $"Press the new shortcut for \"{capturing.Label}\" · Esc cancels · Backspace removes", T.Small, Palette.Accent);
            }
            else if (notice != null)
            {
                SGUI.Icon(noticeRect.TakeLeft(T.S(16f)), noticeIsError ? IconId.Warning : IconId.Check, noticeIsError ? Palette.Warning : Palette.Success, T.S(14f));
                SGUI.Label(noticeRect.CutLeft(T.S(22f)), notice, T.Small, noticeIsError ? Palette.Warning : Palette.TextDim);
            }
            else
            {
                SGUI.Label(noticeRect, "Click a shortcut to change it.", T.Small, Palette.TextFaint);
            }

            var listRect = new Rect(rect.x, noticeRect.yMax + T.S(8f), rect.width, rect.yMax - noticeRect.yMax - T.S(8f));
            SGUI.RoundedOutline(listRect, Palette.Chrome, Palette.Border);
            var inner = listRect.Inset(T.S(1f));

            // build rows
            var rows = new List<(string header, StudioCommand command, string label, string keys)>();
            foreach (var group in Shell.Commands.All.GroupBy(x => x.Category))
            {
                var items = group.Where(c => Matches(c.Label) || Matches(c.BindingText)).ToList();
                if (items.Count == 0)
                    continue;
                rows.Add((group.Key, null, null, null));
                foreach (var command in items)
                    rows.Add((null, command, command.Label, command.BindingText));
            }

            var mouse = MouseControls.Where(x => Matches(x.Action) || Matches(x.Keys)).ToList();
            if (mouse.Count > 0)
            {
                rows.Add(("Mouse", null, null, null));
                foreach (var m in mouse)
                    rows.Add((null, null, m.Action, m.Keys));
            }

            var pad = GamepadControls.Where(x => Matches(x.Action) || Matches(x.Keys)).ToList();
            if (pad.Count > 0)
            {
                rows.Add(("Gamepad", null, null, null));
                foreach (var g in pad)
                    rows.Add((null, null, g.Action, g.Keys));
            }

            float rowHeight = T.S(32f);
            float headerHeight = T.S(30f);
            float contentHeight = rows.Sum(r => r.header != null ? headerHeight : rowHeight) + T.S(6f);

            var content = SGUI.BeginScroll(inner, ref scroll, contentHeight);
            float y = content.y;
            foreach (var row in rows)
            {
                if (row.header != null)
                {
                    SGUI.SectionHeader(new Rect(T.S(14f), y + T.S(10f), content.width - T.S(28f), T.S(18f)), row.header);
                    y += headerHeight;
                    continue;
                }

                var r = new Rect(content.x, y, content.width, rowHeight);
                y += rowHeight;

                var keysRect = new Rect(r.xMax - T.S(196f), r.y, T.S(182f), r.height);
                bool rebindable = row.command != null && row.command.Rebindable;

                if (rebindable)
                {
                    if (SGUI.ClickBehavior(keysRect, true, out bool hover, out bool pressed))
                    {
                        capturing = row.command;
                        notice = null;
                        GUIUtility.keyboardControl = 0;
                    }

                    bool isCapturing = capturing == row.command;
                    if (isCapturing)
                        SGUI.RoundedOutline(keysRect.Inset(0f, T.S(3f)), Palette.AccentSoft, Palette.Accent);
                    else if (hover)
                        SGUI.Rounded(keysRect.Inset(0f, T.S(3f)), Palette.Hover);

                    if (isCapturing)
                        SGUI.Label(keysRect, "Press keys…", T.Small, Palette.Accent, TextAnchor.MiddleCenter);
                    else if (string.IsNullOrEmpty(row.keys))
                        SGUI.Label(keysRect.Inset(T.S(8f), 0f), "Not set", T.Small, Palette.TextFaint, TextAnchor.MiddleRight);
                    else
                        SGUI.KeyCaps(keysRect.Inset(T.S(6f), T.S(5f)), row.keys);

                    SGUI.Tooltip(keysRect, "Click to change");
                }
                else
                {
                    SGUI.Label(keysRect.Inset(T.S(6f), 0f), row.keys, T.Small, Palette.TextDim, TextAnchor.MiddleRight);
                }

                SGUI.Label(new Rect(r.x + T.S(14f), r.y, r.width - T.S(230f), r.height), row.label, T.Body, Palette.Text);
                if (row.command != null && !row.command.Rebindable)
                    SGUI.Label(new Rect(r.x + T.S(14f) + SGUI.TextWidth(row.label, T.Body) + T.S(8f), r.y, T.S(60f), r.height), "fixed", T.Caption, Palette.TextFaint);
            }
            SGUI.EndScroll(inner, ref scroll, contentHeight);
        }

        public override void DrawFooter(Rect rect)
        {
            if (SGUI.Button(FooterButton(ref rect, "Done"), "Done", ButtonKind.Primary))
                Close();

            var left = new Rect(rect.x, rect.y + (rect.height - T.ControlHeight) * 0.5f, T.S(150f), T.ControlHeight);
            if (SGUI.Button(left, "Reset to defaults", ButtonKind.Ghost))
            {
                Shell.OpenDialog(new ConfirmDialog("Reset shortcuts?", "All keyboard shortcuts will be restored to their defaults.", null, 0,
                    new ConfirmDialog.Choice("Reset", ButtonKind.Danger, () =>
                    {
                        Shell.Commands.ResetAllBindings();
                        notice = "All shortcuts were reset to their defaults.";
                        noticeIsError = false;
                    }),
                    new ConfirmDialog.Choice("Cancel", ButtonKind.Secondary, null)));
            }
        }
    }
}

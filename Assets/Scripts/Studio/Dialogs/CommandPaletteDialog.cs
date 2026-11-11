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

namespace Studio
{
    /// <summary>
    /// Ctrl+Shift+P: find any Studio command by name, category, description or key, and run it.
    /// Lists exactly the commands in the registry, so it can't offer anything that doesn't exist.
    /// </summary>
    public sealed class CommandPaletteDialog : StudioDialog
    {
        private const string SearchName = "studio.palette.search";

        // most recently run first, for this session
        private static readonly List<string> recent = new List<string>();

        private string search = string.Empty;
        private string lastSearch;
        private List<StudioCommand> visible = new List<StudioCommand>();
        private int selected;
        private float scroll;
        private float listViewHeight;

        public CommandPaletteDialog()
        {
            Title = "Command Palette";
            TitleIcon = IconId.Command;
            TitleIconColor = Palette.Accent;
        }

        public override float Width => T.S(600f);
        public override bool HasFooter => false;
        public override float BodyHeight(float width) => T.S(440f);

        private float RowHeight => T.S(40f);

        private static bool IsRunnable(StudioCommand c) => c.Execute != null;

        private void Filter()
        {
            if (search == lastSearch)
                return;
            lastSearch = search;

            var terms = (search ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var scored = new List<(StudioCommand Command, int Score)>();
            foreach (var command in Shell.Commands.All)
            {
                if (!IsRunnable(command))
                    continue;
                string haystack = $"{command.Category} {command.Label} {command.Description} {command.BindingText}";
                int score = 0;
                bool all = true;
                foreach (var term in terms)
                {
                    int at = haystack.IndexOf(term, StringComparison.OrdinalIgnoreCase);
                    if (at < 0)
                    {
                        all = false;
                        break;
                    }
                    // label matches beat category/description matches; word starts beat the middle of words
                    int inLabel = command.Label.IndexOf(term, StringComparison.OrdinalIgnoreCase);
                    if (inLabel == 0)
                        score += 40;
                    else if (inLabel > 0)
                        score += command.Label[inLabel - 1] == ' ' ? 30 : 20;
                    else
                        score += 5;
                }
                if (!all)
                    continue;
                int recency = recent.IndexOf(command.Id);
                if (recency >= 0)
                    score += 15 - Math.Min(recency, 10);
                scored.Add((command, score));
            }

            visible = scored
                .OrderByDescending(x => terms.Length == 0 ? (recent.Contains(x.Command.Id) ? 100 - recent.IndexOf(x.Command.Id) : 0) : x.Score)
                .ThenByDescending(x => x.Command.Enabled)
                .ThenBy(x => x.Command.Category)
                .ThenBy(x => x.Command.Label)
                .Select(x => x.Command)
                .ToList();
            selected = visible.Count > 0 ? 0 : -1;
            scroll = 0f;
        }

        public override void DrawBody(Rect rect)
        {
            HandleKeys();

            var searchRect = rect.TakeTop(T.S(34f));
            search = SGUI.SearchField(searchRect, search, SearchName, "Type a command, e.g. \"duplicate\", \"top view\" or \"Ctrl+L\"");
            if (FrameCount == 0)
                GUI.FocusControl(SearchName);
            Filter();

            var listRect = new Rect(rect.x, searchRect.yMax + T.S(10f), rect.width, rect.height - searchRect.height - T.S(10f));
            SGUI.RoundedOutline(listRect, Palette.Chrome, Palette.Border);
            var inner = listRect.Inset(T.S(1f));
            listViewHeight = inner.height;

            if (visible.Count == 0)
            {
                SGUI.Label(new Rect(inner.x, inner.center.y - T.S(10f), inner.width, T.S(20f)), "No commands match", T.BodyStrong, Palette.TextDim, TextAnchor.MiddleCenter);
                return;
            }

            float contentHeight = visible.Count * RowHeight;
            var content = SGUI.BeginScroll(inner, ref scroll, contentHeight);
            for (int i = 0; i < visible.Count; i++)
            {
                var command = visible[i];
                var row = new Rect(content.x, content.y + i * RowHeight, content.width, RowHeight);
                bool enabled = command.Enabled;

                if (SGUI.RowBehavior(row, true, out bool hover, out _))
                {
                    selected = i;
                    Run(command);
                    return;
                }
                if (row.yMax < 0f || row.y > inner.height)
                    continue;

                bool isSelected = i == selected;
                if (isSelected)
                    SGUI.Fill(row, Palette.AccentSoft);
                else if (hover)
                    SGUI.Fill(row, Palette.Hover.WithAlpha(0.6f));

                var iconRect = new Rect(row.x + T.S(12f), row.y, T.S(18f), row.height);
                if (command.Icon.HasValue)
                    SGUI.Icon(iconRect, command.Icon.Value, enabled ? (isSelected ? Palette.Accent : Palette.TextDim) : Palette.TextFaint, T.S(16f));
                else if (command.IsChecked != null)
                    SGUI.Icon(iconRect, command.Checked ? IconId.Check : IconId.Dot, command.Checked ? Palette.Accent : Palette.TextFaint, T.S(command.Checked ? 14f : 8f));

                float keysWidth = SGUI.TextWidth(command.BindingText, T.Mono) + T.S(40f);
                var textRect = new Rect(row.x + T.S(42f), row.y + T.S(4f), row.width - T.S(54f) - keysWidth, T.S(18f));
                string label = command.Label + (command.IsChecked != null ? (command.Checked ? "  (on)" : "  (off)") : string.Empty);
                SGUI.Label(textRect, SGUI.Ellipsize(label, T.Body, textRect.width), T.Body, enabled ? Palette.Text : Palette.TextFaint);
                string detail = string.IsNullOrEmpty(command.Description) ? command.Category : $"{command.Category} · {command.Description}";
                if (!enabled)
                    detail = "Not available right now · " + detail;
                SGUI.Label(new Rect(textRect.x, textRect.yMax, textRect.width, T.S(16f)), SGUI.Ellipsize(detail, T.Small, textRect.width), T.Small, Palette.TextFaint);

                SGUI.KeyCaps(new Rect(row.xMax - keysWidth - T.S(10f), row.y, keysWidth, row.height), command.BindingText);
            }
            SGUI.EndScroll(inner, ref scroll, contentHeight);
        }

        private void HandleKeys()
        {
            var e = Event.current;
            if (SGUI.InputBlocked || e.type != EventType.KeyDown || visible.Count == 0)
                return;

            if (e.keyCode == KeyCode.DownArrow)
            {
                selected = Mathf.Min(visible.Count - 1, selected + 1);
                SGUI.ScrollIntoView(ref scroll, selected * RowHeight, RowHeight, listViewHeight);
                e.Use();
            }
            else if (e.keyCode == KeyCode.UpArrow)
            {
                selected = Mathf.Max(0, selected - 1);
                SGUI.ScrollIntoView(ref scroll, selected * RowHeight, RowHeight, listViewHeight);
                e.Use();
            }
            else if (e.keyCode == KeyCode.PageDown)
            {
                selected = Mathf.Min(visible.Count - 1, selected + 8);
                SGUI.ScrollIntoView(ref scroll, selected * RowHeight, RowHeight, listViewHeight);
                e.Use();
            }
            else if (e.keyCode == KeyCode.PageUp)
            {
                selected = Mathf.Max(0, selected - 8);
                SGUI.ScrollIntoView(ref scroll, selected * RowHeight, RowHeight, listViewHeight);
                e.Use();
            }
        }

        public override bool Submit()
        {
            if (selected >= 0 && selected < visible.Count)
                Run(visible[selected]);
            return true;
        }

        private void Run(StudioCommand command)
        {
            if (!command.Enabled)
            {
                TrackEditor.PlaySound(TrackEditor.SndWarning);
                return;
            }
            recent.Remove(command.Id);
            recent.Insert(0, command.Id);
            Close();
            // run after the dialog has closed, so commands that open dialogs or menus work normally
            Shell.Defer(() => command.Invoke());
        }
    }
}

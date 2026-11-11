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

namespace Studio
{
    /// <summary>
    /// Every placed block in a list: find, select (synced with the viewport), lock and hide. The list is
    /// rebuilt from the track whenever it changes; it never holds track data of its own.
    /// </summary>
    public sealed class OutlinerPanel
    {
        private const string SearchName = "studio.outliner.search";

        private enum Order
        {
            Track,     // following the connections from the start grid
            Type,
            Position
        }

        private struct Row
        {
            public bool IsHeader;
            public string Header;
            public ModulePlacement Placement;
            public Vector2Int Root;
            public bool HasOpenEnd;
            public bool Detached;   // not connected to the start grid's section
        }

        private readonly StudioShell shell;
        private readonly List<Row> rows = new List<Row>();
        private string search = string.Empty;
        private Order order = Order.Track;
        private float scroll;
        private (int revision, string search, Order order, int hidden, int locked) builtKey = (-1, null, Order.Track, -1, -1);
        private int seenSelectionVersion = -1;
        private int rangeAnchor = -1;

        private StudioTheme T => shell.Theme;
        private StudioViewport Viewport => shell.Viewport;

        public OutlinerPanel(StudioShell shell)
        {
            this.shell = shell;
        }

        private void Rebuild()
        {
            var key = (shell.TrackRevision, search, order, Viewport.States.HiddenCount, Viewport.States.LockedCount);
            if (builtKey.Equals(key))
                return;
            builtKey = key;
            rows.Clear();

            var report = ConnectivityService.Current(shell.TrackRevision);
            var placements = ConnectivityService.Placements;
            if (report == null)
                return;

            var openEnds = new HashSet<int>();
            foreach (var joint in report.Joints)
            {
                if (joint.Kind == JointKind.Open)
                    openEnds.Add(joint.A.Placement);
            }

            IEnumerable<int> indices = Enumerable.Range(0, placements.Count);
            if (!string.IsNullOrWhiteSpace(search))
            {
                indices = indices.Where(i =>
                {
                    var p = placements[i];
                    return ModuleCatalog.GetDisplayName(p.ModuleIndex).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                        || ModuleCatalog.GetCodeName(p.ModuleIndex).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                        || $"{p.RootCell.Position.x}, {p.RootCell.Position.y}".Contains(search.Trim());
                });
            }
            var list = indices.ToList();

            Row MakeRow(int i) => new Row
            {
                Placement = placements[i],
                Root = placements[i].RootCell.Position,
                HasOpenEnd = openEnds.Contains(i),
                Detached = report.ComponentCount > 1 && report.Component[i] != report.MainComponent
            };

            switch (order)
            {
                case Order.Track:
                    {
                        var rank = TrackOrder(report);
                        foreach (int i in list.OrderBy(i => rank[i]))
                            rows.Add(MakeRow(i));
                    }
                    break;
                case Order.Type:
                    foreach (var group in list.GroupBy(i => ModuleCatalog.GetGroupIndex(placements[i].ModuleIndex)).OrderBy(g => g.Key))
                    {
                        int first = group.First();
                        var family = ModuleCatalog.GetFamily(placements[first].ModuleIndex);
                        string name = family.HasValue ? ModuleCatalog.Localize(family.Value) : ModuleCatalog.GetDisplayName(placements[first].ModuleIndex);
                        rows.Add(new Row { IsHeader = true, Header = $"{name}  ·  {group.Count()}" });
                        foreach (int i in group.OrderBy(i => placements[i].RootCell.Position.y).ThenBy(i => placements[i].RootCell.Position.x))
                            rows.Add(MakeRow(i));
                    }
                    break;
                default:
                    foreach (int i in list.OrderBy(i => placements[i].RootCell.Position.y).ThenBy(i => placements[i].RootCell.Position.x))
                        rows.Add(MakeRow(i));
                    break;
            }
        }

        /// <summary>
        /// Order of pieces when following connections from the start grid (depth first, so a single loop
        /// comes out in driving order). Separate sections follow, each from its first piece.
        /// </summary>
        private static int[] TrackOrder(ConnectivityReport report)
        {
            int count = report.Placements.Count;
            var neighbours = new List<int>[count];
            for (int i = 0; i < count; i++)
                neighbours[i] = new List<int>(2);
            foreach (var joint in report.Joints)
            {
                if (joint.B == null || joint.Internal)
                    continue;
                neighbours[joint.A.Placement].Add(joint.B.Placement);
                neighbours[joint.B.Placement].Add(joint.A.Placement);
            }

            var rank = new int[count];
            for (int i = 0; i < count; i++)
                rank[i] = -1;
            int next = 0;

            var starts = new List<int>();
            for (int i = 0; i < count; i++)
            {
                if (report.Placements[i].ModuleIndex == (int)Modules.ID.TWM_START)
                    starts.Add(i);
            }
            starts.AddRange(Enumerable.Range(0, count));

            var stack = new Stack<int>();
            foreach (int start in starts)
            {
                if (rank[start] >= 0)
                    continue;
                stack.Push(start);
                while (stack.Count > 0)
                {
                    int i = stack.Pop();
                    if (rank[i] >= 0)
                        continue;
                    rank[i] = next++;
                    // push in reverse so the first neighbour is visited first
                    for (int n = neighbours[i].Count - 1; n >= 0; n--)
                    {
                        if (rank[neighbours[i][n]] < 0)
                            stack.Push(neighbours[i][n]);
                    }
                }
            }
            return rank;
        }

        public void Draw(Rect rect)
        {
            var track = TrackEditor.Track;
            if (track == null)
                return;

            float pad = T.Padding;
            var searchRect = new Rect(rect.x + pad, rect.y + T.S(6f), rect.width - pad * 2f, T.S(30f));
            search = SGUI.SearchField(searchRect, search, SearchName, "Find blocks by name or cell");

            var orderRect = new Rect(searchRect.x, searchRect.yMax + T.S(8f), searchRect.width, T.S(24f));
            var newOrder = (Order)SGUI.Segmented(orderRect, (int)order, new[] { "Track order", "Type", "Position" }, true,
                                                 new[] { "Following the connections from the start grid", "Grouped by module type", "Row by row, from the south" });
            if (newOrder != order)
            {
                order = newOrder;
                scroll = 0f;
            }

            Rebuild();

            var footer = rect.TakeBottom(T.S(58f));
            var listRect = new Rect(rect.x, orderRect.yMax + T.S(8f), rect.width, footer.y - orderRect.yMax - T.S(8f));
            SGUI.HLine(rect.x, listRect.y - 1f, rect.width, Palette.Divider);

            if (rows.Count == 0)
            {
                string text = track.GetAllModuleRootPlacements().Any() ? "No blocks match" : "No blocks yet";
                string hint = track.GetAllModuleRootPlacements().Any() ? "Try another name or a cell like \"4, 7\"." : "Place modules with the Place tool (1). They'll be listed here.";
                SGUI.Icon(new Rect(listRect.x, listRect.y + T.S(30f), listRect.width, T.S(26f)), IconId.List, Palette.TextFaint, T.S(24f));
                SGUI.Label(new Rect(listRect.x, listRect.y + T.S(62f), listRect.width, T.S(20f)), text, T.BodyStrong, Palette.TextDim, TextAnchor.MiddleCenter);
                SGUI.Label(new Rect(listRect.x + pad, listRect.y + T.S(84f), listRect.width - pad * 2f, T.S(40f)), hint, T.SmallWrapped, Palette.TextFaint, TextAnchor.UpperCenter);
            }
            else
            {
                DrawList(listRect);
            }

            DrawFooter(footer);
        }

        private float RowHeight => T.S(34f);
        private float HeaderHeight => T.S(26f);

        private void DrawList(Rect listRect)
        {
            // y offsets (headers are shorter)
            var offsets = new float[rows.Count + 1];
            for (int i = 0; i < rows.Count; i++)
                offsets[i + 1] = offsets[i] + (rows[i].IsHeader ? HeaderHeight : RowHeight);
            float contentHeight = offsets[rows.Count] + T.S(8f);

            // follow selections made in the viewport
            var selection = Viewport.Selection;
            if (selection.Version != seenSelectionVersion)
            {
                seenSelectionVersion = selection.Version;
                var primary = selection.Primary;
                if (primary.HasValue)
                {
                    int index = rows.FindIndex(r => !r.IsHeader && r.Root == primary.Value);
                    if (index >= 0)
                        SGUI.ScrollIntoView(ref scroll, offsets[index], RowHeight, listRect.height);
                }
            }

            var content = SGUI.BeginScroll(listRect, ref scroll, contentHeight);
            float top = scroll;
            float bottom = scroll + listRect.height;
            for (int i = 0; i < rows.Count; i++)
            {
                // only rows in view take part (this list can hold thousands of blocks)
                if (offsets[i + 1] < top || offsets[i] > bottom)
                    continue;

                var row = rows[i];
                var r = new Rect(content.x, content.y + offsets[i], content.width, row.IsHeader ? HeaderHeight : RowHeight);
                if (row.IsHeader)
                {
                    SGUI.Label(new Rect(r.x + T.Padding, r.y + T.S(6f), r.width - T.Padding * 2f, r.height - T.S(6f)), row.Header.ToUpperInvariant(), T.Caption, Palette.TextFaint);
                    continue;
                }
                DrawRow(r, row, i);
            }
            SGUI.EndScroll(listRect, ref scroll, contentHeight);
        }

        private void DrawRow(Rect r, Row row, int index)
        {
            var states = Viewport.States;
            var p = row.Placement;
            bool locked = states.IsLocked(p);
            bool hidden = states.IsHidden(p);
            bool isSelected = Viewport.Selection.Contains(row.Root);
            bool isPrimary = Viewport.Selection.Primary == row.Root;

            // the eye and lock buttons sit on top of the row, so they handle the pointer first
            float button = T.S(24f);
            var eye = new Rect(r.xMax - T.S(8f) - button, r.y + (r.height - button) * 0.5f, button, button);
            var lockRect = new Rect(eye.x - button, eye.y, button, button);
            bool eyeClicked = SGUI.IconButton(eye, hidden ? IconId.EyeOff : IconId.Eye, hidden ? "Show in the viewport" : "Hide in the viewport (still saved and exported)", hidden);
            bool lockClicked = SGUI.IconButton(lockRect, locked ? IconId.Lock : IconId.LockOpen, locked ? "Unlock" : "Lock against editing", locked);

            if (SGUI.RowBehavior(r.CutRight(button * 2f + T.S(8f)), true, out bool hover, out bool doubleClicked))
                OnRowClicked(row, index, doubleClicked);

            if (isSelected)
                SGUI.Fill(r, isPrimary ? Palette.AccentSoft : Palette.AccentSoft.WithAlpha(0.09f));
            else if (hover)
                SGUI.Fill(r, Palette.Hover.WithAlpha(0.6f));
            if (isPrimary)
                SGUI.Fill(new Rect(r.x, r.y, T.S(3f), r.height), Palette.Accent);

            // thumbnail
            var thumbRect = new Rect(r.x + T.Padding, r.y + T.S(4f), T.S(34f), r.height - T.S(8f));
            var thumb = shell.Thumbnails.Get(p.ModuleIndex);
            if (thumb != null && Event.current.type == EventType.Repaint)
                GUI.DrawTexture(thumbRect, thumb, ScaleMode.ScaleAndCrop, false);
            else
                SGUI.Rounded(thumbRect, Palette.Raised, T.S(3f));

            var textRect = new Rect(thumbRect.xMax + T.S(8f), r.y + T.S(2f), lockRect.x - thumbRect.xMax - T.S(12f), T.S(17f));
            Color nameColor = hidden ? Palette.TextFaint : isSelected ? Palette.Text : Palette.TextDim;
            SGUI.Label(textRect, SGUI.Ellipsize(ModuleCatalog.GetDisplayName(p.ModuleIndex), T.SmallStrong, textRect.width), T.SmallStrong, nameColor);

            int level = BlockOperations.LevelOf(p);
            int pickups = p.TouchedCells.Count(c => c.HasPickup);
            string detail = $"{row.Root.x}, {row.Root.y}  ·  L{level}  ·  {(((p.Rotation % 4) + 4) % 4) * 90}°";
            if (pickups > 0)
                detail += pickups == 1 ? "  ·  pickup" : $"  ·  {pickups} pickups";
            var detailRect = new Rect(textRect.x, textRect.yMax, textRect.width - T.S(14f), T.S(14f));
            SGUI.Label(detailRect, SGUI.Ellipsize(detail, T.Caption, detailRect.width), T.Caption, Palette.TextFaint);

            // connection status
            if (row.Detached || row.HasOpenEnd)
            {
                var dot = new Rect(textRect.xMax - T.S(8f), textRect.yMax + T.S(4f), T.S(7f), T.S(7f));
                SGUI.Rounded(dot, row.Detached ? Palette.Warning : Palette.Error.WithAlpha(0.8f), T.S(3.5f));
                SGUI.Tooltip(dot, row.Detached ? "Not connected to the track with the start grid" : "Has an open end");
            }

            if (eyeClicked)
            {
                Viewport.SetHidden(new[] { p }, !hidden);
                builtKey.hidden = -1;
            }
            if (lockClicked)
            {
                states.SetLocked(p, !locked);
                builtKey.locked = -1;
            }
        }

        private void OnRowClicked(Row row, int index, bool doubleClicked)
        {
            if (Viewport.Tool != StudioTool.Select)
                Viewport.SetTool(StudioTool.Select);
            if (shell.Race.Active)
                shell.SetRaceMode(false);

            var selection = Viewport.Selection;
            if (Controls.IsShiftHeld() && rangeAnchor >= 0 && rangeAnchor < rows.Count)
            {
                int from = Mathf.Min(rangeAnchor, index), to = Mathf.Max(rangeAnchor, index);
                var range = new List<Vector2Int>();
                for (int i = from; i <= to; i++)
                {
                    if (!rows[i].IsHeader && !Viewport.States.IsHidden(rows[i].Placement))
                        range.Add(rows[i].Root);
                }
                if (Controls.IsControlHeld())
                {
                    foreach (var root in range)
                        selection.Add(root);
                }
                else
                {
                    selection.SetMany(range);
                }
                selection.Add(row.Root); // primary
            }
            else if (Controls.IsControlHeld())
            {
                selection.Toggle(row.Root);
                rangeAnchor = index;
            }
            else
            {
                selection.Set(row.Root);
                rangeAnchor = index;
            }
            seenSelectionVersion = selection.Version; // the click already shows it; don't jump the list

            if (doubleClicked)
                Viewport.FrameSelection();
            TrackEditor.PlaySound(TrackEditor.SndMenuMove);
        }

        private void DrawFooter(Rect footer)
        {
            SGUI.HLine(footer.x, footer.y, footer.width, Palette.Divider);
            var states = Viewport.States;
            int blocks = rows.Count(r => !r.IsHeader);
            var parts = new List<string> { blocks == 1 ? "1 block" : $"{blocks} blocks" };
            if (!Viewport.Selection.IsEmpty) parts.Add($"{Viewport.Selection.Count} selected");
            if (states.HiddenCount > 0) parts.Add($"{states.HiddenCount} hidden");
            if (states.LockedCount > 0) parts.Add($"{states.LockedCount} locked");
            var info = new Rect(footer.x + T.Padding, footer.y + T.S(4f), footer.width - T.Padding * 2f, T.S(18f));
            SGUI.Label(info, SGUI.Ellipsize(string.Join("  ·  ", parts), T.Small, info.width), T.Small, Palette.TextFaint);

            float bw = (footer.width - T.Padding * 2f - T.S(12f)) / 3f;
            var b = new Rect(footer.x + T.Padding, info.yMax + T.S(4f), bw, T.S(26f));
            if (SGUI.Button(b, "Select all", ButtonKind.Ghost, blocks > 0, null, "Select the blocks listed here"))
            {
                Viewport.SetTool(StudioTool.Select);
                Viewport.Selection.SetMany(rows.Where(r => !r.IsHeader && !states.IsHidden(r.Placement)).Select(r => r.Root).ToList());
            }
            b.x += bw + T.S(6f);
            if (SGUI.Button(b, "Show all", ButtonKind.Ghost, states.HiddenCount > 0, IconId.Eye, "Show hidden blocks  Alt+H"))
            {
                states.ShowAll();
                builtKey.hidden = -1;
            }
            b.x += bw + T.S(6f);
            if (SGUI.Button(b, "Unlock all", ButtonKind.Ghost, states.LockedCount > 0, IconId.LockOpen))
            {
                states.UnlockAll();
                builtKey.locked = -1;
            }
        }
    }
}

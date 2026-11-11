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
    /// The module library: every module group from the original module screen, with rendered previews,
    /// categories, favorites, recently placed modules, and details (size, connections, variants) of the
    /// module to place. Clicking a card makes it the module to place.
    /// </summary>
    public sealed class ModuleLibraryPanel
    {
        private const string SearchName = "studio.modules.search";

        private struct Entry
        {
            public int ModuleId;
            public string Name;
            public string Badge;     // "A" / "B" for groups with two variants
        }

        // categories are made of the original module groups (indices into Modules.Lookup.Groups)
        private static readonly (string Name, int[] Groups)[] Categories =
        {
            ("Straights", new[] { 1, 8, 9 }),       // straights and ramps, rumble strips, narrow
            ("Curves", new[] { 4, 5, 6, 14 }),      // square and round bends, diagonals, chicanes
            ("Dips & humps", new[] { 2, 3 }),
            ("Banks", new[] { 7 }),
            ("Structures", new[] { 10, 11, 12, 13 }), // pipes, bridges, crossroads, jumps
            ("Start", new[] { 0 }),
        };

        // fixed filters before the categories
        private const int FilterAll = 0, FilterFavorites = 1, FilterRecent = 2, FirstCategory = 3;

        private readonly StudioShell shell;
        private readonly List<Entry> entries = new List<Entry>();
        private string search = string.Empty;
        private float scroll;
        private bool detailsOpen = true;

        public ModuleLibraryPanel(StudioShell shell)
        {
            this.shell = shell;
        }

        private StudioTheme T => shell.Theme;

        private void BuildEntries()
        {
            entries.Clear();
            var groups = Modules.Lookup.Groups;
            for (int g = 0; g < groups.Length; g++)
            {
                var group = groups[g];
                string name = ModuleCatalog.Localize(group.Name);
                entries.Add(new Entry { ModuleId = group.ModuleIDs[0], Name = name, Badge = group.HasVariant ? "A" : null });
                if (group.HasVariant)
                    entries.Add(new Entry { ModuleId = group.ModuleIDs[1], Name = name, Badge = "B" });
            }
        }

        private static bool IsFavorite(int id) => AppSettings.Data.FavoriteModules.Contains(id);

        private static void ToggleFavorite(int id)
        {
            var favorites = AppSettings.Data.FavoriteModules;
            if (!favorites.Remove(id))
                favorites.Add(id);
            AppSettings.Save();
        }

        public void Draw(Rect rect)
        {
            if (entries.Count == 0)
                BuildEntries();
            var settings = AppSettings.Data;

            // search and card size
            float pad = T.Padding;
            var top = new Rect(rect.x + pad, rect.y + T.S(6f), rect.width - pad * 2f, T.S(30f));
            var sizeButton = top.TakeRight(T.S(30f));
            search = SGUI.SearchField(top.CutRight(T.S(36f)), search, SearchName, "Search modules");
            string[] sizeNames = { "Small", "Medium", "Large" };
            SGUI.RoundedOutline(sizeButton, Palette.Raised, Palette.Border);
            if (SGUI.IconButton(sizeButton, settings.LibraryCardSize == 2 ? IconId.Layout : IconId.Grid, $"Preview size: {sizeNames[settings.LibraryCardSize]} (click to change)"))
            {
                settings.LibraryCardSize = (settings.LibraryCardSize + 1) % 3;
                AppSettings.Save();
            }

            // filter chips (wrapping)
            float y = top.yMax + T.S(8f);
            var labels = new List<string> { "All", "Favorites", "Recent" };
            labels.AddRange(Categories.Select(c => c.Name));
            float x = rect.x + pad;
            float chipHeight = T.S(22f);
            for (int i = 0; i < labels.Count; i++)
            {
                float w = SGUI.TextWidth(labels[i], T.Small) + T.S(i == FilterFavorites ? 34f : 18f);
                if (x + w > rect.xMax - pad && x > rect.x + pad)
                {
                    x = rect.x + pad;
                    y += chipHeight + T.S(5f);
                }
                var chip = new Rect(x, y, w, chipHeight).Snap();
                bool active = settings.LibraryCategory == i;
                if (SGUI.ClickBehavior(chip, true, out bool hover, out _))
                {
                    settings.LibraryCategory = i;
                    scroll = 0f;
                    AppSettings.Save();
                }
                SGUI.Rounded(chip, active ? Palette.AccentSoft : hover ? Palette.Hover : Palette.Raised, chipHeight * 0.5f);
                var labelRect = chip;
                if (i == FilterFavorites)
                {
                    SGUI.Icon(new Rect(chip.x + T.S(7f), chip.y, T.S(14f), chip.height), active ? IconId.StarFilled : IconId.Star, active ? Palette.Accent : Palette.TextFaint, T.S(11f));
                    labelRect = chip.CutLeft(T.S(16f));
                }
                SGUI.Label(labelRect, labels[i], T.Small, active ? Palette.Text : Palette.TextDim, TextAnchor.MiddleCenter);
                x += w + T.S(5f);
            }
            y += chipHeight + T.S(8f);

            // details of the module to place, at the bottom
            float detailsHeight = detailsOpen ? T.S(206f) : T.S(32f);
            var details = rect.TakeBottom(detailsHeight);
            var listRect = new Rect(rect.x, y, rect.width, details.y - y);
            SGUI.HLine(rect.x, listRect.y - 1f, rect.width, Palette.Divider);
            DrawGrid(listRect, Filtered(settings.LibraryCategory));
            DrawDetails(details);
        }

        private List<Entry> Filtered(int filter)
        {
            IEnumerable<Entry> source;
            if (filter == FilterFavorites)
                source = AppSettings.Data.FavoriteModules.Where(Valid).Select(ToEntry);
            else if (filter == FilterRecent)
                source = AppSettings.Data.RecentModules.Where(Valid).Select(ToEntry);
            else if (filter >= FirstCategory && filter - FirstCategory < Categories.Length)
            {
                var groups = Categories[filter - FirstCategory].Groups;
                source = entries.Where(e => groups.Contains(ModuleCatalog.GetGroupIndex(e.ModuleId)));
            }
            else
                source = entries;

            if (!string.IsNullOrWhiteSpace(search))
            {
                // searching looks through every module, including variants reached with Z/X
                if (filter == FilterAll)
                    source = source.Concat(AllVariants().Where(id => !entries.Any(e => e.ModuleId == id)).Select(ToEntry));
                source = source.Where(e => e.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                                        || ModuleCatalog.GetCodeName(e.ModuleId).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);
            }
            return source.ToList();
        }

        private static bool Valid(int id) => TrackEditor.TrackUnit != null && id >= 0 && id < TrackEditor.TrackUnit.Modules.Count;

        private Entry ToEntry(int id)
        {
            var known = entries.FirstOrDefault(e => e.ModuleId == id);
            return known.Name != null ? known : new Entry { ModuleId = id, Name = ModuleCatalog.GetDisplayName(id) };
        }

        private IEnumerable<int> AllVariants()
        {
            var seen = new HashSet<int>();
            foreach (var entry in entries)
            {
                foreach (int id in ModuleCatalog.GetVariantChain(entry.ModuleId))
                {
                    if (seen.Add(id))
                        yield return id;
                }
            }
        }

        private void DrawGrid(Rect rect, List<Entry> filtered)
        {
            var editing = TrackEditor.GetEditMode<TrackEditingMode>();
            int active = editing.ActiveModuleIndex;
            var activeChain = ModuleCatalog.GetVariantChain(active);

            float pad = T.S(10f);
            float gap = T.S(8f);
            float[] sizes = { 78f, 104f, 150f };
            float minCard = T.S(sizes[AppSettings.Data.LibraryCardSize]);
            float available = rect.width - pad * 2f - T.S(10f); // leave room for the scrollbar
            int columns = Mathf.Max(1, Mathf.FloorToInt((available + gap) / (minCard + gap)));
            float cardWidth = Mathf.Floor((available - gap * (columns - 1)) / columns);
            float thumbHeight = Mathf.Round(cardWidth * 0.78f);
            bool showLabels = AppSettings.Data.LibraryCardSize > 0;
            float cardHeight = thumbHeight + (showLabels ? T.S(30f) : 0f);

            int rows = Mathf.CeilToInt(filtered.Count / (float)columns);
            float contentHeight = pad * 2f + rows * cardHeight + Mathf.Max(0, rows - 1) * gap;

            // a fixed render size per scale band (resizing the panel must not re-render every preview);
            // rendering larger than displayed gives clean, antialiased downscaling
            shell.Thumbnails.Configure(T.Scale > 1.3f ? 384 : 256, Palette.Raised);

            if (filtered.Count == 0)
            {
                int filter = AppSettings.Data.LibraryCategory;
                string text = !string.IsNullOrWhiteSpace(search) ? "No modules match"
                            : filter == FilterFavorites ? "No favorites yet"
                            : filter == FilterRecent ? "Nothing placed yet" : "No modules";
                string hint = filter == FilterFavorites && string.IsNullOrWhiteSpace(search) ? "Hover a card and click its star to keep it here."
                            : filter == FilterRecent && string.IsNullOrWhiteSpace(search) ? "Modules you place show up here." : null;
                SGUI.Label(new Rect(rect.x, rect.y + T.S(24f), rect.width, T.S(20f)), text, T.BodyStrong, Palette.TextDim, TextAnchor.MiddleCenter);
                if (hint != null)
                    SGUI.Label(new Rect(rect.x + pad, rect.y + T.S(48f), rect.width - pad * 2f, T.S(36f)), hint, T.SmallWrapped, Palette.TextFaint, TextAnchor.UpperCenter);
                return;
            }

            var content = SGUI.BeginScroll(rect, ref scroll, contentHeight);
            for (int i = 0; i < filtered.Count; i++)
            {
                var entry = filtered[i];
                int col = i % columns;
                int row = i / columns;
                var card = new Rect(content.x + pad + col * (cardWidth + gap), content.y + pad + row * (cardHeight + gap), cardWidth, cardHeight).Snap();
                bool visible = card.yMax >= 0f && card.y <= rect.height;

                bool exact = entry.ModuleId == active;
                bool related = !exact && activeChain.Contains(entry.ModuleId);
                bool favorite = IsFavorite(entry.ModuleId);

                // the star sits on the card, so it takes the pointer first
                var star = new Rect(card.xMax - T.S(24f), card.y + T.S(3f), T.S(21f), T.S(21f));
                bool hoverCard = SGUI.PointerIn(card);
                // the click area exists on every visible card (so press and release must both be on this star);
                // it's only active while the star shows
                bool starClicked = visible && SGUI.ClickBehavior(star, hoverCard || favorite, out _, out _);
                if (starClicked)
                    ToggleFavorite(entry.ModuleId);

                bool hover = hoverCard, pressed = false;
                if (!starClicked && SGUI.ClickBehavior(card, true, out hover, out pressed))
                {
                    editing.SetActiveModule(entry.ModuleId);
                    shell.Viewport.SetTool(StudioTool.Place);
                    TrackEditor.PlaySound(TrackEditor.SndMenuMove);
                }
                SGUI.Tooltip(card, $"{entry.Name}  ·  {ModuleCatalog.GetCodeName(entry.ModuleId)}");
                if (hoverCard || favorite)
                    SGUI.Tooltip(star, favorite ? "Remove from favorites" : "Add to favorites");

                // skip drawing cards that are scrolled out of view
                if (!visible)
                    continue;

                Color border = exact ? Palette.Accent : related ? Palette.AccentLine : hover ? Palette.BorderStrong : Palette.Border;
                float borderWidth = exact ? Mathf.Round(T.S(2f)) : Mathf.Max(1f, Mathf.Round(T.S(1f)));
                SGUI.RoundedOutline(card, pressed ? Palette.Pressed : hover ? Palette.Hover : Palette.Raised, border, T.S(6f), borderWidth);

                var thumbRect = new Rect(card.x + borderWidth, card.y + borderWidth, card.width - borderWidth * 2f, thumbHeight - borderWidth).Snap();
                var thumb = shell.Thumbnails.Get(entry.ModuleId);
                if (thumb != null && Event.current.type == EventType.Repaint)
                    GUI.DrawTexture(thumbRect, thumb, ScaleMode.ScaleAndCrop, false);
                else
                    SGUI.Icon(thumbRect, IconId.Module, Palette.TextFaint.WithAlpha(0.5f), T.S(22f));

                // redraw the star over the thumbnail
                if (hover || favorite)
                {
                    SGUI.Rounded(star, Palette.Chrome.WithAlpha(0.75f), T.S(5f));
                    SGUI.Icon(star, favorite ? IconId.StarFilled : IconId.Star, favorite ? Palette.Warning : Palette.TextDim, T.S(13f));
                }

                if (!showLabels)
                    continue;
                var labelRect = new Rect(card.x + T.S(8f), thumbRect.yMax, card.width - T.S(16f), card.yMax - thumbRect.yMax);
                float badgeWidth = entry.Badge != null ? T.S(18f) + T.S(6f) : 0f;
                SGUI.Label(labelRect.CutRight(badgeWidth), SGUI.Ellipsize(entry.Name, T.SmallStrong, labelRect.width - badgeWidth),
                           T.SmallStrong, exact ? Palette.Text : Palette.TextDim);
                if (entry.Badge != null)
                {
                    var badge = new Rect(labelRect.xMax - T.S(18f), labelRect.center.y - T.S(9f), T.S(18f), T.S(18f));
                    SGUI.Rounded(badge, exact ? Palette.AccentSoft : Palette.Chrome, T.S(4f));
                    SGUI.Label(badge, entry.Badge, T.Caption, exact ? Palette.Accent : Palette.TextFaint, TextAnchor.MiddleCenter);
                }
            }
            SGUI.EndScroll(rect, ref scroll, contentHeight);
        }

        private void DrawDetails(Rect rect)
        {
            var editing = TrackEditor.GetEditMode<TrackEditingMode>();
            int active = editing.ActiveModuleIndex;
            float pad = T.Padding;

            SGUI.Fill(rect, Palette.Chrome.WithAlpha(0.5f));
            SGUI.HLine(rect.x, rect.y, rect.width, Palette.Divider);
            var header = new Rect(rect.x + pad, rect.y, rect.width - pad * 2f, T.S(32f));
            if (SGUI.ClickBehavior(header, true, out bool headerHover, out _))
                detailsOpen = !detailsOpen;
            SGUI.Icon(header.TakeLeft(T.S(14f)), detailsOpen ? IconId.ChevronDown : IconId.ChevronRight, headerHover ? Palette.Text : Palette.TextFaint, T.S(11f));
            SGUI.Label(header.CutLeft(T.S(20f)), "TO PLACE", T.Caption, Palette.TextFaint);
            string name = ModuleCatalog.GetDisplayName(active);
            SGUI.Label(header.CutLeft(T.S(90f)), SGUI.Ellipsize(name, T.SmallStrong, header.width - T.S(124f)), T.SmallStrong, Palette.Text);
            bool favorite = IsFavorite(active);
            if (SGUI.IconButton(header.TakeRight(T.S(24f)).Inset(0f, T.S(4f)), favorite ? IconId.StarFilled : IconId.Star,
                                favorite ? "Remove from favorites" : "Add to favorites", favorite))
                ToggleFavorite(active);
            if (!detailsOpen)
                return;

            float y = header.yMax;
            var thumbRect = new Rect(rect.x + pad, y, T.S(112f), T.S(84f));
            SGUI.RoundedOutline(thumbRect, Palette.Raised, Palette.Border, T.S(6f));
            var thumb = shell.Thumbnails.Get(active);
            if (thumb != null && Event.current.type == EventType.Repaint)
                GUI.DrawTexture(thumbRect.Inset(T.S(1f)), thumb, ScaleMode.ScaleAndCrop, false);

            // facts from the track unit
            var info = new Rect(thumbRect.xMax + T.S(10f), y, rect.xMax - thumbRect.xMax - T.S(10f) - pad, T.S(17f));
            SGUI.Label(info, ModuleCatalog.GetCodeName(active), T.Mono, Palette.TextFaint);
            info.y += T.S(19f);
            var unit = TrackEditor.TrackUnit;
            if (unit != null && active >= 0 && active < unit.Modules.Count)
            {
                var size = unit.Modules[active].CalculateModuleGridSize();
                SGUI.Label(info, $"{size.width} × {size.height} cell{(size.width * size.height == 1 ? "" : "s")}", T.Small, Palette.TextDim);
                info.y += T.S(17f);
                var shape = ConnectivityService.ShapeOf(active);
                if (shape != null)
                {
                    int ends = 0;
                    var points = shape.Zones.SelectMany(z => new[] { z.A, z.B }).ToList();
                    foreach (var point in points)
                    {
                        if (points.Count(o => Mathf.Abs(o.X - point.X) < 1f && Mathf.Abs(o.Z - point.Z) < 1f && Mathf.Abs(o.Y - point.Y) < 1f) == 1)
                            ends++;
                    }
                    SGUI.Label(info, ends == 0 ? "No track (scenery)" : $"{ends} connection point{(ends == 1 ? "" : "s")}", T.Small, Palette.TextDim);
                    info.y += T.S(17f);
                }
                bool surface = Modules.Lookup.OtherSurface[active] != active;
                SGUI.Label(info, surface ? "Second surface: T" : "One surface", T.Small, Palette.TextFaint);
            }
            y = thumbRect.yMax + T.S(8f);

            // variants (Z / X cycle through these)
            var chain = ModuleCatalog.GetVariantChain(active);
            SGUI.Label(new Rect(rect.x + pad, y, rect.width - pad * 2f, T.S(16f)),
                       chain.Count > 1 ? $"Variants  ·  {chain.Count}  ·  Z / X" : "No variants", T.Caption, Palette.TextFaint);
            y += T.S(18f);
            if (chain.Count <= 1)
                return;

            float cell = T.S(38f);
            float gap = T.S(4f);
            int perRow = Mathf.Max(1, Mathf.FloorToInt((rect.width - pad * 2f + gap) / (cell + gap)));
            int maxVisible = perRow * 2;
            int startIndex = 0;
            int index = 0;
            for (int i = 0; i < chain.Count; i++)
                if (chain[i] == active) index = i;
            if (chain.Count > maxVisible)
                startIndex = Mathf.Clamp(index - maxVisible / 2, 0, chain.Count - maxVisible);

            for (int i = startIndex; i < Mathf.Min(chain.Count, startIndex + maxVisible); i++)
            {
                int slot = i - startIndex;
                var r = new Rect(rect.x + pad + (slot % perRow) * (cell + gap), y + (slot / perRow) * (cell + gap), cell, cell).Snap();
                if (r.yMax > rect.yMax - T.S(4f))
                    break;
                bool isActive = chain[i] == active;
                if (SGUI.ClickBehavior(r, true, out bool hover, out _))
                {
                    editing.SetActiveModule(chain[i]);
                    shell.Viewport.SetTool(StudioTool.Place);
                    TrackEditor.PlaySound(TrackEditor.SndMenuMove);
                }
                SGUI.Tooltip(r, ModuleCatalog.GetCodeName(chain[i]));
                SGUI.RoundedOutline(r, hover ? Palette.Hover : Palette.Raised, isActive ? Palette.Accent : Palette.Border, T.S(4f),
                                    isActive ? Mathf.Round(T.S(2f)) : 1f);
                var t = shell.Thumbnails.Get(chain[i]);
                if (t != null && Event.current.type == EventType.Repaint)
                    GUI.DrawTexture(r.Inset(T.S(2f)), t, ScaleMode.ScaleAndCrop, false);
            }
        }
    }
}

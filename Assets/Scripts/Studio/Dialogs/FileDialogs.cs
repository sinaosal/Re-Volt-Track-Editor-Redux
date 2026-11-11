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
using System.IO;
using System.Linq;
using UnityEngine;

namespace Studio
{
    public static class TimeText
    {
        public static string Relative(DateTime local)
        {
            var span = DateTime.Now - local;
            if (span.TotalSeconds < 45) return "just now";
            if (span.TotalMinutes < 1.5) return "a minute ago";
            if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} minutes ago";
            if (span.TotalHours < 1.5) return "an hour ago";
            if (span.TotalHours < 24) return $"{(int)span.TotalHours} hours ago";
            if (span.TotalDays < 2) return "yesterday";
            if (span.TotalDays < 7) return $"{(int)span.TotalDays} days ago";
            return local.ToString("d MMM yyyy");
        }
    }

    public sealed class SaveAsDialog : StudioDialog
    {
        private const string FieldName = "studio.saveas.name";
        private string name;
        private readonly Action afterSave;

        public SaveAsDialog(string initialName, Action afterSave)
        {
            Title = "Save Track As";
            TitleIcon = IconId.SaveAs;
            name = TdfDocument.SanitizeName(initialName);
            this.afterSave = afterSave;
        }

        public override float Width => T.S(500f);

        private string TargetPath => FileHelper.GetTrackSavePath(TdfDocument.SanitizeName(name));
        private bool NameValid => TdfDocument.SanitizeName(name).Length > 0;
        private bool WillReplace
        {
            get
            {
                if (!NameValid)
                    return false;
                string path = TargetPath;
                return File.Exists(path) && !string.Equals(Path.GetFullPath(path), TrackEditor.CurrentFilePath == null ? null : Path.GetFullPath(TrackEditor.CurrentFilePath), StringComparison.OrdinalIgnoreCase);
            }
        }

        public override float BodyHeight(float width) => T.S(118f);

        public override void DrawBody(Rect rect)
        {
            var r = rect;
            SGUI.Label(r.TakeTop(T.S(20f)), "Track name", T.SmallStrong, Palette.TextDim);
            r = r.CutTop(T.S(24f));

            var field = r.TakeTop(T.S(34f));
            name = SGUI.TextField(field, name, FieldName, "My track", TdfDocument.MaxNameLength, null, TdfDocument.IsValidNameChar);
            if (FrameCount == 0)
                GUI.FocusControl(FieldName);
            r = r.CutTop(T.S(44f));

            if (!NameValid)
            {
                SGUI.Label(r.TakeTop(T.S(20f)), "Enter a name for the track.", T.Small, Palette.TextFaint);
            }
            else
            {
                string file = Path.GetFileName(TargetPath);
                SGUI.Icon(r.TakeTop(T.S(20f)).TakeLeft(T.S(16f)), IconId.File, Palette.TextFaint, T.S(14f));
                string line = $"{file}  in  {FileHelper.SaveDirectory}";
                SGUI.Label(r.TakeTop(T.S(20f)).CutLeft(T.S(22f)), SGUI.Ellipsize(line, T.Small, r.width - T.S(22f)), T.Small, Palette.TextFaint);
            }

            if (WillReplace)
            {
                var warn = r.CutTop(T.S(26f)).TakeTop(T.S(20f));
                SGUI.Icon(warn.TakeLeft(T.S(16f)), IconId.Warning, Palette.Warning, T.S(14f));
                SGUI.Label(warn.CutLeft(T.S(22f)), "A track with this name already exists and will be replaced.", T.Small, Palette.Warning);
            }
        }

        public override void DrawFooter(Rect rect)
        {
            string label = WillReplace ? "Replace" : "Save";
            if (SGUI.Button(FooterButton(ref rect, label), label, WillReplace ? ButtonKind.Danger : ButtonKind.Primary, NameValid))
                Submit();
            if (SGUI.Button(FooterButton(ref rect, "Cancel"), "Cancel"))
                Cancel();
        }

        public override bool Submit()
        {
            if (!NameValid)
                return true;
            Close();
            Shell.SaveTrackAs(TdfDocument.SanitizeName(name), afterSave);
            return true;
        }
    }

    public sealed class OpenTrackDialog : StudioDialog
    {
        private const string SearchName = "studio.open.search";
        private const string PathName = "studio.open.path";

        private string search = string.Empty;
        private string pathInput = string.Empty;
        private float scroll;
        private int selected = -1;
        private List<SavedFilesList.SavedFile> visible = new List<SavedFilesList.SavedFile>();
        private string lastFilter;
        private float listViewHeight = 300f;

        public OpenTrackDialog()
        {
            Title = "Open Track";
            TitleIcon = IconId.Open;
        }

        public override float Width => T.S(640f);

        public override void OnOpened()
        {
            Refresh();
            // preselect the open track
            string current = TrackEditor.CurrentFilePath;
            selected = visible.FindIndex(x => current != null && string.Equals(x.Path, current, StringComparison.OrdinalIgnoreCase));
            if (selected < 0 && visible.Count > 0)
                selected = 0;
        }

        private void Refresh()
        {
            SavedFilesList.Update();
            lastFilter = null;
            Filter();
        }

        private void Filter()
        {
            if (lastFilter == search)
                return;
            lastFilter = search;

            var all = SavedFilesList.SavedFiles;
            visible = string.IsNullOrWhiteSpace(search)
                ? all.ToList()
                : all.Where(x => x.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                              || Path.GetFileName(x.Path).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            selected = Mathf.Clamp(selected, visible.Count > 0 ? 0 : -1, visible.Count - 1);
        }

        public override float BodyHeight(float width) => T.S(430f);

        public override void DrawBody(Rect rect)
        {
            HandleKeys();

            var searchRect = rect.TakeTop(T.S(32f));
            search = SGUI.SearchField(searchRect, search, SearchName, "Search saved tracks");
            if (FrameCount == 0)
                GUI.FocusControl(SearchName);
            Filter();

            var listRect = new Rect(rect.x, searchRect.yMax + T.S(10f), rect.width, rect.height - T.S(32f) - T.S(10f) - T.S(62f));
            DrawList(listRect);

            // open from an arbitrary location (there is no native file picker in Unity's runtime)
            var pathRow = rect.TakeBottom(T.S(52f));
            SGUI.Label(pathRow.TakeTop(T.S(18f)), "Open a .TDF file from another folder", T.Small, Palette.TextFaint);
            var fieldRow = pathRow.CutTop(T.S(22f));
            var openPath = fieldRow.TakeRight(T.S(76f));
            pathInput = SGUI.TextField(fieldRow.CutRight(T.S(84f)), pathInput, PathName, "C:\\path\\to\\track.tdf");
            bool pathValid = !string.IsNullOrWhiteSpace(pathInput);
            if (SGUI.Button(openPath, "Open", ButtonKind.Secondary, pathValid) || (pathValid && SGUI.SubmitPressed(PathName)))
                OpenPath(pathInput.Trim().Trim('"'));
        }

        private void DrawList(Rect listRect)
        {
            SGUI.RoundedOutline(listRect, Palette.Chrome, Palette.Border);
            var inner = listRect.Inset(T.S(1f));

            if (visible.Count == 0)
            {
                string text = SavedFilesList.SavedFiles.Count == 0 ? "No saved tracks yet" : "No tracks match your search";
                SGUI.Icon(new Rect(inner.x, inner.center.y - T.S(36f), inner.width, T.S(28f)), IconId.Track, Palette.TextFaint, T.S(26f));
                SGUI.Label(new Rect(inner.x, inner.center.y - T.S(4f), inner.width, T.S(22f)), text, T.BodyStrong, Palette.TextDim, TextAnchor.MiddleCenter);
                SGUI.Label(new Rect(inner.x + T.S(16f), inner.center.y + T.S(18f), inner.width - T.S(32f), T.S(20f)),
                           SGUI.Ellipsize(FileHelper.SaveDirectory, T.Small, inner.width - T.S(32f)), T.Small, Palette.TextFaint, TextAnchor.MiddleCenter);
                return;
            }

            float rowHeight = T.S(48f);
            float contentHeight = visible.Count * rowHeight;
            listViewHeight = inner.height;
            var content = SGUI.BeginScroll(inner, ref scroll, contentHeight);
            for (int i = 0; i < visible.Count; i++)
            {
                var file = visible[i];
                var row = new Rect(content.x, content.y + i * rowHeight, content.width, rowHeight);

                // behaviour runs for every row (keeps control IDs stable); drawing is skipped off screen
                if (SGUI.RowBehavior(row, true, out bool hover, out bool doubleClicked))
                {
                    selected = i;
                    if (doubleClicked)
                        OpenSelected();
                }
                if (row.yMax < 0f || row.y > inner.height)
                    continue;

                bool isSelected = i == selected;
                if (isSelected)
                    SGUI.Fill(row, Palette.AccentSoft);
                else if (hover)
                    SGUI.Fill(row, Palette.Hover.WithAlpha(0.6f));

                var iconRect = new Rect(row.x + T.S(12f), row.y, T.S(20f), row.height);
                SGUI.Icon(iconRect, file.IsValid ? IconId.Track : IconId.Warning, file.IsValid ? (isSelected ? Palette.Accent : Palette.TextDim) : Palette.Warning);

                var textRect = new Rect(row.x + T.S(44f), row.y + T.S(7f), row.width - T.S(56f), T.S(18f));
                bool isCurrent = TrackEditor.CurrentFilePath != null && string.Equals(file.Path, TrackEditor.CurrentFilePath, StringComparison.OrdinalIgnoreCase);
                float nameWidth = textRect.width - (isCurrent ? T.S(64f) : 0f);
                SGUI.Label(textRect, SGUI.Ellipsize(file.DisplayName, T.BodyStrong, nameWidth), T.BodyStrong, file.IsValid ? Palette.Text : Palette.TextDim);
                if (isCurrent)
                {
                    var badge = new Rect(textRect.xMax - T.S(56f), textRect.y, T.S(56f), T.S(18f));
                    SGUI.Badge(badge, "Open", Palette.Raised, Palette.TextDim);
                }

                string detail = file.IsValid
                    ? $"{Path.GetFileName(file.Path)}  ·  modified {TimeText.Relative(file.ModifiedDate.ToLocalTime())}"
                    : file.Error;
                SGUI.Label(new Rect(textRect.x, textRect.yMax + T.S(2f), textRect.width, T.S(17f)),
                           SGUI.Ellipsize(detail, T.Small, textRect.width), T.Small, file.IsValid ? Palette.TextFaint : Palette.Warning);

                if (i < visible.Count - 1)
                    SGUI.HLine(row.x + T.S(44f), row.yMax - 1f, row.width - T.S(44f), Palette.Divider);
            }
            SGUI.EndScroll(inner, ref scroll, contentHeight);
        }

        private void HandleKeys()
        {
            var e = Event.current;
            if (SGUI.InputBlocked || e.type != EventType.KeyDown || visible.Count == 0 || SGUI.IsFocused(PathName))
                return;

            float rowHeight = T.S(48f);
            if (e.keyCode == KeyCode.DownArrow)
            {
                selected = Mathf.Min(visible.Count - 1, selected + 1);
                SGUI.ScrollIntoView(ref scroll, selected * rowHeight, rowHeight, listViewHeight);
                e.Use();
            }
            else if (e.keyCode == KeyCode.UpArrow)
            {
                selected = Mathf.Max(0, selected - 1);
                SGUI.ScrollIntoView(ref scroll, selected * rowHeight, rowHeight, listViewHeight);
                e.Use();
            }
            else if (e.keyCode == KeyCode.Delete && !SGUI.IsFocused(SearchName))
            {
                DeleteSelected();
                e.Use();
            }
        }

        public override void DrawFooter(Rect rect)
        {
            bool canOpen = selected >= 0 && selected < visible.Count && visible[selected].IsValid;
            bool hasSelection = selected >= 0 && selected < visible.Count;

            if (SGUI.Button(FooterButton(ref rect, "Open"), "Open", ButtonKind.Primary, canOpen))
                OpenSelected();
            if (SGUI.Button(FooterButton(ref rect, "Cancel"), "Cancel"))
                Cancel();

            // left side
            var left = new Rect(rect.x, rect.y + (rect.height - T.ControlHeight) * 0.5f, T.S(96f), T.ControlHeight);
            if (SGUI.Button(left, "Delete", ButtonKind.Ghost, hasSelection, IconId.Trash, "Delete the selected track file"))
                DeleteSelected();
            left.x += left.width + T.S(6f);
            left.width = T.S(124f);
            if (SGUI.Button(left, "Show folder", ButtonKind.Ghost, true, IconId.Folder, FileHelper.SaveDirectory))
                StudioShell.RevealFolder(FileHelper.SaveDirectory);
        }

        public override bool Submit()
        {
            if (SGUI.IsFocused(PathName))
                return false; // handled by the path row
            OpenSelected();
            return true;
        }

        private void OpenSelected()
        {
            if (selected < 0 || selected >= visible.Count)
                return;
            var file = visible[selected];
            if (!file.IsValid)
            {
                Shell.ShowMessage("Can't open track", file.Error, true);
                return;
            }
            Close();
            Shell.OpenTrackFile(file.Path);
        }

        private void OpenPath(string path)
        {
            if (!File.Exists(path))
            {
                Shell.ShowMessage("File not found", $"There is no file at \"{path}\".", true);
                return;
            }
            Close();
            Shell.OpenTrackFile(path);
        }

        private void DeleteSelected()
        {
            if (selected < 0 || selected >= visible.Count)
                return;
            var file = visible[selected];

            Shell.OpenDialog(new ConfirmDialog("Delete track?",
                $"\"{file.DisplayName}\" ({Path.GetFileName(file.Path)}) will be permanently deleted. This can't be undone.",
                null, -1,
                new ConfirmDialog.Choice("Delete", ButtonKind.Danger, () =>
                {
                    try
                    {
                        TrackFiles.Delete(file.Path);
                        AppSettings.RemoveRecentFile(file.Path);
                        Shell.Toast($"Deleted {Path.GetFileName(file.Path)}", IconId.Trash);
                    }
                    catch (Exception ex)
                    {
                        Shell.ShowMessage("Couldn't delete track", ex.Message, true);
                    }
                    Refresh();
                }),
                new ConfirmDialog.Choice("Cancel", ButtonKind.Secondary, null)));
        }
    }
}

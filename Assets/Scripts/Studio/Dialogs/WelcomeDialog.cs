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
using UnityEngine;

namespace Studio
{
    /// <summary>
    /// Start screen: crash recovery, quick actions, and recent tracks.
    /// </summary>
    public sealed class WelcomeDialog : StudioDialog
    {
        private struct RecentEntry
        {
            public string Path;
            public string Name;
            public DateTime Modified;
            public bool Exists;
        }

        private readonly List<RecentEntry> recent = new List<RecentEntry>();
        private TrackRecovery.PendingRecovery? recovery;
        private float scroll;

        public WelcomeDialog()
        {
            Title = "Welcome";
        }

        public override float Width => T.S(760f);
        public override bool HasFooter => true;

        public override void OnOpened()
        {
            recovery = TrackRecovery.FindPendingRecovery();
            recent.Clear();
            foreach (var path in AppSettings.Data.RecentFiles)
            {
                var entry = new RecentEntry { Path = path, Exists = File.Exists(path) };
                if (entry.Exists)
                {
                    entry.Modified = File.GetLastWriteTime(path);
                    try { entry.Name = TdfDocument.ReadName(path); }
                    catch (Exception) { entry.Name = Path.GetFileNameWithoutExtension(path); }
                }
                else
                {
                    entry.Name = Path.GetFileNameWithoutExtension(path);
                }
                recent.Add(entry);
            }
        }

        public override float BodyHeight(float width) => T.S(388f);

        public override void DrawBody(Rect rect)
        {
            float columnGap = T.S(28f);
            var left = rect.TakeLeft(T.S(270f));
            var right = rect.CutLeft(left.width + columnGap);

            // brand
            var mark = new Rect(left.x, left.y, T.S(48f), T.S(48f));
            StudioShell.DrawAppMark(T, mark);
            SGUI.Label(new Rect(left.x, mark.yMax + T.S(16f), left.width, T.S(26f)), "Re-Volt Track Editor", T.Title);
            SGUI.Label(new Rect(left.x, mark.yMax + T.S(44f), left.width, T.S(18f)), "Design, test and export custom tracks", T.Small, Palette.TextFaint);

            // actions
            float y = mark.yMax + T.S(84f);
            if (ActionRow(new Rect(left.x, y, left.width, T.S(52f)), IconId.New, "New track", "Start from an empty 16 × 16 grid"))
            {
                Close();
                Shell.NewTrack();
            }
            y += T.S(58f);
            if (ActionRow(new Rect(left.x, y, left.width, T.S(52f)), IconId.Open, "Open track…", "Browse your saved track designs"))
            {
                Close();
                Shell.ShowOpenDialog();
            }
            y += T.S(58f);
            if (ActionRow(new Rect(left.x, y, left.width, T.S(52f)), IconId.Keyboard, "Shortcuts & controls", "Mouse, keyboard and gamepad"))
            {
                Close();
                Shell.OpenDialog(new ShortcutsDialog());
            }

            // vertical divider
            SGUI.VLine(left.xMax + columnGap * 0.5f, rect.y, rect.height, Palette.Divider);

            // right column: recovery + recent
            var r = right;
            if (recovery.HasValue)
            {
                var card = r.TakeTop(T.S(96f));
                SGUI.RoundedOutline(card, Palette.Warning.WithAlpha(0.08f), Palette.Warning.WithAlpha(0.45f), T.S(7f));
                var inner = card.Inset(T.S(14f));
                SGUI.Icon(new Rect(inner.x, inner.y, T.S(20f), T.S(20f)), IconId.Recover, Palette.Warning, T.S(18f));
                SGUI.Label(new Rect(inner.x + T.S(30f), inner.y, inner.width - T.S(30f), T.S(20f)), "Recover unsaved work", T.BodyStrong);
                var pending = recovery.Value;
                SGUI.Label(new Rect(inner.x + T.S(30f), inner.y + T.S(22f), inner.width - T.S(30f), T.S(18f)),
                           SGUI.Ellipsize($"\"{pending.TrackName}\" from {TimeText.Relative(pending.SavedAt)} wasn't saved before the editor closed.", T.Small, inner.width - T.S(30f)),
                           T.Small, Palette.TextDim);

                var buttons = new Rect(inner.x + T.S(30f), inner.yMax - T.ControlHeight, T.S(100f), T.ControlHeight);
                if (SGUI.Button(buttons, "Recover", ButtonKind.Primary, true, IconId.Recover))
                {
                    Close();
                    Shell.ConfirmUnsavedChanges(Shell.RestoreRecovery);
                }
                buttons.x += buttons.width + T.S(8f);
                buttons.width = T.S(88f);
                if (SGUI.Button(buttons, "Discard", ButtonKind.Ghost))
                {
                    TrackRecovery.DiscardPending();
                    recovery = null;
                    Shell.Toast("Recovery data discarded", IconId.Trash);
                }
                r = r.CutTop(card.height + T.S(18f));
            }

            SGUI.SectionHeader(r.TakeTop(T.S(18f)), "Recent tracks");
            if (recent.Count > 0)
            {
                var clear = new Rect(r.xMax - T.S(60f), r.y - T.S(2f), T.S(60f), T.S(22f));
                if (SGUI.Button(clear, "Clear", ButtonKind.Ghost))
                {
                    AppSettings.ClearRecentFiles();
                    recent.Clear();
                }
            }
            r = r.CutTop(T.S(26f));

            if (recent.Count == 0)
            {
                SGUI.Label(r.TakeTop(T.S(40f)), "Tracks you open or save will appear here.", T.Small, Palette.TextFaint);
                return;
            }

            float rowHeight = T.S(46f);
            float contentHeight = recent.Count * rowHeight;
            var content = SGUI.BeginScroll(r, ref scroll, contentHeight);
            for (int i = 0; i < recent.Count; i++)
            {
                var entry = recent[i];
                var row = new Rect(content.x, content.y + i * rowHeight, content.width, rowHeight);
                if (SGUI.RowBehavior(row, entry.Exists, out bool hover, out _))
                {
                    Close();
                    Shell.OpenTrackFile(entry.Path);
                }
                if (hover)
                    SGUI.Rounded(row.Inset(0f, T.S(2f)), Palette.Hover);

                SGUI.Icon(new Rect(row.x + T.S(8f), row.y, T.S(20f), row.height), entry.Exists ? IconId.Track : IconId.Warning,
                          entry.Exists ? (hover ? Palette.Accent : Palette.TextDim) : Palette.TextFaint);
                var text = new Rect(row.x + T.S(38f), row.y + T.S(6f), row.width - T.S(46f), T.S(18f));
                SGUI.Label(text, SGUI.Ellipsize(entry.Name, T.BodyStrong, text.width - T.S(110f)), T.BodyStrong, entry.Exists ? Palette.Text : Palette.TextFaint);
                SGUI.Label(text, entry.Exists ? TimeText.Relative(entry.Modified) : "missing", T.Small, Palette.TextFaint, TextAnchor.MiddleRight);
                SGUI.Label(new Rect(text.x, text.yMax + T.S(1f), text.width, T.S(16f)), SGUI.Ellipsize(entry.Path, T.Small, text.width), T.Small, Palette.TextFaint);
            }
            SGUI.EndScroll(r, ref scroll, contentHeight);
        }

        private bool ActionRow(Rect r, IconId icon, string title, string subtitle)
        {
            bool clicked = SGUI.ClickBehavior(r, true, out bool hover, out bool pressed);
            SGUI.RoundedOutline(r, pressed ? Palette.Pressed : hover ? Palette.Hover : Palette.Raised, hover ? Palette.BorderStrong : Palette.Border, T.S(7f));

            var iconBox = new Rect(r.x + T.S(10f), r.y + (r.height - T.S(32f)) * 0.5f, T.S(32f), T.S(32f));
            SGUI.Rounded(iconBox, hover ? Palette.AccentSoft : Palette.Chrome, T.S(6f));
            SGUI.Icon(iconBox, icon, hover ? Palette.Accent : Palette.TextDim);

            SGUI.Label(new Rect(iconBox.xMax + T.S(12f), r.y + T.S(8f), r.width, T.S(18f)), title, T.BodyStrong);
            SGUI.Label(new Rect(iconBox.xMax + T.S(12f), r.y + T.S(27f), r.width - iconBox.width - T.S(30f), T.S(16f)), subtitle, T.Small, Palette.TextFaint);
            return clicked;
        }

        public override void DrawFooter(Rect rect)
        {
            if (SGUI.Button(FooterButton(ref rect, "Start editing"), "Start editing", ButtonKind.Primary))
                Close();

            var left = new Rect(rect.x, rect.y, T.S(260f), rect.height);
            bool show = SGUI.Toggle(left, AppSettings.Data.ShowWelcome, "Show this screen on startup");
            if (show != AppSettings.Data.ShowWelcome)
            {
                AppSettings.Data.ShowWelcome = show;
                AppSettings.Save();
            }
        }

        public override bool Submit()
        {
            Close();
            return true;
        }
    }
}

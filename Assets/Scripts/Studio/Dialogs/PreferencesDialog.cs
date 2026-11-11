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
using UnityEngine;

namespace Studio
{
    /// <summary>Preferences. Changes apply immediately and are saved when the dialog closes.</summary>
    public sealed class PreferencesDialog : StudioDialog
    {
        private static readonly float[] ScaleValues = { 0f, 1f, 1.25f, 1.5f, 2f };
        private static readonly string[] ScaleLabels = { "Auto", "100%", "125%", "150%", "200%" };
        private static readonly string[] IntervalLabels = { "30 s", "1 min", "2 min", "5 min", "10 min" };

        private float scroll;
        private bool changed;

        public PreferencesDialog()
        {
            Title = "Preferences";
            TitleIcon = IconId.Settings;
        }

        public override float Width => T.S(560f);

        public override float BodyHeight(float width) => Mathf.Min(T.S(500f), ContentHeight);

        private float ContentHeight => T.S(980f);

        public override void DrawBody(Rect rect)
        {
            var settings = AppSettings.Data;
            var content = SGUI.BeginScroll(rect, ref scroll, ContentHeight);
            float y = content.y;
            float w = content.width;
            float labelW = T.S(170f);

            Rect Row(float h = -1f)
            {
                if (h < 0f) h = T.ControlHeight;
                var r = new Rect(0f, y, w, h);
                y += h + T.S(10f);
                return r;
            }

            void Section(string title)
            {
                if (y > content.y + 1f)
                    y += T.S(6f);
                SGUI.SectionHeader(new Rect(0f, y, w, T.S(16f)), title);
                y += T.S(16f) + T.S(8f);
            }

            void FieldLabel(Rect r, string label) => SGUI.Label(r.TakeLeft(labelW), label, T.Body, Palette.TextDim);

            // Interface
            Section("Interface");
            var row = Row();
            FieldLabel(row, "Interface");
            int mode = SGUI.Segmented(row.CutLeft(labelW).TakeLeft(T.S(220f)), 0, new[] { "Studio", "Classic" }, true,
                                      new[] { "Desktop interface with panels and mouse editing", "The original editor's look and keyboard/gamepad controls (F10)" });
            bool switchToClassic = mode == 1;

            row = Row();
            FieldLabel(row, "Interface scale");
            int scaleIndex = Mathf.Max(0, Array.FindIndex(ScaleValues, v => Mathf.Approximately(v, settings.UiScale)));
            int newScale = SGUI.Segmented(row.CutLeft(labelW), scaleIndex, ScaleLabels);
            if (newScale != scaleIndex)
            {
                settings.UiScale = ScaleValues[newScale];
                changed = true;
            }

            row = Row();
            bool welcome = SGUI.Toggle(row.CutLeft(labelW), settings.ShowWelcome, "Show the welcome screen on startup");
            if (welcome != settings.ShowWelcome) { settings.ShowWelcome = welcome; changed = true; }

            // Viewport
            Section("Viewport");
            row = Row();
            bool follow = SGUI.Toggle(row.CutLeft(labelW), settings.CameraFollowsCursor, "Camera follows the keyboard cursor");
            if (follow != settings.CameraFollowsCursor) { settings.CameraFollowsCursor = follow; changed = true; }

            row = Row();
            bool footprint = SGUI.Toggle(row.CutLeft(labelW), settings.ShowFootprint, "Highlight the cells a module will cover");
            if (footprint != settings.ShowFootprint) { settings.ShowFootprint = footprint; changed = true; }

            row = Row();
            bool pickups = SGUI.Toggle(row.CutLeft(labelW), settings.ShowPickups, "Show pickups while editing modules");
            if (pickups != settings.ShowPickups) { settings.ShowPickups = pickups; changed = true; }

            row = Row();
            bool grid = SGUI.Toggle(row.CutLeft(labelW), settings.ShowGrid, "Show the grid");
            if (grid != settings.ShowGrid) { settings.ShowGrid = grid; changed = true; }

            row = Row();
            bool connections = SGUI.Toggle(row.CutLeft(labelW), settings.ShowConnections, "Show connection markers (joined, height steps, open ends)");
            if (connections != settings.ShowConnections) { settings.ShowConnections = connections; changed = true; }

            row = Row();
            bool minimap = SGUI.Toggle(row.CutLeft(labelW), settings.ShowMinimap, "Show the minimap");
            if (minimap != settings.ShowMinimap) { settings.ShowMinimap = minimap; changed = true; }

            row = Row();
            bool zoom = SGUI.Toggle(row.CutLeft(labelW), settings.ZoomToPointer, "Zoom towards the mouse pointer");
            if (zoom != settings.ZoomToPointer) { settings.ZoomToPointer = zoom; changed = true; }

            // Checks
            Section("Track checks");
            row = Row();
            bool autoCheck = SGUI.Toggle(row.CutLeft(labelW), settings.AutoCheck, "Check the track automatically after changes");
            if (autoCheck != settings.AutoCheck) { settings.AutoCheck = autoCheck; changed = true; }

            // Sound
            Section("Sound");
            row = Row();
            bool sounds = SGUI.Toggle(row.CutLeft(labelW), settings.SoundsEnabled, "Play editor sounds");
            if (sounds != settings.SoundsEnabled) { settings.SoundsEnabled = sounds; changed = true; AppSettings.Save(); }

            row = Row();
            FieldLabel(row, "Volume");
            var sliderRect = row.CutLeft(labelW).TakeLeft(T.S(240f));
            float volume = SGUI.Slider(sliderRect, settings.SoundVolume, 0f, 1f, settings.SoundsEnabled);
            SGUI.Label(new Rect(sliderRect.xMax + T.S(12f), row.y, T.S(60f), row.height), $"{Mathf.RoundToInt(volume * 100f)}%", T.Small, Palette.TextDim);
            if (!Mathf.Approximately(volume, settings.SoundVolume))
            {
                settings.SoundVolume = volume;
                AudioListener.volume = settings.SoundsEnabled ? volume : 0f;
                changed = true;
            }

            // Safety
            Section("Autosave & recovery");
            row = Row();
            bool autosave = SGUI.Toggle(row.CutLeft(labelW), settings.AutosaveEnabled, "Autosave unsaved changes for crash recovery");
            if (autosave != settings.AutosaveEnabled) { settings.AutosaveEnabled = autosave; changed = true; }

            row = Row();
            FieldLabel(row, "Autosave every");
            int interval = Array.IndexOf(AppSettings.AutosaveIntervalChoices, settings.AutosaveIntervalSeconds);
            if (interval < 0) interval = 2;
            int newInterval = SGUI.Segmented(row.CutLeft(labelW), interval, IntervalLabels, settings.AutosaveEnabled);
            if (newInterval != interval)
            {
                settings.AutosaveIntervalSeconds = AppSettings.AutosaveIntervalChoices[newInterval];
                TrackRecovery.ResetTimer();
                changed = true;
            }

            row = Row(T.S(36f));
            SGUI.Label(row.CutLeft(labelW), "Autosaves are kept outside your track folder and removed once the track is saved.", T.SmallWrapped, Palette.TextFaint);

            // Export
            Section("Export");
            row = Row();
            bool backup = SGUI.Toggle(row.CutLeft(labelW), settings.BackupBeforeExport, "Back up the level folder before exporting over it");
            if (backup != settings.BackupBeforeExport) { settings.BackupBeforeExport = backup; changed = true; }

            row = Row();
            FieldLabel(row, "Backups to keep");
            int keep = SGUI.Stepper(row.CutLeft(labelW).TakeLeft(T.S(150f)), settings.ExportBackupsToKeep, 1, 50, $"{settings.ExportBackupsToKeep} per track", settings.BackupBeforeExport);
            if (keep != settings.ExportBackupsToKeep) { settings.ExportBackupsToKeep = keep; changed = true; }
            if (SGUI.Button(row.TakeRight(T.S(84f)), "Show", ButtonKind.Secondary))
                StudioShell.RevealFolder(ExportBackups.RootDirectory);

            row = Row(T.S(36f));
            SGUI.Label(row.CutLeft(labelW), "Exporting replaces everything in the level folder. Backups go to the user data folder, not the game folder.", T.SmallWrapped, Palette.TextFaint);

            // Files
            Section("Files");
            row = Row();
            FieldLabel(row, "Tracks folder");
            var folder = row.CutLeft(labelW);
            SGUI.Label(folder.CutRight(T.S(90f)), SGUI.Ellipsize(FileHelper.SaveDirectory, T.Small, folder.width - T.S(96f)), T.Small, Palette.TextDim);
            if (SGUI.Button(folder.TakeRight(T.S(84f)), "Show", ButtonKind.Secondary))
                StudioShell.RevealFolder(FileHelper.SaveDirectory);

            row = Row();
            FieldLabel(row, "Game folder");
            SGUI.Label(row.CutLeft(labelW), FileHelper.GameDirectory ?? "Not found (export is unavailable)", T.Small,
                       FileHelper.GameDirectory != null ? Palette.TextDim : Palette.Warning);

            SGUI.EndScroll(rect, ref scroll, ContentHeight);

            // switch only after the scroll group is closed
            if (switchToClassic)
            {
                Close();
                Shell.Defer(() => TrackEditor.Instance.SetInterface(InterfaceMode.Classic, true));
            }
        }

        public override void DrawFooter(Rect rect)
        {
            if (SGUI.Button(FooterButton(ref rect, "Done"), "Done", ButtonKind.Primary))
                Close();
            var left = new Rect(rect.x, rect.y + (rect.height - T.ControlHeight) * 0.5f, T.S(150f), T.ControlHeight);
            if (SGUI.Button(left, "Keyboard shortcuts…", ButtonKind.Ghost, true, IconId.Keyboard))
            {
                Close();
                Shell.OpenDialog(new ShortcutsDialog());
            }
        }

        public override bool Submit()
        {
            Close();
            return true;
        }

        public override void OnClosed()
        {
            if (changed)
                AppSettings.Save();
        }
    }
}

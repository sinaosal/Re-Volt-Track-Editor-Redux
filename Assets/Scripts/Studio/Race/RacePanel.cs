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
using System.IO;
using System.Linq;
using UnityEngine;

namespace Studio
{
    /// <summary>
    /// Left panel of the race data workspace: status, layers, and imported models.
    /// </summary>
    public sealed class RacePanel
    {
        private const string ImportField = "studio.race.importModel";

        private readonly StudioShell shell;
        private float scroll;
        private string importPath = string.Empty;

        private StudioTheme T => shell.Theme;
        private RaceEditor Race => shell.Race;

        public RacePanel(StudioShell shell)
        {
            this.shell = shell;
        }

        public void Draw(Rect rect)
        {
            SGUI.Fill(rect, Palette.Panel);

            var header = new Rect(rect.x + T.Padding, rect.y, rect.width - T.Padding * 2f, T.S(40f));
            SGUI.Label(header, "Race data", T.BodyStrong);

            var body = new Rect(rect.x, header.yMax, rect.width, rect.yMax - header.yMax);
            SGUI.HLine(rect.x, body.y - 1f, rect.width, Palette.Divider);

            var data = Race.Data;
            float contentHeight = data == null ? T.S(320f) : T.S(330f) + RaceLayers.All.Length * T.S(34f) + data.Models.Count * T.S(30f);
            var content = SGUI.BeginScroll(body, ref scroll, contentHeight);
            var r = new Rect(content.x + T.Padding, content.y + T.S(12f), content.width - T.Padding * 2f, contentHeight);

            if (data == null)
                DrawEmpty(r);
            else
                DrawData(r, data);

            SGUI.EndScroll(body, ref scroll, contentHeight);
        }

        private void DrawEmpty(Rect r)
        {
            float y = r.y;
            if (Race.DataError != null)
            {
                SGUI.Label(new Rect(r.x, y, r.width, T.S(50f)), $"The stored race data couldn't be read: {Race.DataError}", T.SmallWrapped, Palette.Error);
                y += T.S(56f);
            }

            SGUI.Icon(new Rect(r.x, y + T.S(8f), r.width, T.S(28f)), IconId.Track, Palette.TextFaint, T.S(26f));
            y += T.S(44f);
            SGUI.Label(new Rect(r.x, y, r.width, T.S(20f)), "No race data yet", T.BodyStrong, Palette.TextDim, TextAnchor.MiddleCenter);
            y += T.S(26f);
            const string text = "Exports generate AI lines, position nodes, zones, lights and pickups from your track. " +
                                "Generate them here to adjust them, and to add instances and force fields.";
            SGUI.Label(new Rect(r.x, y, r.width, T.S(80f)), text, T.SmallWrapped, Palette.TextFaint, TextAnchor.UpperCenter);
            y += T.S(88f);

            if (SGUI.Button(new Rect(r.x, y, r.width, T.ControlHeight), "Generate from track", ButtonKind.Primary, true, IconId.Lightning))
                Race.Generate();
        }

        private void DrawData(Rect r, RaceData data)
        {
            float y = r.y;

            // status
            var status = new Rect(r.x, y, r.width, T.S(64f));
            bool stale = Race.IsStale;
            SGUI.RoundedOutline(status, stale ? Palette.Warning.WithAlpha(0.08f) : Palette.Raised, stale ? Palette.Warning.WithAlpha(0.45f) : Palette.Border, T.S(6f));
            var inner = status.Inset(T.S(10f));
            SGUI.Icon(new Rect(inner.x, inner.y, T.S(18f), T.S(18f)), stale ? IconId.Warning : IconId.Success, stale ? Palette.Warning : Palette.Success, T.S(16f));
            SGUI.Label(new Rect(inner.x + T.S(26f), inner.y, inner.width - T.S(26f), T.S(18f)),
                       stale ? "Track changed since generation" : "Matches the current track", T.SmallStrong, stale ? Palette.Warning : Palette.Text);
            SGUI.Label(new Rect(inner.x + T.S(26f), inner.y + T.S(20f), inner.width - T.S(26f), T.S(24f)),
                       stale ? "Regenerate if the lap no longer fits." : "Edits here are exported instead of generated data.",
                       T.SmallWrapped, Palette.TextFaint);
            y += status.height + T.S(10f);

            float half = (r.width - T.S(8f)) * 0.5f;
            if (SGUI.Button(new Rect(r.x, y, half, T.ControlHeight), "Regenerate…", stale ? ButtonKind.Primary : ButtonKind.Secondary, true, IconId.Lightning))
                Race.Generate();
            if (SGUI.Button(new Rect(r.x + half + T.S(8f), y, half, T.ControlHeight), "Remove…", ButtonKind.Ghost, true, IconId.Trash))
                Race.Remove();
            y += T.ControlHeight + T.S(10f);

            bool enabled = SGUI.Toggle(new Rect(r.x, y, r.width, T.S(24f)), data.Enabled, "Use when exporting");
            if (enabled != data.Enabled)
                Race.Commit(enabled ? "Use edited race data" : "Export generated race data", d => d.Enabled = enabled);
            y += T.S(36f);

            // layers
            SGUI.SectionHeader(new Rect(r.x, y, r.width, T.S(16f)), "Layers");
            y += T.S(22f);
            foreach (var layer in RaceLayers.All)
            {
                var row = new Rect(r.x - T.S(4f), y, r.width + T.S(8f), T.S(32f));
                bool active = Race.Layer == layer;
                bool hidden = Race.Hidden.Contains(layer);

                if (SGUI.RowBehavior(row.CutRight(T.S(32f)), true, out bool hover, out _))
                    Race.SetLayer(layer);

                if (active)
                    SGUI.Rounded(row, Palette.AccentSoft, T.S(5f));
                else if (hover)
                    SGUI.Rounded(row, Palette.Hover, T.S(5f));

                // drawn after the row background so it stays visible
                var eye = row.TakeRight(T.S(30f)).Inset(T.S(3f));
                if (SGUI.IconButton(eye, hidden ? IconId.Close : IconId.Eye, hidden ? "Show" : "Hide", false, true, hidden ? Palette.TextFaint : (Color?)null))
                {
                    if (hidden) Race.Hidden.Remove(layer);
                    else Race.Hidden.Add(layer);
                }

                var dot = new Rect(row.x + T.S(10f), row.center.y - T.S(4f), T.S(8f), T.S(8f));
                SGUI.Rounded(dot, RaceLayers.LayerColor(layer).WithAlpha(hidden ? 0.3f : 1f), T.S(4f));
                SGUI.Label(new Rect(row.x + T.S(26f), row.y, row.width - T.S(100f), row.height), RaceLayers.Name(layer),
                           active ? T.BodyStrong : T.Body, hidden ? Palette.TextFaint : Palette.Text);
                SGUI.Label(new Rect(row.xMax - T.S(80f), row.y, T.S(44f), row.height), RaceLayers.Count(data, layer).ToString(),
                           T.Small, Palette.TextFaint, TextAnchor.MiddleRight);
                y += T.S(34f);
            }
            y += T.S(8f);

            if (SGUI.Button(new Rect(r.x, y, r.width, T.ControlHeight), $"Add {RaceLayers.Singular(Race.Layer)}", ButtonKind.Secondary, true, IconId.Plus,
                            "Adds at the view centre, or after the selected node  Ins"))
                Race.AddElement();
            y += T.ControlHeight + T.S(22f);

            // models
            SGUI.SectionHeader(new Rect(r.x, y, r.width, T.S(16f)), "Models for instances");
            y += T.S(22f);
            if (data.Models.Count == 0)
            {
                SGUI.Label(new Rect(r.x, y, r.width, T.S(32f)), "Import .prm models to place them as instances.", T.SmallWrapped, Palette.TextFaint);
                y += T.S(34f);
            }
            for (int i = 0; i < data.Models.Count; i++)
            {
                var model = data.Models[i];
                bool exists = File.Exists(model.SourcePath);
                var row = new Rect(r.x, y, r.width, T.S(26f));
                SGUI.Icon(row.TakeLeft(T.S(18f)), exists ? IconId.Module : IconId.Warning, exists ? Palette.TextDim : Palette.Warning, T.S(14f));
                SGUI.Label(new Rect(row.x + T.S(24f), row.y, row.width - T.S(54f), row.height),
                           SGUI.Ellipsize($"{model.FileName}  ({model.InstanceName})", T.Small, row.width - T.S(54f)), T.Small, exists ? Palette.Text : Palette.Warning);
                SGUI.Tooltip(row, exists ? model.SourcePath : $"Missing: {model.SourcePath}");
                int index = i;
                if (SGUI.IconButton(row.TakeRight(T.S(26f)), IconId.Close, "Remove model"))
                    Race.Commit("Remove model", d => d.Models.RemoveAt(index));
                y += T.S(30f);
            }

            var field = new Rect(r.x, y, r.width - T.S(76f), T.S(30f));
            importPath = SGUI.TextField(field, importPath, ImportField, "Path to a .prm file");
            bool canImport = !string.IsNullOrWhiteSpace(importPath);
            if (SGUI.Button(new Rect(field.xMax + T.S(6f), y, T.S(70f), T.S(30f)), "Import", ButtonKind.Secondary, canImport)
                || (canImport && SGUI.SubmitPressed(ImportField)))
            {
                ImportModel(importPath.Trim().Trim('"'));
            }
        }

        private void ImportModel(string path)
        {
            if (!File.Exists(path) || !path.EndsWith(".prm", StringComparison.OrdinalIgnoreCase))
            {
                shell.ShowMessage("Can't import model", $"\"{path}\" isn't a .prm file that exists.", true);
                return;
            }

            string fileName = Path.GetFileName(path);
            if (fileName.Length > 19)
            {
                shell.ShowMessage("File name too long", "The game only loads models whose file name (with .prm) is at most 19 characters. Rename the file and import it again.", true);
                return;
            }

            ModelPreviews.Get(path, out string error);
            if (error != null)
            {
                shell.ShowMessage("Can't import model", error, true);
                return;
            }

            var data = Race.Data;
            var newModel = new RaceData.ModelFile { FileName = fileName, SourcePath = Path.GetFullPath(path) };
            var clash = data.Models.FirstOrDefault(m => string.Equals(m.InstanceName, newModel.InstanceName, StringComparison.OrdinalIgnoreCase));
            if (clash.FileName != null)
            {
                shell.ShowMessage("Name conflict",
                    $"The game tells models apart by the first 8 letters of their names, and \"{fileName}\" clashes with \"{clash.FileName}\". Rename one of them.", true);
                return;
            }

            Race.Commit($"Import {fileName}", d => d.Models.Add(newModel));
            importPath = string.Empty;
            GUIUtility.keyboardControl = 0;
            shell.Toast($"Imported {fileName}", IconId.Module);
        }
    }
}

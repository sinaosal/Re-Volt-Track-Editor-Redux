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
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Studio
{
    /// <summary>
    /// Export to the game: pre-flight checks, scale choice, progress, and results.
    /// </summary>
    public sealed class ExportDialog : StudioDialog
    {
        private enum Stage
        {
            Setup,
            Running,
            Done
        }

        private static readonly string[] ScaleLabels = { "Half", "Regular", "Double" };
        private static readonly float[] Scales = { 0.5f, 1f, 2f };

        private Stage stage = Stage.Setup;
        private int scaleIndex;
        private TrackValidator.Result check;
        private string checkError;
        private TrackExportJob job;

        public ExportDialog()
        {
            Title = "Export to Re-Volt";
            TitleIcon = IconId.Export;
        }

        public override float Width => T.S(540f);

        private bool HasGameFolder => FileHelper.GetCompiledLevelsPath() != null;
        private bool CanExport => HasGameFolder && checkError == null && check != null && check.IsExportable;

        private string Destination
        {
            get
            {
                string levels = FileHelper.GetCompiledLevelsPath();
                return levels == null ? null : Path.Combine(levels, FileHelper.TrackNameToExportDirectory(TrackEditor.Track.Name));
            }
        }

        public override void OnOpened()
        {
            scaleIndex = AppSettings.Data.LastExportScaleIndex;
            try
            {
                check = TrackValidator.Validate(TrackEditor.Track, TrackEditor.TrackUnit);
                Shell.SetValidationResult(check);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Export check failed: {ex}");
                checkError = ex.Message;
            }
        }

        public override void Cancel()
        {
            if (stage != Stage.Running)
                Close();
        }

        // Body
        public override float BodyHeight(float width)
        {
            switch (stage)
            {
                case Stage.Running:
                    return T.S(76f);
                case Stage.Done:
                    return T.S(96f) + (job != null && !job.Succeeded ? SGUI.WrappedHeight(job.Problem?.Message, T.Wrapped, width - T.S(44f))
                                                                     + (job.Problem?.Suggestion != null ? SGUI.WrappedHeight(job.Problem.Suggestion, T.SmallWrapped, width - T.S(44f)) + T.S(8f) : 0f)
                                                                   : T.S(24f) + (job?.BackupFolder != null ? T.S(26f) : 0f));
                default:
                    return T.S(172f) + ChecksHeight(width);
            }
        }

        private float ChecksHeight(float width)
        {
            if (checkError != null)
                return T.S(34f);
            if (check == null || check.Problems.Count == 0)
                return T.S(34f);
            float h = 0f;
            foreach (var p in check.Problems)
                h += Mathf.Max(T.S(30f), SGUI.WrappedHeight(p.Message, T.SmallWrapped, width - T.S(40f)) + T.S(12f));
            return h;
        }

        public override void DrawBody(Rect rect)
        {
            switch (stage)
            {
                case Stage.Setup:
                    DrawSetup(rect);
                    break;
                case Stage.Running:
                    DrawRunning(rect);
                    break;
                case Stage.Done:
                    DrawDone(rect);
                    break;
            }
        }

        private void DrawSetup(Rect rect)
        {
            var track = TrackEditor.Track;
            var r = rect;

            // summary
            int modules = track.GetAllModuleRootPlacements().Count();
            int pickups = track.Cells.Count(x => x.HasPickup);
            SGUI.Label(r.TakeTop(T.S(22f)), string.IsNullOrEmpty(track.Name) ? "Untitled track" : track.Name, T.Heading);
            r = r.CutTop(T.S(24f));
            SGUI.Label(r.TakeTop(T.S(18f)), $"{track.Width} × {track.Height} grid  ·  {modules} modules  ·  {pickups} pickups", T.Small, Palette.TextFaint);
            r = r.CutTop(T.S(32f));

            // scale
            SGUI.Label(r.TakeTop(T.S(18f)), "Scale", T.SmallStrong, Palette.TextDim);
            r = r.CutTop(T.S(22f));
            var scaleRow = r.TakeTop(T.ControlHeight);
            if (TrackEditor.ExportScaleOverride.HasValue)
            {
                SGUI.Label(scaleRow, $"Set to {TrackEditor.ExportScaleOverride.Value:0.###}× by the -exportscale command line option", T.Small, Palette.TextDim);
            }
            else
            {
                scaleIndex = SGUI.Segmented(scaleRow.TakeLeft(T.S(270f)), scaleIndex, ScaleLabels, true,
                                            new[] { "Half size in game", "Same size as the original editor", "Twice the size in game" });
            }
            r = r.CutTop(T.ControlHeight + T.S(14f));

            // destination
            SGUI.Label(r.TakeTop(T.S(18f)), "Destination", T.SmallStrong, Palette.TextDim);
            r = r.CutTop(T.S(22f));
            var destRow = r.TakeTop(T.S(20f));
            if (HasGameFolder)
            {
                SGUI.Icon(destRow.TakeLeft(T.S(16f)), IconId.Folder, Palette.TextFaint, T.S(14f));
                SGUI.Label(destRow.CutLeft(T.S(22f)), SGUI.Ellipsize(Destination, T.Small, destRow.width - T.S(22f)), T.Small, Palette.TextDim);
            }
            else
            {
                SGUI.Icon(destRow.TakeLeft(T.S(16f)), IconId.Warning, Palette.Warning, T.S(14f));
                SGUI.Label(destRow.CutLeft(T.S(22f)), "No Re-Volt installation found next to the editor folder", T.Small, Palette.Warning);
            }
            r = r.CutTop(T.S(32f));

            // checks
            DrawChecks(r);
        }

        private void DrawChecks(Rect r)
        {
            if (checkError != null)
            {
                DrawProblemRow(r.TakeTop(T.S(30f)), IconId.Error, Palette.Error, $"The track check failed: {checkError}");
                return;
            }

            if (check == null || check.Problems.Count == 0)
            {
                string reverse = check != null && check.ReverseAvailable ? " A reversed version will be exported too." : string.Empty;
                DrawProblemRow(r.TakeTop(T.S(30f)), IconId.Success, Palette.Success, "The track forms a valid lap and is ready to export." + reverse);
                return;
            }

            float y = r.y;
            foreach (var problem in check.Problems)
            {
                float h = Mathf.Max(T.S(30f), SGUI.WrappedHeight(problem.Message, T.SmallWrapped, r.width - T.S(40f)) + T.S(12f));
                var row = new Rect(r.x, y, r.width, h);
                var (icon, color) = StudioShell.ProblemStyle(problem);
                DrawProblemRow(row, icon, color, problem.Message);
                y += h;
            }
        }

        private void DrawProblemRow(Rect row, IconId icon, Color color, string text)
        {
            SGUI.Icon(new Rect(row.x, row.y + T.S(6f), T.S(18f), T.S(18f)), icon, color, T.S(16f));
            SGUI.Label(new Rect(row.x + T.S(28f), row.y + T.S(6f), row.width - T.S(28f), row.height - T.S(6f)), text, T.SmallWrapped, Palette.TextDim);
        }

        private void DrawRunning(Rect rect)
        {
            SGUI.Label(rect.TakeTop(T.S(22f)), job != null ? job.Stage + "…" : "Starting…", T.BodyStrong);
            var bar = new Rect(rect.x, rect.y + T.S(34f), rect.width, T.S(8f));
            SGUI.ProgressBar(bar, job?.Progress ?? 0f);
            SGUI.Label(new Rect(rect.x, bar.yMax + T.S(8f), rect.width, T.S(18f)),
                       $"{Mathf.RoundToInt((job?.Progress ?? 0f) * 100f)}%  ·  building world, collision and AI data", T.Small, Palette.TextFaint);
        }

        private void DrawDone(Rect rect)
        {
            if (job == null)
                return;

            var icon = new Rect(rect.x, rect.y, T.S(32f), T.S(32f));
            if (job.Succeeded)
            {
                SGUI.Icon(icon, IconId.Success, Palette.Success, T.S(30f));
                SGUI.Label(new Rect(icon.xMax + T.S(12f), rect.y + T.S(2f), rect.width, T.S(24f)), "Export complete", T.Heading);
                SGUI.Label(new Rect(icon.xMax + T.S(12f), rect.y + T.S(26f), rect.width - T.S(44f), T.S(18f)),
                           $"Finished in {job.Duration.TotalSeconds:0.0} s  ·  {(job.ReverseExported ? "forward and reversed" : "forward only")}", T.Small, Palette.TextFaint);

                var dest = new Rect(rect.x, rect.y + T.S(56f), rect.width, T.S(34f));
                SGUI.RoundedOutline(dest, Palette.Chrome, Palette.Border);
                SGUI.Icon(dest.TakeLeft(T.S(34f)), IconId.Folder, Palette.TextDim, T.S(15f));
                SGUI.Label(dest.CutLeft(T.S(34f)).CutRight(T.S(8f)), SGUI.Ellipsize(job.OutputFolder, T.Small, dest.width - T.S(44f)), T.Small, Palette.Text);
                SGUI.Label(new Rect(rect.x, dest.yMax + T.S(8f), rect.width, T.S(18f)),
                           "Start Re-Volt / RVGL and pick the track from the user tracks list.", T.Small, Palette.TextFaint);
                if (job.BackupFolder != null)
                {
                    var backup = new Rect(rect.x, dest.yMax + T.S(30f), rect.width, T.S(20f));
                    SGUI.Icon(backup.TakeLeft(T.S(16f)), IconId.Recover, Palette.TextFaint, T.S(13f));
                    SGUI.Label(backup.CutLeft(T.S(22f)).CutRight(T.S(90f)), "The previous version of the level folder was backed up.", T.Small, Palette.TextFaint);
                    if (SGUI.Button(backup.TakeRight(T.S(84f)), "Show backup", ButtonKind.Ghost))
                        StudioShell.RevealFolder(job.BackupFolder);
                }
            }
            else
            {
                SGUI.Icon(icon, IconId.Error, Palette.Error, T.S(30f));
                SGUI.Label(new Rect(icon.xMax + T.S(12f), rect.y + T.S(5f), rect.width, T.S(24f)), "Export failed", T.Heading);
                float mh = SGUI.WrappedHeight(job.Problem?.Message, T.Wrapped, rect.width - T.S(44f));
                SGUI.Label(new Rect(icon.xMax + T.S(12f), rect.y + T.S(40f), rect.width - T.S(44f), mh),
                           job.Problem?.Message, T.Wrapped, Palette.TextDim);
                if (job.Problem?.Suggestion != null)
                    SGUI.Label(new Rect(icon.xMax + T.S(12f), rect.y + T.S(48f) + mh, rect.width - T.S(44f), rect.height - mh - T.S(48f)),
                               "→ " + job.Problem.Suggestion, T.SmallWrapped, Palette.TextFaint);
            }
        }

        // Footer
        public override void DrawFooter(Rect rect)
        {
            switch (stage)
            {
                case Stage.Setup:
                    if (SGUI.Button(FooterButton(ref rect, "Export"), "Export", ButtonKind.Primary, CanExport, IconId.Export))
                        Submit();
                    if (SGUI.Button(FooterButton(ref rect, "Cancel"), "Cancel"))
                        Cancel();
                    var firstError = check?.Problems.FirstOrDefault(x => x.IsError && x.Cell.HasValue);
                    if (firstError != null && SGUI.Button(FooterButton(ref rect, "Show problem"), "Show problem", ButtonKind.Ghost))
                    {
                        Close();
                        Shell.FocusProblem(firstError);
                    }
                    break;

                case Stage.Running:
                    SGUI.Label(rect, "Please wait. Files are being written to the game folder.", T.Small, Palette.TextFaint);
                    break;

                case Stage.Done:
                    if (SGUI.Button(FooterButton(ref rect, "Done"), "Done", ButtonKind.Primary))
                        Close();
                    if (job != null && job.Succeeded && job.OutputFolder != null && SGUI.Button(FooterButton(ref rect, "Show folder"), "Show folder"))
                        StudioShell.RevealFolder(job.OutputFolder);
                    if (job != null && !job.Succeeded && job.Problem != null && job.Problem.Cell.HasValue && SGUI.Button(FooterButton(ref rect, "Show problem"), "Show problem"))
                    {
                        Close();
                        Shell.FocusProblem(job.Problem);
                    }
                    break;
            }
        }

        public override bool Submit()
        {
            if (stage == Stage.Setup && CanExport)
            {
                StartExport();
                return true;
            }
            if (stage == Stage.Done)
            {
                Close();
                return true;
            }
            return false;
        }

        private void StartExport()
        {
            AppSettings.Data.LastExportScaleIndex = scaleIndex;
            AppSettings.Save();

            float scale = TrackEditor.ExportScaleOverride ?? Scales[scaleIndex];
            job = new TrackExportJob(TrackEditor.Track, TrackEditor.TrackUnit, scale);
            stage = Stage.Running;
            Dismissible = false;
            Shell.StartCoroutine(RunJob());
        }

        private IEnumerator RunJob()
        {
            yield return job.Run();

            stage = Stage.Done;
            Dismissible = true;

            if (job.Succeeded)
            {
                TrackEditor.PlaySound(TrackEditor.SndExportComplete);
                Shell.Toast("Track exported", IconId.Success);
            }
            else
            {
                TrackEditor.PlaySound(TrackEditor.SndWarning);
                if (job.Problem != null)
                {
                    check = new TrackValidator.Result();
                    check.Problems.Add(job.Problem);
                    Shell.SetValidationResult(check);
                }
            }
        }
    }
}

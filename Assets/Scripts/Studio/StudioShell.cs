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
    /// Root of the Studio interface: a desktop style workspace (menu bar, toolbar, module library,
    /// viewport, inspector, status bar) drawn with IMGUI on top of the existing editor scene.
    ///
    /// Drawing is layered: workspace, toasts, dialogs, menus, tooltip. While a dialog or menu is
    /// open, the layers beneath keep drawing normally but ignore the pointer and keyboard.
    /// </summary>
    public sealed partial class StudioShell : MonoBehaviour
    {
        private struct ToastMessage
        {
            public string Text;
            public IconId Icon;
            public float Time;
        }

        public StudioTheme Theme { get; } = new StudioTheme();
        public CommandRegistry Commands { get; } = new CommandRegistry();
        public ModuleThumbnails Thumbnails { get; } = new ModuleThumbnails();
        public StudioViewport Viewport { get; private set; }

        private ModuleLibraryPanel library;
        private OutlinerPanel outliner;
        private InspectorPanel inspector;
        private RacePanel racePanel;

        public RaceEditor Race { get; private set; }
        public int TrackRevision => trackRevision;

        private readonly List<StudioDialog> dialogs = new List<StudioDialog>();
        private readonly List<ToastMessage> toasts = new List<ToastMessage>();
        private readonly Queue<Action> deferred = new Queue<Action>();
        private readonly List<Rect> overlayRects = new List<Rect>();
        private readonly List<Rect> overlayScratch = new List<Rect>();

        private PopupMenu menu;
        private int menuBarIndex = -1;

        private bool active;
        private bool initialized;
        private bool startupHandled;

        // scene state restored when switching back to the classic interface
        private Camera backgroundCamera;
        private Color classicBackgroundColor;
        private Color classicGridColor;

        // layout (GUI coordinates)
        private Rect menuBarRect, toolbarRect, statusRect, leftRect, rightRect, viewportRect;

        // validation
        public TrackValidator.Result LastValidation { get; private set; }
        private int trackRevision;
        private int validationRevision;
        public bool ValidationIsStale => LastValidation != null && validationRevision != trackRevision;
        private float lastChangeTime;
        private float autoCheckDelay = 0.8f;

        // State queried by TrackEditor
        public bool HasModal => dialogs.Count > 0;
        public bool IsTextInputActive => active && GUIUtility.keyboardControl != 0;
        /// <summary>
        /// Keyboard/gamepad editing (arrows, Enter, Delete, ...) acts on the placement cursor, which is only
        /// shown by the place tool; the other tools are pointer driven.
        /// </summary>
        public bool AllowsEditorInput => active && !HasModal && menu == null && !IsTextInputActive
                                          && Viewport != null && Viewport.Tool == StudioTool.Place && !Race.Active && !Viewport.PasteActive;

        private static TrackEditingMode Editing => TrackEditor.GetEditMode<TrackEditingMode>();

        // Lifecycle
        private void Initialize()
        {
            if (initialized)
                return;
            initialized = true;

            Viewport = new StudioViewport(this);
            Race = new RaceEditor(this);
            library = new ModuleLibraryPanel(this);
            outliner = new OutlinerPanel(this);
            racePanel = new RacePanel(this);
            inspector = new InspectorPanel(this);

            RegisterCommands();
            Commands.LoadBindings();

            backgroundCamera = TrackEditor.FindSceneCamera("Background Camera");
            if (backgroundCamera != null)
                classicBackgroundColor = backgroundCamera.backgroundColor;
            classicGridColor = TrackEditor.Instance.Grid.Color;
        }

        public void Activate()
        {
            Initialize();
            active = true;
            enabled = true;

            var editing = Editing;
            editing.ControlsCamera = false;
            editing.BlinkPreview = false;
            editing.DrawCursorBox = false;
            editing.PreviewVisible = true;
            editing.CameraRotateRequested += OnCameraRotateRequested;
            editing.IsProtected = Viewport.States.IsLocked;
            editing.IsHidden = Viewport.States.IsHidden;
            editing.EditRefused += OnEditRefused;
            BlockOperations.States = Viewport.States;

            TrackEditor.UnsavedChangesPrompt = ConfirmUnsavedChanges;
            TrackEditor.TrackChanged += OnTrackChanged;
            TrackEditor.TrackReplaced += OnTrackReplaced;
            TrackEditor.TrackSaved += OnTrackSaved;

            if (backgroundCamera != null)
                backgroundCamera.backgroundColor = Palette.Viewport;
            TrackEditor.Instance.Grid.Color = Palette.Grid;

            useGUILayout = false; // only explicit-rect IMGUI is used; skips the layout pass

            // the track may have changed while the classic interface was active
            LastValidation = null;
            trackRevision++;
            ComputeLayout();
            Viewport.Rect = viewportRect;
            Viewport.Activate();

            if (!startupHandled)
            {
                startupHandled = true;
                // the welcome screen also offers crash recovery
                bool recovery = TrackRecovery.FindPendingRecovery().HasValue;
                if ((AppSettings.Data.ShowWelcome && TrackEditor.StartupFilePath == null) || recovery)
                    OpenDialog(new WelcomeDialog());
            }
        }

        public void Deactivate()
        {
            if (!active)
                return;
            active = false;

            Viewport.CancelInteraction();
            Viewport.EndPaste();
            foreach (var dialog in dialogs.ToArray())
                CloseDialog(dialog);
            CloseMenu();
            GUIUtility.keyboardControl = 0;

            var editing = Editing;
            editing.ControlsCamera = true;
            editing.BlinkPreview = true;
            editing.DrawCursorBox = true;
            editing.PreviewVisible = true;
            editing.CameraRotateRequested -= OnCameraRotateRequested;
            // the classic interface has no way to show locks or hidden blocks, so they don't apply there
            editing.IsProtected = null;
            editing.IsHidden = null;
            editing.EditRefused -= OnEditRefused;
            BlockOperations.States = null;

            TrackEditor.TrackChanged -= OnTrackChanged;
            TrackEditor.TrackReplaced -= OnTrackReplaced;
            TrackEditor.TrackSaved -= OnTrackSaved;

            var cam = TrackEditor.Instance.Camera;
            cam.rect = new Rect(0f, 0f, 1f, 1f);
            cam.orthographic = false;
            if (backgroundCamera != null)
                backgroundCamera.backgroundColor = classicBackgroundColor;
            TrackEditor.Instance.Grid.Color = classicGridColor;
            TrackEditor.Instance.Grid.enabled = true;

            AppSettings.Save();
            enabled = false;
        }

        private void OnDestroy()
        {
            Thumbnails.Dispose();
            Theme.Dispose();
            StudioIcons.Clear();
        }

        // Event handlers
        private void OnCameraRotateRequested(int turns) => Viewport.Orbit90(turns);
        private void OnEditRefused(string message) => Toast(message, IconId.Lock);
        private void OnTrackSaved(string path) { }

        private void OnTrackChanged()
        {
            trackRevision++;
            lastChangeTime = Time.unscaledTime;
            Viewport.States.Prune();
        }

        private void OnTrackReplaced()
        {
            trackRevision++;
            lastChangeTime = Time.unscaledTime;
            LastValidation = null;
            Viewport.EndPaste();
            Viewport.Selection.Clear();
            Viewport.States.Clear();
            Viewport.FrameTrack(true);
        }

        /// <summary>Re-checks the track shortly after edits stop, so the Issues tab stays current.</summary>
        private void AutoCheck()
        {
            if (!AppSettings.Data.AutoCheck || HasModal || Viewport.IsDragging || Race.Active)
                return;
            if (LastValidation != null && validationRevision == trackRevision)
                return;
            if (Time.unscaledTime - lastChangeTime < autoCheckDelay)
                return;

            int revision = trackRevision;
            try
            {
                SetValidationResult(TrackValidator.Validate(TrackEditor.Track, TrackEditor.TrackUnit));
                // big tracks: check less eagerly so editing stays smooth
                autoCheckDelay = LastValidation.Milliseconds > 120 ? 3f : 0.8f;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Automatic track check failed: {ex}");
                validationRevision = revision; // don't retry every frame
                autoCheckDelay = 3f;
            }
        }

        // Update
        private void Update()
        {
            if (!active)
                return;

            while (deferred.Count > 0)
            {
                var action = deferred.Dequeue();
                try { action(); }
                catch (Exception ex) { Debug.LogError($"StudioShell deferred action failed: {ex}"); }
                if (!active)
                    return;
            }

            ComputeLayout();
            Viewport.Rect = viewportRect;
            TrackEditor.Instance.Grid.enabled = AppSettings.Data.ShowGrid;

            HandleGlobalKeys();

            if (dialogs.Count > 0)
                dialogs[dialogs.Count - 1].Update();

            Viewport.Update(PointerAllowedInViewport());
            AutoCheck();

            for (int i = toasts.Count - 1; i >= 0; i--)
            {
                if (Time.unscaledTime - toasts[i].Time > 3.2f)
                    toasts.RemoveAt(i);
            }
        }

        private void LateUpdate()
        {
            if (!active)
                return;
            Viewport.ApplyCamera();
            Thumbnails.Process(3);
        }

        private bool PointerAllowedInViewport()
        {
            if (HasModal || menu != null || GUIUtility.hotControl != 0)
                return false;

            if (Mouse.current == null)
                return false;
            var screen = Mouse.current.position.ReadValue();
            var gui = new Vector2(screen.x, Screen.height - screen.y);
            foreach (var r in overlayRects)
            {
                if (r.Contains(gui))
                    return false;
            }
            return true;
        }

        private void HandleGlobalKeys()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || HasModal)
                return;

            if (IsTextInputActive)
                return;

            if (Commands.ProcessShortcuts())
            {
                CloseMenu();
                return;
            }

            // common alternative redo binding
            if (keyboard.zKey.wasPressedThisFrame && Controls.IsControlHeld() && Controls.IsShiftHeld() && !Controls.IsAltHeld())
            {
                TrackEditor.Redo();
                return;
            }

            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                if (menu != null)
                    CloseMenu();
                else if (Viewport.IsDragging)
                    Viewport.CancelInteraction();
                else if (Viewport.PasteActive)
                    Viewport.EndPaste();
                else if (Race.Active && Race.Selected >= 0)
                    Race.Selected = -1;
                else if (Viewport.Tool == StudioTool.Select && !Viewport.Selection.IsEmpty)
                    Viewport.SelectNone();
                else if (Viewport.Tool != StudioTool.Place)
                    Viewport.SetTool(StudioTool.Place);
                return;
            }

            if (Race.Active && menu == null)
                Race.HandleKeys();
            else if (Viewport.Tool == StudioTool.Select && menu == null)
                Viewport.HandleSelectKeys();
        }

        // Layout
        private float ResolveScale()
        {
            float setting = AppSettings.Data.UiScale;
            return setting > 0f ? setting : StudioTheme.DetectScale();
        }

        private void ComputeLayout()
        {
            var T = Theme;
            var settings = AppSettings.Data;
            float w = Screen.width;
            float h = Screen.height;

            menuBarRect = new Rect(0f, 0f, w, T.MenuBarHeight);
            toolbarRect = new Rect(0f, menuBarRect.yMax, w, T.ToolbarHeight);
            float statusHeight = settings.StatusBarVisible ? T.StatusBarHeight : 0f;
            statusRect = new Rect(0f, h - statusHeight, w, statusHeight);

            float top = toolbarRect.yMax;
            float bottom = statusRect.y;
            float minViewport = T.S(320f);

            float left = settings.LeftPanelVisible ? T.S(settings.LeftPanelWidth) : 0f;
            float right = settings.RightPanelVisible ? T.S(settings.RightPanelWidth) : 0f;

            // shrink panels on small windows so the viewport stays usable
            float overflow = left + right + minViewport - w;
            if (overflow > 0f)
            {
                float total = Mathf.Max(1f, left + right);
                left = Mathf.Max(0f, left - overflow * left / total);
                right = Mathf.Max(0f, right - overflow * right / total);
            }

            leftRect = new Rect(0f, top, left, bottom - top);
            rightRect = new Rect(w - right, top, right, bottom - top);
            viewportRect = new Rect(leftRect.xMax, top, rightRect.x - leftRect.xMax, bottom - top);
        }

        // GUI
        private void OnGUI()
        {
            if (!active)
                return;

            Theme.Ensure(ResolveScale());
            SGUI.T = Theme;
            GUI.skin = Theme.Skin;
            SGUI.ResetFrameState();
            SGUI.BeginTooltipFrame();

            var e = Event.current;
            bool blockBase = dialogs.Count > 0 || menu != null;

            if (e.type == EventType.Repaint)
                overlayRects.Clear();

            // layer 1: workspace
            bool oldEnabled = GUI.enabled;
            SGUI.InputBlocked = blockBase;
            if (blockBase && e.type != EventType.Repaint && e.type != EventType.Layout)
                GUI.enabled = false;

            DrawWorkspace();

            GUI.enabled = oldEnabled;
            SGUI.InputBlocked = false;

            // the menu bar stays interactive while a menu is open, to switch between menus
            if (menu != null && menuBarIndex >= 0)
                HandleMenuBarWhileOpen();

            // layer 2: toasts
            DrawToasts();

            // layer 3: dialogs (only the top one is interactive)
            var snapshot = dialogs.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                bool isTop = i == snapshot.Length - 1;
                SGUI.InputBlocked = !isTop;
                bool dialogEnabled = GUI.enabled;
                if (!isTop && e.type != EventType.Repaint && e.type != EventType.Layout)
                    GUI.enabled = false;
                DrawDialog(snapshot[i], isTop);
                GUI.enabled = dialogEnabled;
            }
            SGUI.InputBlocked = false;

            // layer 4: menus
            if (menu != null)
                DrawMenu();

            // layer 5: tooltip
            SGUI.DrawTooltip();

            // clicking empty space ends text editing
            if (e.type == EventType.MouseDown && GUIUtility.keyboardControl != 0)
                GUIUtility.keyboardControl = 0;

            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape && GUIUtility.keyboardControl != 0 && dialogs.Count == 0)
            {
                GUIUtility.keyboardControl = 0;
                e.Use();
            }
        }

        private void DrawWorkspace()
        {
            var settings = AppSettings.Data;

            if (leftRect.width > 0f)
            {
                if (Race.Active)
                    racePanel.Draw(leftRect);
                else
                    DrawLeftPanel(leftRect);
            }
            if (rightRect.width > 0f)
                inspector.Draw(rightRect);

            DrawSplitters();

            // viewport overlays (always drawn, so control IDs stay stable; blockers recorded on repaint)
            overlayScratch.Clear();
            Viewport.DrawOverlay(overlayScratch);
            if (Race.Active)
                Race.DrawLabels(Theme);
            if (Event.current.type == EventType.Repaint)
                overlayRects.AddRange(overlayScratch);
            DrawViewportEmptyState();

            DrawToolbar(toolbarRect);
            DrawMenuBar(menuBarRect);
            if (settings.StatusBarVisible)
                DrawStatusBar(statusRect);
        }

        /// <summary>Module library and outliner share the left panel as tabs.</summary>
        private void DrawLeftPanel(Rect rect)
        {
            var T = Theme;
            var settings = AppSettings.Data;
            SGUI.Fill(rect, Palette.Panel);
            var tabs = new Rect(rect.x + T.S(10f), rect.y + T.S(8f), rect.width - T.S(20f), T.S(28f));
            int count = TrackEditor.Track?.GetAllModuleRootPlacements().Count() ?? 0;
            int tab = SGUI.Segmented(tabs, settings.LeftPanelTab, new[] { "Modules", count > 0 ? $"Outliner {count}" : "Outliner" }, true,
                                     new[] { "Module library", "Every placed block: select, find, lock and hide" });
            if (tab != settings.LeftPanelTab)
            {
                settings.LeftPanelTab = tab;
                AppSettings.Save();
            }
            var body = new Rect(rect.x, tabs.yMax + T.S(4f), rect.width, rect.yMax - tabs.yMax - T.S(4f));
            if (settings.LeftPanelTab == 1)
                outliner.Draw(body);
            else
                library.Draw(body);
        }

        public void ShowOutliner()
        {
            AppSettings.Data.LeftPanelVisible = true;
            AppSettings.Data.LeftPanelTab = 1;
            AppSettings.Save();
        }

        private void DrawViewportEmptyState()
        {
            if (TrackEditor.Track == null || TrackEditor.Track.Cells.Length == 0 || Race.Active)
                return;
            foreach (var _ in TrackEditor.Track.GetAllModuleRootPlacements())
                return;

            var T = Theme;
            string title = "Start with a start grid";
            string text = "Choose a module on the left and click in the grid to place it. Build a closed loop, then export.";
            float width = Mathf.Min(T.S(420f), viewportRect.width - T.S(40f));
            var card = new Rect(viewportRect.center.x - width * 0.5f, viewportRect.yMax - T.S(110f), width, T.S(66f)).Snap();
            SGUI.Rounded(card, Palette.Chrome.WithAlpha(0.9f), T.S(8f));
            SGUI.Icon(new Rect(card.x + T.S(14f), card.y, T.S(22f), card.height), IconId.Lightning, Palette.Accent, T.S(18f));
            SGUI.Label(new Rect(card.x + T.S(46f), card.y + T.S(12f), card.width - T.S(58f), T.S(20f)), title, T.BodyStrong);
            SGUI.Label(new Rect(card.x + T.S(46f), card.y + T.S(32f), card.width - T.S(58f), T.S(30f)), text, T.SmallWrapped, Palette.TextDim);
            if (Event.current.type == EventType.Repaint)
                overlayRects.Add(card);
        }

        private void DrawSplitters()
        {
            var T = Theme;
            var settings = AppSettings.Data;
            if (leftRect.width > 0f)
            {
                var hit = new Rect(leftRect.xMax - T.S(3f), leftRect.y, T.S(6f), leftRect.height);
                if (Event.current.type == EventType.Repaint)
                    overlayRects.Add(hit); // the hit area overlaps the viewport edge
                float width = Splitter(hit, leftRect.xMax, true);
                if (width >= 0f)
                    settings.LeftPanelWidth = Mathf.Clamp(width / T.Scale, 180f, 640f);
                SGUI.VLine(leftRect.xMax - 1f, leftRect.y, leftRect.height, Palette.Border);
            }
            if (rightRect.width > 0f)
            {
                var hit = new Rect(rightRect.x - T.S(3f), rightRect.y, T.S(6f), rightRect.height);
                if (Event.current.type == EventType.Repaint)
                    overlayRects.Add(hit);
                float width = Splitter(hit, rightRect.x, false);
                if (width >= 0f)
                    settings.RightPanelWidth = Mathf.Clamp(width / T.Scale, 200f, 640f);
                SGUI.VLine(rightRect.x, rightRect.y, rightRect.height, Palette.Border);
            }
        }

        /// <summary>Draggable panel edge. Returns the new panel width while dragging, otherwise -1.</summary>
        private float Splitter(Rect hit, float edge, bool isLeft)
        {
            int id = GUIUtility.GetControlID(FocusType.Passive);
            var e = Event.current;
            bool hover = SGUI.PointerIn(hit);

            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (hover && e.button == 0)
                    {
                        GUIUtility.hotControl = id;
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id)
                    {
                        e.Use();
                        return isLeft ? e.mousePosition.x : Screen.width - e.mousePosition.x;
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        AppSettings.Save();
                        e.Use();
                    }
                    break;
                case EventType.Repaint:
                    if (hover || GUIUtility.hotControl == id)
                        SGUI.Fill(new Rect(edge - Theme.S(1f), hit.y, Theme.S(2f), hit.height), Palette.Accent.WithAlpha(0.8f));
                    break;
            }
            return -1f;
        }

        // Dialogs
        public void OpenDialog(StudioDialog dialog)
        {
            Viewport?.CancelInteraction();
            Race?.CancelInteraction();
            CloseMenu();
            GUIUtility.keyboardControl = 0;
            dialog.Shell = this;
            dialogs.Add(dialog);
            dialog.OnOpened();
        }

        public void CloseDialog(StudioDialog dialog)
        {
            if (!dialogs.Remove(dialog))
                return;
            GUIUtility.keyboardControl = 0;
            dialog.OnClosed();
        }

        private void DrawDialog(StudioDialog dialog, bool isTop)
        {
            var T = Theme;
            var e = Event.current;

            SGUI.Fill(new Rect(0f, 0f, Screen.width, Screen.height), Palette.Scrim);

            float width = Mathf.Min(dialog.Width, Screen.width - T.S(32f));
            float pad = T.S(22f);
            float headerHeight = T.S(56f);
            float footerHeight = dialog.HasFooter ? dialog.FooterHeight : T.S(16f);
            float bodyHeight = dialog.BodyHeight(width - pad * 2f);
            float height = headerHeight + bodyHeight + footerHeight + T.S(8f);
            float maxHeight = Screen.height - T.S(32f);
            if (height > maxHeight)
            {
                bodyHeight -= height - maxHeight;
                height = maxHeight;
            }

            var card = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height).Snap();

            // keyboard (before the body so Enter submits even while a text field has focus)
            if (isTop && e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Escape)
                {
                    if (dialog.Dismissible)
                        dialog.Cancel();
                    e.Use();
                }
                else if ((e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && dialog.Submit())
                {
                    e.Use();
                }
            }

            SGUI.Shadow(card, T.S(16f));
            SGUI.RoundedOutline(card, Palette.Overlay, Palette.BorderStrong, T.S(10f));

            // header
            var header = new Rect(card.x + pad, card.y, card.width - pad * 2f, headerHeight);
            float titleX = header.x;
            if (dialog.TitleIcon.HasValue)
            {
                SGUI.Icon(new Rect(header.x, header.y, T.S(20f), header.height), dialog.TitleIcon.Value, dialog.TitleIconColor, T.S(18f));
                titleX += T.S(30f);
            }
            SGUI.Label(new Rect(titleX, header.y, header.xMax - titleX - T.S(36f), header.height), dialog.Title, T.Heading);
            if (dialog.Dismissible)
            {
                var close = new Rect(card.xMax - T.S(44f), card.y + (headerHeight - T.S(28f)) * 0.5f, T.S(28f), T.S(28f));
                if (SGUI.IconButton(close, IconId.Close, "Close  Esc"))
                    dialog.Cancel();
            }

            // body
            var body = new Rect(card.x + pad, card.y + headerHeight, card.width - pad * 2f, bodyHeight);
            dialog.DrawBody(body);

            // footer
            if (dialog.HasFooter)
            {
                var footer = new Rect(card.x, card.yMax - footerHeight, card.width, footerHeight);
                SGUI.HLine(footer.x + T.S(1f), footer.y, footer.width - T.S(2f), Palette.Border);
                dialog.DrawFooter(new Rect(footer.x + pad, footer.y, footer.width - pad * 2f, footer.height));
            }

            dialog.AdvanceFrame();

            // swallow clicks on the backdrop so they never reach the workspace
            if (isTop && e.type == EventType.MouseDown && !card.Contains(e.mousePosition))
                e.Use();
        }

        public void ShowMessage(string title, string message, bool isError)
        {
            OpenDialog(new MessageDialog(title, message, isError ? MessageKind.Error : MessageKind.Warning));
        }

        // Menus
        public void OpenMenu(List<MenuItem> items, Vector2 position, int barIndex = -1, float minWidth = 0f)
        {
            Viewport.CancelInteraction();
            menu = new PopupMenu(items);
            menu.Layout(Theme, position, minWidth);
            menuBarIndex = barIndex;
            GUIUtility.keyboardControl = 0;
            TrackEditor.PlaySound(TrackEditor.SndMenuMove);
        }

        public void CloseMenu()
        {
            menu = null;
            menuBarIndex = -1;
        }

        private void DrawMenu()
        {
            var e = Event.current;
            var current = menu;
            bool chosen = current.Draw(Theme);
            if (chosen || current.PendingClose)
            {
                if (menu == current)
                    CloseMenu();
                return;
            }

            if (menu == null)
                return;

            if (e.type == EventType.MouseDown && !menu.Contains(e.mousePosition))
            {
                CloseMenu();
                e.Use();
            }
            else if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                CloseMenu();
                e.Use();
            }
        }

        public void OpenViewportContextMenu(Vector2 guiPosition, Vector2Int? cell)
        {
            var items = new List<MenuItem>();
            var track = TrackEditor.Track;
            var editing = Editing;

            // with blocks selected, the menu acts on the selection
            if (Viewport.Tool == StudioTool.Select && !Viewport.Selection.IsEmpty)
            {
                var selected = Viewport.SelectedPlacements;
                bool locked = selected.All(BlockOperations.IsLocked);
                items.Add(MenuItem.Command(Commands.Get("edit.copy")));
                items.Add(MenuItem.Command(Commands.Get("edit.cut")));
                items.Add(MenuItem.Command(Commands.Get("edit.duplicate")));
                items.Add(MenuItem.Command(Commands.Get("edit.paste")));
                items.Add(MenuItem.Separator());
                items.Add(MenuItem.Command(Commands.Get("place.rotateCW")));
                items.Add(MenuItem.Command(Commands.Get("place.rotateCCW")));
                items.Add(MenuItem.Command(Commands.Get("place.raise")));
                items.Add(MenuItem.Command(Commands.Get("place.lower")));
                items.Add(MenuItem.Separator());
                items.Add(MenuItem.Command(Commands.Get("edit.selectConnected")));
                items.Add(MenuItem.Command(Commands.Get("edit.selectSameType")));
                items.Add(new MenuItem { Label = locked ? "Unlock" : "Lock", Icon = locked ? IconId.LockOpen : IconId.Lock, Action = () => Viewport.SetLocked(selected, !locked) });
                items.Add(MenuItem.Command(Commands.Get("edit.hide")));
                items.Add(MenuItem.Separator());
                items.Add(MenuItem.Command(Commands.Get("edit.delete"), selected.Count == 1 ? "Delete block" : $"Delete {selected.Count} blocks"));
                OpenMenu(items, guiPosition);
                return;
            }

            if (cell.HasValue)
            {
                var c = cell.Value;
                var trackCell = track.GetCell(c);
                bool hasModule = trackCell.Module != null;
                string moduleName = hasModule ? ModuleCatalog.GetDisplayName(trackCell.Module.ModuleIndex) : null;

                items.Add(new MenuItem { Label = hasModule ? $"Select {moduleName}" : "Select block", Icon = IconId.Move, Enabled = hasModule, Action = () =>
                {
                    Viewport.SetTool(StudioTool.Select);
                    Viewport.SelectedRoot = trackCell.Module.RootCell.Position;
                    inspector.SetTab(InspectorTab.Place);
                } });
                items.Add(new MenuItem { Label = hasModule ? $"Pick {moduleName}" : "Pick module", Shortcut = "Alt+Click", Icon = IconId.ToolEyedropper, Enabled = hasModule, Action = () => Viewport.PickModule(c) });
                items.Add(new MenuItem { Label = hasModule ? $"Delete {moduleName}" : "Delete module", Icon = IconId.Trash, Enabled = hasModule, Action = () => Viewport.EraseAt(c) });
                items.Add(new MenuItem { Label = trackCell.HasPickup ? "Remove pickup" : "Add pickup", Icon = IconId.ToolPickup, Enabled = hasModule, Action = () => Viewport.SetPickup(c, !trackCell.HasPickup, true) });
                items.Add(MenuItem.Separator());
                items.Add(new MenuItem { Label = $"Place {ModuleCatalog.GetDisplayName(editing.ActiveModuleIndex)} here", Icon = IconId.ToolPlace, Action = () =>
                {
                    editing.CursorPosition = c;
                    editing.PlaceModule();
                } });
                items.Add(new MenuItem { Label = "Paste here", Icon = IconId.Paste, Shortcut = Commands.Get("edit.paste").BindingText, Enabled = !Viewport.Clipboard.IsEmpty, Action = Viewport.PasteClipboard });
                if (hasModule)
                {
                    items.Add(new MenuItem { Label = "Select connected section", Icon = IconId.Link, Action = () =>
                    {
                        Viewport.SetTool(StudioTool.Select);
                        Viewport.SelectedRoot = trackCell.Module.RootCell.Position;
                        Viewport.SelectConnected();
                    } });
                }
                items.Add(MenuItem.Separator());
            }

            items.Add(MenuItem.Command(Commands.Get("place.rotateCW")));
            items.Add(MenuItem.Command(Commands.Get("place.fit")));
            items.Add(MenuItem.Command(Commands.Get("view.frame")));
            items.Add(MenuItem.Command(Commands.Get("view.top")));
            OpenMenu(items, guiPosition);
        }

        /// <summary>Camera presets and viewport display options (the chevron in the viewport's view bar).</summary>
        public void OpenViewMenu(Vector2 topRight)
        {
            var items = new List<MenuItem>
            {
                MenuItem.Command(Commands.Get("view.perspective")),
                MenuItem.Command(Commands.Get("view.top")),
                MenuItem.Command(Commands.Get("view.front")),
                MenuItem.Command(Commands.Get("view.side")),
                MenuItem.Command(Commands.Get("view.ortho")),
                MenuItem.Separator(),
                MenuItem.Command(Commands.Get("view.frame")),
                MenuItem.Command(Commands.Get("view.frameAll")),
                MenuItem.Command(Commands.Get("view.reset")),
                MenuItem.Separator(),
                MenuItem.Command(Commands.Get("view.connections")),
                MenuItem.Command(Commands.Get("view.minimap")),
                MenuItem.Command(Commands.Get("view.grid")),
                MenuItem.Command(Commands.Get("view.pickups")),
                MenuItem.Command(Commands.Get("view.zoomToPointer")),
            };
            float width = Theme.S(250f);
            OpenMenu(items, new Vector2(topRight.x - width, topRight.y), -1, width);
        }

        public void OpenRaceContextMenu(Vector2 guiPosition, Vector3 groundPoint)
        {
            var items = new List<MenuItem>();
            var data = Race.Data;
            if (data == null)
            {
                items.Add(new MenuItem { Label = "Generate race data", Icon = IconId.Lightning, Action = Race.Generate });
            }
            else
            {
                string singular = RaceLayers.Singular(Race.Layer);
                items.Add(new MenuItem { Label = $"Add {singular} here", Icon = IconId.Plus, Action = () => Race.AddElement(groundPoint) });
                items.Add(new MenuItem { Label = $"Delete {singular}", Icon = IconId.Trash, Enabled = Race.Selected >= 0, Shortcut = "Del", Action = Race.DeleteSelected });
                items.Add(MenuItem.Separator());
                var layers = new List<MenuItem>();
                foreach (var layer in RaceLayers.All)
                {
                    var l = layer;
                    layers.Add(new MenuItem { Label = RaceLayers.Name(layer), IsCheckable = true, Checked = Race.Layer == layer, Action = () => Race.SetLayer(l) });
                }
                items.Add(MenuItem.Sub("Edit layer", layers));
            }
            items.Add(MenuItem.Separator());
            items.Add(MenuItem.Command(Commands.Get("view.frame")));
            items.Add(MenuItem.Command(Commands.Get("view.race"), "Back to track editing"));
            OpenMenu(items, guiPosition);
        }

        public void SetRaceMode(bool active)
        {
            if (Race.Active == active)
                return;
            Viewport.CancelInteraction();
            Viewport.EndPaste();
            Race.SetActive(active);
            if (active)
                Viewport.SetTool(StudioTool.Place);
            inspector.SetTab(InspectorTab.Place);
            TrackEditor.PlaySound(TrackEditor.SndMenuOpen);
        }

        // Toasts
        public void Toast(string text, IconId icon = IconId.Info)
        {
            toasts.Add(new ToastMessage { Text = text, Icon = icon, Time = Time.unscaledTime });
            if (toasts.Count > 3)
                toasts.RemoveAt(0);
        }

        private void DrawToasts()
        {
            if (Event.current.type != EventType.Repaint || toasts.Count == 0)
                return;

            var T = Theme;
            float y = viewportRect.yMax - T.S(16f);
            for (int i = toasts.Count - 1; i >= 0; i--)
            {
                var toast = toasts[i];
                float age = Time.unscaledTime - toast.Time;
                float alpha = Mathf.Clamp01(age / 0.15f) * Mathf.Clamp01((3.2f - age) / 0.4f);
                float rise = (1f - Mathf.Clamp01(age / 0.15f)) * T.S(8f);

                float width = SGUI.TextWidth(toast.Text, T.Body) + T.S(50f);
                var rect = new Rect(viewportRect.xMax - width - T.S(16f), y - T.S(38f) + rise, width, T.S(38f)).Snap();
                y -= T.S(46f);

                var old = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, alpha);
                SGUI.Shadow(rect, T.S(8f));
                SGUI.RoundedOutline(rect, Palette.Overlay, Palette.BorderStrong, T.S(7f));
                Color iconColor = toast.Icon == IconId.Warning ? Palette.Warning : toast.Icon == IconId.Error ? Palette.Error
                                : toast.Icon == IconId.Success || toast.Icon == IconId.Check ? Palette.Success : Palette.Accent;
                SGUI.Icon(new Rect(rect.x + T.S(12f), rect.y, T.S(18f), rect.height), toast.Icon, iconColor, T.S(16f));
                SGUI.Label(new Rect(rect.x + T.S(38f), rect.y, rect.width - T.S(48f), rect.height), toast.Text, T.Body, Palette.Text);
                GUI.color = old;
            }
        }

        // Misc helpers
        /// <summary>Runs an action at the start of the next Update (outside of IMGUI event processing).</summary>
        public void Defer(Action action) => deferred.Enqueue(action);

        public static void RevealFolder(string path)
        {
            if (string.IsNullOrEmpty(path))
                return;
            try
            {
                System.IO.Directory.CreateDirectory(path);
                Application.OpenURL("file:///" + path.Replace('\\', '/'));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Could not open folder '{path}': {ex.Message}");
            }
        }

        public static void DrawAppMark(StudioTheme T, Rect r)
        {
            SGUI.Rounded(r, Palette.Accent, r.width * 0.24f);
            SGUI.Icon(r, IconId.Track, Palette.TextOnAccent, Mathf.Round(r.width * 0.58f));
        }

        public static (IconId icon, Color color) ProblemStyle(TrackProblem problem)
        {
            switch (problem.Severity)
            {
                case TrackProblemSeverity.Error: return (IconId.Error, Palette.Error);
                case TrackProblemSeverity.Warning: return (IconId.Warning, Palette.Warning);
                default: return (IconId.Info, Palette.Info);
            }
        }
    }
}

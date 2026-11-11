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
using UnityEngine;

namespace Studio
{
    /// <summary>
    /// Base class for modal dialogs. The shell draws the backdrop, frame and title;
    /// subclasses draw the body and footer buttons.
    /// </summary>
    public abstract class StudioDialog
    {
        public StudioShell Shell { get; internal set; }
        protected StudioTheme T => Shell.Theme;

        public string Title = string.Empty;
        public IconId? TitleIcon;
        public Color TitleIconColor = Palette.TextDim;

        /// <summary>Whether Esc / the close button / clicking the backdrop may dismiss the dialog.</summary>
        public bool Dismissible = true;

        /// <summary>Frames the dialog has been open; use 0 to set initial keyboard focus.</summary>
        protected int FrameCount { get; private set; }

        public virtual float Width => T.S(460f);
        public virtual bool HasFooter => true;
        public float FooterHeight => T.S(60f);

        /// <summary>Height of the body for the given width.</summary>
        public abstract float BodyHeight(float width);
        public abstract void DrawBody(Rect rect);
        public virtual void DrawFooter(Rect rect) { }

        /// <summary>Enter key. Return true if handled.</summary>
        public virtual bool Submit() => false;

        /// <summary>Esc key, the close button, or a backdrop click.</summary>
        public virtual void Cancel() => Close();

        public virtual void OnOpened() { }
        public virtual void OnClosed() { }
        public virtual void Update() { }

        public void Close() => Shell.CloseDialog(this);

        internal void AdvanceFrame()
        {
            if (Event.current.type == EventType.Repaint)
                FrameCount++;
        }

        // footer helpers: buttons laid out right to left
        protected Rect FooterButton(ref Rect footer, string label, float minWidth = 0f)
        {
            float width = Mathf.Max(minWidth > 0f ? minWidth : T.S(92f), SGUI.TextWidth(label, T.BodyStrong) + T.S(32f));
            var r = new Rect(footer.xMax - width, footer.y + (footer.height - T.ControlHeight) * 0.5f, width, T.ControlHeight).Snap();
            footer.width -= width + T.S(8f);
            return r;
        }
    }

    public enum MessageKind
    {
        Info,
        Success,
        Warning,
        Error
    }

    public sealed class MessageDialog : StudioDialog
    {
        private readonly string message;
        private readonly Action onClose;

        public MessageDialog(string title, string message, MessageKind kind, Action onClose = null)
        {
            Title = title;
            this.message = message;
            this.onClose = onClose;

            switch (kind)
            {
                case MessageKind.Error:
                    TitleIcon = IconId.Error;
                    TitleIconColor = Palette.Error;
                    break;
                case MessageKind.Warning:
                    TitleIcon = IconId.Warning;
                    TitleIconColor = Palette.Warning;
                    break;
                case MessageKind.Success:
                    TitleIcon = IconId.Success;
                    TitleIconColor = Palette.Success;
                    break;
                default:
                    TitleIcon = IconId.Info;
                    TitleIconColor = Palette.Info;
                    break;
            }
        }

        public override float BodyHeight(float width) => SGUI.WrappedHeight(message, T.Wrapped, width) + T.S(4f);

        public override void DrawBody(Rect rect)
        {
            SGUI.Label(rect, message, T.Wrapped, Palette.TextDim);
        }

        public override void DrawFooter(Rect rect)
        {
            if (SGUI.Button(FooterButton(ref rect, "OK"), "OK", ButtonKind.Primary))
                Submit();
        }

        public override bool Submit()
        {
            Close();
            return true;
        }

        public override void OnClosed() => onClose?.Invoke();
    }

    /// <summary>A question with up to three answers, e.g. Save / Don't Save / Cancel.</summary>
    public sealed class ConfirmDialog : StudioDialog
    {
        public struct Choice
        {
            public string Label;
            public ButtonKind Kind;
            public Action Action;

            public Choice(string label, ButtonKind kind, Action action)
            {
                Label = label;
                Kind = kind;
                Action = action;
            }
        }

        private readonly string message;
        private readonly List<Choice> choices;
        private readonly Action onCancel;
        private readonly int defaultChoice;

        /// <param name="choices">Right to left: the first is the primary (rightmost) button.</param>
        public ConfirmDialog(string title, string message, Action onCancel, int defaultChoice, params Choice[] choices)
        {
            Title = title;
            TitleIcon = IconId.Help;
            TitleIconColor = Palette.Info;
            this.message = message;
            this.onCancel = onCancel;
            this.defaultChoice = defaultChoice;
            this.choices = new List<Choice>(choices);
        }

        public override float BodyHeight(float width) => SGUI.WrappedHeight(message, T.Wrapped, width) + T.S(4f);

        public override void DrawBody(Rect rect)
        {
            SGUI.Label(rect, message, T.Wrapped, Palette.TextDim);
        }

        public override void DrawFooter(Rect rect)
        {
            for (int i = 0; i < choices.Count; i++)
            {
                var choice = choices[i];
                if (SGUI.Button(FooterButton(ref rect, choice.Label), choice.Label, choice.Kind))
                {
                    Choose(i);
                    return;
                }
            }
        }

        private void Choose(int index)
        {
            Close();
            choices[index].Action?.Invoke();
        }

        public override bool Submit()
        {
            if (defaultChoice >= 0 && defaultChoice < choices.Count)
            {
                Choose(defaultChoice);
                return true;
            }
            return false;
        }

        public override void Cancel()
        {
            Close();
            onCancel?.Invoke();
        }
    }

    public sealed class AboutDialog : StudioDialog
    {
        private const string Text =
            "A recreation of the Re-Volt track editor, built from the ground up in Unity by Dummiesman, " +
            "with the Studio interface: mouse editing, undo history, autosave and crash recovery, track checks, " +
            "and customizable shortcuts.\n\n" +
            "Track designs (.TDF) stay fully compatible with the original editor. The classic interface is " +
            "one key away (F10).\n\n" +
            "Licensed under the GNU General Public License v3. The editor data files (trackunit.rtu, bitmaps, sounds) " +
            "come from the original Re-Volt track editor and are not covered by this license. Interface font: Inter (SIL Open Font License).";

        public AboutDialog()
        {
            Title = "About";
            TitleIcon = IconId.Info;
        }

        public override float Width => T.S(500f);

        public override float BodyHeight(float width) => T.S(70f) + SGUI.WrappedHeight(Text, T.Wrapped, width);

        public override void DrawBody(Rect rect)
        {
            var mark = new Rect(rect.x, rect.y, T.S(52f), T.S(52f));
            StudioShell.DrawAppMark(T, mark);
            SGUI.Label(new Rect(mark.xMax + T.S(14f), rect.y + T.S(4f), rect.width, T.S(24f)), "Re-Volt Track Editor", T.Title);
            SGUI.Label(new Rect(mark.xMax + T.S(14f), rect.y + T.S(28f), rect.width, T.S(20f)),
                       $"Unity Edition · version {Application.version} · Studio interface", T.Small, Palette.TextFaint);
            SGUI.Label(rect.CutTop(T.S(70f)), Text, T.Wrapped, Palette.TextDim);
        }

        public override void DrawFooter(Rect rect)
        {
            if (SGUI.Button(FooterButton(ref rect, "Close"), "Close", ButtonKind.Primary))
                Close();
        }

        public override bool Submit()
        {
            Close();
            return true;
        }
    }
}

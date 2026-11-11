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
using System.Text;
using UnityEngine.InputSystem;

namespace Studio
{
    /// <summary>
    /// A key plus modifiers, e.g. Ctrl+Shift+S. Serialized as text ("Ctrl+Shift+S") in the settings file.
    /// </summary>
    public struct KeyBinding : IEquatable<KeyBinding>
    {
        public Key Key;
        public bool Ctrl;
        public bool Shift;
        public bool Alt;

        public static readonly KeyBinding None = new KeyBinding();

        public bool IsNone => Key == Key.None;
        public bool HasModifier => Ctrl || Alt;

        public KeyBinding(Key key, bool ctrl = false, bool shift = false, bool alt = false)
        {
            Key = key;
            Ctrl = ctrl;
            Shift = shift;
            Alt = alt;
        }

        public static KeyBinding CtrlKey(Key key) => new KeyBinding(key, ctrl: true);
        public static KeyBinding CtrlShiftKey(Key key) => new KeyBinding(key, ctrl: true, shift: true);

        public bool WasPressedThisFrame()
        {
            if (IsNone || !(Keyboard.current is Keyboard keyboard))
                return false;

            var control = ResolveControl(keyboard, Key);
            if (control == null || !control.wasPressedThisFrame)
                return false;

            return Ctrl == Controls.IsControlHeld() && Shift == Controls.IsShiftHeld() && Alt == Controls.IsAltHeld();
        }

        /// <summary>
        /// Input System keys are physical positions. Letter shortcuts are matched by the character on the
        /// key instead, so Ctrl+Z means the key labelled Z on AZERTY/QWERTZ layouts too.
        /// </summary>
        public static UnityEngine.InputSystem.Controls.KeyControl ResolveControl(Keyboard keyboard, Key key)
        {
            if (key >= Key.A && key <= Key.Z)
            {
                var byLabel = keyboard.FindKeyOnCurrentKeyboardLayout(key.ToString());
                if (byLabel != null)
                    return byLabel;
            }
            return keyboard[key];
        }

        /// <summary>The logical key for a pressed control: letters by their label on the current layout.</summary>
        public static Key LogicalKey(UnityEngine.InputSystem.Controls.KeyControl control)
        {
            string label = control.displayName;
            if (!string.IsNullOrEmpty(label) && label.Length == 1 && char.IsLetter(label[0])
                && Enum.TryParse(label.ToUpperInvariant(), out Key letter) && letter >= Key.A && letter <= Key.Z)
            {
                return letter;
            }
            return control.keyCode;
        }

        public bool Equals(KeyBinding other) => Key == other.Key && Ctrl == other.Ctrl && Shift == other.Shift && Alt == other.Alt;
        public override bool Equals(object obj) => obj is KeyBinding other && Equals(other);
        public override int GetHashCode() => ((int)Key << 3) | (Ctrl ? 1 : 0) | (Shift ? 2 : 0) | (Alt ? 4 : 0);

        public override string ToString()
        {
            if (IsNone)
                return string.Empty;

            var sb = new StringBuilder();
            if (Ctrl) sb.Append("Ctrl+");
            if (Alt) sb.Append("Alt+");
            if (Shift) sb.Append("Shift+");
            sb.Append(KeyName(Key));
            return sb.ToString();
        }

        public static bool TryParse(string text, out KeyBinding binding)
        {
            binding = None;
            if (string.IsNullOrWhiteSpace(text))
                return true; // explicitly unbound

            var parts = text.Split('+');
            var result = new KeyBinding();
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].Trim();
                bool last = i == parts.Length - 1;
                if (!last)
                {
                    if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)) result.Ctrl = true;
                    else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase)) result.Shift = true;
                    else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase)) result.Alt = true;
                    else return false;
                }
                else
                {
                    if (!TryParseKey(part, out result.Key))
                        return false;
                }
            }
            binding = result;
            return true;
        }

        private static readonly Dictionary<Key, string> displayNames = new Dictionary<Key, string>
        {
            { Key.Digit0, "0" }, { Key.Digit1, "1" }, { Key.Digit2, "2" }, { Key.Digit3, "3" }, { Key.Digit4, "4" },
            { Key.Digit5, "5" }, { Key.Digit6, "6" }, { Key.Digit7, "7" }, { Key.Digit8, "8" }, { Key.Digit9, "9" },
            { Key.LeftBracket, "[" }, { Key.RightBracket, "]" }, { Key.Comma, "," }, { Key.Period, "." },
            { Key.Slash, "/" }, { Key.Backslash, "\\" }, { Key.Semicolon, ";" }, { Key.Quote, "'" },
            { Key.Minus, "-" }, { Key.Equals, "=" }, { Key.Backquote, "`" },
            { Key.PageUp, "PgUp" }, { Key.PageDown, "PgDn" }, { Key.Delete, "Del" }, { Key.Insert, "Ins" },
            { Key.Enter, "Enter" }, { Key.Escape, "Esc" }, { Key.Backspace, "Backspace" }, { Key.Space, "Space" },
            { Key.UpArrow, "Up" }, { Key.DownArrow, "Down" }, { Key.LeftArrow, "Left" }, { Key.RightArrow, "Right" },
            { Key.NumpadPlus, "Num+" }, { Key.NumpadMinus, "Num-" }, { Key.NumpadMultiply, "Num*" }, { Key.NumpadDivide, "Num/" },
        };

        public static string KeyName(Key key)
        {
            if (displayNames.TryGetValue(key, out var name))
                return name;
            return key.ToString();
        }

        private static bool TryParseKey(string text, out Key key)
        {
            foreach (var pair in displayNames)
            {
                if (pair.Value.Equals(text, StringComparison.OrdinalIgnoreCase))
                {
                    key = pair.Key;
                    return true;
                }
            }
            return Enum.TryParse(text, true, out key) && key != Key.None;
        }

        /// <summary>
        /// Keys the editing controls use on their own (shared with the classic interface and gamepad
        /// layout). They can't be bound without Ctrl or Alt.
        /// </summary>
        public static bool IsReservedEditingKey(Key key)
        {
            switch (key)
            {
                case Key.UpArrow:
                case Key.DownArrow:
                case Key.LeftArrow:
                case Key.RightArrow:
                case Key.Enter:
                case Key.NumpadEnter:
                case Key.Delete:
                case Key.Space:
                case Key.PageUp:
                case Key.PageDown:
                case Key.Z:
                case Key.X:
                case Key.T:
                case Key.C:
                case Key.LeftBracket:
                case Key.RightBracket:
                case Key.NumpadPlus:
                case Key.NumpadMinus:
                case Key.Escape:
                case Key.F10:
                case Key.F12:
                    return true;
                default:
                    return false;
            }
        }

        public static bool IsModifierKey(Key key)
        {
            return key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftShift || key == Key.RightShift
                || key == Key.LeftAlt || key == Key.RightAlt || key == Key.LeftMeta || key == Key.RightMeta
                || key == Key.ContextMenu;
        }
    }

    public sealed class StudioCommand
    {
        public string Id;
        public string Label;
        public string Category;
        public string Description;
        public IconId? Icon;
        public Action Execute;
        public Func<bool> IsEnabled;
        public Func<bool> IsChecked;

        public KeyBinding DefaultBinding;
        public KeyBinding Binding;

        /// <summary>For actions handled by the editing controls themselves (arrow keys, Enter, ...).</summary>
        public string FixedBindingText;

        public bool Rebindable => FixedBindingText == null;
        public bool Enabled => IsEnabled == null || IsEnabled();
        public bool Checked => IsChecked != null && IsChecked();

        public string BindingText => FixedBindingText ?? Binding.ToString();

        public bool Invoke()
        {
            if (!Enabled)
                return false;
            Execute?.Invoke();
            return true;
        }
    }

    /// <summary>
    /// All Studio actions. Menus, the toolbar, keyboard shortcuts, and the shortcut editor
    /// all go through this registry so labels, enabled states and bindings never disagree.
    /// </summary>
    public sealed class CommandRegistry
    {
        private readonly List<StudioCommand> commands = new List<StudioCommand>();
        private readonly Dictionary<string, StudioCommand> byId = new Dictionary<string, StudioCommand>();

        public IReadOnlyList<StudioCommand> All => commands;

        public StudioCommand Add(string id, string category, string label, Action execute, KeyBinding binding = default,
                                 IconId? icon = null, Func<bool> enabled = null, Func<bool> isChecked = null, string description = null)
        {
            var command = new StudioCommand
            {
                Id = id,
                Category = category,
                Label = label,
                Execute = execute,
                DefaultBinding = binding,
                Binding = binding,
                Icon = icon,
                IsEnabled = enabled,
                IsChecked = isChecked,
                Description = description
            };
            commands.Add(command);
            byId[id] = command;
            return command;
        }

        /// <summary>Registers an action whose key is handled by the editing controls (not rebindable).</summary>
        public StudioCommand AddFixed(string id, string category, string label, string keyText, Action execute,
                                      IconId? icon = null, Func<bool> enabled = null, string description = null)
        {
            var command = Add(id, category, label, execute, KeyBinding.None, icon, enabled, null, description);
            command.FixedBindingText = keyText;
            return command;
        }

        public StudioCommand Get(string id) => byId.TryGetValue(id, out var command) ? command : null;

        public bool Invoke(string id)
        {
            var command = Get(id);
            return command != null && command.Invoke();
        }

        public StudioCommand FindByBinding(KeyBinding binding, StudioCommand except = null)
        {
            if (binding.IsNone)
                return null;
            foreach (var command in commands)
            {
                if (command != except && command.Rebindable && command.Binding.Equals(binding))
                    return command;
            }
            return null;
        }

        /// <summary>
        /// Executes the command whose shortcut was pressed this frame, if any.
        /// </summary>
        public bool ProcessShortcuts()
        {
            foreach (var command in commands)
            {
                if (command.Rebindable && command.Binding.WasPressedThisFrame())
                {
                    command.Invoke();
                    return true;
                }
            }
            return false;
        }

        // Persistence
        public void LoadBindings()
        {
            foreach (var command in commands)
                command.Binding = command.DefaultBinding;

            foreach (var entry in AppSettings.Data.Shortcuts)
            {
                var command = Get(entry.Action);
                if (command == null || !command.Rebindable)
                    continue;
                if (KeyBinding.TryParse(entry.Binding, out var binding))
                    command.Binding = binding;
            }
        }

        public void SaveBindings()
        {
            var list = AppSettings.Data.Shortcuts;
            list.Clear();
            foreach (var command in commands)
            {
                // only store differences from the defaults so new defaults reach existing users
                if (command.Rebindable && !command.Binding.Equals(command.DefaultBinding))
                    list.Add(new ShortcutBindingData { Action = command.Id, Binding = command.Binding.ToString() });
            }
            AppSettings.Save();
        }

        public void ResetAllBindings()
        {
            foreach (var command in commands)
                command.Binding = command.DefaultBinding;
            SaveBindings();
        }
    }
}

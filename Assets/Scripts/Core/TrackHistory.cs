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

using System;
using System.Collections.Generic;

namespace ReVolt.Track
{
    /// <summary>
    /// Snapshot based undo/redo.
    ///
    /// Every undoable operation stores the complete track state from before the operation as a
    /// <see cref="TdfDocument"/>. Track states are tiny (6 bytes per cell plus pickups), and restoring
    /// whole snapshots means every kind of edit (placing, deleting, pickups, resizing, shifting,
    /// renaming) is undoable without writing an inverse for each one, so undo can never drift out
    /// of sync with the actual track data.
    /// </summary>
    public sealed class TrackHistory
    {
        public sealed class Entry
        {
            public readonly string Label;
            public readonly TdfDocument State;
            public readonly DateTime Time;

            public Entry(string label, TdfDocument state)
            {
                Label = label;
                State = state;
                Time = DateTime.Now;
            }
        }

        private readonly Func<TdfDocument> capture;
        private readonly Action<TdfDocument> apply;

        // most recent entry is last
        private readonly List<Entry> undoStack = new List<Entry>();
        private readonly List<Entry> redoStack = new List<Entry>();

        // grouping of multi-step operations (drag painting, pickup sessions)
        private int groupDepth = 0;
        private string groupLabel;
        private TdfDocument groupBefore;

        /// <summary>Maximum number of undo steps kept.</summary>
        public int MaxDepth { get; set; } = 200;

        /// <summary>True while a snapshot is being restored.</summary>
        public bool IsApplying { get; private set; }

        /// <summary>True while a grouped change is open.</summary>
        public bool IsGrouping => groupDepth > 0;

        public bool CanUndo => undoStack.Count > 0 && !IsGrouping;
        public bool CanRedo => redoStack.Count > 0 && !IsGrouping;

        public string UndoLabel => undoStack.Count > 0 ? undoStack[undoStack.Count - 1].Label : null;
        public string RedoLabel => redoStack.Count > 0 ? redoStack[redoStack.Count - 1].Label : null;

        /// <summary>Undo entries, oldest first.</summary>
        public IReadOnlyList<Entry> UndoEntries => undoStack;

        /// <summary>Redo entries, the next one to redo is last.</summary>
        public IReadOnlyList<Entry> RedoEntries => redoStack;

        /// <summary>Raised after any change to the history or after a snapshot was restored.</summary>
        public event Action Changed;

        public TrackHistory(Func<TdfDocument> capture, Action<TdfDocument> apply)
        {
            this.capture = capture ?? throw new ArgumentNullException(nameof(capture));
            this.apply = apply ?? throw new ArgumentNullException(nameof(apply));
        }

        /// <summary>
        /// Runs <paramref name="change"/> as a single undoable step.
        /// Nothing is recorded if the change didn't alter the track.
        /// </summary>
        /// <returns>True if the track changed.</returns>
        public bool Perform(string label, Action change)
        {
            if (IsApplying || IsGrouping)
            {
                // nested inside a restore or a group: the outer operation records the state
                change();
                return true;
            }

            var before = capture();
            change();
            var after = capture();

            if (before.ContentEquals(after))
                return false;

            Push(label, before);
            return true;
        }

        /// <summary>
        /// Starts a grouped change. Every modification until the matching <see cref="EndGroup"/>
        /// becomes a single undo step. Groups can nest; only the outermost one records.
        /// </summary>
        public void BeginGroup(string label)
        {
            if (IsApplying)
                return;

            if (groupDepth == 0)
            {
                groupBefore = capture();
                groupLabel = label;
            }
            groupDepth++;
        }

        /// <summary>
        /// Closes a grouped change. Returns true if the outermost group changed the track.
        /// </summary>
        public bool EndGroup()
        {
            if (IsApplying || groupDepth == 0)
                return false;

            groupDepth--;
            if (groupDepth > 0)
                return false;

            var before = groupBefore;
            groupBefore = null;

            if (before.ContentEquals(capture()))
                return false;

            Push(groupLabel, before);
            return true;
        }

        /// <summary>
        /// Abandons an open group, restoring the state from when it began.
        /// </summary>
        public void CancelGroup()
        {
            if (groupDepth == 0)
                return;

            var before = groupBefore;
            groupDepth = 0;
            groupBefore = null;
            Restore(before);
            Changed?.Invoke();
        }

        public bool Undo()
        {
            if (!CanUndo)
                return false;

            var entry = Pop(undoStack);
            redoStack.Add(new Entry(entry.Label, capture()));
            Restore(entry.State);
            Changed?.Invoke();
            return true;
        }

        public bool Redo()
        {
            if (!CanRedo)
                return false;

            var entry = Pop(redoStack);
            undoStack.Add(new Entry(entry.Label, capture()));
            Restore(entry.State);
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// Moves through history so that exactly <paramref name="undoCount"/> undo entries remain.
        /// Used by the history panel to jump to any point with a single restore.
        /// </summary>
        public void JumpTo(int undoCount)
        {
            if (IsGrouping)
                return;

            undoCount = Math.Max(0, Math.Min(undoCount, undoStack.Count + redoStack.Count));
            if (undoCount == undoStack.Count)
                return;

            // move entries between stacks without restoring each intermediate state
            var current = capture();
            TdfDocument target;

            if (undoCount < undoStack.Count)
            {
                // undo: each moved entry's "after" state is the "before" state of the entry above it
                TdfDocument after = current;
                while (undoStack.Count > undoCount)
                {
                    var entry = Pop(undoStack);
                    redoStack.Add(new Entry(entry.Label, after));
                    after = entry.State;
                }
                target = after;
            }
            else
            {
                TdfDocument before = current;
                while (undoStack.Count < undoCount)
                {
                    var entry = Pop(redoStack);
                    undoStack.Add(new Entry(entry.Label, before));
                    before = entry.State;
                }
                target = before;
            }

            Restore(target);
            Changed?.Invoke();
        }

        public void Clear()
        {
            undoStack.Clear();
            redoStack.Clear();
            groupDepth = 0;
            groupBefore = null;
            Changed?.Invoke();
        }

        private void Push(string label, TdfDocument before)
        {
            undoStack.Add(new Entry(label, before));
            redoStack.Clear();

            int max = Math.Max(1, MaxDepth);
            if (undoStack.Count > max)
                undoStack.RemoveRange(0, undoStack.Count - max);

            Changed?.Invoke();
        }

        private void Restore(TdfDocument state)
        {
            IsApplying = true;
            try
            {
                apply(state.Clone());
            }
            finally
            {
                IsApplying = false;
            }
        }

        private static Entry Pop(List<Entry> stack)
        {
            var entry = stack[stack.Count - 1];
            stack.RemoveAt(stack.Count - 1);
            return entry;
        }
    }
}

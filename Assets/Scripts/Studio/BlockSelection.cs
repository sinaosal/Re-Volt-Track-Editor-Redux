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

using System.Collections.Generic;
using UnityEngine;

namespace Studio
{
    /// <summary>
    /// The selected blocks, stored as root cells. The track stays the only source of truth: entries
    /// that no longer point at a block root (after undo, deleting, loading) are dropped when read.
    /// </summary>
    public sealed class BlockSelection
    {
        private readonly List<Vector2Int> roots = new List<Vector2Int>();
        private readonly HashSet<Vector2Int> lookup = new HashSet<Vector2Int>();

        /// <summary>Increases on every change, so views can tell when to refresh.</summary>
        public int Version { get; private set; }

        public int Count
        {
            get
            {
                Prune();
                return roots.Count;
            }
        }

        public bool IsEmpty => Count == 0;

        /// <summary>The block clicked last; the inspector shows its details.</summary>
        public Vector2Int? Primary
        {
            get
            {
                Prune();
                return roots.Count > 0 ? roots[roots.Count - 1] : (Vector2Int?)null;
            }
        }

        public ModulePlacement PrimaryPlacement => Primary.HasValue ? RootPlacement(Primary.Value) : null;

        public IReadOnlyList<Vector2Int> Roots
        {
            get
            {
                Prune();
                return roots;
            }
        }

        public List<ModulePlacement> Placements
        {
            get
            {
                Prune();
                var list = new List<ModulePlacement>(roots.Count);
                foreach (var root in roots)
                    list.Add(RootPlacement(root));
                return list;
            }
        }

        public bool Contains(Vector2Int root) => lookup.Contains(root) && RootPlacement(root) != null;

        public bool Contains(ModulePlacement placement) => placement != null && placement.RootCell != null && Contains(placement.RootCell.Position);

        public void Clear()
        {
            if (roots.Count == 0)
                return;
            roots.Clear();
            lookup.Clear();
            Version++;
        }

        public void Set(Vector2Int root)
        {
            roots.Clear();
            lookup.Clear();
            Add(root);
            Version++;
        }

        public void SetMany(IEnumerable<Vector2Int> newRoots)
        {
            roots.Clear();
            lookup.Clear();
            foreach (var root in newRoots)
                Add(root);
            Version++;
        }

        public void Add(Vector2Int root)
        {
            if (RootPlacement(root) == null)
                return;
            if (lookup.Add(root))
            {
                roots.Add(root);
            }
            else
            {
                // re-adding makes it the primary
                roots.Remove(root);
                roots.Add(root);
            }
            Version++;
        }

        public void Remove(Vector2Int root)
        {
            if (!lookup.Remove(root))
                return;
            roots.Remove(root);
            Version++;
        }

        public void Toggle(Vector2Int root)
        {
            if (lookup.Contains(root))
                Remove(root);
            else
                Add(root);
        }

        /// <summary>Drops entries that no longer point at the root of a block.</summary>
        public void Prune()
        {
            for (int i = roots.Count - 1; i >= 0; i--)
            {
                if (RootPlacement(roots[i]) == null)
                {
                    lookup.Remove(roots[i]);
                    roots.RemoveAt(i);
                    Version++;
                }
            }
        }

        public static ModulePlacement RootPlacement(Vector2Int root)
        {
            var track = TrackEditor.Track;
            if (track == null || root.x < 0 || root.y < 0 || root.x >= track.Width || root.y >= track.Height)
                return null;
            var module = track.GetCell(root).Module;
            return module != null && module.RootCell != null && module.RootCell.Position == root ? module : null;
        }
    }

    /// <summary>
    /// Editing-session state of blocks: locked (protected from Studio edits and keyboard placement) and
    /// hidden (not drawn in the viewport, still saved and exported). Not stored in the track file.
    /// </summary>
    public sealed class BlockStates
    {
        private readonly HashSet<Vector2Int> locked = new HashSet<Vector2Int>();
        private readonly HashSet<Vector2Int> hidden = new HashSet<Vector2Int>();

        public int LockedCount => locked.Count;
        public int HiddenCount => hidden.Count;

        public bool IsLocked(ModulePlacement p) => p != null && p.RootCell != null && locked.Contains(p.RootCell.Position);
        public bool IsHidden(ModulePlacement p) => p != null && p.RootCell != null && hidden.Contains(p.RootCell.Position);

        public void SetLocked(ModulePlacement p, bool value) => Set(locked, p, value);
        public void SetHidden(ModulePlacement p, bool value) => Set(hidden, p, value);

        private static void Set(HashSet<Vector2Int> set, ModulePlacement p, bool value)
        {
            if (p == null || p.RootCell == null)
                return;
            if (value)
                set.Add(p.RootCell.Position);
            else
                set.Remove(p.RootCell.Position);
        }

        public void ShowAll() => hidden.Clear();
        public void UnlockAll() => locked.Clear();

        public void Clear()
        {
            locked.Clear();
            hidden.Clear();
        }

        /// <summary>Keeps the state with blocks that moved (old root to new root).</summary>
        public void Remap(IList<(Vector2Int From, Vector2Int To)> moves)
        {
            Remap(locked, moves);
            Remap(hidden, moves);
        }

        private static void Remap(HashSet<Vector2Int> set, IList<(Vector2Int From, Vector2Int To)> moves)
        {
            var carried = new List<Vector2Int>();
            foreach (var (from, to) in moves)
            {
                if (set.Remove(from))
                    carried.Add(to);
            }
            foreach (var to in carried)
                set.Add(to);
        }

        /// <summary>Drops entries whose block is gone.</summary>
        public void Prune()
        {
            locked.RemoveWhere(r => BlockSelection.RootPlacement(r) == null);
            hidden.RemoveWhere(r => BlockSelection.RootPlacement(r) == null);
        }
    }
}

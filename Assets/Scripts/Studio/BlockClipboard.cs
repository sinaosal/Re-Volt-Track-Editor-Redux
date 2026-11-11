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
using ReVolt.TrackUnit.Unity;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Studio
{
    /// <summary>
    /// Copied blocks, stored relative to the middle of the group so they can be pasted anywhere.
    /// Heights stay as they were copied. Kept for the whole session, also across opened tracks.
    /// </summary>
    public sealed class BlockClipboard
    {
        private readonly List<BlockSpec> items = new List<BlockSpec>();

        public bool IsEmpty => items.Count == 0;
        public int Count => items.Count;

        public static List<BlockSpec> Normalize(IList<ModulePlacement> blocks, out Vector2Int anchor)
        {
            var cells = blocks.SelectMany(p => p.TouchedCells).Select(c => (c.Position.x, c.Position.y)).ToList();
            var pivot = BlockGroup.Pivot(cells);
            anchor = new Vector2Int(pivot.X, pivot.Y);
            return blocks.Select(p => BlockGroup.Translate(BlockOperations.ToSpec(p), -pivot.X, -pivot.Y, 0)).ToList();
        }

        public void Copy(IList<ModulePlacement> blocks)
        {
            if (blocks.Count == 0)
                return;
            items.Clear();
            items.AddRange(Normalize(blocks, out _));
        }

        public List<BlockSpec> Items => items.Select(i => i.Clone()).ToList();
    }

    /// <summary>
    /// Blocks following the pointer until they're placed (paste and duplicate). The copies are shown as
    /// real module meshes; the viewport colours their cells by whether they fit.
    /// </summary>
    public sealed class PasteSession
    {
        private readonly List<BlockSpec> items;     // relative to the anchor
        private readonly List<GameObject> ghosts = new List<GameObject>();

        public string Verb { get; }
        public Vector2Int Anchor;
        public int Turns { get; private set; }
        public int LevelOffset { get; private set; }
        public int Count => items.Count;
        public int Placements { get; set; }

        private int ghostTurns = int.MinValue, ghostLevel = int.MinValue;
        private Vector2Int ghostAnchor = new Vector2Int(int.MinValue, int.MinValue);

        public PasteSession(List<BlockSpec> relativeItems, Vector2Int anchor, string verb)
        {
            items = relativeItems;
            Anchor = anchor;
            Verb = verb;

            var editing = TrackEditor.GetEditMode<TrackEditingMode>();
            foreach (var item in items)
            {
                var ghost = editing.CreateModuleGhost(item.ModuleIndex);
                ghosts.Add(ghost);
            }
        }

        public void Rotate(int turns)
        {
            Turns = (((Turns + turns) % 4) + 4) % 4;
        }

        public bool ChangeLevel(int delta, int maxLevel)
        {
            int min = items.Min(i => i.Level) + LevelOffset + delta;
            int max = items.Max(i => i.Level) + LevelOffset + delta;
            if (min < 0 || max > maxLevel)
                return false;
            LevelOffset += delta;
            return true;
        }

        /// <summary>The blocks as they would be placed now.</summary>
        public List<BlockSpec> Resolve()
        {
            var result = new List<BlockSpec>(items.Count);
            foreach (var item in items)
            {
                var turned = BlockGroup.Rotate(item, (0, 0), Turns);
                result.Add(BlockGroup.Translate(turned, Anchor.x, Anchor.y, LevelOffset));
            }
            return result;
        }

        /// <summary>Positions the ghost meshes. Cheap when nothing changed.</summary>
        public void UpdateGhosts()
        {
            if (ghostAnchor == Anchor && ghostTurns == Turns && ghostLevel == LevelOffset)
                return;
            ghostAnchor = Anchor;
            ghostTurns = Turns;
            ghostLevel = LevelOffset;

            var placed = Resolve();
            for (int i = 0; i < ghosts.Count; i++)
            {
                var ghost = ghosts[i];
                if (ghost == null)
                    continue;
                var spec = placed[i];
                float elevation = spec.Level * EditorConstants.ElevationStep;
                var instance = ghost.GetComponent<ModuleInstance>();
                if (instance != null)
                    instance.SetRootHeight(elevation);
                ghost.transform.position = new Vector3(spec.X, elevation, spec.Y);
                ghost.transform.rotation = Quaternion.Euler(0f, 90f * spec.Rotation, 0f);
                ghost.SetActive(true);
            }
        }

        public void SetGhostsVisible(bool visible)
        {
            foreach (var ghost in ghosts)
            {
                if (ghost != null && ghost.activeSelf != visible)
                    ghost.SetActive(visible);
            }
            if (!visible)
                ghostTurns = int.MinValue; // re-position when shown again
        }

        public void Dispose()
        {
            foreach (var ghost in ghosts)
            {
                if (ghost != null)
                {
                    ghost.SetActive(false);
                    UnityEngine.Object.Destroy(ghost);
                }
            }
            ghosts.Clear();
        }
    }
}

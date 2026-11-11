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
using System.IO;
using System.Linq;
using ReVolt.Track;
using UnityEngine;

public class EditorTrack : IBinSerializable, ISaveLoad
{
    public string Name = string.Empty;
    public int Width { get; private set; }
    public int Height { get; private set; }
    public bool IsDirty => isDirty;
    public EditorTrackCell[] Cells
    {
        get => cells;
        private set
        {
            cells = value;
        }
    }

    // 
    private EditorTrackCell[] cells = null;
    private bool isDirty = false;

    /// <summary>
    /// Sidecar data that isn't tied to a single block (unknown sections from newer versions, and
    /// later race data). Per-block support styles live on the placements themselves.
    /// </summary>
    public TrackExtras Extras = new TrackExtras();

    // events
    public Action AfterLoadCallback;
    public Action AfterSaveCallback;

    // helpers
    private EditorTrackCell[] CreateCellArray(int width, int height)
    {
        var array = new EditorTrackCell[width * height];
        for (int i = 0; i < array.Length; i++)
        {
            int x = i % width;
            int y = i / width;
            array[i] = new EditorTrackCell(new Vector2Int(x, y));
        }
        return array;
    }

    // public interface
    public void MarkDirty()
    {
        isDirty = true;
    }

    public void ClearDirtyFlag()
    {
        isDirty = false;
    }

    public EditorTrackCell GetCell(Vector2Int cell)
    {
        return GetCell(cell.x, cell.y);
    }

    public EditorTrackCell GetCell(int x, int y)
    {
        if (x >= Width || y >= Height || x < 0 || y < 0)
            throw new ArgumentException($"Requested cell outside grid! ({x}/{Width}, {y}/{Height})", "cell");
        return cells[(y * Width) + x];
    }

    public void SetSize(int width, int height)
    {
        // don't resize if this is already our size
        if (Width == width && Height == height)
            return;
        AdjustTrack(new Vector2Int(width, height), Vector2Int.zero);
    }

    public void SetSize(Vector2Int size)
    {
        SetSize(size.x, size.y);
    }

    public void AdjustTrack(Vector2Int newSize, Vector2Int shift)
    {
        //keep copy of old data
        int oldWidth = this.Width;
        int oldHeight = this.Height;
        var oldCells = cells;

        //
        this.Width = newSize.x;
        this.Height = newSize.y;

        //init new cells
        cells = CreateCellArray(newSize.x, newSize.y);

        //copy over old cell data, but shifted
        if (oldCells == null)
            return;

        for (int y = 0; y < oldHeight; y++)
        {
            for (int x = 0; x < oldWidth; x++)
            {
                var originalCell = oldCells[(y * oldWidth) + x];
                var shiftedCellPos = new Vector2Int(x + shift.x, y + shift.y);

                if(shiftedCellPos.x >= 0 && shiftedCellPos.y >= 0 && shiftedCellPos.x < Width && shiftedCellPos.y < Height)
                {
                    //copy over pickup
                    var shiftedCell = GetCell(shiftedCellPos);
                    shiftedCell.HasPickup = originalCell.HasPickup;

                    //copy over module
                    if (originalCell.Module != null && originalCell.Module.RootCell == originalCell)
                    {
                        //create shifted data
                        shiftedCell.Module = new ModulePlacement()
                        {
                            RootCell = shiftedCell,
                            Position = shiftedCell.Position,
                            Elevation = originalCell.Module.Elevation,
                            ModuleIndex = originalCell.Module.ModuleIndex,
                            Object = originalCell.Module.Object,
                            Rotation = originalCell.Module.Rotation,
                            Support = originalCell.Module.Support
                        };

                        foreach (var touchedCell in originalCell.Module.TouchedCells)
                        {
                            var newTouchedCell = GetCell(touchedCell.Position + shift);
                            newTouchedCell.Module = shiftedCell.Module;
                            shiftedCell.Module.TouchedCells.Add(newTouchedCell);
                        }

                        //shift the actual object
                        shiftedCell.Module.PositionObject();
                    }
                }
            }
        }
    }

    /// <summary>
    /// Get all ModulePlacement's where the modules origin resides
    /// </summary>
    public IEnumerable<ModulePlacement> GetAllModuleRootPlacements()
    {
        return cells.Where(x => x.Module != null && x.Module.RootCell == x).Select(x => x.Module);
    }

    public void Clear()
    {
        foreach(var cell in cells)
        {
            if (cell.Module != null && cell.Module.RootCell == cell)
                cell.Module.Delete();
        }

        // pickups loaded onto empty cells aren't removed by Module.Delete()
        foreach (var cell in cells)
            cell.HasPickup = false;
    }

    public EditorTrack Clone(bool keepObjectReferences)
    {
        var track = new EditorTrack(Width, Height)
        {
            Name = this.Name
        };

        foreach(var rootModulePlacement in this.GetAllModuleRootPlacements())
        {
            var newRootCell = track.GetCell(rootModulePlacement.Position);
            var newPlacement = new ModulePlacement()
            {
                Elevation = rootModulePlacement.Elevation,
                ModuleIndex = rootModulePlacement.ModuleIndex,
                Object = (keepObjectReferences) ? rootModulePlacement.Object : null,
                Position = rootModulePlacement.Position,
                RootCell = newRootCell,
                Rotation = rootModulePlacement.Rotation,
                Support = rootModulePlacement.Support
            };
            newRootCell.Module = newPlacement;

            foreach(var touchedCell in rootModulePlacement.TouchedCells)
            {
                var newTouchedCell = track.GetCell(touchedCell.Position);
                newTouchedCell.HasPickup = touchedCell.HasPickup;
                newTouchedCell.Module = newPlacement;
            }
        }
        return track;
    }

    // Document conversion
    /// <summary>
    /// Captures the current track (size, name, module root placements and pickups) as a
    /// Unity-independent document. Used for saving, autosave, and undo/redo snapshots.
    /// </summary>
    public TdfDocument ToDocument()
    {
        var doc = new TdfDocument(Math.Max(1, Width), Math.Max(1, Height))
        {
            Name = Name ?? string.Empty
        };

        doc.Extras = (Extras ?? new TrackExtras()).Clone();
        doc.Extras.Supports.Clear();

        for (int i = 0; i < cells.Length; i++)
        {
            var cell = cells[i];
            if (cell.Module != null && cell.Module.RootCell == cell)
            {
                int steps = Mathf.RoundToInt(cell.Module.Elevation / EditorConstants.ElevationStep);
                doc.Cells[i] = new TdfCell((ushort)cell.Module.ModuleIndex,
                                           (ushort)(((cell.Module.Rotation % 4) + 4) % 4),
                                           (ushort)Mathf.Clamp(steps, 0, ushort.MaxValue));
                if (cell.Module.Support.HasValue)
                    doc.Extras.Supports[(cell.Position.x, cell.Position.y)] = cell.Module.Support.Value;
            }
            doc.Pickups[i] = cell.HasPickup;
        }

        return doc;
    }

    public void SetDirty(bool dirty)
    {
        isDirty = dirty;
    }

    // File IO
    /// <summary>
    /// Saves the track atomically; an existing file is only replaced once the new data is fully written.
    /// </summary>
    public void Save(string filepath)
    {
        TrackFiles.Save(ToDocument(), filepath);
        AfterSaveCallback?.Invoke();
    }

    /// <summary>
    /// Loads track data only (no scene objects are created). Prefer <see cref="TrackEditor.LoadTrackFile"/>,
    /// which validates the file against the track unit and builds the scene.
    /// </summary>
    public void Load(string filepath)
    {
        ApplyDocumentData(TrackFiles.Load(filepath));
    }

    public void WriteBinary(BinaryWriter writer)
    {
        writer.Write(ToDocument().ToBytes());
        AfterSaveCallback?.Invoke();
    }

    public void ReadBinaryHeader(BinaryReader reader)
    {
        int magic = reader.ReadInt32();
        if (magic != TdfDocument.Magic)
            throw new InvalidDataException("Wrong TDF magic.");

        int version = reader.ReadUInt16();
        if (version != TdfDocument.Version2 && version != TdfDocument.Version1)
            throw new InvalidDataException("Wrong TDF version.");

        Name = TdfDocument.ReadPaddedString(reader);
    }

    public void ReadBinary(BinaryReader reader)
    {
        ApplyDocumentData(TdfDocument.Read(reader.BaseStream));
    }

    /// <summary>
    /// Replaces the track data with the document contents without creating scene objects,
    /// then raises <see cref="AfterLoadCallback"/> so the owner can build them.
    /// </summary>
    private void ApplyDocumentData(TdfDocument doc)
    {
        Name = doc.Name;
        Extras = (doc.Extras ?? new TrackExtras()).Clone();
        cells = null; // discard everything, including stray pickups on empty cells
        Width = 0;
        Height = 0;
        AdjustTrack(new Vector2Int(doc.Width, doc.Height), Vector2Int.zero);

        for (int i = 0; i < doc.Cells.Length; i++)
        {
            var cell = cells[i];
            cell.HasPickup = doc.Pickups[i];

            var data = doc.Cells[i];
            if (!data.HasModule)
                continue;

            cell.Module = new ModulePlacement()
            {
                RootCell = cell,
                Elevation = data.ElevationSteps * EditorConstants.ElevationStep,
                Object = null,
                ModuleIndex = data.ModuleId,
                Position = cell.Position,
                Rotation = data.Rotation,
                Support = doc.Extras != null && doc.Extras.Supports.TryGetValue((cell.Position.x, cell.Position.y), out var style) ? style : (SupportStyle?)null
            };
        }

        AfterLoadCallback?.Invoke();
    }

    public EditorTrack(int width, int height)
    {
        SetSize(width, height);
    }

    public EditorTrack()
    {
        cells = new EditorTrackCell[0];
        Width = 0;
        Height = 0;
    }
}

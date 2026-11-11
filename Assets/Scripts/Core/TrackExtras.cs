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
using System.Globalization;
using System.Linq;

namespace ReVolt.Track
{
    /// <summary>
    /// Custom colors for the support pillars under a raised block, as 0xRRGGBB.
    /// </summary>
    public struct SupportStyle : IEquatable<SupportStyle>
    {
        public uint Top;
        public uint Bottom;

        public SupportStyle(uint top, uint bottom)
        {
            Top = top & 0xFFFFFF;
            Bottom = bottom & 0xFFFFFF;
        }

        public bool Equals(SupportStyle other) => Top == other.Top && Bottom == other.Bottom;
        public override bool Equals(object obj) => obj is SupportStyle other && Equals(other);
        public override int GetHashCode() => (int)(Top * 397 ^ Bottom);

        public static string ToHex(uint rgb) => "#" + (rgb & 0xFFFFFF).ToString("X6", CultureInfo.InvariantCulture);

        public static bool TryParseHex(string text, out uint rgb)
        {
            rgb = 0;
            if (string.IsNullOrWhiteSpace(text))
                return false;
            text = text.Trim().TrimStart('#');
            if (text.Length != 6)
                return false;
            return uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb);
        }
    }

    /// <summary>
    /// Editor data the TDF format can't hold, stored in a sidecar file next to the track
    /// ("!Name.tdx", JSON). The original editor and the game ignore it, so tracks stay compatible.
    ///
    /// Sections this version doesn't know are kept and written back unchanged, so data created
    /// by a newer version of the editor survives being saved by an older one.
    /// </summary>
    public sealed class TrackExtras
    {
        public const int FormatVersion = 1;

        /// <summary>Support pillar colors per block, keyed by the block's root cell.</summary>
        public readonly Dictionary<(int x, int y), SupportStyle> Supports = new Dictionary<(int x, int y), SupportStyle>();

        private Dictionary<string, object> preserved = new Dictionary<string, object>();

        public bool IsEmpty => Supports.Count == 0 && preserved.Count == 0;

        /// <summary>
        /// A named JSON section (e.g. "race"). Section values must be treated as immutable: replace
        /// them with <see cref="SetSection"/> instead of modifying them, because clones share them.
        /// </summary>
        public object GetSection(string key) => preserved.TryGetValue(key, out var value) ? value : null;

        /// <summary>Sets or (with null) removes a section.</summary>
        public void SetSection(string key, object value)
        {
            if (key == "supports" || key == "format" || key == "version")
                throw new ArgumentException($"'{key}' is reserved.", nameof(key));
            if (value == null)
                preserved.Remove(key);
            else
                preserved[key] = value;
        }

        public TrackExtras Clone()
        {
            var clone = new TrackExtras();
            foreach (var pair in Supports)
                clone.Supports[pair.Key] = pair.Value;
            // preserved values are never mutated, a shallow copy is enough
            clone.preserved = new Dictionary<string, object>(preserved);
            return clone;
        }

        public bool ContentEquals(TrackExtras other)
        {
            if (other == null)
                return IsEmpty;
            if (Supports.Count != other.Supports.Count || preserved.Count != other.preserved.Count)
                return false;
            foreach (var pair in Supports)
            {
                if (!other.Supports.TryGetValue(pair.Key, out var style) || !style.Equals(pair.Value))
                    return false;
            }
            if (preserved.Count == 0)
                return true;
            return MiniJson.Serialize(preserved, false) == MiniJson.Serialize(other.preserved, false);
        }

        // Serialization
        public string ToJson()
        {
            var root = new Dictionary<string, object>
            {
                ["format"] = "rvtrackeditor-extras",
                ["version"] = FormatVersion
            };

            if (Supports.Count > 0)
            {
                var list = new List<object>();
                foreach (var pair in Supports.OrderBy(p => p.Key.y).ThenBy(p => p.Key.x))
                {
                    list.Add(new Dictionary<string, object>
                    {
                        ["x"] = pair.Key.x,
                        ["y"] = pair.Key.y,
                        ["top"] = SupportStyle.ToHex(pair.Value.Top),
                        ["bottom"] = SupportStyle.ToHex(pair.Value.Bottom)
                    });
                }
                root["supports"] = list;
            }

            foreach (var pair in preserved)
                root[pair.Key] = pair.Value;

            return MiniJson.Serialize(root, true);
        }

        /// <summary>
        /// Parses extras. Throws <see cref="MiniJson.ParseException"/> for malformed files; entries with
        /// invalid values are skipped and reported in <paramref name="warnings"/>.
        /// </summary>
        public static TrackExtras FromJson(string json, List<string> warnings = null)
        {
            var root = MiniJson.AsObject(MiniJson.Parse(json));
            if (root == null)
                throw new MiniJson.ParseException("The extras file doesn't contain a JSON object.");

            var extras = new TrackExtras();
            int skipped = 0;

            foreach (var pair in root)
            {
                switch (pair.Key)
                {
                    case "format":
                    case "version":
                        break;

                    case "supports":
                        foreach (var item in MiniJson.AsArray(pair.Value) ?? new List<object>())
                        {
                            var entry = MiniJson.AsObject(item);
                            if (entry == null
                                || !SupportStyle.TryParseHex(MiniJson.GetString(entry, "top"), out uint top)
                                || !SupportStyle.TryParseHex(MiniJson.GetString(entry, "bottom"), out uint bottom))
                            {
                                skipped++;
                                continue;
                            }
                            int x = MiniJson.GetInt(entry, "x", -1);
                            int y = MiniJson.GetInt(entry, "y", -1);
                            if (x < 0 || y < 0)
                            {
                                skipped++;
                                continue;
                            }
                            extras.Supports[(x, y)] = new SupportStyle(top, bottom);
                        }
                        break;

                    default:
                        extras.preserved[pair.Key] = pair.Value;
                        break;
                }
            }

            int version = MiniJson.GetInt(root, "version", 1);
            if (version > FormatVersion)
                warnings?.Add($"The track's extra data was written by a newer editor (version {version}). Parts this version doesn't understand are kept unchanged.");
            if (skipped > 0)
                warnings?.Add($"{skipped} invalid support color entr{(skipped == 1 ? "y was" : "ies were")} ignored.");

            return extras;
        }

        /// <summary>Removes support styles that don't belong to a module root in <paramref name="doc"/>.</summary>
        public int RemoveOrphans(TdfDocument doc)
        {
            var orphans = Supports.Keys.Where(k => !doc.InBounds(k.x, k.y) || !doc.GetCell(k.x, k.y).HasModule).ToList();
            foreach (var key in orphans)
                Supports.Remove(key);
            return orphans.Count;
        }
    }
}

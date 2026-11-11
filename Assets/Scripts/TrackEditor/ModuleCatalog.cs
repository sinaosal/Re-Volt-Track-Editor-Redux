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

/// <summary>
/// Human readable information about track modules for the interface: family names
/// (localized through the game's strings file where available), code names, and variant chains.
/// </summary>
public static class ModuleCatalog
{
    private static readonly Dictionary<LocString, string> englishFallback = new Dictionary<LocString, string>
    {
        { LocString.MODULE_STARTGRID, "Start Grid" },
        { LocString.MODULE_STRAIGHTS, "Straights" },
        { LocString.MODULE_DIPS, "Dips" },
        { LocString.MODULE_HUMPS, "Humps" },
        { LocString.MODULE_SQUARE_BEND, "Square Bend" },
        { LocString.MODULE_ROUND_BEND, "Round Bend" },
        { LocString.MODULE_DIAGONAL, "Diagonal" },
        { LocString.MODULE_BANK, "Bank" },
        { LocString.MODULE_RUMBLE, "Rumble Strip" },
        { LocString.MODULE_NARROW, "Narrow" },
        { LocString.MODULE_PIPE, "Pipe" },
        { LocString.MODULE_BRIDGE, "Bridge" },
        { LocString.MODULE_CROSSROAD, "Crossroad" },
        { LocString.MODULE_JUMP, "Jump" },
        { LocString.MODULE_CHICANE, "Chicane" },
    };

    // enum name prefix -> family; longer prefixes first
    private static readonly KeyValuePair<string, LocString>[] familyPrefixes =
    {
        new KeyValuePair<string, LocString>("START", LocString.MODULE_STARTGRID),
        new KeyValuePair<string, LocString>("RAMP", LocString.MODULE_STRAIGHTS),
        new KeyValuePair<string, LocString>("DIP", LocString.MODULE_DIPS),
        new KeyValuePair<string, LocString>("HUMP", LocString.MODULE_HUMPS),
        new KeyValuePair<string, LocString>("CORNER_SQUARE", LocString.MODULE_SQUARE_BEND),
        new KeyValuePair<string, LocString>("CORNER", LocString.MODULE_ROUND_BEND),
        new KeyValuePair<string, LocString>("DIAGONAL", LocString.MODULE_DIAGONAL),
        new KeyValuePair<string, LocString>("BANK", LocString.MODULE_BANK),
        new KeyValuePair<string, LocString>("RUMBLE", LocString.MODULE_RUMBLE),
        new KeyValuePair<string, LocString>("NARROW", LocString.MODULE_NARROW),
        new KeyValuePair<string, LocString>("PIPE", LocString.MODULE_PIPE),
        new KeyValuePair<string, LocString>("BRIDGE", LocString.MODULE_BRIDGE),
        new KeyValuePair<string, LocString>("CROSS_ROAD", LocString.MODULE_CROSSROAD),
        new KeyValuePair<string, LocString>("JUMP", LocString.MODULE_JUMP),
        new KeyValuePair<string, LocString>("ZIGZAG", LocString.MODULE_CHICANE),
    };

    private static readonly Dictionary<int, List<int>> variantChainCache = new Dictionary<int, List<int>>();

    public static string Localize(LocString id)
    {
        string text = Localization.Lookup(id);
        // Lookup returns the enum name when no strings file is installed
        if (text == id.ToString() && englishFallback.TryGetValue(id, out var fallback))
            return fallback;
        return text;
    }

    /// <summary>"bank_10_2_lh" style identifier, matching the original module names.</summary>
    public static string GetCodeName(int moduleId)
    {
        string name = ((Modules.ID)moduleId).ToString();
        if (name.StartsWith("TWM_", StringComparison.Ordinal))
            name = name.Substring(4);
        return name.ToLowerInvariant();
    }

    public static LocString? GetFamily(int moduleId)
    {
        string name = ((Modules.ID)moduleId).ToString();
        if (!name.StartsWith("TWM_", StringComparison.Ordinal))
            return null;
        name = name.Substring(4);

        foreach (var pair in familyPrefixes)
        {
            if (name.StartsWith(pair.Key, StringComparison.Ordinal))
                return pair.Value;
        }
        return null;
    }

    /// <summary>Family name such as "Bank" or "Round Bend".</summary>
    public static string GetDisplayName(int moduleId)
    {
        var family = GetFamily(moduleId);
        return family.HasValue ? Localize(family.Value) : GetCodeName(moduleId);
    }

    /// <summary>
    /// All modules reachable with the previous/next variant keys starting from <paramref name="moduleId"/>,
    /// in the order the keys cycle through them.
    /// </summary>
    public static IReadOnlyList<int> GetVariantChain(int moduleId)
    {
        if (variantChainCache.TryGetValue(moduleId, out var cached))
            return cached;

        var changes = Modules.Lookup.Changes;
        var chain = new List<int>();
        if (moduleId < 0 || moduleId >= changes.Length)
        {
            chain.Add(moduleId);
            return chain;
        }

        // walk backwards to the start of the chain (or once around a cycle)
        int start = moduleId;
        var seen = new HashSet<int> { start };
        while (true)
        {
            int previous = changes[start].PreviousVar;
            if (previous == start || previous < 0 || previous >= changes.Length || !seen.Add(previous))
                break;
            start = previous;
        }

        // then forwards
        int current = start;
        seen.Clear();
        while (seen.Add(current))
        {
            chain.Add(current);
            int next = changes[current].NextVar;
            if (next == current || next < 0 || next >= changes.Length)
                break;
            current = next;
        }

        if (!chain.Contains(moduleId))
            chain.Insert(0, moduleId);

        foreach (var id in chain)
            variantChainCache[id] = chain;
        return chain;
    }

    /// <summary>Index of the module group (as shown in the module menu) containing the module, or -1.</summary>
    public static int GetGroupIndex(int moduleId)
    {
        var groups = Modules.Lookup.Groups;
        for (int i = 0; i < groups.Length; i++)
        {
            foreach (int id in groups[i].ModuleIDs)
            {
                if (id == moduleId)
                    return i;
            }
        }

        // fall back to the family
        var family = GetFamily(moduleId);
        if (family.HasValue)
        {
            for (int i = 0; i < groups.Length; i++)
            {
                if (groups[i].Name == family.Value)
                    return i;
            }
        }
        return -1;
    }
}

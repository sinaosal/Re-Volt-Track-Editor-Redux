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

/// <summary>
/// English text used when no strings file is installed (copies of the editor files without the
/// "strings" folder are common). Without this, the interface shows identifiers such as
/// "SAVE_NAME_TEMPLATE" and every new track gets the same name.
/// Format placeholders match what <see cref="Localization"/> produces from the game's %d / %s.
/// </summary>
public static class LocalizationFallback
{
    private static readonly Dictionary<LocString, string> english = new Dictionary<LocString, string>
    {
        { LocString.MODE_PLACEMODULES, "Place Modules" },
        { LocString.MODE_MODULES, "Modules" },
        { LocString.MODE_PICKUPS, "Pickups" },
        { LocString.MODE_ADJUST, "Adjust" },
        { LocString.MODE_EXPORT, "Export" },
        { LocString.MODE_MENU, "Menu" },

        { LocString.PROMPT_WARNING, "Warning" },
        { LocString.PROMPT_QUESTION, "Question" },
        { LocString.PROMPT_INFO, "Information" },

        { LocString.ANSWER_YES, "Yes" },
        { LocString.ANSWER_NO, "No" },
        { LocString.ANSWER_HALF, "Half" },
        { LocString.ANSWER_REGULAR, "Regular" },
        { LocString.ANSWER_DOUBLE, "Double" },

        { LocString.PROMPT_EXPORT_CHOOSE_SCALE, "Choose the size of the track in the game." },
        { LocString.PROMPT_TRACK_NOT_SAVED, "The track has unsaved changes. Save it first?" },
        { LocString.PROMPT_TRACK_SAVE_FAILED, "The track could not be saved." },
        { LocString.PROMPT_OVERWRITE_TRACK, "A track with this name already exists. Replace it?" },
        { LocString.PROMPT_REALLY_DELETE, "Really delete \"{0}\"?" },
        { LocString.PROMPT_KEEP_CHANGES, "Keep the changes?" },
        { LocString.PROMPT_TRACK_SAVED, "Track saved." },
        { LocString.PROMPT_ERROR_SAVING_TRACK, "Error saving the track." },
        { LocString.PROMPT_NO_LEVELS_DIR, "Re-Volt's levels folder was not found. Put the editor folder inside your Re-Volt folder to export tracks." },
        { LocString.PROMPT_NO_LOOP_FORMED_PIPE_C, "The track doesn't form a loop. The pipe at the cursor isn't connected properly." },
        { LocString.PROMPT_NO_LOOP_FORMED_STEP_C, "The track doesn't form a loop. Check the connection at the cursor." },
        { LocString.PROMPT_NO_LOOP_FORMED_PIPE, "The track doesn't form a loop. A pipe isn't connected properly." },
        { LocString.PROMPT_NO_LOOP_FORMED_STEP, "The track doesn't form a loop." },
        { LocString.PROMPT_TRACK_TOO_COMPLEX, "The track is too complex to export." },
        { LocString.PROMPT_MISSING_START_GRID, "The track needs a start grid." },
        { LocString.PROMPT_MULTIPLE_START_GRIDS, "The track has more than one start grid." },

        { LocString.PICKUPS_PLACED, "Pickups placed: {0} / {1}" },
        { LocString.ADJUST_SUBMODE_POSITION, "Position" },
        { LocString.ADJUST_SUBMODE_SIZE, "Size" },
        { LocString.ADJUST_WIDTH, "Width: {0}" },
        { LocString.ADJUST_HEIGHT, "Height: {0}" },

        { LocString.MENU_SAVE, "Save" },
        { LocString.MENU_NEW, "New" },
        { LocString.MENU_LOAD, "Load" },
        { LocString.MENU_QUIT, "Quit" },

        { LocString.EXPORT_STAGE_NUM, "Stage {0} of 12" },
        { LocString.EXPORT_STAGE_FINISHED, "Finished" },

        { LocString.SAVE_SAVE, "Save" },
        { LocString.SAVE_NAME_TEMPLATE, "Track {0}" },
        { LocString.LOAD_NO_TRACKS_EXIST, "No saved tracks" },

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

    public static bool TryGet(LocString id, out string text) => english.TryGetValue(id, out text);
}

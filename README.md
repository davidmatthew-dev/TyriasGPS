# Tyria's GPS

Tyria's GPS helps you quickly find a place and copy its chat link.

## What It Does

- Search by map or location name.
- Search NPC's by name.
- Press Enter in the search box or click Search to run a search.
- Show up to 25 matching results.
- Click any result to copy its chat link or wiki command.
- Copy your current character name with the Copy Name button.
- Set an Open Window keybind to show or hide the window.
- Clear the search box and results with the Clear Search button.
- Clear all of Tyria's GPS cache with the Clear Cache button.
- Show a Searching... state while a search is running.
- Show results source state: Waiting for first search, Searching, Previous query cache, or Fresh index search.
- Migrate from CSV to JSON for POI indexing.
- Load manual POI and NPC entries from JSON files.
- When API search returns no results, attempt wiki page location extraction and show those location matches.
- Show Search Wiki button for no result searches and open the GW2 wiki Special:Search page.
- Save wiki query results to JSON cache for faster repeated lookups.
- Display the module version in the bottom right corner of the window.

## Why Copy Name Exists

When you want to whisper yourself a location link, you usually need two separate pieces:

1. Your current character name for the whisper recipient box.
2. The location chat link to paste into the message.

The Copy Name button speeds that up. It copies your active character name so you can paste it directly into the whisper window name box before pasting the location link.

## Installation

### Option 1: Install from Blish HUD (Recommended)

1. Open Blish HUD.
2. Open the **Modules** window.
3. Click the **Browse Modules** button or search for "Tyria's GPS" in the module browser.
4. Click **Install** on the Tyria's GPS module.
5. The module will automatically download and install.

Your module will stay updated automatically when new versions are released.

### Option 2: Manual Installation

1. Download the .bhm file from the [latest release](https://github.com/davidmatthew-dev/TyriasGPS/releases).
2. Open Blish HUD.
3. Open the **Modules** window.
4. Right-click inside the Modules window and choose the option to open your module folder.
5. Copy the .bhm file into that folder.
6. Back in Blish HUD, refresh modules (or restart Blish HUD).

## Version History

**v1.9.0**
- Added NPC search support.
- Migrated POI indexing and manual entries to JSON files.
- Added manual POI/NPC override handling with JSON files.
- Added Mistlock Sanctuary manual waypoint entry.
- Added Search Wiki button for no result searches.
- Search Wiki now opens GW2 wiki using Special:Search URLs.
- Result tooltip text now matches what will be copied (chat link or wiki command).
- Added wiki page location extraction when API results are empty.
- Added wiki search result cache JSON storage.
- Clear Cache now clears POI cache, wiki cache, and override files, then rebuilds from fresh data/defaults.
- Added version checks for cache and override files during upgrades, including obsolete cache file removal.
- JSON output now preserves literal waypoint links (for example: [&BPMJAAA=]).
- Bank, TP, trading post, and merchant searches now return multiple major city hub locations.
- Banking helper location links are saved to NPC override files for reuse.
- Improved top area spacing behavior so extra vertical space is only used for longer status text.
- Wiki location cache now expires after 7 days so results stay accurate after game patches.
- Searches that time out after 15 seconds now fail gracefully instead of hanging.
- Added CI workflow to automatically build and attach the .bhm file to GitHub releases.
- Replaced default CodeQL setup with a custom workflow that builds the project for higher C# analysis quality.
- Updated to version 1.9.0.

**v1.8.6**
- Add Enter key support to run search from the search box.
- Updated to version 1.8.6.

**v1.8.5**
- Fix .sln project path reference for SSRD build.
- Updated to version 1.8.5.

**v1.8.4**
- Move solution files into src/ for SSRD build compatibility.
- Updated to version 1.8.4.

**v1.8.3**
- Fix module namespace to match SSRD format.
- Updated to version 1.8.3.

**v1.8.2**
- Removed duplicate root manifest.json to fix SSRD submission.
- Updated to version 1.8.2.

**v1.8.1**
- Replaced custom LogHelper with Blish HUD's built in Logger.
- Logs now write to Blish HUD's log directory instead of a separate module log file.
- Removed custom log file rotation.
- Removed unsupported icon field from module manifest.
- Added repository url and contributor details to module manifest.
- Updated to version 1.8.1.

**v1.8.0**
- Replaced all buttons with custom GpsActionButton controls (animated blue style).
- Added Clear Search button to clear the search box and results.
- Added Clear Cache button (orange accent style) to clear in-memory and disk POI cache.
- Removed character name preview text box.
- Character name is now detected in the background for the Copy Name button.
- Added instructions on using the addon upon loading and clearing results.
- Adjusted window layout and button sizes for the new controls.
- Updated to version 1.8.0.

**v1.7.0**
- Added an Open Window keybind setting to show or hide the window without the corner icon.
- Keybind stays active even when typing in text fields.
- Switched to a custom compass icon for the corner icon and window emblem.
- Redesigned result rows with a custom control, dark background, and blue hover animation.
- Changed window background texture and tightened control layout.
- Character name preview now auto-refreshes every 500ms.
- Renamed internal whisper-prefixed variables and methods to use clearer naming.
- Updated to version 1.7.0.

**v1.6.0**
- Logs and cache now write to the BlishHUD\tyrias-gps directory.
- Log file renamed from actions.log to tyrias-gps.log.
- Automatic log rotation when file reaches 10 MB, archived with regional date suffix.
- Version label added to the bottom right of the window.
- Updated to version 1.6.0.

**v1.5.0**
- Removed whisper-ready copy from result rows — clicking a result now copies only the chat link.
- Added a Copy Name button to copy the active character name separately.
- Added POI cache saving to speed up subsequent searches.
- Added search button Searching... state and results source indicator.
- Updated to version 1.5.0.

**v1.4.0**
- Expanded results layout for easier scanning.
- Renamed panel title from Matches to Results.
- Added whisper-ready copy behavior from result rows when character name is available.
- Copies only the POI chat link when character name is unavailable.
- Updated to version 1.4.0.

**v1.3.0**
- Made result rows click-to-copy with tooltips.
- Updated to version 1.3.0.

**v1.2.0**
- Added a dedicated scrollable panel for search results.
- Show larger results in the module window.
- Updated to version 1.2.0.

**v1.1.1**
- Reworked search to build a POI index from API data on first search.
- Orders results by closer name matches.
- Returns up to 25 results per search.
- Updated to version 1.1.1.

**v1.1.0**
- Load map data from the public API when a search runs.
- Use map results for location matching.
- Updated to version 1.1.0.

**v1.0.2**
- Added the module window with search input and search button.
- Added corner icon.
- Show the first matching result in the window.
- Updated to version 1.0.2.

**v1.0.1**
- Fixed module enable behavior.
- Updated BlishHUD dependency version requirements.
- Updated to version 1.0.1.

**v1.0.0**
- Added initial solution, project, and module manifest files.
- Added first ModuleMain and LogHelper files.
- Set initial module version to 1.0.0.

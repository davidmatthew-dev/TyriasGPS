# Tyria's GPS

Tyria's GPS helps you quickly find a place and copy its chat link.

## What It Does

- Search by map or location name.
- Show up to 25 matching results.
- Click any result to copy whisper-ready text in the format `/w Your Character, [chat link]`.
- If your active character name is not available yet, clicking a result copies just the chat link.

## How Copying Works

When your current character name is available, Tyria's GPS includes it automatically so one click gives you a whisper-ready line you can paste directly into chat.

If the game has not reported your active character name yet, the module copies only the location chat link.

## Installation

1. Download the module .bhm file.
2. Open BlishHUD.
3. Open the Modules window.
4. Right-click inside the Modules window and choose the option to open your module folder.
5. Copy the .bhm file into that folder.
6. Back in BlishHUD, refresh modules (or restart BlishHUD).

## Version History

**v1.4.0**
- Expanded results layout for easier scanning.
- Renamed panel title from Matches to Results.
- Added whisper-ready copy behavior from result rows when character name is available.
- Falls back to copying only the POI chat link when character name is unavailable.
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

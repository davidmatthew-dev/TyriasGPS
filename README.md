# Tyria's GPS

Tyria's GPS helps you quickly find a place and copy its chat link.

## What It Does

- Search by map or location name.
- Show up to 25 matching results.
- Click any result to copy its chat link.
- Copy your current character name with the Copy Name button.
- Show a Searching... state while a search is running.
- Show results source state: Waiting for first search, Searching, Previous query cache, or Fresh index search.
- Cache the POI index to poi-index-cache.csv to speed up future loads.

## Why Copy Name Exists

When you want to whisper yourself a location link, you usually need two separate pieces:

1. Your current character name for the whisper recipient box.
2. The location chat link to paste into the message.

The Copy Name button speeds that up. It copies your active character name so you can paste it directly into the whisper window name box before pasting the location link.

## Installation

1. Download the module .bhm file.
2. Open BlishHUD.
3. Open the Modules window.
4. Right-click inside the Modules window and choose the option to open your module folder.
5. Copy the .bhm file into that folder.
6. Back in BlishHUD, refresh modules (or restart BlishHUD).

## Version History

**v1.5.0**
- Removed whisper-ready copy from result rows — clicking a result now copies only the chat link.
- Added a Copy Name button to copy the active character name separately.
- Added POI cache persistence to speed up subsequent searches.
- Added search button Searching... state and results source indicator.
- Updated to version 1.5.0.

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

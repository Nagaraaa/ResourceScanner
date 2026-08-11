# Resource Scanner

Resource Scanner is a lightweight BepInEx 5 quality-of-life mod for **The Planet Crafter**.

Select a mineable resource and the mod points you toward the nearest matching deposit within the configured range. When that deposit disappears, the scanner automatically selects the next nearest one.

## Features

- Tracks one mineable resource at a time.
- Displays only the nearest matching deposit.
- Shows the resource name and distance on screen.
- Keeps an edge-of-screen direction indicator when the target is outside the camera view.
- Automatically switches to the next nearest deposit.
- Searchable in-game resource selection menu.
- Configurable scan range from 25 to 1,000 metres.
- Configurable keyboard shortcut, set directly in game.
- Automatic English and French interface based on the game language.
- Lightweight scanning with a cached resource list and configurable scan interval.

Resource Scanner is visual only. It does not collect, create, delete, or transfer resources.

## Usage

1. Press `F7` by default.
2. Choose a mineable resource.
3. Follow the on-screen marker to the nearest matching deposit.
4. Open the menu again to change the resource, range, shortcut, or stop tracking.

## Installation

1. Install **BepInEx 5 for Unity** for The Planet Crafter.
2. Download and extract Resource Scanner.
3. Copy the included `Naagara - Resource Scanner` folder into:

   `The Planet Crafter\BepInEx\plugins\`

4. Start the game.

The final DLL path should be:

`The Planet Crafter\BepInEx\plugins\Naagara - Resource Scanner\ResourceScanner.dll`

## Requirements

- The Planet Crafter
- BepInEx 5 for Unity

## Download

Download compiled versions from [GitHub Releases](https://github.com/Nagaraaa/ResourceScanner/releases). The source code in this repository does not include game assemblies or other third-party binaries.

## Changelog

See [CHANGELOG.md](CHANGELOG.md) for release notes.

## Author

Created by **Naagara**.

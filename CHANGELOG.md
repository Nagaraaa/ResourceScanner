# Changelog

## 0.1.2 - 2026-08-11

### Fixed

- French and English status messages now fit without being clipped on either side.
- Bottom status positions are offset farther from the game version text and player HUD.

### Changed

- The status panel now sizes itself to its text, uses a shorter message, left-aligned text, and a subtle cyan HUD accent.

## 0.1.1 - 2026-08-11

### Fixed

- The player camera no longer moves while the F7 scanner menu is open.
- Typing in the scanner search field no longer moves the player or triggers game shortcuts and menus.
- Deposits completely enclosed by terrain or rock geometry are ignored when choosing the nearest target.
- The resource list now includes every mineable group registered by the game instead of only resources currently loaded nearby.
- Long tracking text is no longer attached to the world marker or clipped at screen edges.
- The marker now stays outside a safe area reserved for the game compass and bottom HUD.

### Added

- A compact fixed status panel for the selected resource and distance.
- Four status positions selectable from the F7 menu: top left, top right, bottom left, or bottom right.
- Compact diamond and direction-arrow markers that do not cover the game HUD with resource text.

### Performance

- Geometry exposure checks use reusable physics buffers to avoid repeated allocation pressure.

## 0.1.0 - 2026-08-11

### Added

- Nearest-deposit tracking for mineable resources.
- On-screen resource name, distance, and direction marker.
- Automatic selection of the next nearest matching deposit.
- Searchable resource selection menu.
- In-game range and keyboard shortcut configuration.
- Automatic French and English interface support.
- Cached resource discovery and adjustable scan interval for lightweight operation.

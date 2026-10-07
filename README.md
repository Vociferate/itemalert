# Item Alert

Item Alert is an ExileAPI plugin for Path of Exile that detects high-value unique item drops before identification by matching their item artwork/resource path and comparing them against live poe.ninja economy data.

## Features

- Detects valuable unidentified unique items
- Uses `RenderItem.ResourcePath` for item identification
- Pulls live pricing data from poe.ninja
- Supports both Divine and Chaos value thresholds
- Tracks unique:
  - Accessories
  - Armour
  - Weapons
  - Jewels
  - Flasks
- Displays a temporary center-screen alert
- Shows the estimated item value
- Draws an arrow toward the detected ground item
- Supports manually tracked items such as Angler's Plait
- Includes beta logging and diagnostic tools
- Includes one-click support bundle creation
- Can open a prefilled GitHub support issue for beta testers

## How It Works

Item Alert reads item information already exposed through ExileAPI.

For unidentified unique items, the normal unique name may not yet be available. However, the item's `RenderItem.ResourcePath` can already contain the unique-specific inventory artwork path.

Item Alert compares that artwork path against its target list and uses current poe.ninja economy data to determine whether the item meets the configured value threshold.

The plugin only draws an overlay notification. It does not automatically loot items or interact with the game.

## Price Filtering

Users can configure:

- Minimum Divine value
- Minimum Chaos value
- Minimum listing count
- poe.ninja refresh interval

An item is tracked when it meets either the configured Divine or Chaos threshold.

## Alerts

When a tracked item is detected, Item Alert displays:

- Unique item name
- Current or estimated market value
- A directional arrow pointing toward the ground item

The alert disappears automatically after a few seconds.

## Manual Tracking

`AlwaysTrack.txt` can be used for uniques that should always trigger regardless of current market price.

This is useful for extremely rare or low-volume items where public market data may not be reliable.

## Beta Testing and Support

Item Alert includes built-in beta support tools.

### Create Support Bundle

Creates a ZIP containing relevant diagnostic information such as:

- Plugin version
- Current settings
- Current target list
- Detection history
- poe.ninja refresh history
- Error logs
- Recent diagnostic summaries

### Open Support Issue

Opens a prefilled GitHub issue in the user's default browser.

No GitHub token is used and nothing is automatically submitted or uploaded. The tester can review the report and manually attach the generated support bundle.

## Diagnostic Component Scanner

The Component Scanner is an optional debugging tool used to inspect item components and resource paths.

It is disabled by default and is not required for normal Item Alert operation.

## Installation

Place the `ItemAlert` folder inside:

```text
ExileApi/Plugins/Source/

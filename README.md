# ItemAlert

ItemAlert is an ExileAPI plugin for Path of Exile that detects configured high-value unique drops, shows their estimated market value, highlights the exact ground-item label, and can draw a color-matched connection arrow so the drop is easy to locate.

It uses item artwork/resource paths exposed by ExileAPI together with current poe.ninja economy data.

![ItemAlert Screenshot 1](https://i.imgur.com/u2q4GyV.png)

![ItemAlert Screenshot 2](https://i.imgur.com/k42HMQU.png)

## Features

- Detects valuable uniques while still unidentified
- Tracks the same ground item through identified/unidentified label changes
- Uses `RenderItem.ResourcePath` for unique artwork matching
- Pulls current pricing from poe.ninja
- Supports Divine, Chaos, and minimum-listing thresholds
- Tracks unique accessories, armour, weapons, jewels, and optional flasks
- Supports manual targets through `AlwaysTrack.txt`
- Handles several valuable drops from the same loot event
- Uses a high-priority detection queue for burst drops
- Highlights the exact rendered Path of Exile ground-item label
- Draws an optional color-matched arrow from each alert to its exact matched ground item
- Assigns colors by detection order only
- Keeps alerts visible until the exact item is picked up or removed
- Automatically re-stacks remaining alerts
- Uses resolution-independent center-relative X/Y positioning
- Includes support bundles and a browser-based GitHub issue workflow

## Detection

For an unidentified unique, the unique name may not yet be available through normal item metadata.

ExileAPI can expose the unique-specific inventory artwork through `RenderItem.ResourcePath`.

ItemAlert matches that resource path against a target table built from public poe.ninja economy data plus optional entries in `AlwaysTrack.txt`.

This allows ItemAlert to detect supported high-value uniques before they have been identified in-game.

## Price Filtering

ItemAlert can build its target list using current poe.ninja economy data.

Users can configure:

- Minimum Divine value
- Minimum Chaos value
- Minimum listing count
- poe.ninja refresh interval
- Unique item categories to scan

Market values displayed by ItemAlert should be treated as estimates rather than guaranteed trade prices.

## Multiple Simultaneous Drops

ItemAlert can track multiple valuable ground items at the same time.

The default visible maximum is six, configurable up to ten.

Default detection-order colors:

1. Magenta
2. Cyan
3. Green
4. White
5. Yellow
6. Violet

Colors are assigned strictly by detection order.

They are not tied to Mageblood, Headhunter, base type, rarity, category, or value.

Each alert retains the exact ground entity that caused it, allowing multiple copies of the same unique to be tracked and highlighted independently.

## Ground-Label Highlighting

ItemAlert draws a configurable border around the exact ground-item label Path of Exile is rendering.

The ground label is matched to the alert using the exact `WorldItem` entity rather than the displayed item name or base type.

Because the rectangle comes from ExileAPI's rendered label, it naturally follows:

- loot-filter font size
- item-label width
- UI scale
- resolution and aspect ratio
- identified/unidentified label changes
- multiple items sharing the same base type

Users can configure:

- highlight padding
- highlight border thickness
- highlight corner rounding

## Connection Arrows

Connection arrows are optional and work alongside ground-label highlighting.

Each arrow:

- uses the same detection-order color as its matching alert and ground-label border
- starts directly from the nearest corner of the alert
- terminates on the matching loot-label border
- points to the exact `WorldItem` associated with that alert
- remains independently matched when several valuable items drop at once

Arrow matching is based on the exact ground entity rather than item name or base type.

## Alert Position and Appearance

`X = 0` and `Y = 0` places the alert stack at the center of the current Path of Exile game window.

- Negative X moves the alerts left
- Positive X moves the alerts right
- Negative Y moves the alerts up
- Positive Y moves the alerts down

The offsets scale relative to the current game-window dimensions rather than using fixed screen coordinates.

This allows the positioning system to adapt to:

- 1080p
- 1440p
- 4K
- ultrawide displays
- super-ultrawide displays
- windowed clients
- different aspect ratios

Users can also configure alert font size, spacing, colors, rounded corners, border thickness, and the number of simultaneous alerts displayed.

## Alert Lifetime

Alerts do not expire on a timer.

A toast, ground-label highlight, and optional connection arrow remain visible while that exact ground `WorldItem` exists.

When the item is picked up or otherwise removed:

- its alert disappears
- its ground-label highlight disappears
- its connection arrow disappears
- remaining alerts automatically re-stack

## High-Priority Drop Scanning

High-value detection runs through a dedicated lightweight priority queue.

This is separate from heavier diagnostic and capture processing.

As a result, a burst of multiple ground-item drops can be scanned without being delayed by component dumps, raw-memory diagnostics, or other troubleshooting work.

The diagnostic `MaxItemsPerTick` setting does not throttle high-value target detection.

## Manual Tracking

`AlwaysTrack.txt` can force selected uniques to remain targets regardless of current market value.

This is useful for extremely rare or low-volume items where public pricing information may be incomplete or unreliable.

## Installation

Copy the `ItemAlert` folder into:

```text
ExileApi/Plugins/Source/

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
- Supports live poe.ninja league selection
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
- Keeps routine scanning silent during normal gameplay

## Detection

For an unidentified unique, the unique name may not yet be available through normal item metadata.

ExileAPI can expose the unique-specific inventory artwork through `RenderItem.ResourcePath`.

ItemAlert matches that resource path against a target table built from public poe.ninja economy data plus optional entries in `AlwaysTrack.txt`.

This allows supported high-value uniques to be detected before they have been identified in-game.

## Price Filtering

Users can configure:

- Minimum Divine value
- Minimum Chaos value
- Minimum listing count
- poe.ninja refresh interval
- Unique item categories to scan
- poe.ninja league

An item qualifies when it meets either the configured Divine or Chaos threshold and the configured minimum listing requirement.

Displayed prices should be treated as market estimates rather than guaranteed trade values.

## poe.ninja League Selection

ItemAlert loads the available Path of Exile 1 **economy leagues directly from poe.ninja** instead of hard-coding league names.

The league selector includes:

- **Auto (Current Challenge League)** — uses the first economy league returned by poe.ninja
- every other active economy league returned by poe.ninja

This allows entries such as Standard, Hardcore, the current challenge league, Hardcore challenge variants, and any other economy league supported by poe.ninja to appear automatically when available.

SSF leagues are shown only if poe.ninja exposes them through its economy-league endpoint. If poe.ninja does not provide economy pricing for a league, ItemAlert does not invent or substitute pricing for it.

Changing the selected league automatically triggers a fresh price scan.

## Multiple Simultaneous Drops

ItemAlert can track several valuable items at the same time.

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

Each alert retains the exact ground entity that caused it, so multiple copies of the same unique can be highlighted independently.

## Ground-Label Highlighting

ItemAlert draws one configurable border around the exact ground-item label Path of Exile is rendering.

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

Offsets scale relative to the current game-window dimensions rather than fixed screen coordinates.

This allows the same positioning settings to adapt to:

- 1080p
- 1440p
- 4K
- ultrawide
- super-ultrawide
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

A burst of multiple ground-item drops can therefore be scanned without being delayed by component dumps, raw-memory diagnostics, or other troubleshooting work.

The diagnostic `MaxItemsPerTick` setting does not throttle high-value target detection.

## Manual Tracking

`AlwaysTrack.txt` can force selected uniques to remain targets regardless of current market value.

This is useful for extremely rare or low-volume items where public pricing information may be incomplete or unreliable.

## Installation

Download the release ZIP and extract the `ItemAlert` folder into:

```text
ExileApi/Plugins/Source/
```

The final structure should look like:

```text
ExileApi/
└── Plugins/
    └── Source/
        └── ItemAlert/
            ├── ItemAlert.cs
            ├── ItemAlertSettings.cs
            ├── ItemAlert.csproj
            ├── AlwaysTrack.txt
            └── SupportIssueUrl.txt
```

Then restart or reload ExileAPI.

## Support

### Create Support Bundle

**Create Support Bundle** generates a local ZIP containing useful diagnostic information such as:

- current targets
- detection history
- poe.ninja refresh history
- error logs
- configuration information
- recent diagnostic summaries

Large raw diagnostic files are not included by default.

### Open Support Issue

**Open Support Issue** opens:

https://github.com/Vociferate/itemalert/issues/new

in the user's default browser with report information prefilled.

ItemAlert does not:

- store a GitHub token
- authenticate to GitHub
- automatically submit an issue
- automatically upload a support bundle

The tester reviews the report and submits it manually.

## Advanced Diagnostics

The collapsed **Advanced / Diagnostic Scanner** is disabled by default.

It exists for troubleshooting new or broken resource-path matches and is not required for normal:

- detection
- poe.ninja pricing
- league selection
- alerts
- ground-label highlighting
- connection arrows

## Silent Operation

Normal scanning is silent.

ItemAlert does not print routine capture, scan, or debug messages on screen.

Visible output is limited to:

- a qualifying valuable-item alert
- the matching ground-label highlight
- the optional connection arrow
- genuine error messages when something fails

Routine diagnostic information is written to local log files instead.

## Safety / Scope

ItemAlert is an informational ExileAPI overlay.

It does not:

- automate looting
- click items
- send mouse input
- send keyboard input
- move the player
- modify game memory
- decrypt network traffic
- bypass anti-cheat
- automatically upload diagnostic files

## Privacy

Support bundles can contain local filesystem paths.

Depending on where ExileAPI is installed, those paths may include a Windows username.

Review support bundles before posting them publicly if that information is sensitive.

## Version

**v1.0.0.2**

## License

ItemAlert is released under the MIT License.

See `LICENSE` for details.

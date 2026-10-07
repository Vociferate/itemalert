# ItemAlert

ItemAlert is an ExileAPI plugin for Path of Exile that detects configured high-value unique drops, shows their estimated market value, and highlights the exact ground-item label so the drop is easy to locate.

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
- Assigns colors by detection order only
- Keeps alerts visible until the exact item is picked up or removed
- Automatically re-stacks remaining alerts
- Uses resolution-independent center-relative X/Y positioning
- Includes support bundles and a browser-based GitHub issue workflow

## Detection

For an unidentified unique, the unique name may not yet be available through normal item metadata. ExileAPI can expose the unique-specific inventory artwork through `RenderItem.ResourcePath`.

ItemAlert matches that resource path against a target table built from public poe.ninja economy data plus optional entries in `AlwaysTrack.txt`.

## Multiple Simultaneous Drops

The default visible maximum is six, configurable up to ten.

Default detection-order colors:

1. Magenta
2. Cyan
3. Green
4. White
5. Yellow
6. Violet

Colors are not tied to Mageblood, Headhunter, base type, rarity, category, or value.

Each alert retains the exact ground entity that caused it, so multiple copies of the same unique can be highlighted independently.

## Ground-Label Highlighting

ItemAlert draws one configurable border around the exact ground-item label Path of Exile is rendering.

Because the rectangle comes from ExileAPI's rendered label, it naturally follows loot-filter font size, text width, UI scale, resolution, and identified/unidentified label changes.

Users can configure highlight padding, border thickness, and corner rounding.

## Alert Position and Appearance

`X = 0` and `Y = 0` is the center of the current Path of Exile game window.

- negative X moves left
- positive X moves right
- negative Y moves up
- positive Y moves down

Offsets scale to the current game-window dimensions, including ultrawide and windowed clients.

## Alert Lifetime

Alerts do not expire on a timer.

A toast and highlight remain visible while that exact ground `WorldItem` exists. When the item is picked up or removed, its toast/highlight disappear and remaining alerts re-stack.

## High-Priority Drop Scanning

High-value detection runs in a dedicated lightweight queue separate from heavier diagnostics. Burst loot events are therefore not throttled by the diagnostic `MaxItemsPerTick` setting.

## Manual Tracking

`AlwaysTrack.txt` can force selected uniques to remain targets regardless of current market value.

## Installation

Copy the `ItemAlert` folder into:

```text
ExileApi/Plugins/Source/
```

Then restart or reload ExileAPI.

## Support

**Create Support Bundle** creates a local diagnostic ZIP.

**Open Support Issue** opens:

https://github.com/Vociferate/itemalert/issues/new

in the default browser with report details prefilled.

No GitHub token is used. Nothing is uploaded or submitted automatically.

## Advanced Diagnostics

The collapsed **Advanced / Diagnostic Scanner** is disabled by default and is not required for normal detection, pricing, alerts, or highlighting.


## Silent Operation

Normal scanning is silent. ItemAlert does not print routine capture, scan, or debug messages on screen.

Visible output is limited to:

- a qualifying valuable-item alert
- the matching ground-label highlight
- genuine error messages when something fails

Routine diagnostics are written to local log files instead.

## Safety / Scope

ItemAlert is an informational overlay. It does not automate looting, click items, send input, move the player, modify game memory, decrypt network traffic, bypass anti-cheat, or automatically upload diagnostic files.

## Privacy

Support bundles can contain local filesystem paths. Review them before posting publicly if your path contains information you do not want to share.

## Version

**v1.0.0**

## License

See `LICENSE`.

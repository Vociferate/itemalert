# Moderator / Source Review Notes

## Purpose

ItemAlert is an informational ExileAPI overlay for Path of Exile.

It detects configured high-value unique ground items, displays estimated poe.ninja market values, highlights exact rendered ground-item labels, and can draw color-matched connection arrows.

## Normal Detection Path

The normal detector uses state exposed through ExileAPI, including:

- `WorldItem`
- `Mods`
- `RenderItem.ResourcePath`
- `IngameUi.ItemsOnGroundLabels`
- `Label.GetClientRectCache`

Ground labels are matched by the exact ground entity rather than displayed label text.

## External Network Access

ItemAlert performs HTTPS GET requests to public poe.ninja Path of Exile 1 economy endpoints.

This includes the poe.ninja economy-league endpoint so available pricing leagues can be populated dynamically.

No poe.ninja authentication is used.

## Gameplay Interaction

ItemAlert does not:

- automate looting
- click items
- send keyboard or mouse input
- move the player
- alter game memory
- decrypt game packets
- bypass anti-cheat

Normal operation is limited to reading ExileAPI-exposed state, fetching public pricing data, writing local diagnostics, and drawing overlay UI.

## Optional Diagnostics

The Advanced / Diagnostic Scanner is disabled by default.

It exists to troubleshoot resource-path/component matching and writes diagnostic information locally. It is not required for normal operation.

## GitHub Support Workflow

No GitHub token is stored or used.

The plugin opens:

https://github.com/Vociferate/itemalert/issues/new

in the user's default browser with prefilled report information.

The user reviews and submits the issue manually.

## Support Bundles

Support bundles are generated locally and are not uploaded automatically.

They can contain local installation paths, which is disclosed in the README and beta testing guide.

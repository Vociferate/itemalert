# Moderator / Source Review Notes

ItemAlert is an informational ExileAPI overlay for Path of Exile.

## Normal operation

The plugin reads ExileAPI-exposed state including `WorldItem`, `Mods`, `RenderItem.ResourcePath`, `IngameUi.ItemsOnGroundLabels`, and `Label.GetClientRectCache`.

It fetches public poe.ninja economy data over HTTPS, including the PoE 1 economy-league endpoint and draws overlay UI, including exact ground-label borders and optional connection arrows.

## It does not

- automate looting
- click items
- send keyboard or mouse input
- move the player
- alter game memory
- decrypt packets
- bypass anti-cheat
- automatically upload files

## Optional diagnostics

The Advanced / Diagnostic Scanner is disabled by default. It exists to troubleshoot resource-path/component matching and writes local diagnostic data.

## GitHub support

No GitHub token is stored or used. The plugin opens:

https://github.com/Vociferate/itemalert/issues/new

in the user's default browser. The tester reviews and submits the issue manually.

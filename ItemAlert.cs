using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Net.Http;
using System.Threading.Tasks;
using ExileCore;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared;
using ExileCore.Shared.Enums;
using SharpDX;
using ImGuiNET;

namespace ItemAlert
{
    public class ItemAlert : BaseSettingsPlugin<ItemAlertSettings>
    {
        public ItemAlert()
        {
            Name = "ItemAlert";
            Description = "High-value unique item alerts using poe.ninja pricing, RenderItem artwork detection, and exact ground-label highlighting.";
            // Public build behavior: routine scanning/logging is silent.
            // Only actual item alerts/highlights and genuine error messages
            // should produce visible UI output.
        }

        // Heavy/diagnostic processing queue. This remains rate-limited so
        // optional capture/logging work cannot stall ExileAPI.
        private readonly ConcurrentQueue<Entity> _pending = new();

        // High-priority alert queue. Valuable-item detection is intentionally
        // separated from the throttled diagnostic queue so a burst of several
        // simultaneous drops cannot wait behind capture/logging work.
        private readonly ConcurrentQueue<Entity> _targetPending = new();
        private readonly Dictionary<string, DateTime> _lastCapture = new();
        private readonly List<string> _recent = new();

        private readonly List<CaptureSnapshot> _unidentifiedSnapshots = new();

        private string _logsFolder = string.Empty;
        private string _capturesFolder = string.Empty;
        private string _pairsFolder = string.Empty;
        private string _componentCapturesFolder = string.Empty;
        private string _logPath = string.Empty;
        private string _spamPath = string.Empty;
        private string _errorsPath = string.Empty;
        private string _priceLogPath = string.Empty;
        private string _detectionsPath = string.Empty;
        private string _supportBundlesFolder = string.Empty;

        // ------------------------------------------------------------------
        // Public beta support workflow
        // ------------------------------------------------------------------
        // The plugin NEVER stores or uses a GitHub token. The configured URL
        // is only opened in the user's default browser so the tester can review
        // and manually submit the issue. This keeps issue submission transparent
        // and avoids distributing credentials with the plugin.
        private string _supportIssueUrlFile = string.Empty;
        private string _latestSupportBundlePath = string.Empty;

        private int _captureNumber;
        private int _pairNumber;

        private readonly Dictionary<long, TargetAlert> _activeTargetAlerts = new();

        private static readonly HttpClient PoeNinjaHttp = new HttpClient();
        private readonly object _targetLock = new object();
        private Dictionary<string, PriceTarget> _priceTargets =
            new Dictionary<string, PriceTarget>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, PriceTarget> _priceTargetsByName =
            new Dictionary<string, PriceTarget>(StringComparer.OrdinalIgnoreCase);

        private Task<PriceRefreshResult> _priceRefreshTask;
        private DateTime _nextPriceRefresh = DateTime.MinValue;
        private string _targetsFile = string.Empty;
        private string _alwaysTrackFile = string.Empty;
        private string _leagueOverrideFile = string.Empty;
        private string _priceStatus = "Not loaded";
        private string _activeLeague = string.Empty;


        public override bool Initialise()
        {
            _logsFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins", "Source", "ItemAlert", "Logs");
            _capturesFolder = Path.Combine(_logsFolder, "Captures");
            _pairsFolder = Path.Combine(_logsFolder, "Pairs");
            _componentCapturesFolder = Path.Combine(_logsFolder, "ComponentCaptures");
            _logPath = Path.Combine(_logsFolder, "ItemAlert.log");
            _spamPath = Path.Combine(_logsFolder, "ItemAlert_Spam.csv");
            _errorsPath = Path.Combine(_logsFolder, "Errors.log");
            _priceLogPath = Path.Combine(_logsFolder, "PriceRefresh.log");
            _detectionsPath = Path.Combine(_logsFolder, "Detections.csv");
            _supportBundlesFolder = Path.Combine(DirectoryFullName, "SupportBundles");
            _supportIssueUrlFile = Path.Combine(DirectoryFullName, "SupportIssueUrl.txt");
            _targetsFile = Path.Combine(DirectoryFullName, "Targets_Current.csv");
            _alwaysTrackFile = Path.Combine(DirectoryFullName, "AlwaysTrack.txt");
            _leagueOverrideFile = Path.Combine(DirectoryFullName, "PoeNinjaLeague.txt");

            try
            {
                Directory.CreateDirectory(_logsFolder);
                Directory.CreateDirectory(_capturesFolder);
                Directory.CreateDirectory(_pairsFolder);
                Directory.CreateDirectory(_componentCapturesFolder);
                Directory.CreateDirectory(_supportBundlesFolder);

                EnsurePriceScannerFiles();
                EnsureBetaLogFiles();
                EnsureSupportIssueConfig();

                // Upgrade legacy saved color settings. ExileAPI persists plugin
                // settings between builds, so changing the source default alone
                // does not replace the old orange Slot 4 value.
                MigrateLegacyAlertColors();

                WriteStartupDiagnostics();

                // These buttons are deliberately user-initiated. Nothing is
                // uploaded and no browser is opened without the tester clicking.
                Settings.CreateSupportBundle.OnPressed = CreateSupportBundle;
                Settings.OpenSupportIssue.OnPressed = OpenSupportIssue;


                File.AppendAllText(
                    _logPath,
                    Environment.NewLine +
                    "======================================================================" + Environment.NewLine +
                    $"ItemAlert v1.0.0.1 started {DateTime.Now:yyyy-MM-dd HH:mm:ss}" + Environment.NewLine +
                    $"Logs folder: {_logsFolder}" + Environment.NewLine +
                    "======================================================================" + Environment.NewLine);

                if (!File.Exists(_spamPath))
                {
                    File.WriteAllText(
                        _spamPath,
                        "CaptureId,Timestamp,Identified,UniqueName,ItemLevel,RequiredLevel,ImplicitSummary,RenderItemResourcePath,DetectedTarget,ItemPath,GroundAddress,ItemAddress,ModsAddress,EntityId,ModsHash,UniqueNameField,ImplicitArray,ExplicitArray" +
                        Environment.NewLine);
                }
            }
            catch (Exception ex)
            {
                DebugWindow.LogError($"[ItemAlert v1.0.0.1] Failed to initialise Logs folder: {ex}");
            }

            foreach (var entity in GameController.EntityListWrapper.ValidEntitiesByType[EntityType.WorldItem])
                Queue(entity);

            if (Settings.EnablePoeNinjaPriceScanner)
                StartPriceRefresh();

            return true;
        }

        public override void AreaChange(AreaInstance area)
        {
            _lastCapture.Clear();
            _recent.Clear();
            _unidentifiedSnapshots.Clear();
            _activeTargetAlerts.Clear();

            while (_targetPending.TryDequeue(out _))
            {
            }

            while (_pending.TryDequeue(out _))
            {
            }

            foreach (var entity in GameController.EntityListWrapper.ValidEntitiesByType[EntityType.WorldItem])
                Queue(entity);
        }

        public override void EntityAdded(Entity entity)
        {
            if (!Settings.Enable || entity == null || entity.Type != EntityType.WorldItem)
                return;

            Queue(entity);

            entity.OnUpdate += (_, updatedEntity) =>
            {
                if (Settings.Enable && updatedEntity != null)
                    Queue(updatedEntity);
            };
        }

        public override Job Tick()
        {
            if (!Settings.Enable)
                return null;

            UpdatePriceScanner();

            // Remove alerts whose exact WorldItem entity has been picked up or
            // otherwise removed. This makes toast cleanup immediate and keeps
            // the rendered stack synchronized with what is actually on ground.
            CleanupPickedUpAlerts();

            // --------------------------------------------------------------
            // HIGH-PRIORITY TARGET PASS
            // --------------------------------------------------------------
            // Drain the alert queue before any diagnostic work. A single loot
            // event can create many WorldItem entities in the same frame; this
            // pass is intentionally lightweight and is NOT restricted by the
            // legacy MaxItemsPerTick diagnostic setting.
            //
            // OnUpdate can enqueue the same entity more than once, so dedupe
            // addresses within this Tick to avoid redundant component reads.
            var targetAddressesSeen = new HashSet<long>();

            while (_targetPending.TryDequeue(out var targetGroundEntity))
            {
                if (targetGroundEntity == null ||
                    targetGroundEntity.Address == 0 ||
                    !targetAddressesSeen.Add(targetGroundEntity.Address))
                    continue;

                InspectTargetFast(targetGroundEntity);
            }

            // --------------------------------------------------------------
            // NORMAL / DIAGNOSTIC PASS
            // --------------------------------------------------------------
            // Raw dumps, component scans and capture logging can be expensive,
            // so those remain rate-limited independently.
            var processed = 0;

            while (processed < Settings.MaxItemsPerTick.Value &&
                   _pending.TryDequeue(out var groundEntity))
            {
                processed++;
                InspectGroundItem(groundEntity);
            }

            return null;
        }


        public override void DrawSettings()
        {
            // Draw the normal user-facing controls first.
            base.DrawSettings();

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            var open = ImGui.CollapsingHeader(
                "Advanced / Diagnostic Scanner",
                ImGuiTreeNodeFlags.None);

            ImGui.SameLine();
            ImGui.TextDisabled("(?)");

            if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
            {
                ImGui.SetTooltip(
                    "Diagnostic tools used while reverse-engineering unidentified unique items.\n" +
                    "The Component Scanner dumps item components such as RenderItem, Mods and Base,\n" +
                    "extracts strings/pointers, and writes detailed logs for research/debugging.\n\n" +
                    "It is NOT required for normal price scanning, artwork matching, alerts, or ground-label highlighting.\n" +
                    "Leave this section disabled unless you are investigating a new or broken item match.");
            }

            if (!open)
                return;

            ImGui.Indent();

            ImGui.TextWrapped(
                "Research/debug controls. Normal high-value unique detection does not require these.");

            ImGui.Spacing();

            var componentScanner = Settings.EnableComponentScanner.Value;
            if (ImGui.Checkbox("Enable Component Scanner", ref componentScanner))
                Settings.EnableComponentScanner.Value = componentScanner;

            ImGui.SameLine();
            ImGui.TextDisabled("(?)");
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
            {
                ImGui.SetTooltip(
                    "Scans every component attached to a detected item entity and writes raw/debug data.\n" +
                    "Useful when discovering artwork/resource paths for new uniques or diagnosing a bad match.\n" +
                    "Adds extra logging and disk I/O, so leave it OFF during normal mapping.");
            }

            DrawAdvancedSlider("Component Dump Bytes", Settings.ComponentDumpBytes);
            DrawAdvancedSlider("Component Pointer String Bytes", Settings.ComponentPointerStringBytes);
            DrawAdvancedSlider("Max Component Pointer Strings", Settings.MaxComponentPointerStrings);

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.TextDisabled("Legacy probe / capture diagnostics");

            DrawAdvancedSlider("Raw Dump Bytes", Settings.RawDumpBytes);
            DrawAdvancedSlider("Recapture Cooldown Ms", Settings.RecaptureCooldownMs);
            DrawAdvancedSlider("Max Pointer Strings", Settings.MaxPointerStrings);
            DrawAdvancedSlider("Max Items Per Tick", Settings.MaxItemsPerTick);
            DrawAdvancedSlider("Pair Window Minutes", Settings.PairWindowMinutes);
            DrawAdvancedSlider("Max Pending Unidentified", Settings.MaxPendingUnidentified);

            ImGui.Unindent();
        }

        private static void DrawAdvancedSlider(
            string label,
            ExileCore.Shared.Nodes.RangeNode<int> node)
        {
            var value = node.Value;

            ImGui.PushItemWidth(-1);
            if (ImGui.SliderInt(
                label,
                ref value,
                node.Min,
                node.Max))
            {
                node.Value = value;
            }
            ImGui.PopItemWidth();
        }

        public override void Render()
        {
            if (!Settings.Enable)
                return;

            RenderTargetAlerts();
        }

        private void Queue(Entity entity)
        {
            if (entity == null || entity.Type != EntityType.WorldItem)
                return;

            // Alert detection gets its own high-priority copy of the event.
            // The same entity also enters the normal queue for diagnostics.
            _targetPending.Enqueue(entity);
            _pending.Enqueue(entity);
        }

        // ==================================================================
        // PICKUP / ENTITY REMOVAL CLEANUP
        // ==================================================================
        // An alert belongs to one exact ground Entity. When that WorldItem is
        // picked up, ExileAPI removes it from ValidEntitiesByType[WorldItem].
        // Removing the corresponding TargetAlert immediately drops both the
        // toast and the ground-label highlight.
        // ==================================================================
        private void CleanupPickedUpAlerts()
        {
            if (_activeTargetAlerts.Count == 0)
                return;

            try
            {
                var liveGroundAddresses =
                    GameController.EntityListWrapper
                        .ValidEntitiesByType[EntityType.WorldItem]
                        .Where(x => x != null && x.Address != 0)
                        .Select(x => x.Address)
                        .ToHashSet();

                var removed = _activeTargetAlerts
                    .Where(x =>
                        x.Value?.GroundEntity == null ||
                        x.Value.GroundEntity.Address == 0 ||
                        !liveGroundAddresses.Contains(
                            x.Value.GroundEntity.Address))
                    .Select(x => x.Key)
                    .ToList();

                foreach (var key in removed)
                    _activeTargetAlerts.Remove(key);
            }
            catch (Exception ex)
            {
                LogBetaError(
                    "CleanupPickedUpAlerts",
                    ex);
            }
        }


        // ==================================================================
        // HIGH-PRIORITY TARGET DETECTION
        // ==================================================================
        // This method performs only the minimum reads required for an alert:
        // WorldItem -> ItemEntity -> Mods/rarity -> RenderItem.ResourcePath.
        //
        // It deliberately avoids raw memory dumps, component enumeration,
        // string-pointer probing, capture files, and other diagnostics.
        //
        // Keeping this path small allows every queued drop from a burst loot
        // event to be checked immediately in the same Tick.
        // ==================================================================
        private void InspectTargetFast(Entity groundEntity)
        {
            if (!Settings.EnableTargetDetector ||
                groundEntity == null ||
                groundEntity.Address == 0)
                return;

            try
            {
                var worldItem = groundEntity.GetComponent<WorldItem>();
                var item = worldItem?.ItemEntity;

                if (item == null || item.Address == 0)
                    return;

                var mods = item.GetComponent<Mods>();

                if (mods == null || mods.Address == 0)
                    return;

                if (Settings.OnlyUnique &&
                    mods.ItemRarity != ItemRarity.Unique)
                    return;

                var path = item.Path ?? string.Empty;

                if (Settings.OnlyBelts &&
                    path.IndexOf(
                        "/Belts/",
                        StringComparison.OrdinalIgnoreCase) < 0 &&
                    path.IndexOf(
                        "\\\\Belts\\\\",
                        StringComparison.OrdinalIgnoreCase) < 0)
                    return;

                var renderItem = item.GetComponent<RenderItem>();
                var resourcePath = renderItem?.ResourcePath ?? string.Empty;

                if (string.IsNullOrWhiteSpace(resourcePath))
                    return;

                var detectedTarget = DetectTarget(resourcePath);

                if (string.IsNullOrWhiteSpace(detectedTarget))
                    return;

                var priceTarget = GetPriceTarget(resourcePath);

                if (priceTarget == null ||
                    priceTarget.DivineValue <= 0)
                {
                    var byName =
                        GetPriceTargetByName(detectedTarget);

                    if (byName != null &&
                        (byName.DivineValue > 0 ||
                         byName.ChaosValue > 0))
                    {
                        priceTarget = byName;
                    }
                }

                var now = DateTime.Now;

                // Preserve the original detection-order slot/color when this
                // same item is seen again through Entity.OnUpdate.
                var firstSeen = now;

                if (_activeTargetAlerts.TryGetValue(
                        item.Address,
                        out var existingAlert))
                {
                    firstSeen = existingAlert.FirstSeen;
                }

                var isNewDetection =
                    existingAlert == null;

                _activeTargetAlerts[item.Address] =
                    new TargetAlert
                    {
                        ItemAddress = item.Address,
                        GroundEntity = groundEntity,
                        TargetName = detectedTarget,
                        ResourcePath = resourcePath,
                        DivineValue =
                            priceTarget?.DivineValue ?? 0,
                        ChaosValue =
                            priceTarget?.ChaosValue ?? 0,
                        Category =
                            priceTarget?.Category ?? "Built-in",
                        Variant =
                            priceTarget?.Variant ?? string.Empty,
                        PriceIsEstimate =
                            priceTarget != null &&
                            !string.Equals(
                                priceTarget.ResourcePath,
                                resourcePath,
                                StringComparison.OrdinalIgnoreCase),
                        FirstSeen = firstSeen,
                        LastSeen = now
                    };

                // Log only the first fast-path detection. The normal diagnostic
                // pass may inspect the same entity later, but the alert itself
                // is already active immediately.
                if (isNewDetection)
                {
                    AppendDetectionLog(
                        detectedTarget,
                        resourcePath,
                        priceTarget,
                        path,
                        mods.ItemLevel,
                        mods.RequiredLevel);
                }
            }
            catch (Exception ex)
            {
                // A single item becoming invalid between entity callbacks must
                // never stop the rest of a simultaneous drop batch.
                LogBetaError(
                    "InspectTargetFast",
                    ex);
            }
        }


        private void InspectGroundItem(Entity groundEntity)
        {
            try
            {
                var worldItem = groundEntity.GetComponent<WorldItem>();
                var item = worldItem?.ItemEntity;

                if (item == null || item.Address == 0)
                    return;

                var mods = item.GetComponent<Mods>();

                if (mods == null || mods.Address == 0)
                    return;

                if (Settings.OnlyUnique && mods.ItemRarity != ItemRarity.Unique)
                    return;

                var path = item.Path ?? string.Empty;

                if (Settings.OnlyBelts &&
                    path.IndexOf("/Belts/", StringComparison.OrdinalIgnoreCase) < 0 &&
                    path.IndexOf("\\Belts\\", StringComparison.OrdinalIgnoreCase) < 0)
                    return;

                var uniqueName = Safe(() => mods.UniqueName);

                var captureKey =
                    $"{groundEntity.Address:X}:{item.Address:X}:{mods.Address:X}:{mods.Identified}:{uniqueName}";

                var now = DateTime.Now;
                var cooldown = TimeSpan.FromMilliseconds(Settings.RecaptureCooldownMs.Value);

                if (_lastCapture.TryGetValue(captureKey, out var last) && now - last < cooldown)
                    return;

                _lastCapture[captureKey] = now;

                if (_lastCapture.Count > 5000)
                {
                    var cutoff = now - TimeSpan.FromMinutes(10);
                    var stale = _lastCapture
                        .Where(x => x.Value < cutoff)
                        .Select(x => x.Key)
                        .ToList();

                    foreach (var key in stale)
                        _lastCapture.Remove(key);
                }

                var itemLevel = Safe(() => mods.ItemLevel.ToString());
                var requiredLevel = Safe(() => mods.RequiredLevel.ToString());
                var hash = Safe(() => mods.Hash.ToString());
                var implicitSummary = GetImplicitSummary(mods);

                string renderItemResourcePath = string.Empty;
                try
                {
                    var renderItem = item.GetComponent<RenderItem>();
                    renderItemResourcePath = renderItem?.ResourcePath ?? string.Empty;
                }
                catch
                {
                    renderItemResourcePath = string.Empty;
                }

                // Alerting is handled by the dedicated high-priority pass.
                // Keep this fallback for unusual cases where an entity reached
                // the diagnostic queue without first entering the target queue.
                if (Settings.EnableTargetDetector &&
                    !_activeTargetAlerts.ContainsKey(item.Address))
                {
                    InspectTargetFast(groundEntity);
                }

                var rawSize = Settings.RawDumpBytes.Value;
                byte[] raw = Array.Empty<byte>();

                try
                {
                    raw = GameController.Memory.ReadBytes(mods.Address, rawSize) ?? Array.Empty<byte>();
                }
                catch
                {
                }

                var uniqueNameField = SafeObject(() => mods.ModsStruct.UniqueName);
                var implicitArray = SafeObject(() => mods.ModsStruct.implicitMods);
                var explicitArray = SafeObject(() => mods.ModsStruct.explicitMods);

                _captureNumber++;

                var snapshot = new CaptureSnapshot
                {
                    CaptureId = _captureNumber,
                    Time = now,
                    Identified = mods.Identified,
                    UniqueName = uniqueName,
                    ItemLevel = itemLevel,
                    RequiredLevel = requiredLevel,
                    ImplicitSummary = implicitSummary,
                    RenderItemResourcePath = renderItemResourcePath,
                    ItemPath = path,
                    GroundAddress = groundEntity.Address,
                    ItemAddress = item.Address,
                    ModsAddress = mods.Address,
                    EntityId = groundEntity.Id,
                    ModsHash = hash,
                    UniqueNameField = uniqueNameField,
                    ImplicitArray = implicitArray,
                    ExplicitArray = explicitArray,
                    Raw = raw
                };

                var header =
                    $"[C{snapshot.CaptureId:0000} {now:HH:mm:ss}] " +
                    $"{(mods.Identified ? "ID" : "UNID")} " +
                    $"Name='{uniqueName}' iLvl={itemLevel} Req={requiredLevel} " +
                    $"Path='{path}'" + (string.IsNullOrWhiteSpace(renderItemResourcePath) ? "" : $" Render='{renderItemResourcePath}'");

                _recent.Add(header);
                while (_recent.Count > 100)
                    _recent.RemoveAt(0);

                WriteDetailedCapture(snapshot, mods);
                AppendSpamCsv(snapshot);
                WriteCaptureFiles(snapshot);

                if (Settings.EnableComponentScanner)
                    WriteComponentCapture(snapshot, item);

                if (!snapshot.Identified)
                {
                    _unidentifiedSnapshots.Add(snapshot);

                    while (_unidentifiedSnapshots.Count > Settings.MaxPendingUnidentified.Value)
                        _unidentifiedSnapshots.RemoveAt(0);
                }
                else
                {
                    TryCreatePair(snapshot);
                }

            }
            catch (Exception ex)
            {
                DebugWindow.LogError($"[ItemAlert v1.0.0.1] {ex}");
            }
        }

        private void TryCreatePair(CaptureSnapshot identified)
        {
            var candidate = _unidentifiedSnapshots
                .Where(x =>
                    x.ItemPath == identified.ItemPath &&
                    x.ItemLevel == identified.ItemLevel &&
                    x.RequiredLevel == identified.RequiredLevel &&
                    x.ImplicitSummary == identified.ImplicitSummary)
                .OrderByDescending(x => x.Time)
                .FirstOrDefault();

            if (candidate == null)
                return;

            var maxAge = TimeSpan.FromMinutes(Settings.PairWindowMinutes.Value);

            if (identified.Time - candidate.Time > maxAge)
                return;

            _pairNumber++;

            var diff = BuildDiff(candidate.Raw, identified.Raw);

            var pairPath = Path.Combine(
                _pairsFolder,
                $"Pair_{_pairNumber:0000}_C{candidate.CaptureId:0000}_to_C{identified.CaptureId:0000}_{SanitizeFileName(identified.UniqueName)}.txt");

            var lines = new List<string>
            {
                "ITEM ALERT - BEFORE / AFTER IDENTIFICATION PAIR",
                "=====================================================",
                $"Pair Id:             {_pairNumber}",
                $"UNID Capture:        C{candidate.CaptureId:0000}",
                $"ID Capture:          C{identified.CaptureId:0000}",
                $"Identified Name:     {identified.UniqueName}",
                $"Item Path:           {identified.ItemPath}",
                $"Item Level:          {identified.ItemLevel}",
                $"Required Level:      {identified.RequiredLevel}",
                $"Implicit Summary:    {identified.ImplicitSummary}",
                $"UNID Time:           {candidate.Time:yyyy-MM-dd HH:mm:ss.fff}",
                $"ID Time:             {identified.Time:yyyy-MM-dd HH:mm:ss.fff}",
                "",
                "BYTE DIFF",
                "---------",
                diff,
                "",
                "UNIDENTIFIED POINTER-LIKE QWORDS",
                "-------------------------------",
                FormatPointerLikeQwords(candidate.Raw),
                "",
                "IDENTIFIED POINTER-LIKE QWORDS",
                "-----------------------------",
                FormatPointerLikeQwords(identified.Raw),
                ""
            };

            try
            {
                File.WriteAllLines(pairPath, lines);

                _recent.Add(
                    $"PAIR {_pairNumber:0000}: C{candidate.CaptureId:0000} -> " +
                    $"C{identified.CaptureId:0000} '{identified.UniqueName}'");
            }
            catch (Exception ex)
            {
                DebugWindow.LogError($"[ItemAlert v1.0.0.1] Failed writing pair: {ex}");
            }

            _unidentifiedSnapshots.Remove(candidate);
        }

        private void WriteDetailedCapture(CaptureSnapshot s, Mods mods)
        {
            var lines = new List<string>
            {
                "",
                "====================== UNIQUE GROUND ITEM ======================",
                $"Capture Id:           C{s.CaptureId:0000}",
                $"Time:                 {s.Time:yyyy-MM-dd HH:mm:ss.fff}",
                $"Ground Address:       0x{s.GroundAddress:X}",
                $"Item Address:         0x{s.ItemAddress:X}",
                $"Mods Address:         0x{s.ModsAddress:X}",
                $"Entity Id:            {s.EntityId}",
                $"Item Path:            {s.ItemPath}",
                $"UniqueName:           {s.UniqueName}",
                $"Rarity:               {mods.ItemRarity}",
                $"Identified:           {s.Identified}",
                $"ItemLevel:            {s.ItemLevel}",
                $"RequiredLevel:        {s.RequiredLevel}",
                $"Implicit Summary:     {s.ImplicitSummary}",
                $"RenderItem Resource: {s.RenderItemResourcePath}",
                $"Detected Target:      {DetectTarget(s.RenderItemResourcePath)}",
                $"Mods.Hash:            {s.ModsHash}",
                $"UniqueName field:     {s.UniqueNameField}",
                $"Implicit mod array:   {s.ImplicitArray}",
                $"Explicit mod array:   {s.ExplicitArray}",
                "",
                $"RAW MODS MEMORY ({s.Raw.Length} bytes from 0x{s.ModsAddress:X}):",
                FormatHexDump(s.Raw),
                "",
                "8-BYTE VALUES / POINTER CANDIDATES:",
                FormatQwords(s.Raw),
                "",
                "POINTER STRING PROBE:",
                ProbePointerStrings(s.Raw),
                "",
                "HumanStats:",
                SafeList(() => mods.HumanStats),
                "",
                "HumanImplicitStats:",
                SafeList(() => mods.HumanImpStats),
                "",
                "ItemMods:",
                SafeItemMods(mods),
                "================================================================",
                ""
            };

            try
            {
                File.AppendAllLines(_logPath, lines);
            }
            catch
            {
            }
        }

        private void WriteCaptureFiles(CaptureSnapshot s)
        {
            try
            {
                var stem =
                    $"C{s.CaptureId:0000}_{s.Time:HHmmssfff}_{(s.Identified ? "ID" : "UNID")}_{SanitizeFileName(s.UniqueName)}";

                var binPath = Path.Combine(_capturesFolder, stem + ".bin");
                var metaPath = Path.Combine(_capturesFolder, stem + ".txt");

                File.WriteAllBytes(binPath, s.Raw);

                File.WriteAllLines(metaPath, new[]
                {
                    $"CaptureId=C{s.CaptureId:0000}",
                    $"Time={s.Time:yyyy-MM-dd HH:mm:ss.fff}",
                    $"Identified={s.Identified}",
                    $"UniqueName={s.UniqueName}",
                    $"ItemLevel={s.ItemLevel}",
                    $"RequiredLevel={s.RequiredLevel}",
                    $"ImplicitSummary={s.ImplicitSummary}",
                    $"RenderItemResourcePath={s.RenderItemResourcePath}",
                    $"DetectedTarget={DetectTarget(s.RenderItemResourcePath)}",
                    $"ItemPath={s.ItemPath}",
                    $"GroundAddress=0x{s.GroundAddress:X}",
                    $"ItemAddress=0x{s.ItemAddress:X}",
                    $"ModsAddress=0x{s.ModsAddress:X}",
                    $"EntityId={s.EntityId}",
                    $"ModsHash={s.ModsHash}",
                    $"UniqueNameField={s.UniqueNameField}",
                    $"ImplicitArray={s.ImplicitArray}",
                    $"ExplicitArray={s.ExplicitArray}"
                });
            }
            catch
            {
            }
        }



        private const string MagebloodResourcePath =
            "Art/2DItems/Belts/InjectorBelt.dds";

        private const string HeadhunterResourcePath =
            "Art/2DItems/Belts/Headhunter.dds";

        private string DetectTarget(string resourcePath)
        {
            if (string.IsNullOrWhiteSpace(resourcePath))
                return string.Empty;

            lock (_targetLock)
            {
                if (_priceTargets.TryGetValue(resourcePath, out var target))
                    return target.Name;
            }

            // Permanent safety fallbacks.
            if (string.Equals(
                resourcePath,
                MagebloodResourcePath,
                StringComparison.OrdinalIgnoreCase))
                return "Mageblood";

            if (string.Equals(
                resourcePath,
                HeadhunterResourcePath,
                StringComparison.OrdinalIgnoreCase))
                return "Headhunter";

            return string.Empty;
        }

        private PriceTarget GetPriceTarget(string resourcePath)
        {
            if (string.IsNullOrWhiteSpace(resourcePath))
                return null;

            lock (_targetLock)
            {
                _priceTargets.TryGetValue(resourcePath, out var target);
                return target;
            }
        }

        private PriceTarget GetPriceTargetByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            lock (_targetLock)
            {
                _priceTargetsByName.TryGetValue(name, out var target);
                return target;
            }
        }

        // ==================================================================
        // USER-VISIBLE ALERT RENDERING
        // ==================================================================
        // Pure overlay rendering: shows the detected name/value, highlights the
        // exact ground label, and can draw a color-matched connection arrow.
        // This does not click, loot, move the
        // player, inject input, or modify game state.
        // ==================================================================
        private void RenderTargetAlerts()
        {
            if (!Settings.EnableTargetDetector || _activeTargetAlerts.Count == 0)
                return;

            // --------------------------------------------------------------
            // ALERT LIFETIME = ITEM LIFETIME
            // --------------------------------------------------------------
            // Alerts do not expire on a timer anymore. They remain visible for
            // as long as the exact ground WorldItem still exists. As soon as
            // the player picks it up (or the entity otherwise leaves the area),
            // the toast/highlight is removed and the remaining alerts re-stack.
            var liveGroundAddresses =
                GameController.EntityListWrapper
                    .ValidEntitiesByType[EntityType.WorldItem]
                    .Where(x => x != null && x.Address != 0)
                    .Select(x => x.Address)
                    .ToHashSet();

            var stale = _activeTargetAlerts
                .Where(x =>
                    x.Value?.GroundEntity == null ||
                    x.Value.GroundEntity.Address == 0 ||
                    !liveGroundAddresses.Contains(
                        x.Value.GroundEntity.Address))
                .Select(x => x.Key)
                .ToList();

            foreach (var key in stale)
                _activeTargetAlerts.Remove(key);

            if (_activeTargetAlerts.Count == 0)
                return;

            var maxAlerts = Math.Max(
                1,
                Math.Min(
                    10,
                    Settings.MaximumSimultaneousAlerts.Value));

            // Stable ordering keeps a detected item in the same color slot for
            // the lifetime of its alert. Earlier detections are displayed first;
            // value is used only as a deterministic tie-breaker.
            var alerts = _activeTargetAlerts.Values
                .OrderBy(x => x.FirstSeen)
                .ThenByDescending(x => x.DivineValue)
                .ThenByDescending(x => x.ChaosValue)
                .Take(maxAlerts)
                .ToList();

            if (alerts.Count == 0)
                return;

            var fontSize = Settings.AlertFontSize.Value;
            var alertHeight = Math.Max(72f, fontSize * 3.5f);
            var spacing = Settings.AlertStackSpacing.Value;

            // Measure all active alert widths first so the entire stack can be
            // positioned as one resolution-independent group.
            var layouts = alerts
                .Select(alert => BuildAlertLayout(alert, fontSize, alertHeight))
                .ToList();

            var maxWidth = layouts.Max(x => x.Width);
            var totalHeight =
                layouts.Count * alertHeight +
                Math.Max(0, layouts.Count - 1) * spacing;

            var window = GameController.Window.GetWindowRectangle();

            GetAlertCenter(
                window,
                maxWidth,
                totalHeight,
                out var groupCenterX,
                out var groupCenterY);

            var groupTop = groupCenterY - totalHeight / 2f;

            for (var i = 0; i < layouts.Count; i++)
            {
                var layout = layouts[i];
                var alert = layout.Alert;

                var centerX = groupCenterX;
                var centerY =
                    groupTop +
                    alertHeight / 2f +
                    i * (alertHeight + spacing);

                var rect = new RectangleF(
                    centerX - layout.Width / 2f,
                    centerY - alertHeight / 2f,
                    layout.Width,
                    alertHeight);

                var slotColor = GetAlertSlotColor(i);

                // Background stays user-configurable and consistent. Border,
                // title, label highlight, and connection arrow use the same
                // detection-order slot color.
                Graphics.DrawBox(
                    rect,
                    Settings.AlertBackgroundColor.Value,
                    Settings.AlertCornerRadius.Value);

                Graphics.DrawFrame(
                    rect,
                    slotColor,
                    Settings.AlertCornerRadius.Value,
                    Settings.AlertBorderThickness.Value,
                    0);

                if (TryGetGroundItemLabelRect(
                        alert,
                        out var groundLabelRect))
                {
                    DrawGroundItemLabelHighlight(
                        groundLabelRect,
                        slotColor);

                    DrawConnectionArrow(
                        rect,
                        groundLabelRect,
                        slotColor);
                }

                Graphics.DrawText(
                    layout.Line1,
                    new System.Numerics.Vector2(
                        centerX - layout.Size1.X / 2f,
                        centerY - fontSize * 1.25f),
                    slotColor,
                    fontSize);

                if (layout.HasPrice)
                {
                    var startX =
                        centerX -
                        layout.CombinedValueWidth / 2f;

                    Graphics.DrawText(
                        layout.Line2,
                        new System.Numerics.Vector2(
                            startX,
                            centerY + fontSize * 0.15f),
                        Settings.AlertValueColor.Value,
                        fontSize);

                    Graphics.DrawText(
                        layout.CurrencyLabel,
                        new System.Numerics.Vector2(
                            startX + layout.Size2.X + 6f,
                            centerY + fontSize * 0.15f),
                        Settings.AlertCurrencyColor.Value,
                        fontSize);
                }
                else
                {
                    Graphics.DrawText(
                        layout.Line2,
                        new System.Numerics.Vector2(
                            centerX - layout.Size2.X / 2f,
                            centerY + fontSize * 0.15f),
                        Settings.AlertValueColor.Value,
                        fontSize);
                }
            }
        }

        private AlertLayout BuildAlertLayout(
            TargetAlert alert,
            int fontSize,
            float alertHeight)
        {
            var line1 =
                $"{alert.TargetName.ToUpperInvariant()} DETECTED";

            var showDivine = alert.DivineValue >= 1;
            var showChaos =
                !showDivine &&
                alert.ChaosValue > 0;

            var hasPrice = showDivine || showChaos;

            var line2 = showDivine
                ? $"{(alert.PriceIsEstimate ? "~" : "")}{alert.DivineValue:0.##}"
                : showChaos
                    ? $"{(alert.PriceIsEstimate ? "~" : "")}{alert.ChaosValue:0.#}"
                    : "HIGH-VALUE UNIQUE";

            var currencyLabel = showDivine
                ? " DIV"
                : showChaos
                    ? " C"
                    : string.Empty;

            var size1 = Graphics.MeasureText(
                line1,
                fontSize);

            var size2 = Graphics.MeasureText(
                line2,
                fontSize);

            var currencySize = hasPrice
                ? Graphics.MeasureText(
                    currencyLabel,
                    fontSize)
                : new System.Numerics.Vector2(0, 0);

            var combinedValueWidth = hasPrice
                ? size2.X + 6f + currencySize.X
                : size2.X;

            var width =
                Math.Max(
                    size1.X,
                    combinedValueWidth) +
                48f;

            return new AlertLayout
            {
                Alert = alert,
                Line1 = line1,
                Line2 = line2,
                CurrencyLabel = currencyLabel,
                Size1 = size1,
                Size2 = size2,
                CombinedValueWidth = combinedValueWidth,
                Width = width,
                Height = alertHeight,
                HasPrice = hasPrice
            };
        }

        // Colors are assigned strictly by detection order.
        // They are not tied to item name, base type, rarity, category, or value.
        private Color GetAlertSlotColor(int slotIndex)
        {
            return slotIndex switch
            {
                0 => Settings.AlertSlot1Color.Value,
                1 => Settings.AlertSlot2Color.Value,
                2 => Settings.AlertSlot3Color.Value,
                3 => Settings.AlertSlot4Color.Value,
                4 => Settings.AlertSlot5Color.Value,
                5 => Settings.AlertSlot6Color.Value,
                _ => (slotIndex % 6) switch
                {
                    0 => Settings.AlertSlot1Color.Value,
                    1 => Settings.AlertSlot2Color.Value,
                    2 => Settings.AlertSlot3Color.Value,
                    3 => Settings.AlertSlot4Color.Value,
                    4 => Settings.AlertSlot5Color.Value,
                    _ => Settings.AlertSlot6Color.Value
                }
            };
        }


        // ==================================================================
        // RESOLUTION-INDEPENDENT ALERT POSITIONING
        // ==================================================================
        // The alert always begins at the actual center of the current PoE game
        // window. X/Y settings are percentages of the usable distance from
        // center to each screen edge:
        //
        //   X =   0%  -> horizontal center
        //   X = -100% -> far left
        //   X = +100% -> far right
        //
        //   Y =   0%  -> vertical center
        //   Y = -100% -> top
        //   Y = +100% -> bottom
        //
        // Because this uses GameController.Window.GetWindowRectangle() every
        // frame, it automatically adapts to 16:9, 16:10, 21:9, 32:9, 4K,
        // windowed mode, and other resolutions/aspect ratios.
        //
        // The usable range accounts for half of the alert box itself, keeping
        // the alert on-screen even at +/-100%.
        // ==================================================================
        private void GetAlertCenter(
            RectangleF window,
            float alertWidth,
            float alertHeight,
            out float centerX,
            out float centerY)
        {
            var screenCenterX = window.Left + window.Width / 2f;
            var screenCenterY = window.Top + window.Height / 2f;

            var xPercent = Math.Max(
                -100,
                Math.Min(100, Settings.AlertOffsetXPercent.Value));

            var yPercent = Math.Max(
                -100,
                Math.Min(100, Settings.AlertOffsetYPercent.Value));

            // Available travel from center after reserving enough room to keep
            // the entire alert rectangle inside the game window.
            var maxTravelX = Math.Max(
                0f,
                window.Width / 2f - alertWidth / 2f);

            var maxTravelY = Math.Max(
                0f,
                window.Height / 2f - alertHeight / 2f);

            centerX =
                screenCenterX +
                maxTravelX * (xPercent / 100f);

            centerY =
                screenCenterY +
                maxTravelY * (yPercent / 100f);
        }


        // ==================================================================
        // GROUND-ITEM LABEL HIGHLIGHT
        // ==================================================================
        // ExileAPI exposes the exact ground labels currently rendered by PoE.
        // We match by the ground Entity address so duplicate uniques and items
        // with the same base-type text are still handled independently.
        //
        // Slot colors are assigned ONLY by detection order:
        // first detected valuable item -> Slot 1, second -> Slot 2, etc.
        // They are never assigned by unique name, base type, rarity, or price.
        //
        // Identification state does not control highlighting. The target is
        // detected from RenderItem artwork and the box follows the same world
        // entity whether the item is unidentified or identified.
        // ==================================================================
        private bool TryGetGroundItemLabelRect(
            TargetAlert alert,
            out RectangleF labelRect)
        {
            labelRect = default;

            if (!Settings.HighlightGroundItemLabel.Value ||
                alert?.GroundEntity == null)
                return false;

            try
            {
                var labels =
                    GameController.Game?.IngameState?.IngameUi?.ItemsOnGroundLabels;

                if (labels == null)
                    return false;

                var groundLabel = labels.FirstOrDefault(x =>
                    x != null &&
                    x.Address != 0 &&
                    x.ItemOnGround != null &&
                    x.ItemOnGround.Address == alert.GroundEntity.Address &&
                    x.IsVisible &&
                    x.Label != null);

                if (groundLabel?.Label == null)
                    return false;

                var rawRect = groundLabel.Label.GetClientRectCache;

                if (rawRect.Width <= 0 ||
                    rawRect.Height <= 0)
                    return false;

                var padding = Settings.GroundHighlightPadding.Value;

                labelRect = new RectangleF(
                    rawRect.X - padding,
                    rawRect.Y - padding,
                    rawRect.Width + padding * 2f,
                    rawRect.Height + padding * 2f);

                return true;
            }
            catch (Exception ex)
            {
                LogBetaError(
                    "TryGetGroundItemLabelRect",
                    ex);

                return false;
            }
        }

        private void DrawGroundItemLabelHighlight(
            RectangleF highlightRect,
            Color slotColor)
        {
            try
            {
                Graphics.DrawFrame(
                    highlightRect,
                    slotColor,
                    Settings.GroundHighlightCornerRadius.Value,
                    Settings.GroundHighlightBorderThickness.Value,
                    0);
            }
            catch (Exception ex)
            {
                LogBetaError(
                    "DrawGroundItemLabelHighlight",
                    ex);
            }
        }

        // ==================================================================
        // CONNECTION ARROWS
        // ==================================================================
        // Each arrow uses the same detection-order color as its toast and
        // highlighted ground label.
        //
        // Geometry:
        // - Start exactly on the nearest corner of the toast rectangle.
        // - End exactly on the closest point of the ground-label rectangle.
        // - Arrowhead points into the ground-label rectangle.
        //
        // Matching is still by the exact WorldItem entity, not by name/base type.
        // ==================================================================
        private void DrawConnectionArrow(
            RectangleF toastRect,
            RectangleF labelRect,
            Color slotColor)
        {
            if (!Settings.ShowConnectionArrows.Value)
                return;

            try
            {
                var labelCenter = new SharpDX.Vector2(
                    labelRect.Left + labelRect.Width / 2f,
                    labelRect.Top + labelRect.Height / 2f);

                var start = GetNearestToastCorner(
                    toastRect,
                    labelCenter);

                var end = GetClosestPointOnRectangle(
                    labelRect,
                    start);

                var dx = end.X - start.X;
                var dy = end.Y - start.Y;
                var length = (float)Math.Sqrt(dx * dx + dy * dy);

                if (length < 2f)
                    return;

                var thickness =
                    Settings.ConnectionArrowThickness.Value;

                Graphics.DrawLine(
                    start,
                    end,
                    thickness,
                    slotColor);

                // Arrowhead size scales gently with line thickness without
                // adding another user-facing setting.
                var headLength = Math.Max(
                    10f,
                    thickness * 4f);

                var ux = dx / length;
                var uy = dy / length;

                var backX = end.X - ux * headLength;
                var backY = end.Y - uy * headLength;

                var perpX = -uy;
                var perpY = ux;

                var wing = headLength * 0.55f;

                var leftWing = new SharpDX.Vector2(
                    backX + perpX * wing,
                    backY + perpY * wing);

                var rightWing = new SharpDX.Vector2(
                    backX - perpX * wing,
                    backY - perpY * wing);

                Graphics.DrawLine(
                    end,
                    leftWing,
                    thickness,
                    slotColor);

                Graphics.DrawLine(
                    end,
                    rightWing,
                    thickness,
                    slotColor);
            }
            catch (Exception ex)
            {
                LogBetaError(
                    "DrawConnectionArrow",
                    ex);
            }
        }

        private static SharpDX.Vector2 GetNearestToastCorner(
            RectangleF rect,
            SharpDX.Vector2 target)
        {
            var corners = new[]
            {
                new SharpDX.Vector2(rect.Left, rect.Top),
                new SharpDX.Vector2(rect.Right, rect.Top),
                new SharpDX.Vector2(rect.Left, rect.Bottom),
                new SharpDX.Vector2(rect.Right, rect.Bottom)
            };

            return corners
                .OrderBy(c =>
                {
                    var dx = c.X - target.X;
                    var dy = c.Y - target.Y;
                    return dx * dx + dy * dy;
                })
                .First();
        }

        private static SharpDX.Vector2 GetClosestPointOnRectangle(
            RectangleF rect,
            SharpDX.Vector2 source)
        {
            var x = Math.Max(
                rect.Left,
                Math.Min(rect.Right, source.X));

            var y = Math.Max(
                rect.Top,
                Math.Min(rect.Bottom, source.Y));

            // If the source projects directly inside one axis of the rectangle,
            // clamp to the nearest actual edge so the arrow visibly touches the
            // loot-label border rather than terminating somewhere inside it.
            var distLeft = Math.Abs(source.X - rect.Left);
            var distRight = Math.Abs(source.X - rect.Right);
            var distTop = Math.Abs(source.Y - rect.Top);
            var distBottom = Math.Abs(source.Y - rect.Bottom);

            var min = Math.Min(
                Math.Min(distLeft, distRight),
                Math.Min(distTop, distBottom));

            if (min == distLeft)
                x = rect.Left;
            else if (min == distRight)
                x = rect.Right;
            else if (min == distTop)
                y = rect.Top;
            else
                y = rect.Bottom;

            return new SharpDX.Vector2(x, y);
        }


        private void EnsureBetaLogFiles()
        {
            try
            {
                if (!File.Exists(_errorsPath))
                    File.WriteAllText(_errorsPath, "");

                if (!File.Exists(_priceLogPath))
                    File.WriteAllText(_priceLogPath, "");

                if (!File.Exists(_detectionsPath))
                {
                    File.WriteAllText(
                        _detectionsPath,
                        "Timestamp,League,Name,DivineValue,ChaosValue,Category,Variant,ItemPath,ResourcePath,ItemLevel,RequiredLevel" +
                        Environment.NewLine);
                }
            }
            catch (Exception ex)
            {
                DebugWindow.LogError($"[ItemAlert v1.0.0.1] Beta log setup failed: {ex}");
            }
        }

        private void WriteStartupDiagnostics()
        {
            try
            {
                var lines = new[]
                {
                    "",
                    "============================================================",
                    $"ItemAlert v1.0.0.1 startup {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                    $"OS={Environment.OSVersion}",
                    $"64BitProcess={Environment.Is64BitProcess}",
                    $"ProcessorCount={Environment.ProcessorCount}",
                    $"Runtime={Environment.Version}",
                    $"BaseDirectory={AppDomain.CurrentDomain.BaseDirectory}",
                    $"PluginDirectory={DirectoryFullName}",
                    $"OnlyUnique={Settings.OnlyUnique.Value}",
                    $"OnlyBelts={Settings.OnlyBelts.Value}",
                    $"PoeNinjaScanner={Settings.EnablePoeNinjaPriceScanner.Value}",
                    $"MinimumDivine={Settings.MinimumDivineValue.Value}",
                    $"MinimumChaos={Settings.MinimumChaosValue.Value}",
                    $"MinimumListings={Settings.MinimumListings.Value}",
                    $"ComponentScanner={Settings.EnableComponentScanner.Value}",
                    "============================================================"
                };

                File.AppendAllLines(_logPath, lines);
            }
            catch
            {
            }
        }

        private void AppendPriceLog(string message)
        {
            try
            {
                File.AppendAllText(
                    _priceLogPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
            }
            catch
            {
            }
        }

        private void AppendDetectionLog(
            string name,
            string resourcePath,
            PriceTarget priceTarget,
            string itemPath,
            int itemLevel,
            int requiredLevel)
        {
            try
            {
                var row = string.Join(",",
                    Csv(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")),
                    Csv(_activeLeague),
                    Csv(name),
                    Csv((priceTarget?.DivineValue ?? 0).ToString("0.####", CultureInfo.InvariantCulture)),
                    Csv((priceTarget?.ChaosValue ?? 0).ToString("0.####", CultureInfo.InvariantCulture)),
                    Csv(priceTarget?.Category ?? "Built-in"),
                    Csv(priceTarget?.Variant ?? string.Empty),
                    Csv(itemPath),
                    Csv(resourcePath),
                    Csv(itemLevel.ToString(CultureInfo.InvariantCulture)),
                    Csv(requiredLevel.ToString(CultureInfo.InvariantCulture)));

                File.AppendAllText(
                    _detectionsPath,
                    row + Environment.NewLine);
            }
            catch
            {
            }
        }

        private void LogBetaError(string context, Exception ex)
        {
            try
            {
                File.AppendAllText(
                    _errorsPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {context}{Environment.NewLine}" +
                    $"{ex}{Environment.NewLine}" +
                    "------------------------------------------------------------" +
                    Environment.NewLine);
            }
            catch
            {
            }
        }

        private void CreateSupportBundle()
        {
            try
            {
                Directory.CreateDirectory(_supportBundlesFolder);

                var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var staging = Path.Combine(
                    _supportBundlesFolder,
                    $"ItemAlert_Support_{stamp}");

                if (Directory.Exists(staging))
                    Directory.Delete(staging, true);

                Directory.CreateDirectory(staging);

                var infoPath = Path.Combine(staging, "SupportInfo.txt");
                var info = new List<string>
                {
                    "ITEM ALERT BETA SUPPORT BUNDLE",
                    "==============================",
                    $"Created={DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                    "PluginVersion=v1.0.0.1.1",
                    $"League={_activeLeague}",
                    $"PriceStatus={_priceStatus}",
                    $"OS={Environment.OSVersion}",
                    $"64BitProcess={Environment.Is64BitProcess}",
                    $"ProcessorCount={Environment.ProcessorCount}",
                    $"Runtime={Environment.Version}",
                    "",
                    "CURRENT SETTINGS",
                    "----------------",
                    $"OnlyUnique={Settings.OnlyUnique.Value}",
                    $"OnlyBelts={Settings.OnlyBelts.Value}",
                    $"EnableTargetDetector={Settings.EnableTargetDetector.Value}",
                    $"EnablePoeNinjaPriceScanner={Settings.EnablePoeNinjaPriceScanner.Value}",
                    $"MinimumDivineValue={Settings.MinimumDivineValue.Value}",
                    $"MinimumChaosValue={Settings.MinimumChaosValue.Value}",
                    $"MinimumListings={Settings.MinimumListings.Value}",
                    $"PoeNinjaRefreshMinutes={Settings.PoeNinjaRefreshMinutes.Value}",
                    $"TrackUniqueAccessories={Settings.TrackUniqueAccessories.Value}",
                    $"TrackUniqueArmours={Settings.TrackUniqueArmours.Value}",
                    $"TrackUniqueWeapons={Settings.TrackUniqueWeapons.Value}",
                    $"TrackUniqueJewels={Settings.TrackUniqueJewels.Value}",
                    $"TrackUniqueFlasks={Settings.TrackUniqueFlasks.Value}",
                    $"EnableComponentScanner={Settings.EnableComponentScanner.Value}",
                    "",
                    "WHAT TO SEND THE AUTHOR",
                    "-----------------------",
                    "Send this ZIP plus a short description of what happened.",
                    "If possible include the item name, map/zone, and whether the item was identified."
                };

                File.WriteAllLines(infoPath, info);

                CopyIfExists(_targetsFile, staging);
                CopyIfExists(_alwaysTrackFile, staging);
                CopyIfExists(_leagueOverrideFile, staging);
                CopyIfExists(_logPath, staging);
                CopyIfExists(_errorsPath, staging);
                CopyIfExists(_priceLogPath, staging);
                CopyIfExists(_detectionsPath, staging);
                CopyIfExists(_spamPath, staging);

                // Include recent capture metadata and component summaries, but not
                // every large raw .bin file by default.
                CopyRecentTextFiles(_capturesFolder, Path.Combine(staging, "Captures"), 25);
                CopyRecentTextFiles(_pairsFolder, Path.Combine(staging, "Pairs"), 25);
                CopyRecentTextFiles(_componentCapturesFolder, Path.Combine(staging, "ComponentCaptures"), 40);

                var zipPath = Path.Combine(
                    _supportBundlesFolder,
                    $"ItemAlert_Support_{stamp}.zip");

                if (File.Exists(zipPath))
                    File.Delete(zipPath);

                ZipFile.CreateFromDirectory(
                    staging,
                    zipPath,
                    CompressionLevel.Optimal,
                    false);

                Directory.Delete(staging, true);

                // Remember the newest bundle so the issue template can tell the
                // tester exactly which ZIP to attach to the GitHub issue.
                _latestSupportBundlePath = zipPath;

            }
            catch (Exception ex)
            {
                LogBetaError("CreateSupportBundle", ex);
                DebugWindow.LogError(
                    $"[ItemAlert] Failed to create support bundle: {ex.Message}",
                    8);
            }
        }

        private static void CopyIfExists(string source, string destinationFolder)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
                    return;

                Directory.CreateDirectory(destinationFolder);

                File.Copy(
                    source,
                    Path.Combine(destinationFolder, Path.GetFileName(source)),
                    true);
            }
            catch
            {
            }
        }

        private static void CopyRecentTextFiles(
            string sourceFolder,
            string destinationFolder,
            int maxFiles)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(sourceFolder) ||
                    !Directory.Exists(sourceFolder))
                    return;

                var files = Directory
                    .EnumerateFiles(sourceFolder, "*.txt", SearchOption.AllDirectories)
                    .Select(path => new FileInfo(path))
                    .OrderByDescending(x => x.LastWriteTimeUtc)
                    .Take(maxFiles)
                    .ToList();

                if (files.Count == 0)
                    return;

                Directory.CreateDirectory(destinationFolder);

                foreach (var file in files)
                {
                    var safeName =
                        $"{file.LastWriteTimeUtc:yyyyMMdd_HHmmss}_{file.Name}";

                    File.Copy(
                        file.FullName,
                        Path.Combine(destinationFolder, safeName),
                        true);
                }
            }
            catch
            {
            }
        }


        // ==================================================================
        // BETA SUPPORT ISSUE WORKFLOW
        // ==================================================================
        // SupportIssueUrl.txt contains the repository's GitHub "new issue"
        // URL. Keeping this outside the compiled source lets the maintainer
        // change repositories or issue-template routing without rebuilding
        // the plugin.
        //
        // Security / moderation note:
        // - No API token is used.
        // - No issue is submitted automatically.
        // - No files are uploaded automatically.
        // - The tester's default browser opens a normal GitHub issue page.
        // - The tester reviews the prefilled content and attaches the ZIP.
        // ==================================================================

        // ==================================================================
        // SETTINGS MIGRATION
        // ==================================================================
        // ItemAlert v1.0.0.1 originally shipped Slot 4 as orange
        // (255,150,50,255). ExileAPI persists ColorNode values in the user's
        // settings file, so simply changing the source default to white does
        // not affect an existing installation.
        //
        // This migration changes ONLY the known legacy orange default to white.
        // Other user-selected Slot 4 colors are preserved.
        // ==================================================================
        private void MigrateLegacyAlertColors()
        {
            try
            {
                var slot4 = Settings.AlertSlot4Color.Value;

                var isLegacyOrange =
                    slot4.R == 255 &&
                    slot4.G == 150 &&
                    slot4.B == 50 &&
                    slot4.A == 255;

                if (isLegacyOrange)
                {
                    Settings.AlertSlot4Color.Value = Color.White;

                    File.AppendAllText(
                        _logPath,
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Migrated legacy Slot 4 orange to white.{Environment.NewLine}");
                }
            }
            catch (Exception ex)
            {
                LogBetaError(
                    "MigrateLegacyAlertColors",
                    ex);
            }
        }


        private void EnsureSupportIssueConfig()
        {
            try
            {
                if (File.Exists(_supportIssueUrlFile))
                    return;

                File.WriteAllLines(
                    _supportIssueUrlFile,
                    new[]
                    {
                        "# ItemAlert beta support issue URL",
                        "#",
                        "# Replace the URL below with the GitHub repository's new-issue URL.",
                        "# Examples:",
                        "# https://github.com/OWNER/REPOSITORY/issues/new",
                        "# https://github.com/OWNER/REPOSITORY/issues/new?template=bug_report.yml",
                        "#",
                        "# The plugin does NOT authenticate to GitHub and does NOT submit issues automatically.",
                        "# It only opens this URL in the tester's normal web browser.",
                        "https://github.com/Vociferate/itemalert/issues/new"
                    });
            }
            catch (Exception ex)
            {
                LogBetaError("EnsureSupportIssueConfig", ex);
            }
        }

        private string ReadSupportIssueUrl()
        {
            try
            {
                if (!File.Exists(_supportIssueUrlFile))
                    return string.Empty;

                foreach (var raw in File.ReadAllLines(_supportIssueUrlFile))
                {
                    var line = raw.Trim();

                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                        continue;

                    return line;
                }
            }
            catch (Exception ex)
            {
                LogBetaError("ReadSupportIssueUrl", ex);
            }

            return string.Empty;
        }

        private void OpenSupportIssue()
        {
            try
            {
                var baseUrl = ReadSupportIssueUrl();

                if (string.IsNullOrWhiteSpace(baseUrl) ||
                    baseUrl.Contains("OWNER/REPOSITORY", StringComparison.OrdinalIgnoreCase))
                {
                    DebugWindow.LogError(
                        "[ItemAlert] Set your GitHub new-issue URL in SupportIssueUrl.txt first.",
                        8);
                    return;
                }

                if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var issueUri) ||
                    (issueUri.Scheme != Uri.UriSchemeHttps &&
                     issueUri.Scheme != Uri.UriSchemeHttp))
                {
                    DebugWindow.LogError(
                        "[ItemAlert] SupportIssueUrl.txt does not contain a valid HTTP/HTTPS URL.",
                        8);
                    return;
                }

                var latestBundle = GetLatestSupportBundlePath();

                var title = $"[ItemAlert v1.0.0.1] Bug report";

                var body = BuildSupportIssueBody(latestBundle);

                // Preserve any existing query parameters, such as a GitHub issue
                // template, and append the prefilled title/body safely.
                var separator = baseUrl.Contains("?") ? "&" : "?";
                var url =
                    baseUrl +
                    separator +
                    "title=" + Uri.EscapeDataString(title) +
                    "&body=" + Uri.EscapeDataString(body);

                // UseShellExecute=true opens the URL using the tester's normal
                // browser. There is no hidden HTTP POST and no credential use.
                Process.Start(
                    new ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });

            }
            catch (Exception ex)
            {
                LogBetaError("OpenSupportIssue", ex);
                DebugWindow.LogError(
                    $"[ItemAlert] Could not open support issue page: {ex.Message}",
                    8);
            }
        }

        private string BuildSupportIssueBody(string latestBundle)
        {
            var bundleName = string.IsNullOrWhiteSpace(latestBundle)
                ? "No support bundle created yet."
                : Path.GetFileName(latestBundle);

            return
                "## ItemAlert Beta Report\n\n" +
                $"**Plugin version:** v1.0.0.1" +
                $"**League:** {(_activeLeague ?? string.Empty)}\n" +
                $"**Price status:** {(_priceStatus ?? string.Empty)}\n" +
                $"**Minimum Divine:** {Settings.MinimumDivineValue.Value}\n" +
                $"**Minimum Chaos:** {Settings.MinimumChaosValue.Value}\n" +
                $"**Minimum listings:** {Settings.MinimumListings.Value}\n\n" +
                "### What happened?\n" +
                "<!-- Describe the problem here. -->\n\n" +
                "### What did you expect to happen?\n" +
                "<!-- Describe the expected result here. -->\n\n" +
                "### Item / situation\n" +
                "- Item name (if known):\n" +
                "- Identified or unidentified:\n" +
                "- Map / zone:\n\n" +
                "### Support bundle\n" +
                $"Latest generated bundle: `{bundleName}`\n\n" +
                "Please drag/drop that ZIP into this issue if one was created.\n\n" +
                "### Screenshot\n" +
                "<!-- Drag/drop a screenshot here if the issue is visual. -->\n\n" +
                "### Privacy note\n" +
                "Support bundles can contain local installation paths. Review the ZIP before posting publicly if that matters to you.\n";
        }

        private string GetLatestSupportBundlePath()
        {
            if (!string.IsNullOrWhiteSpace(_latestSupportBundlePath) &&
                File.Exists(_latestSupportBundlePath))
                return _latestSupportBundlePath;

            try
            {
                if (!Directory.Exists(_supportBundlesFolder))
                    return string.Empty;

                var latest = Directory
                    .EnumerateFiles(
                        _supportBundlesFolder,
                        "ItemAlert_Support_*.zip",
                        SearchOption.TopDirectoryOnly)
                    .Select(path => new FileInfo(path))
                    .OrderByDescending(x => x.LastWriteTimeUtc)
                    .FirstOrDefault();

                if (latest == null)
                    return string.Empty;

                _latestSupportBundlePath = latest.FullName;
                return latest.FullName;
            }
            catch (Exception ex)
            {
                LogBetaError("GetLatestSupportBundlePath", ex);
                return string.Empty;
            }
        }

        private void EnsurePriceScannerFiles()
        {
            try
            {
                if (!File.Exists(_alwaysTrackFile))
                {
                    File.WriteAllLines(
                        _alwaysTrackFile,
                        new[]
                        {
                            "# Name|RenderItem.ResourcePath|OptionalDivineValue",
                            "# Manual targets are always included regardless of poe.ninja price.",
                            "Angler's Plait|Art/2DItems/Rings/AnglersPlait.dds|0",
                            "Mageblood|Art/2DItems/Belts/InjectorBelt.dds|0",
                            "Headhunter|Art/2DItems/Belts/Headhunter.dds|0"
                        });
                }

                if (!File.Exists(_leagueOverrideFile))
                {
                    File.WriteAllLines(
                        _leagueOverrideFile,
                        new[]
                        {
                            "# Leave blank for the current temporary challenge league.",
                            "# Or put an exact poe.ninja league id on the next line.",
                            ""
                        });
                }
            }
            catch (Exception ex)
            {
                DebugWindow.LogError($"[ItemAlert v1.0.0.1] Target-file setup failed: {ex}");
            }
        }

        private void UpdatePriceScanner()
        {
            if (!Settings.EnablePoeNinjaPriceScanner)
                return;

            if (_priceRefreshTask != null && _priceRefreshTask.IsCompleted)
            {
                try
                {
                    var result = _priceRefreshTask.GetAwaiter().GetResult();

                    if (result.Success)
                    {
                        lock (_targetLock)
                        {
                            _priceTargets = result.Targets;
                            _priceTargetsByName = result.TargetsByName;
                        }

                        _activeLeague = result.League;
                        _priceStatus =
                            $"{result.Targets.Count} targets | >= {Settings.MinimumDivineValue.Value}d OR >= {Settings.MinimumChaosValue.Value}c";
                        SaveCurrentTargets(result);
                        AppendPriceLog(
                            $"SUCCESS league={result.League} targets={result.Targets.Count} " +
                            $"minDiv={Settings.MinimumDivineValue.Value} minChaos={Settings.MinimumChaosValue.Value} " +
                            $"minListings={Settings.MinimumListings.Value}");
                    }
                    else
                    {
                        _priceStatus = "refresh failed: " + result.Error;
                        AppendPriceLog("FAILED " + result.Error);
                    }
                }
                catch (Exception ex)
                {
                    _priceStatus = "refresh failed: " + ex.GetType().Name;
                    LogBetaError("UpdatePriceScanner", ex);
                }

                _priceRefreshTask = null;
                _nextPriceRefresh = DateTime.Now.AddMinutes(
                    Math.Max(15, Settings.PoeNinjaRefreshMinutes.Value));
            }

            if (_priceRefreshTask == null && DateTime.Now >= _nextPriceRefresh)
                StartPriceRefresh();
        }

        private void StartPriceRefresh()
        {
            if (_priceRefreshTask != null)
                return;

            _priceStatus = "refreshing...";

            var minimumDivines = Settings.MinimumDivineValue.Value;
            var minimumChaos = Settings.MinimumChaosValue.Value;
            var minimumListings = Settings.MinimumListings.Value;

            var categories = new List<string>();

            if (Settings.TrackUniqueAccessories)
                categories.Add("UniqueAccessory");
            if (Settings.TrackUniqueArmours)
                categories.Add("UniqueArmour");
            if (Settings.TrackUniqueWeapons)
                categories.Add("UniqueWeapon");
            if (Settings.TrackUniqueJewels)
                categories.Add("UniqueJewel");
            if (Settings.TrackUniqueFlasks)
                categories.Add("UniqueFlask");

            _priceRefreshTask = Task.Run(
                () => RefreshPoeNinjaTargetsAsync(
                    minimumDivines,
                    minimumChaos,
                    minimumListings,
                    categories));
        }

        // ==================================================================
        // POE.NINJA ECONOMY REFRESH
        // ==================================================================
        // Reads public poe.ninja economy JSON and builds the in-memory target
        // table. Requests are deliberately infrequent and no authentication,
        // session cookies, game packets, or private account data are accessed.
        // ==================================================================
        private async Task<PriceRefreshResult> RefreshPoeNinjaTargetsAsync(
            int minimumDivines,
            int minimumChaos,
            int minimumListings,
            List<string> categories)
        {
            var result = new PriceRefreshResult
            {
                Success = false,
                Targets = new Dictionary<string, PriceTarget>(
                    StringComparer.OrdinalIgnoreCase),
                TargetsByName = new Dictionary<string, PriceTarget>(
                    StringComparer.OrdinalIgnoreCase)
            };

            try
            {
                PoeNinjaHttp.Timeout = TimeSpan.FromSeconds(20);

                if (!PoeNinjaHttp.DefaultRequestHeaders.UserAgent.Any())
                {
                    PoeNinjaHttp.DefaultRequestHeaders.UserAgent.ParseAdd(
                        "ItemAlert/2.8 (personal ExileAPI economy scanner)");
                }

                var league = ReadLeagueOverride();

                if (string.IsNullOrWhiteSpace(league))
                {
                    var leaguesJson = await PoeNinjaHttp.GetStringAsync(
                        "https://poe.ninja/poe1/api/economy/leagues");

                    using var leaguesDoc = JsonDocument.Parse(leaguesJson);

                    if (leaguesDoc.RootElement.ValueKind != JsonValueKind.Array ||
                        leaguesDoc.RootElement.GetArrayLength() == 0)
                        throw new InvalidOperationException("No poe.ninja leagues returned.");

                    var first = leaguesDoc.RootElement[0];

                    league = first.TryGetProperty("id", out var id)
                        ? id.GetString()
                        : null;

                    if (string.IsNullOrWhiteSpace(league))
                        throw new InvalidOperationException("Current league id missing.");
                }

                result.League = league;

                foreach (var category in categories)
                {
                    var url =
                        "https://poe.ninja/poe1/api/economy/stash/current/item/overview" +
                        $"?league={Uri.EscapeDataString(league)}" +
                        $"&type={Uri.EscapeDataString(category)}";

                    var json = await PoeNinjaHttp.GetStringAsync(url);

                    using var doc = JsonDocument.Parse(json);

                    if (!doc.RootElement.TryGetProperty("lines", out var lines) ||
                        lines.ValueKind != JsonValueKind.Array)
                        continue;

                    foreach (var line in lines.EnumerateArray())
                    {
                        var name = GetJsonString(line, "name");
                        var baseType = GetJsonString(line, "baseType");
                        var variant = GetJsonString(line, "variant");
                        var icon = GetJsonString(line, "icon");
                        var divineValue = GetJsonDouble(line, "divineValue");
                        var chaosValue = GetJsonDouble(line, "chaosValue");
                        var listingCount = GetJsonInt(line, "listingCount");

                        if (string.IsNullOrWhiteSpace(name))
                            continue;

                        // Keep a representative live price by unique name even when
                        // the CDN icon URL cannot be converted to an internal resource path.
                        // The entry with the largest listing count is the best "typical"
                        // market estimate for an unidentified unique with multiple variants.
                        if (divineValue > 0)
                        {
                            var nameTarget = new PriceTarget
                            {
                                Name = name,
                                BaseType = baseType,
                                Variant = variant,
                                Category = category,
                                ResourcePath = string.Empty,
                                DivineValue = divineValue,
                                ChaosValue = chaosValue,
                                ListingCount = listingCount,
                                IconUrl = icon,
                                AlwaysTrack = false
                            };

                            if (!result.TargetsByName.TryGetValue(name, out var existingName) ||
                                listingCount > existingName.ListingCount ||
                                (listingCount == existingName.ListingCount &&
                                 divineValue > existingName.DivineValue))
                            {
                                result.TargetsByName[name] = nameTarget;
                            }
                        }

                        if (string.IsNullOrWhiteSpace(icon))
                            continue;

                        var meetsDivine =
                            minimumDivines > 0 && divineValue >= minimumDivines;

                        var meetsChaos =
                            minimumChaos > 0 && chaosValue >= minimumChaos;

                        if ((!meetsDivine && !meetsChaos) ||
                            listingCount < minimumListings)
                            continue;

                        var resourcePath = IconUrlToResourcePath(icon);

                        if (string.IsNullOrWhiteSpace(resourcePath))
                            continue;

                        var target = new PriceTarget
                        {
                            Name = name,
                            BaseType = baseType,
                            Variant = variant,
                            Category = category,
                            ResourcePath = resourcePath,
                            DivineValue = divineValue,
                            ChaosValue = chaosValue,
                            ListingCount = listingCount,
                            IconUrl = icon,
                            AlwaysTrack = false
                        };

                        if (!result.Targets.TryGetValue(resourcePath, out var existing) ||
                            target.DivineValue > existing.DivineValue)
                        {
                            result.Targets[resourcePath] = target;
                        }
                    }
                }

                LoadAlwaysTrack(result.Targets);

                // Permanent safety entries.
                AddOrKeepTarget(
                    result.Targets,
                    new PriceTarget
                    {
                        Name = "Mageblood",
                        Category = "Built-in",
                        ResourcePath = MagebloodResourcePath,
                        AlwaysTrack = true
                    });

                AddOrKeepTarget(
                    result.Targets,
                    new PriceTarget
                    {
                        Name = "Headhunter",
                        Category = "Built-in",
                        ResourcePath = HeadhunterResourcePath,
                        AlwaysTrack = true
                    });

                result.Success = true;
                return result;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                return result;
            }
        }

        private string ReadLeagueOverride()
        {
            try
            {
                if (!File.Exists(_leagueOverrideFile))
                    return string.Empty;

                foreach (var raw in File.ReadAllLines(_leagueOverrideFile))
                {
                    var line = raw.Trim();

                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                        continue;

                    return line;
                }
            }
            catch
            {
            }

            return string.Empty;
        }

        private void LoadAlwaysTrack(Dictionary<string, PriceTarget> targets)
        {
            try
            {
                if (!File.Exists(_alwaysTrackFile))
                    return;

                foreach (var raw in File.ReadAllLines(_alwaysTrackFile))
                {
                    var line = raw.Trim();

                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                        continue;

                    var parts = line.Split('|');

                    if (parts.Length < 2)
                        continue;

                    var name = parts[0].Trim();
                    var resourcePath = parts[1].Trim();

                    double.TryParse(
                        parts.Length >= 3 ? parts[2].Trim() : "0",
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var value);

                    if (string.IsNullOrWhiteSpace(name) ||
                        string.IsNullOrWhiteSpace(resourcePath))
                        continue;

                    AddOrKeepTarget(
                        targets,
                        new PriceTarget
                        {
                            Name = name,
                            Category = "AlwaysTrack",
                            ResourcePath = resourcePath,
                            DivineValue = value,
                            ListingCount = 0,
                            AlwaysTrack = true
                        });
                }
            }
            catch (Exception ex)
            {
                DebugWindow.LogError(
                    $"[ItemAlert v1.0.0.1] AlwaysTrack load failed: {ex}");
            }
        }

        private static void AddOrKeepTarget(
            Dictionary<string, PriceTarget> targets,
            PriceTarget target)
        {
            if (targets.TryGetValue(target.ResourcePath, out var existing))
            {
                if (target.AlwaysTrack)
                    existing.AlwaysTrack = true;

                if (target.DivineValue > existing.DivineValue)
                    existing.DivineValue = target.DivineValue;

                return;
            }

            targets[target.ResourcePath] = target;
        }

        private void SaveCurrentTargets(PriceRefreshResult result)
        {
            try
            {
                var rows = new List<string>
                {
                    "Name,BaseType,Variant,Category,DivineValue,ChaosValue,ListingCount,AlwaysTrack,ResourcePath,IconUrl"
                };

                foreach (var target in result.Targets.Values
                             .OrderByDescending(x => x.DivineValue)
                             .ThenBy(x => x.Name))
                {
                    rows.Add(string.Join(",",
                        Csv(target.Name),
                        Csv(target.BaseType),
                        Csv(target.Variant),
                        Csv(target.Category),
                        Csv(target.DivineValue.ToString(
                            "0.####",
                            CultureInfo.InvariantCulture)),
                        Csv(target.ChaosValue.ToString(
                            "0.####",
                            CultureInfo.InvariantCulture)),
                        Csv(target.ListingCount.ToString()),
                        Csv(target.AlwaysTrack.ToString()),
                        Csv(target.ResourcePath),
                        Csv(target.IconUrl)));
                }

                File.WriteAllLines(_targetsFile, rows);
            }
            catch (Exception ex)
            {
                DebugWindow.LogError(
                    $"[ItemAlert v1.0.0.1] Could not save Targets_Current.csv: {ex}");
            }
        }

        // ==================================================================
        // ARTWORK PATH NORMALIZATION
        // ==================================================================
        // Converts the public CDN inventory icon path into the corresponding
        // internal Art/2DItems/... .dds path exposed by ExileAPI's RenderItem
        // component. If no Art/... path is present, the item is skipped rather
        // than guessed.
        // ==================================================================
        private static string IconUrlToResourcePath(string iconUrl)
        {
            try
            {
                var decoded = Uri.UnescapeDataString(iconUrl);
                var artIndex = decoded.IndexOf(
                    "/Art/",
                    StringComparison.OrdinalIgnoreCase);

                if (artIndex < 0)
                    return string.Empty;

                var end = decoded.IndexOf('?', artIndex);

                var path = end >= 0
                    ? decoded.Substring(artIndex + 1, end - artIndex - 1)
                    : decoded.Substring(artIndex + 1);

                // poe.ninja/web.poecdn serves PNG/WebP versions of the same
                // inventory assets RenderItem exposes internally as DDS paths.
                if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                {
                    path = Path.ChangeExtension(path, ".dds")
                        .Replace('\\', '/');
                }

                return path;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string GetJsonString(JsonElement line, string property)
        {
            if (!line.TryGetProperty(property, out var value) ||
                value.ValueKind == JsonValueKind.Null)
                return string.Empty;

            return value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : value.ToString();
        }

        private static double GetJsonDouble(JsonElement line, string property)
        {
            if (!line.TryGetProperty(property, out var value))
                return 0;

            if (value.ValueKind == JsonValueKind.Number &&
                value.TryGetDouble(out var number))
                return number;

            return 0;
        }

        private static int GetJsonInt(JsonElement line, string property)
        {
            if (!line.TryGetProperty(property, out var value))
                return 0;

            if (value.ValueKind == JsonValueKind.Number &&
                value.TryGetInt32(out var number))
                return number;

            return 0;
        }

        // ==================================================================
        // OPTIONAL DIAGNOSTIC COMPONENT SCANNER
        // ==================================================================
        // Disabled by default. Used only for research/debugging when an item
        // cannot be matched. It reads item-component data already exposed to
        // the plugin through ExileAPI and writes local diagnostic files.
        // Normal ItemAlert operation does not require this scanner.
        // ==================================================================
        private void WriteComponentCapture(CaptureSnapshot s, Entity item)
        {
            try
            {
                var captureFolder = Path.Combine(
                    _componentCapturesFolder,
                    $"C{s.CaptureId:0000}_{s.Time:HHmmssfff}_{(s.Identified ? "ID" : "UNID")}");

                Directory.CreateDirectory(captureFolder);

                var summary = new List<string>
                {
                    "ITEM ALERT v1.0.0.1 - FULL ITEM COMPONENT SCAN",
                    "================================================",
                    $"CaptureId=C{s.CaptureId:0000}",
                    $"Time={s.Time:yyyy-MM-dd HH:mm:ss.fff}",
                    $"Identified={s.Identified}",
                    $"UniqueName={s.UniqueName}",
                    $"ItemPath={s.ItemPath}",
                    $"ItemLevel={s.ItemLevel}",
                    $"RequiredLevel={s.RequiredLevel}",
                    $"ImplicitSummary={s.ImplicitSummary}",
                    $"ItemAddress=0x{s.ItemAddress:X}",
                    $"RenderItemResourcePath={s.RenderItemResourcePath}",
                    $"DetectedTarget={DetectTarget(s.RenderItemResourcePath)}",
                    "",
                    "COMPONENTS",
                    "----------"
                };

                var components = item.CacheComp;

                if (components == null || components.Count == 0)
                {
                    summary.Add("<no components>");
                    File.WriteAllLines(Path.Combine(captureFolder, "summary.txt"), summary);
                    return;
                }

                foreach (var pair in components.OrderBy(x => x.Key))
                {
                    var componentName = pair.Key ?? "Unknown";
                    var address = pair.Value;

                    summary.Add($"{componentName}=0x{address:X}");

                    if (address <= 0)
                        continue;

                    byte[] raw = Array.Empty<byte>();

                    try
                    {
                        raw = GameController.Memory.ReadBytes(
                            address,
                            Settings.ComponentDumpBytes.Value) ?? Array.Empty<byte>();
                    }
                    catch
                    {
                        continue;
                    }

                    var safeName = SanitizeFileName(componentName);
                    var binPath = Path.Combine(captureFolder, safeName + ".bin");
                    var txtPath = Path.Combine(captureFolder, safeName + ".txt");

                    File.WriteAllBytes(binPath, raw);

                    var directStrings = ExtractPrintableStrings(raw);
                    var pointerStrings = ProbeComponentPointerStrings(raw);
                    var hits = FindArtworkKeywordHits(
                        directStrings.Concat(pointerStrings).Distinct().ToList());

                    var detail = new List<string>
                    {
                        $"CaptureId=C{s.CaptureId:0000}",
                        $"Component={componentName}",
                        $"Address=0x{address:X}",
                        $"BytesRead={raw.Length}",
                        "",
                        "HEX DUMP",
                        "--------",
                        FormatHexDump(raw),
                        "",
                        "DIRECT PRINTABLE STRINGS",
                        "------------------------",
                        directStrings.Count == 0
                            ? "<none>"
                            : string.Join(Environment.NewLine, directStrings.Select(x => "  " + x)),
                        "",
                        "POINTER-REFERENCED STRINGS",
                        "--------------------------",
                        pointerStrings.Count == 0
                            ? "<none>"
                            : string.Join(Environment.NewLine, pointerStrings.Select(x => "  " + x)),
                        "",
                        "ARTWORK / IDENTITY KEYWORD HITS",
                        "-------------------------------",
                        hits.Count == 0
                            ? "<none>"
                            : string.Join(Environment.NewLine, hits.Select(x => "  " + x))
                    };

                    if (string.Equals(componentName, "RenderItem", StringComparison.OrdinalIgnoreCase))
                    {
                        detail.Insert(4, $"RenderItem.ResourcePath={s.RenderItemResourcePath}");
                    }

                    File.WriteAllLines(txtPath, detail);
                }

                File.WriteAllLines(
                    Path.Combine(captureFolder, "summary.txt"),
                    summary);
            }
            catch (Exception ex)
            {
                DebugWindow.LogError($"[ItemAlert v1.0.0.1] Component scan failed: {ex}");
            }
        }

        private List<string> ProbeComponentPointerStrings(byte[] raw)
        {
            var results = new List<string>();

            if (raw == null || raw.Length < 8)
                return results;

            var seen = new HashSet<long>();

            for (var offset = 0; offset + 8 <= raw.Length; offset += 8)
            {
                var ptr = BitConverter.ToInt64(raw, offset);

                if (ptr < 0x10000 || ptr > 0x00007FFFFFFFFFFF)
                    continue;

                if (!seen.Add(ptr))
                    continue;

                try
                {
                    var ascii = GameController.Memory.ReadString(
                        ptr,
                        Settings.ComponentPointerStringBytes.Value,
                        true);

                    if (LooksUseful(ascii))
                        results.Add($"+0x{offset:X3} -> 0x{ptr:X}: A '{Clean(ascii)}'");
                }
                catch
                {
                }

                try
                {
                    var unicode = GameController.Memory.ReadStringU(
                        ptr,
                        Settings.ComponentPointerStringBytes.Value,
                        true);

                    if (LooksUseful(unicode))
                        results.Add($"+0x{offset:X3} -> 0x{ptr:X}: U '{Clean(unicode)}'");
                }
                catch
                {
                }

                if (results.Count >= Settings.MaxComponentPointerStrings.Value)
                    break;
            }

            return results;
        }

        private static List<string> ExtractPrintableStrings(byte[] data)
        {
            var results = new List<string>();

            if (data == null || data.Length == 0)
                return results;

            var ascii = new StringBuilder();

            void FlushAscii()
            {
                if (ascii.Length >= 4)
                {
                    var value = ascii.ToString();

                    if (!results.Contains(value))
                        results.Add(value);
                }

                ascii.Clear();
            }

            foreach (var b in data)
            {
                if (b >= 32 && b <= 126)
                    ascii.Append((char)b);
                else
                    FlushAscii();

                if (results.Count >= 200)
                    break;
            }

            FlushAscii();

            for (var start = 0; start + 7 < data.Length && results.Count < 300; start += 2)
            {
                var sb = new StringBuilder();
                var i = start;

                while (i + 1 < data.Length)
                {
                    var lo = data[i];
                    var hi = data[i + 1];

                    if (hi == 0 && lo >= 32 && lo <= 126)
                    {
                        sb.Append((char)lo);
                        i += 2;
                    }
                    else
                    {
                        break;
                    }
                }

                if (sb.Length >= 4)
                {
                    var value = sb.ToString();

                    if (!results.Contains(value))
                        results.Add(value);
                }
            }

            return results;
        }

        private static List<string> FindArtworkKeywordHits(List<string> strings)
        {
            if (strings == null || strings.Count == 0)
                return new List<string>();

            var keywords = new[]
            {
                "mageblood",
                "headhunter",
                "art/",
                "2ditems",
                ".dds",
                ".png",
                ".ao",
                "texture",
                "resource",
                "render",
                "inventory",
                "icon",
                "belt",
                "unique",
                "flask",
                "utility",
                "headhunter",
                "mageblood"
            };

            return strings
                .Where(s => keywords.Any(k =>
                    s.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0))
                .Distinct()
                .ToList();
        }

        private void AppendSpamCsv(CaptureSnapshot s)
        {
            try
            {
                var row = string.Join(",",
                    Csv($"C{s.CaptureId:0000}"),
                    Csv(s.Time.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)),
                    Csv(s.Identified.ToString()),
                    Csv(s.UniqueName),
                    Csv(s.ItemLevel),
                    Csv(s.RequiredLevel),
                    Csv(s.ImplicitSummary),
                    Csv(s.RenderItemResourcePath),
                    Csv(DetectTarget(s.RenderItemResourcePath)),
                    Csv(s.ItemPath),
                    Csv($"0x{s.GroundAddress:X}"),
                    Csv($"0x{s.ItemAddress:X}"),
                    Csv($"0x{s.ModsAddress:X}"),
                    Csv(s.EntityId.ToString()),
                    Csv(s.ModsHash),
                    Csv(s.UniqueNameField),
                    Csv(s.ImplicitArray),
                    Csv(s.ExplicitArray));

                File.AppendAllText(_spamPath, row + Environment.NewLine);
            }
            catch
            {
            }
        }

        private static string BuildDiff(byte[] before, byte[] after)
        {
            if (before == null || after == null || before.Length == 0 || after.Length == 0)
                return "<missing raw data>";

            var max = Math.Min(before.Length, after.Length);
            var sb = new StringBuilder();
            var changes = 0;

            for (var i = 0; i < max; i++)
            {
                if (before[i] == after[i])
                    continue;

                sb.AppendLine(
                    $"+0x{i:X3}: {before[i]:X2} -> {after[i]:X2}");

                changes++;
            }

            if (before.Length != after.Length)
                sb.AppendLine($"Length changed: {before.Length} -> {after.Length}");

            if (changes == 0)
                return "<no byte changes>";

            return $"Changed bytes: {changes}{Environment.NewLine}{sb}".TrimEnd();
        }

        private static string FormatPointerLikeQwords(byte[] data)
        {
            if (data == null || data.Length < 8)
                return "<none>";

            var sb = new StringBuilder();

            for (var i = 0; i + 8 <= data.Length; i += 8)
            {
                var value = BitConverter.ToInt64(data, i);

                if (value >= 0x10000 && value <= 0x00007FFFFFFFFFFF)
                    sb.AppendLine($"+0x{i:X3}: 0x{value:X16}");
            }

            return sb.Length == 0 ? "<none>" : sb.ToString().TrimEnd();
        }

        private static string GetImplicitSummary(Mods mods)
        {
            try
            {
                var values = mods.ItemMods;

                if (values == null || values.Count == 0)
                    return "<none>";

                // On unidentified unique belts the visible item-mod collection is normally
                // just the base implicit. Keeping the exact ToString() value gives us a
                // useful pairing key after the same item is identified.
                return string.Join(" | ", values.Take(2).Select(x => x?.ToString() ?? "<null>"));
            }
            catch
            {
                return "<error>";
            }
        }

        private string ProbePointerStrings(byte[] raw)
        {
            if (raw == null || raw.Length < 8)
                return "  <none>";

            var output = new List<string>();
            var seenPointers = new HashSet<long>();

            for (var offset = 0; offset + 8 <= raw.Length; offset += 8)
            {
                long ptr;

                try
                {
                    ptr = BitConverter.ToInt64(raw, offset);
                }
                catch
                {
                    continue;
                }

                if (ptr < 0x10000 || ptr > 0x00007FFFFFFFFFFF)
                    continue;

                if (!seenPointers.Add(ptr))
                    continue;

                var unicode = TryReadUnicode(ptr);
                if (LooksUseful(unicode))
                    output.Add($"  +0x{offset:X3} -> 0x{ptr:X} U: '{Clean(unicode)}'");

                var ascii = TryReadAscii(ptr);
                if (LooksUseful(ascii) && !string.Equals(ascii, unicode, StringComparison.Ordinal))
                    output.Add($"  +0x{offset:X3} -> 0x{ptr:X} A: '{Clean(ascii)}'");

                if (output.Count >= Settings.MaxPointerStrings.Value)
                    break;
            }

            return output.Count == 0
                ? "  <no readable string pointers found>"
                : string.Join(Environment.NewLine, output);
        }

        private string TryReadUnicode(long ptr)
        {
            try
            {
                return GameController.Memory.ReadStringU(ptr, 128, true);
            }
            catch
            {
                return string.Empty;
            }
        }

        private string TryReadAscii(long ptr)
        {
            try
            {
                return GameController.Memory.ReadString(ptr, 128, true);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool LooksUseful(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var clean = Clean(value);

            if (clean.Length < 3 || clean.Length > 128)
                return false;

            var printable = clean.Count(c => !char.IsControl(c));

            return printable >= clean.Length * 0.85;
        }

        private static string Clean(string value)
        {
            if (value == null)
                return string.Empty;

            var s = value.Replace("\0", string.Empty)
                         .Replace("\r", " ")
                         .Replace("\n", " ")
                         .Trim();

            return s.Length > 128 ? s.Substring(0, 128) : s;
        }

        private static string FormatHexDump(byte[] data)
        {
            if (data == null || data.Length == 0)
                return "  <no bytes read>";

            var sb = new StringBuilder();

            for (var i = 0; i < data.Length; i += 16)
            {
                sb.Append($"  +0x{i:X3}  ");

                var count = Math.Min(16, data.Length - i);

                for (var j = 0; j < 16; j++)
                {
                    if (j < count)
                        sb.Append(data[i + j].ToString("X2")).Append(' ');
                    else
                        sb.Append("   ");
                }

                sb.Append(" |");

                for (var j = 0; j < count; j++)
                {
                    var b = data[i + j];
                    sb.Append(b >= 32 && b <= 126 ? (char)b : '.');
                }

                sb.AppendLine("|");
            }

            return sb.ToString().TrimEnd();
        }

        private static string FormatQwords(byte[] data)
        {
            if (data == null || data.Length < 8)
                return "  <none>";

            var sb = new StringBuilder();

            for (var i = 0; i + 8 <= data.Length; i += 8)
            {
                var value = BitConverter.ToInt64(data, i);
                sb.AppendLine($"  +0x{i:X3}: 0x{value:X16}");
            }

            return sb.ToString().TrimEnd();
        }

        private static string Safe(Func<string> read)
        {
            try
            {
                return read()?.Replace("\0", string.Empty) ?? "<null>";
            }
            catch (Exception ex)
            {
                return $"<error: {ex.GetType().Name}>";
            }
        }

        private static string SafeObject<T>(Func<T> read)
        {
            try
            {
                var value = read();
                return value?.ToString() ?? "<null>";
            }
            catch (Exception ex)
            {
                return $"<error: {ex.GetType().Name}>";
            }
        }

        private static string SafeList(Func<List<string>> read)
        {
            try
            {
                var values = read();

                if (values == null || values.Count == 0)
                    return "  <none>";

                return string.Join(
                    Environment.NewLine,
                    values.Select(x => "  " + (x ?? "<null>")));
            }
            catch (Exception ex)
            {
                return $"  <error: {ex.GetType().Name}>";
            }
        }

        private static string SafeItemMods(Mods mods)
        {
            try
            {
                var values = mods.ItemMods;

                if (values == null || values.Count == 0)
                    return "  <none>";

                return string.Join(
                    Environment.NewLine,
                    values.Select(x => "  " + (x?.ToString() ?? "<null>")));
            }
            catch (Exception ex)
            {
                return $"  <error: {ex.GetType().Name}>";
            }
        }

        private static string Csv(string value)
        {
            value ??= string.Empty;

            if (value.Contains("\""))
                value = value.Replace("\"", "\"\"");

            return "\"" + value + "\"";
        }

        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "Unknown";

            var invalid = Path.GetInvalidFileNameChars();
            var result = new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray());

            return string.IsNullOrWhiteSpace(result) ? "Unknown" : result;
        }



        private sealed class PriceTarget
        {
            public string Name { get; set; }
            public string BaseType { get; set; }
            public string Variant { get; set; }
            public string Category { get; set; }
            public string ResourcePath { get; set; }
            public double DivineValue { get; set; }
            public double ChaosValue { get; set; }
            public int ListingCount { get; set; }
            public string IconUrl { get; set; }
            public bool AlwaysTrack { get; set; }
        }

        private sealed class PriceRefreshResult
        {
            public bool Success { get; set; }
            public string League { get; set; }
            public string Error { get; set; }
            public Dictionary<string, PriceTarget> Targets { get; set; }
            public Dictionary<string, PriceTarget> TargetsByName { get; set; }
        }

        private sealed class AlertLayout
        {
            public TargetAlert Alert { get; set; }
            public string Line1 { get; set; }
            public string Line2 { get; set; }
            public string CurrencyLabel { get; set; }
            public System.Numerics.Vector2 Size1 { get; set; }
            public System.Numerics.Vector2 Size2 { get; set; }
            public float CombinedValueWidth { get; set; }
            public float Width { get; set; }
            public float Height { get; set; }
            public bool HasPrice { get; set; }
        }

        private sealed class TargetAlert
        {
            public long ItemAddress { get; set; }
            public Entity GroundEntity { get; set; }
            public string TargetName { get; set; }
            public string ResourcePath { get; set; }
            public double DivineValue { get; set; }
            public double ChaosValue { get; set; }
            public string Category { get; set; }
            public string Variant { get; set; }
            public bool PriceIsEstimate { get; set; }

            // FirstSeen keeps simultaneous alerts in a stable slot/color. LastSeen is
            // retained for diagnostics; alert lifetime now follows the ground Entity.
            public DateTime FirstSeen { get; set; }
            public DateTime LastSeen { get; set; }
        }

        private sealed class CaptureSnapshot
        {
            public int CaptureId { get; set; }
            public DateTime Time { get; set; }
            public bool Identified { get; set; }
            public string UniqueName { get; set; }
            public string ItemLevel { get; set; }
            public string RequiredLevel { get; set; }
            public string ImplicitSummary { get; set; }
            public string RenderItemResourcePath { get; set; }
            public string ItemPath { get; set; }
            public long GroundAddress { get; set; }
            public long ItemAddress { get; set; }
            public long ModsAddress { get; set; }
            public uint EntityId { get; set; }
            public string ModsHash { get; set; }
            public string UniqueNameField { get; set; }
            public string ImplicitArray { get; set; }
            public string ExplicitArray { get; set; }
            public byte[] Raw { get; set; }
        }
    }
}

using ExileCore.Shared.Interfaces;
using ExileCore.Shared.Attributes;
using ExileCore.Shared.Nodes;
using SharpDX;

namespace ItemAlert
{
    public class ItemAlertSettings : ISettings
    {
        public ToggleNode Enable { get; set; } =
            new ToggleNode(true);

        // Legacy diagnostic gates kept for compatibility, hidden from normal users.
        [IgnoreMenu]
        public ToggleNode OnlyUnique { get; set; } =
            new ToggleNode(true);

        [IgnoreMenu]
        public ToggleNode OnlyBelts { get; set; } =
            new ToggleNode(false);

        public ToggleNode EnableTargetDetector { get; set; } =
            new ToggleNode(true);

        [Menu("Alert X Offset %",
            "0 = center. Negative moves left; positive moves right. Scales with the current game-window width.")]
        public RangeNode<int> AlertOffsetXPercent { get; set; } =
            new RangeNode<int>(0, -100, 100);

        [Menu("Alert Y Offset %",
            "0 = center. Negative moves up; positive moves down. Scales with the current game-window height.")]
        public RangeNode<int> AlertOffsetYPercent { get; set; } =
            new RangeNode<int>(0, -100, 100);

        [Menu("Alert Font Size")]
        public RangeNode<int> AlertFontSize { get; set; } =
            new RangeNode<int>(16, 10, 40);

        [Menu("Maximum Simultaneous Alerts",
            "Maximum number of high-value alerts shown at the same time.")]
        public RangeNode<int> MaximumSimultaneousAlerts { get; set; } =
            new RangeNode<int>(6, 1, 10);

        [Menu("Alert Stack Spacing",
            "Vertical spacing in pixels between simultaneous alert boxes.")]
        public RangeNode<int> AlertStackSpacing { get; set; } =
            new RangeNode<int>(10, 0, 60);

        [Menu("Alert Slot 1 Color")]
        public ColorNode AlertSlot1Color { get; set; } =
            new ColorNode(new Color(235, 65, 255, 255));

        [Menu("Alert Slot 2 Color")]
        public ColorNode AlertSlot2Color { get; set; } =
            new ColorNode(new Color(55, 210, 255, 255));

        [Menu("Alert Slot 3 Color")]
        public ColorNode AlertSlot3Color { get; set; } =
            new ColorNode(new Color(100, 255, 120, 255));

        [Menu("Alert Slot 4 Color")]
        public ColorNode AlertSlot4Color { get; set; } =
            new ColorNode(Color.White);

        [Menu("Alert Slot 5 Color")]
        public ColorNode AlertSlot5Color { get; set; } =
            new ColorNode(new Color(255, 225, 60, 255));

        [Menu("Alert Slot 6 Color")]
        public ColorNode AlertSlot6Color { get; set; } =
            new ColorNode(new Color(165, 110, 255, 255));

        [Menu("Alert Rounded Corners")]
        public RangeNode<int> AlertCornerRadius { get; set; } =
            new RangeNode<int>(0, 0, 24);

        [Menu("Alert Border Thickness")]
        public RangeNode<int> AlertBorderThickness { get; set; } =
            new RangeNode<int>(3, 1, 8);

        [Menu("Highlight Ground Item Label",
            "Draws a colored border around the exact Path of Exile ground-item label belonging to the detected item.")]
        public ToggleNode HighlightGroundItemLabel { get; set; } =
            new ToggleNode(true);

        [Menu("Ground Highlight Padding",
            "Extra pixels added around the rendered ground-item label.")]
        public RangeNode<int> GroundHighlightPadding { get; set; } =
            new RangeNode<int>(4, 0, 30);

        [Menu("Ground Highlight Border Thickness")]
        public RangeNode<int> GroundHighlightBorderThickness { get; set; } =
            new RangeNode<int>(4, 1, 12);

        [Menu("Ground Highlight Rounded Corners")]
        public RangeNode<int> GroundHighlightCornerRadius { get; set; } =
            new RangeNode<int>(2, 0, 20);


        [Menu(
            "Show Connection Arrows",
            "Draws a color-matched arrow from each alert toast to that exact item's ground-label highlight.")]
        public ToggleNode ShowConnectionArrows { get; set; } =
            new ToggleNode(true);

        [Menu(
            "Connection Arrow Thickness",
            "Thickness of the line connecting an alert toast to its matched ground item.")]
        public RangeNode<int> ConnectionArrowThickness { get; set; } =
            new RangeNode<int>(3, 1, 8);

        [Menu("Alert Background Color")]
        public ColorNode AlertBackgroundColor { get; set; } =
            new ColorNode(new Color(0, 0, 0, 220));

        [Menu("Alert Value Color")]
        public ColorNode AlertValueColor { get; set; } =
            new ColorNode(Color.White);

        [Menu("Alert Currency Color")]
        public ColorNode AlertCurrencyColor { get; set; } =
            new ColorNode(Color.Gold);

        public ToggleNode EnablePoeNinjaPriceScanner { get; set; } =
            new ToggleNode(true);

        [Menu(
            "poe.ninja League",
            "Populated from poe.ninja's live Path of Exile 1 economy league list. Auto uses poe.ninja's current temporary challenge league.")]
        public ListNode PoeNinjaLeague { get; set; } =
            new ListNode { Value = "Auto (Current Challenge League)" };

        [IgnoreMenu]
        public ButtonNode CreateSupportBundle { get; set; } =
            new ButtonNode();

        [IgnoreMenu]
        public ButtonNode OpenSupportIssue { get; set; } =
            new ButtonNode();

        public RangeNode<int> MinimumDivineValue { get; set; } =
            new RangeNode<int>(10, 0, 2000);

        public RangeNode<int> MinimumChaosValue { get; set; } =
            new RangeNode<int>(500, 0, 50000);

        public RangeNode<int> MinimumListings { get; set; } =
            new RangeNode<int>(5, 0, 1000);

        public RangeNode<int> PoeNinjaRefreshMinutes { get; set; } =
            new RangeNode<int>(30, 15, 240);

        public ToggleNode TrackUniqueAccessories { get; set; } =
            new ToggleNode(true);

        public ToggleNode TrackUniqueArmours { get; set; } =
            new ToggleNode(true);

        public ToggleNode TrackUniqueWeapons { get; set; } =
            new ToggleNode(true);

        public ToggleNode TrackUniqueJewels { get; set; } =
            new ToggleNode(true);

        public ToggleNode TrackUniqueFlasks { get; set; } =
            new ToggleNode(false);

        // Hidden diagnostic values are rendered manually in the collapsed
        // Advanced / Diagnostic Scanner section.
        [IgnoreMenu]
        public RangeNode<int> RecaptureCooldownMs { get; set; } =
            new RangeNode<int>(750, 100, 10000);

        [IgnoreMenu]
        public RangeNode<int> RawDumpBytes { get; set; } =
            new RangeNode<int>(1280, 256, 4096);

        [IgnoreMenu]
        public ToggleNode EnableComponentScanner { get; set; } =
            new ToggleNode(false);

        [IgnoreMenu]
        public RangeNode<int> ComponentDumpBytes { get; set; } =
            new RangeNode<int>(1024, 256, 4096);

        [IgnoreMenu]
        public RangeNode<int> ComponentPointerStringBytes { get; set; } =
            new RangeNode<int>(192, 64, 512);

        [IgnoreMenu]
        public RangeNode<int> MaxComponentPointerStrings { get; set; } =
            new RangeNode<int>(80, 10, 300);

        [IgnoreMenu]
        public RangeNode<int> MaxPointerStrings { get; set; } =
            new RangeNode<int>(40, 1, 200);

        [IgnoreMenu]
        public RangeNode<int> MaxItemsPerTick { get; set; } =
            new RangeNode<int>(50, 1, 500);

        [IgnoreMenu]
        public RangeNode<int> PairWindowMinutes { get; set; } =
            new RangeNode<int>(10, 1, 60);

        [IgnoreMenu]
        public RangeNode<int> MaxPendingUnidentified { get; set; } =
            new RangeNode<int>(100, 5, 1000);
    }
}

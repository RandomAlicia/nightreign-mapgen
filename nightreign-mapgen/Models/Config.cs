using System.Collections.Generic;

namespace NightReign.MapGen
{
    public sealed class IconSettings
    {
        public double? WidthPercent { get; set; } = 0.18;
        public string? Anchor       { get; set; } = "bottom-left";
        public int?    MarginX      { get; set; } = 0;
        public int?    MarginY      { get; set; } = 0;
        public int?    MaxWidthPx   { get; set; } = null;
        public int?    MaxHeightPx  { get; set; } = null;

        public int? FixedWidthPx    { get; set; } = 356;
        public int? FixedHeightPx   { get; set; } = 356;
        public bool PreserveAspect  { get; set; } = true;
        public bool FitInsideBox    { get; set; } = true;
    }

    public sealed class SizeBox
    {
        public int WidthPx { get; set; }
        public int HeightPx { get; set; }
    }

    public sealed class MajorBaseConfig
    {
        public SizeBox? Camp { get; set; }
        public SizeBox? Fort { get; set; }
        public SizeBox? Great_Church { get; set; }
        public SizeBox? Ruins { get; set; }
    }

    public sealed class MinorBaseConfig
    {
        public SizeBox? Church { get; set; }
        public SizeBox? Small_Camp { get; set; }
        public SizeBox? Sorcerers_Rise { get; set; }
        public SizeBox? Township { get; set; }
    }

    public sealed class EventConfig
    {
        public SizeBox? Scale_Bearing_Merchant { get; set; }
        public SizeBox? Meteor_Strike { get; set; }
        public SizeBox? Walking_Mausoleum { get; set; }
    }

    public sealed class EvergaolConfig
    {
        public SizeBox? Default { get; set; }
    }

    public sealed class FieldBossConfig
    {
        public SizeBox? Arena_Boss { get; set; }
        public SizeBox? Field_Boss { get; set; }
        public SizeBox? Strong_Field_Boss { get; set; }
        public SizeBox? Castle { get; set; }
    }

    public sealed class NightBossConfig
    {
        public SizeBox? Default { get; set; }
    }

    public sealed class AppConfig
    {
        public string? BackgroundPath   { get; set; }
        public string? OutputFolder     { get; set; }
        public string? SummaryPath      { get; set; }
        public string? NightlordFolder  { get; set; }
        public string? MapRawFolder     { get; set; }
        public string? TreasureFolder   { get; set; }
        public string? IndexPath        { get; set; }

        public IconSettings? NightlordIcon { get; set; }
        public MajorBaseConfig? MajorBase { get; set; }
        public MinorBaseConfig? MinorBase { get; set; }
        public EventConfig? Event { get; set; }
        public EvergaolConfig? Evergaol { get; set; }
        public FieldBossConfig? FieldBoss { get; set; }
        public NightBossConfig? NightBoss { get; set; }

        public Dictionary<string,string>? IconOverrides { get; set; }
    }
}

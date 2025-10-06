using System;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Linq;
using System.Collections.Generic;
using ImageMagick;

namespace NightReign.MapGen
{
    // ===================== Config Models =====================

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

    // ===================== Summary Models =====================

    public sealed class SummaryRoot
    {
        public SummaryPattern[]? Patterns { get; set; }
    }

    public sealed class SummaryPattern
    {
        public string? Id             { get; set; }
        public string? Nightlord      { get; set; }
        public string? Special        { get; set; }
        public string? Treasure       { get; set; }
        public string? Frenzy_Tower   { get; set; }   // "North" / "South"
        public string? Rot_Blessing   { get; set; }   // "west"/"northwest"/"southwest"
        public string? Spawn_Point_Id { get; set; }   // maps from "spawn_point_id"
    }

    // ===================== Pattern & Index Models =====================

    public sealed class PatternDoc
    {
        public int? patternId { get; set; }
        public string? Nightlord { get; set; }
        public string? SpecialEvent { get; set; }
        public List<Poi>? pois { get; set; }
    }

    public sealed class Poi
    {
        public double x { get; set; }
        public double z { get; set; }
        public string? name { get; set; }
        public int? dupCount { get; set; }
        public List<string>? names { get; set; }
    }

    public sealed class IndexEntry
    {
        public string? name { get; set; }
        public string? category { get; set; }
        public string? icon { get; set; }
        public string? cid { get; set; }
        public string? id { get; set; }
    }

    // ===================== Program =====================

    public static class Program
    {
        public static int Main(string[] args)
        {
            try
            {
                if (args.Length < 1)
                {
                    Console.WriteLine("Usage: dotnet run -- <path-to-pattern_xxx.json>");
                    return 1;
                }

                var cfg = LoadConfig("appsettings.json");
                var cwd = Directory.GetCurrentDirectory();

                var backgroundPath = ResolvePath(cfg.BackgroundPath ?? throw new InvalidOperationException("BackgroundPath missing."), cwd);
                var outputFolder   = ResolvePath(cfg.OutputFolder   ?? "output", cwd, allowCreateDir: true);

                var patternPath = args[0];
                var id = GetPatternId(patternPath);
                if (string.IsNullOrWhiteSpace(id))
                    throw new InvalidOperationException($"Could not determine a pattern id from: {patternPath}");
                Console.WriteLine($"[Info] Pattern id = {id}");

                var summary = LoadSummary(ResolvePath(cfg.SummaryPath ?? throw new InvalidOperationException("SummaryPath missing."), cwd));
                var patSummary = summary.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase))
                          ?? throw new InvalidOperationException($"Pattern '{id}' not found in summary.json.");

                var patternDoc = LoadPattern(patternPath);

                var indexPath = ResolvePath(cfg.IndexPath ?? "../data/index.json", cwd);
                var indexLookup = LoadIndex(indexPath);
                Console.WriteLine($"[Index] Entries loaded: {indexLookup.Count}");

                using var background = new MagickImage(backgroundPath); // 1) Background

                // 2) Nightlord backdrop (full canvas)
                if (!string.IsNullOrWhiteSpace(cfg.NightlordFolder) && !string.IsNullOrWhiteSpace(patSummary.Nightlord))
                {
                    var nightlordPath = Path.Combine(ResolvePath(cfg.NightlordFolder!, cwd), $"backdrop_{patSummary.Nightlord}.png");
                    CompositeFullCanvasIfExists(background, nightlordPath);
                }

                // 3) Map raw overlay (full canvas)
                if (!string.IsNullOrWhiteSpace(cfg.MapRawFolder) && !string.IsNullOrWhiteSpace(patSummary.Special))
                {
                    var rawName = MapRawFilenameForSpecial(patSummary.Special!);
                    if (rawName is not null)
                    {
                        var rawPath = Path.Combine(ResolvePath(cfg.MapRawFolder!, cwd), rawName);
                        CompositeFullCanvasIfExists(background, rawPath);
                    }
                }

                // 3a) Shifting earth overlay (full canvas) based on Special
                if (!string.IsNullOrWhiteSpace(patSummary.Special))
                {
                    var overName = MapShiftingOverlayForSpecial(patSummary.Special!);
                    if (overName is not null)
                    {
                        var overFolder = ResolvePath("../assets/map/shifting_earth", cwd);
                        var overPath = Path.Combine(overFolder, overName);
                        CompositeFullCanvasIfExists(background, overPath);
                    }
                }

                // 3a-ii) Final event overlays driven by summary.json
                // Frenzy Tower
                {
                    var frenzyFile = MapFrenzyOverlay(patSummary.Frenzy_Tower);
                    if (frenzyFile != null)
                    {
                        var evtFolder = ResolvePath("../assets/map/event", cwd);
                        var fpath = Path.Combine(evtFolder, frenzyFile);
                        CompositeFullCanvasIfExists(background, fpath);
                    }
                }
                // Rot Blessing
                {
                    var blessFile = MapRotBlessingOverlay(patSummary.Rot_Blessing);
                    if (blessFile != null)
                    {
                        var evtFolder = ResolvePath("../assets/map/event", cwd);
                        var bpath = Path.Combine(evtFolder, blessFile);
                        CompositeFullCanvasIfExists(background, bpath);
                    }
                }

                // 3b) Castle overlay (no resize) if Special in {0,1,2,3}
                if (!string.IsNullOrWhiteSpace(patSummary.Special))
                {
                    if (int.TryParse(patSummary.Special, out var sVal) && (sVal == 0 || sVal == 1 || sVal == 2 || sVal == 3))
                    {
                        var castlePath = ResolvePath("../assets/map/castle.png", cwd);
                        CompositeNoResizeIfExists(background, castlePath);
                    }
                }

                // 4) Treasure overlay (full canvas): file = treasure_{T*10 + S:D5}.png
                if (!string.IsNullOrWhiteSpace(cfg.TreasureFolder) && !string.IsNullOrWhiteSpace(patSummary.Treasure) && !string.IsNullOrWhiteSpace(patSummary.Special))
                {
                    if (TryComputeTreasureCode(patSummary.Treasure!, patSummary.Special!, out var code))
                    {
                        var treasureFile = $"treasure_{code:D5}.png";
                        var treasurePath = Path.Combine(ResolvePath(cfg.TreasureFolder!, cwd), treasureFile);
                        CompositeFullCanvasIfExists(background, treasurePath);
                    }
                    else
                    {
                        Console.WriteLine($"[Warn] Could not parse treasure/special for id={id} (treasure='{patSummary.Treasure}', special='{patSummary.Special}').");
                    }
                }

                // 4b) Spawn point overlay (full canvas) based on summary.spawn_point_id
                {
                    var spawnFile = MapSpawnOverlay(patSummary.Spawn_Point_Id);
                    if (spawnFile != null)
                    {
                        var spawnFolder = ResolvePath("../assets/map/spawn_point", cwd);
                        var spawnPath = Path.Combine(spawnFolder, spawnFile);
                        CompositeFullCanvasIfExists(background, spawnPath);
                    }
                }

                // 5) Nightlord emblem (anchored)
                if (!string.IsNullOrWhiteSpace(cfg.NightlordFolder) && !string.IsNullOrWhiteSpace(patSummary.Nightlord))
                {
                    var emblemName = MapNightlordIconFilename(patSummary.Nightlord!);
                    if (emblemName is not null)
                    {
                        var emblemPath = Path.Combine(ResolvePath(cfg.NightlordFolder!, cwd), emblemName);
                        CompositeIconAnchored(background, emblemPath, cfg.NightlordIcon);
                    }
                }

                // 6) POIs - Major Base
                PlotMajorBase(background, patternDoc, indexLookup, cfg, cwd);

                // 7) POIs - Minor Base
                PlotMinorBase(background, patternDoc, indexLookup, cfg, cwd);

                // 8) POIs - Event
                PlotEvents(background, patternDoc, indexLookup, cfg, cwd);

                // 9) POIs - Evergaol
                PlotEvergaol(background, patternDoc, indexLookup, cfg, cwd);

                // 10) POIs - Field Bosses
                PlotFieldBosses(background, patternDoc, indexLookup, cfg, cwd);

                // 11) POIs - Night Bosses
                PlotNightBosses(background, patternDoc, indexLookup, cfg, cwd);

                // Save
                background.Format = MagickFormat.Png;
                var outputPath = Path.Combine(outputFolder, $"{id}.png");
                background.Write(outputPath);
                Console.WriteLine($"[Wrote] {outputPath}");

                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Error] {ex.Message}");
                return 2;
            }
        }

        // ===================== Helpers =====================

        private static AppConfig LoadConfig(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"Config file not found: {path}");

            var json = File.ReadAllText(path);
            var cfg = JsonSerializer.Deserialize<AppConfig>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                     ?? throw new InvalidOperationException("Failed to parse appsettings.json.");
            if (string.IsNullOrWhiteSpace(cfg.OutputFolder))
                cfg.OutputFolder = "output";
            return cfg;
        }

        private static string ResolvePath(string value, string baseDir, bool allowCreateDir = false)
        {
            var full = Path.IsPathRooted(value) ? value : Path.GetFullPath(Path.Combine(baseDir, value));
            if (allowCreateDir && !Directory.Exists(full))
                Directory.CreateDirectory(full);
            return full;
        }

        private static string? GetPatternId(string patternPath)
        {
            if (!File.Exists(patternPath))
                throw new FileNotFoundException($"Pattern JSON not found: {patternPath}");

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(patternPath));
                var root = doc.RootElement;
                if (root.TryGetProperty("patternId", out var pid)) return NormalizeId(pid);
                if (root.TryGetProperty("id", out var idProp))     return NormalizeId(idProp);
            }
            catch { }

            var name = Path.GetFileNameWithoutExtension(patternPath);
            var m = Regex.Match(name, @"pattern_(\d+)", RegexOptions.IgnoreCase);
            if (m.Success) return m.Groups[1].Value.PadLeft(3, '0');

            return name;
        }

        private static string NormalizeId(JsonElement el) =>
            el.ValueKind switch
            {
                JsonValueKind.String => (int.TryParse(el.GetString(), out var n) ? n.ToString("D3") : el.GetString() ?? "unknown"),
                JsonValueKind.Number => el.TryGetInt32(out var i) ? i.ToString("D3") : ((int)Math.Round(el.GetDouble())).ToString("D3"),
                _ => "unknown"
            };

        private static IEnumerable<SummaryPattern> LoadSummary(string summaryPath)
        {
            if (!File.Exists(summaryPath))
                throw new FileNotFoundException($"Summary JSON not found: {summaryPath}");

            var json = File.ReadAllText(summaryPath);
            var root = JsonSerializer.Deserialize<SummaryRoot>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return root?.Patterns ?? Array.Empty<SummaryPattern>();
        }

        private static PatternDoc LoadPattern(string patternPath)
        {
            var json = File.ReadAllText(patternPath);
            var doc = JsonSerializer.Deserialize<PatternDoc>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                      ?? new PatternDoc { pois = new List<Poi>() };
            doc.pois ??= new List<Poi>();
            return doc;
        }

        private static Dictionary<string, IndexEntry> LoadIndex(string indexPath)
        {
            var json = File.ReadAllText(indexPath);
            var dict = new Dictionary<string, IndexEntry>(StringComparer.OrdinalIgnoreCase);
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            using (var doc = JsonDocument.Parse(json))
            {
                var root = doc.RootElement;

                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("entries", out var entriesEl) && entriesEl.ValueKind == JsonValueKind.Array)
                {
                    try
                    {
                        var list = JsonSerializer.Deserialize<List<IndexEntry>>(entriesEl.GetRawText(), opts);
                        if (list != null)
                        {
                            foreach (var e in list)
                            {
                                if (e == null) continue;
                                var key = (e.name ?? string.Empty).Trim();
                                if (string.IsNullOrEmpty(key)) continue;
                                dict[key] = e;
                            }
                            Console.WriteLine($"[Index] Loaded {dict.Count} entries (object.entries).");
                            return dict;
                        }
                    }
                    catch { }
                }

                if (root.ValueKind == JsonValueKind.Array)
                {
                    try
                    {
                        var list = JsonSerializer.Deserialize<List<IndexEntry>>(json, opts);
                        if (list != null)
                        {
                            foreach (var e in list)
                            {
                                if (e == null) continue;
                                var key = (e.name ?? string.Empty).Trim();
                                if (string.IsNullOrEmpty(key)) continue;
                                dict[key] = e;
                            }
                            Console.WriteLine($"[Index] Loaded {dict.Count} entries (array).");
                            return dict;
                        }
                    }
                    catch { }
                }

                if (root.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in root.EnumerateObject())
                    {
                        var key = prop.Name.Trim();
                        if (string.Equals(key, "entries", StringComparison.OrdinalIgnoreCase)) continue;

                        var e = new IndexEntry { name = key };
                        var obj = prop.Value;
                        if (obj.ValueKind == JsonValueKind.Object)
                        {
                            if (obj.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
                                e.name = n.GetString();
                            if (obj.TryGetProperty("category", out var c) && c.ValueKind == JsonValueKind.String)
                                e.category = c.GetString();
                            if (obj.TryGetProperty("icon", out var i) && i.ValueKind == JsonValueKind.String)
                                e.icon = i.GetString();
                            if (obj.TryGetProperty("cid", out var cidEl) && cidEl.ValueKind == JsonValueKind.String)
                                e.cid = cidEl.GetString();
                            if (obj.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                                e.id = idEl.GetString();
                        }
                        dict[key] = e;
                    }
                    Console.WriteLine($"[Index] Loaded {dict.Count} entries (object map).");
                }
            }

            return dict;
        }

        private static string? MapRawFilenameForSpecial(string special)
        {
            if (int.TryParse(special, out var s))
            {
                return s switch
                {
                    0 => "default.png",
                    1 => "mountaintop.png",
                    2 => "crater.png",
                    3 => "rotted_wood.png",
                    5 => "noklateo.png",
                    _ => null
                };
            }
            return special.ToLowerInvariant() switch
            {
                "0" => "default.png",
                "1" => "mountaintop.png",
                "2" => "crater.png",
                "3" => "rotted_wood.png",
                "5" => "noklateo.png",
                _ => null
            };
        }

        private static string? MapShiftingOverlayForSpecial(string special)
        {
            if (int.TryParse(special, out var s))
            {
                return s switch
                {
                    1 => "mountaintop_overlay.png",
                    2 => "crater_overlay.png",
                    3 => "rotted_wood_overlay.png",
                    5 => "noklateo_overlay.png",
                    0 => null,
                    _ => null
                };
            }
            var t = special.Trim().ToLowerInvariant();
            return t switch
            {
                "1" => "mountaintop_overlay.png",
                "2" => "crater_overlay.png",
                "3" => "rotted_wood_overlay.png",
                "5" => "noklateo_overlay.png",
                "0" => null,
                _ => null
            };
        }

        private static string? MapFrenzyOverlay(string? dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) return null;
            switch (dir.Trim().ToLowerInvariant())
            {
                case "south": return "frenzy_south.png";
                case "north": return "frenzy_north.png";
                default: return null;
            }
        }

        private static string? MapRotBlessingOverlay(string? dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) return null;
            switch (dir.Trim().ToLowerInvariant())
            {
                case "west":       return "blessing_west.png";
                case "northwest":  return "blessing_northwest.png";
                case "southwest":  return "blessing_southwest.png";
                default: return null;
            }
        }

        private static string? MapSpawnOverlay(string? id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            var trimmed = id.Trim();
            if (int.TryParse(trimmed, out var n))
                return $"start_{n}.png";
            var digits = new string(trimmed.Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var m))
                return $"start_{m}.png";
            return null;
        }

        private static bool TryComputeTreasureCode(string treasure, string special, out int code)
        {
            code = 0;
            if (!int.TryParse(treasure, out var t)) return false;
            if (!int.TryParse(special, out var s)) return false;
            code = t * 10 + s;
            return true;
        }

        private static string? MapNightlordIconFilename(string nightlord)
        {
            if (int.TryParse(nightlord, out var n))
            {
                return n switch
                {
                    0 => "Gladius.png",
                    1 => "Adel.png",
                    2 => "Gnoster.png",
                    3 => "Maris.png",
                    4 => "Libra.png",
                    5 => "Fulghor.png",
                    6 => "Caligo.png",
                    7 => "Heolstor.png",
                    _ => null
                };
            }
            return nightlord.ToLowerInvariant() switch
            {
                "0" => "Gladius.png",
                "1" => "Adel.png",
                "2" => "Gnoster.png",
                "3" => "Maris.png",
                "4" => "Libra.png",
                "5" => "Fulghor.png",
                "6" => "Caligo.png",
                "7" => "Heolstor.png",
                "gladius"  => "Gladius.png",
                "adel"     => "Adel.png",
                "gnoster"  => "Gnoster.png",
                "maris"    => "Maris.png",
                "libra"    => "Libra.png",
                "fulghor"  => "Fulghor.png",
                "caligo"   => "Caligo.png",
                "heolstor" => "Heolstor.png",
                _ => null
            };
        }

        private static void CompositeFullCanvasIfExists(MagickImage background, string overlayPath)
        {
            if (!File.Exists(overlayPath))
            {
                Console.WriteLine($"[Warn] Overlay not found: {overlayPath}");
                return;
            }

            using var overlay = new MagickImage(overlayPath);
            if ((int)overlay.Width != (int)background.Width || (int)overlay.Height != (int)background.Height)
            {
                overlay.Resize((uint)background.Width, (uint)background.Height);
            }
            background.Composite(overlay, 0, 0, CompositeOperator.Over);
            Console.WriteLine($"[OK] Applied overlay: {Path.GetFileName(overlayPath)}");
        }

        private static void CompositeNoResizeIfExists(MagickImage background, string overlayPath)
        {
            if (!File.Exists(overlayPath))
            {
                Console.WriteLine($"[Warn] Overlay not found: {overlayPath}");
                return;
            }
            using var overlay = new MagickImage(overlayPath);
            background.Composite(overlay, 0, 0, CompositeOperator.Over);
            Console.WriteLine($"[OK] Applied (no-resize) overlay: {Path.GetFileName(overlayPath)}");
        }

        // ===== Mapping helpers =====
        private static (double px, double py) MapToPxPyExact1536(double x, double z)
        {
            const double S = 1536.0;
            return (x + S / 2.0, S / 2.0 - z);
        }

        private static void CompositeIconAt(MagickImage background, string iconPath, double x, double z, int targetW, int targetH)
        {
            if (!File.Exists(iconPath))
            {
                Console.WriteLine($"[Warn] Icon missing: {iconPath}");
                return;
            }
            using var icon = new MagickImage(iconPath);

            double s = Math.Min((double)targetW / (int)icon.Width, (double)targetH / (int)icon.Height);
            int newW = Math.Max(1, (int)Math.Round((int)icon.Width * s));
            int newH = Math.Max(1, (int)Math.Round((int)icon.Height * s));
            if (newW != (int)icon.Width || newH != (int)icon.Height)
                icon.Resize((uint)newW, (uint)newH);

            var (px, py) = MapToPxPyExact1536(x, z);
            int xTopLeft = (int)Math.Round(px - newW / 2.0);
            int yTopLeft = (int)Math.Round(py - newH / 2.0);

            background.Composite(icon, xTopLeft, yTopLeft, CompositeOperator.Over);
        }

        private static (int x, int y) MeasureAnchor(
            int canvasW, int canvasH,
            int objW, int objH,
            string anchor,
            int marginX, int marginY)
        {
            anchor = (anchor ?? "bottom-left").ToLowerInvariant();
            return anchor switch
            {
                "top-left"     => (marginX, marginY),
                "top-right"    => (canvasW - objW - marginX, marginY),
                "bottom-left"  => (marginX, canvasH - objH - marginY),
                "bottom-right" => (canvasW - objW - marginX, canvasH - objH - marginY),
                "center"       => ((canvasW - objW) / 2, (canvasH - objH) / 2),
                _              => (marginX, canvasH - objH - marginY),
            };
        }

        private static void CompositeIconAnchored(MagickImage background, string iconPath, IconSettings? settings)
        {
            if (!File.Exists(iconPath))
            {
                Console.WriteLine($"[Warn] Icon not found: {iconPath}");
                return;
            }

            using var icon = new MagickImage(iconPath);
            var cfg = settings ?? new IconSettings();

            int bgW = (int)background.Width;
            int bgH = (int)background.Height;

            int boxW, boxH;
            if (cfg.FixedWidthPx.HasValue || cfg.FixedHeightPx.HasValue)
            {
                boxW = cfg.FixedWidthPx  ?? cfg.FixedHeightPx!.Value;
                boxH = cfg.FixedHeightPx ?? cfg.FixedWidthPx!.Value;
            }
            else
            {
                boxW = cfg.MaxWidthPx  ?? (int)Math.Round((cfg.WidthPercent ?? 0.18) * bgW);
                boxH = cfg.MaxHeightPx ?? int.MaxValue;
            }

            int newW, newH;
            if (cfg.PreserveAspect)
            {
                double sW = (double)boxW / (int)icon.Width;
                double sH = (double)boxH / (int)icon.Height;
                double scale = (cfg.FitInsideBox ? Math.Min(sW, sH) : Math.Max(sW, sH));
                newW = Math.Max(1, (int)Math.Round((int)icon.Width  * scale));
                newH = Math.Max(1, (int)Math.Round((int)icon.Height * scale));
            }
            else
            {
                newW = Math.Max(1, boxW);
                newH = Math.Max(1, (boxH == int.MaxValue ? (int)icon.Height : boxH));
            }

            if (newW != (int)icon.Width || newH != (int)icon.Height)
                icon.Resize((uint)newW, (uint)newH);

            var (boxX, boxY) = MeasureAnchor(
                canvasW: bgW,
                canvasH: bgH,
                objW: Math.Min(boxW, bgW),
                objH: (boxH == int.MaxValue ? newH : Math.Min(boxH, bgH)),
                anchor: cfg.Anchor ?? "bottom-left",
                marginX: cfg.MarginX ?? 0,
                marginY: cfg.MarginY ?? 0
            );

            int placeX = boxX + Math.Max(0, (boxW - newW) / 2);
            int placeY = boxY + Math.Max(0, (boxH == int.MaxValue ? 0 : (boxH - newH) / 2));

            background.Composite(icon, placeX, placeY, CompositeOperator.Over);
            Console.WriteLine($"[OK] Anchored '{Path.GetFileName(iconPath)}' at {placeX},{placeY} ({newW}x{newH})");
        }

        private static string? ResolveIconPath(string? indexIconPath, string poiName, AppConfig cfg, string cwd)
        {
            // 1) Per-POI override by exact name (from appsettings.json)
            if (cfg.IconOverrides != null && poiName != null && cfg.IconOverrides.TryGetValue(poiName.Trim(), out var overridePath) && !string.IsNullOrWhiteSpace(overridePath))
            {
                var normalizedOv = NormalizeIconPath(overridePath.Trim());
                var absOv = ResolvePath(normalizedOv, cwd);
                if (File.Exists(absOv)) return absOv;
                Console.WriteLine($"[IconOverride] Missing override file for '{poiName}': {absOv}");
            }

            // 2) Fallback: use icon path from index.json
            if (!string.IsNullOrWhiteSpace(indexIconPath))
            {
                var normalized = NormalizeIconPath(indexIconPath.Trim());
                var abs = ResolvePath(normalized, cwd);
                if (File.Exists(abs)) return abs;
                Console.WriteLine($"[Icon] Missing file for '{poiName}': {abs}");
            }
            return null;
        }

        // ===== Major Base plotting =====
        private static void PlotMajorBase(MagickImage background, PatternDoc pattern, Dictionary<string, IndexEntry> indexLookup, AppConfig cfg, string cwd)
        {
            if (pattern.pois == null || pattern.pois.Count == 0) { Console.WriteLine("[MajorBase] No POIs."); return; }
            if (cfg.MajorBase == null) { Console.WriteLine("[MajorBase] Config missing."); return; }

            int total = 0, matched = 0, drawn = 0, skippedCat = 0, missingIcon = 0, notInIndex = 0;

            foreach (var poi in pattern.pois)
            {
                if (string.IsNullOrWhiteSpace(poi.name)) continue;
                total++;

                if (!indexLookup.TryGetValue(poi.name.Trim(), out var entry) || entry == null)
                {
                    notInIndex++;
                    continue;
                }
                matched++;

                var catNorm = NormalizeType(entry.category ?? "");
                SizeBox? box = catNorm switch
                {
                    "Camp" => cfg.MajorBase.Camp,
                    "Fort" => cfg.MajorBase.Fort,
                    "Great_Church" or "GreatChurch" => cfg.MajorBase.Great_Church,
                    "Ruins" => cfg.MajorBase.Ruins,
                    _ => null
                };
                if (box == null) { skippedCat++; continue; }

                var iconPath = ResolveIconPath(entry.icon, poi.name ?? "", cfg, cwd);
                if (iconPath == null) { missingIcon++; continue; }

                CompositeIconAt(background, iconPath, poi.x, poi.z, box.WidthPx, box.HeightPx);
                drawn++;
            }

            Console.WriteLine($"[MajorBase] total={total} matchedIndex={matched} drawn={drawn} skippedCat={skippedCat} missingIcon={missingIcon} notInIndex={notInIndex}");
        }

        // ===== Minor Base plotting =====
        private static void PlotMinorBase(MagickImage background, PatternDoc pattern, Dictionary<string, IndexEntry> indexLookup, AppConfig cfg, string cwd)
        {
            if (pattern.pois == null || pattern.pois.Count == 0) { Console.WriteLine("[MinorBase] No POIs."); return; }
            if (cfg.MinorBase == null) { Console.WriteLine("[MinorBase] Config missing."); return; }

            int total = 0, matched = 0, drawn = 0, skippedCat = 0, missingIcon = 0, notInIndex = 0;

            foreach (var poi in pattern.pois)
            {
                if (string.IsNullOrWhiteSpace(poi.name)) continue;
                total++;

                if (!indexLookup.TryGetValue(poi.name.Trim(), out var entry) || entry == null)
                {
                    notInIndex++;
                    continue;
                }
                matched++;

                var catNorm = NormalizeType(entry.category ?? "");
                SizeBox? box = catNorm switch
                {
                    "Church"          => cfg.MinorBase.Church,
                    "Small_Camp"      => cfg.MinorBase.Small_Camp,
                    "Sorcerers_Rise"  => cfg.MinorBase.Sorcerers_Rise,
                    "Township"        => cfg.MinorBase.Township,
                    _ => null
                };
                if (box == null) { skippedCat++; continue; }

                var iconPath = ResolveIconPath(entry.icon, poi.name ?? "", cfg, cwd);
                if (iconPath == null) { missingIcon++; continue; }

                CompositeIconAt(background, iconPath, poi.x, poi.z, box.WidthPx, box.HeightPx);
                drawn++;
            }

            Console.WriteLine($"[MinorBase] total={total} matchedIndex={matched} drawn={drawn} skippedCat={skippedCat} missingIcon={missingIcon} notInIndex={notInIndex}");
        }

        // ===== Event plotting =====
        private static void PlotEvents(MagickImage background, PatternDoc pattern, Dictionary<string, IndexEntry> indexLookup, AppConfig cfg, string cwd)
        {
            if (pattern.pois == null || pattern.pois.Count == 0) { Console.WriteLine("[Event] No POIs."); return; }
            if (cfg.Event == null) { Console.WriteLine("[Event] Config missing."); return; }

            int total = 0, matched = 0, drawn = 0, missingIcon = 0, notInIndex = 0, skippedSubtype = 0;

            foreach (var poi in pattern.pois)
            {
                if (string.IsNullOrWhiteSpace(poi.name)) continue;
                var n = poi.name.Trim();
                if (!n.StartsWith("Event -", StringComparison.OrdinalIgnoreCase)) continue;
                total++;

                if (!indexLookup.TryGetValue(n, out var entry) || entry == null)
                {
                    notInIndex++;
                    continue;
                }
                matched++;

                var idx = n.IndexOf("Event -", StringComparison.OrdinalIgnoreCase);
                string subtype = n.Substring(idx + "Event -".Length).Trim();

                var key = NormalizeType(subtype);

                SizeBox? box = key switch
                {
                    "Scale_Bearing_Merchant" => cfg.Event.Scale_Bearing_Merchant,
                    "Meteor_Strike"          => cfg.Event.Meteor_Strike,
                    "Walking_Mausoleum"      => cfg.Event.Walking_Mausoleum,
                    _ => null
                };
                if (box == null) { skippedSubtype++; continue; }

                var iconPath = ResolveIconPath(entry.icon, poi.name ?? "", cfg, cwd);
                if (iconPath == null) { missingIcon++; continue; }

                CompositeIconAt(background, iconPath, poi.x, poi.z, box.WidthPx, box.HeightPx);
                drawn++;
            }

            Console.WriteLine($"[Event] total={total} matchedIndex={matched} drawn={drawn} missingIcon={missingIcon} notInIndex={notInIndex} skippedSubtype={skippedSubtype}");
        }

        // ===== Evergaol plotting =====
        private static void PlotEvergaol(MagickImage background, PatternDoc pattern, Dictionary<string, IndexEntry> indexLookup, AppConfig cfg, string cwd)
        {
            if (pattern.pois == null || pattern.pois.Count == 0) { Console.WriteLine("[Evergaol] No POIs."); return; }
            if (cfg.Evergaol == null || cfg.Evergaol.Default == null) { Console.WriteLine("[Evergaol] Config missing (Evergaol.Default)."); return; }

            int total = 0, matched = 0, drawn = 0, missingIcon = 0, notInIndex = 0;

            foreach (var poi in pattern.pois)
            {
                if (string.IsNullOrWhiteSpace(poi.name)) continue;
                var n = poi.name.Trim();
                if (!n.StartsWith("Evergaol -", StringComparison.OrdinalIgnoreCase)) continue;
                total++;

                if (!indexLookup.TryGetValue(n, out var entry) || entry == null)
                {
                    notInIndex++;
                    continue;
                }
                matched++;

                var iconPath = ResolveIconPath(entry.icon, poi.name ?? "", cfg, cwd);
                if (iconPath == null) { missingIcon++; continue; }

                CompositeIconAt(background, iconPath, poi.x, poi.z, cfg.Evergaol.Default.WidthPx, cfg.Evergaol.Default.HeightPx);
                drawn++;
            }

            Console.WriteLine($"[Evergaol] total={total} matchedIndex={matched} drawn={drawn} missingIcon={missingIcon} notInIndex={notInIndex}");
        }

        // ===== Field Boss plotting =====
        private static void PlotFieldBosses(MagickImage background, PatternDoc pattern, Dictionary<string, IndexEntry> indexLookup, AppConfig cfg, string cwd)
        {
            if (pattern.pois == null || pattern.pois.Count == 0) { Console.WriteLine("[FieldBoss] No POIs."); return; }
            if (cfg.FieldBoss == null) { Console.WriteLine("[FieldBoss] Config missing."); return; }

            int total = 0, matched = 0, drawn = 0, missingIcon = 0, notInIndex = 0, skippedSubtype = 0;

            string[] prefixesSpace = { "Arena Boss -", "Field Boss -", "Strong Field Boss -", "Castle -" };
            string[] prefixesUnd   = { "Arena_Boss -", "Field_Boss -", "Strong_Field_Boss -", "Castle -" };

            foreach (var poi in pattern.pois)
            {
                if (string.IsNullOrWhiteSpace(poi.name)) continue;
                var n = poi.name.Trim();

                bool isField = false;
                foreach (var p in prefixesSpace) if (n.StartsWith(p, StringComparison.OrdinalIgnoreCase)) { isField = true; break; }
                if (!isField)
                    foreach (var p in prefixesUnd) if (n.StartsWith(p, StringComparison.OrdinalIgnoreCase)) { isField = true; break; }
                if (!isField) continue;

                total++;

                if (!indexLookup.TryGetValue(n, out var entry) || entry == null)
                {
                    notInIndex++;
                    continue;
                }
                matched++;

                string subtype;
                if (n.StartsWith("Arena Boss -", StringComparison.OrdinalIgnoreCase) || n.StartsWith("Arena_Boss -", StringComparison.OrdinalIgnoreCase))
                    subtype = "Arena_Boss";
                else if (n.StartsWith("Strong Field Boss -", StringComparison.OrdinalIgnoreCase) || n.StartsWith("Strong_Field_Boss -", StringComparison.OrdinalIgnoreCase))
                    subtype = "Strong_Field_Boss";
                else if (n.StartsWith("Field Boss -", StringComparison.OrdinalIgnoreCase) || n.StartsWith("Field_Boss -", StringComparison.OrdinalIgnoreCase))
                    subtype = "Field_Boss";
                else if (n.StartsWith("Castle -", StringComparison.OrdinalIgnoreCase))
                    subtype = "Castle";
                else
                    subtype = "Field_Boss";

                SizeBox? box = subtype switch
                {
                    "Arena_Boss"         => cfg.FieldBoss.Arena_Boss,
                    "Strong_Field_Boss"  => cfg.FieldBoss.Strong_Field_Boss,
                    "Field_Boss"         => cfg.FieldBoss.Field_Boss,
                    "Castle"             => cfg.FieldBoss.Castle,
                    _ => null
                };
                if (box == null) { skippedSubtype++; continue; }

                var iconPath = ResolveIconPath(entry.icon, poi.name ?? "", cfg, cwd);
                if (iconPath == null) { missingIcon++; continue; }

                CompositeIconAt(background, iconPath, poi.x, poi.z, box.WidthPx, box.HeightPx);
                drawn++;
            }

            Console.WriteLine($"[FieldBoss] total={total} matchedIndex={matched} drawn={drawn} missingIcon={missingIcon} notInIndex={notInIndex} skippedSubtype={skippedSubtype}");
        }

        // ===== Night Boss plotting =====
        private static void PlotNightBosses(MagickImage background, PatternDoc pattern, Dictionary<string, IndexEntry> indexLookup, AppConfig cfg, string cwd)
        {
            if (pattern.pois == null || pattern.pois.Count == 0) { Console.WriteLine("[NightBoss] No POIs."); return; }
            if (cfg.NightBoss == null || cfg.NightBoss.Default == null) { Console.WriteLine("[NightBoss] Config missing (NightBoss.Default)."); return; }

            int total = 0, matched = 0, drawn = 0, missingIcon = 0, notInIndex = 0;

            foreach (var poi in pattern.pois)
            {
                if (string.IsNullOrWhiteSpace(poi.name)) continue;
                var n = poi.name.Trim();
                if (!(n.StartsWith("Night Boss -", StringComparison.OrdinalIgnoreCase) || n.StartsWith("Night_Boss -", StringComparison.OrdinalIgnoreCase)))
                    continue;

                total++;

                if (!indexLookup.TryGetValue(n, out var entry) || entry == null)
                {
                    notInIndex++;
                    continue;
                }
                matched++;

                var iconPath = ResolveIconPath(entry.icon, poi.name ?? "", cfg, cwd);
                if (iconPath == null) { missingIcon++; continue; }

                CompositeIconAt(background, iconPath, poi.x, poi.z, cfg.NightBoss.Default.WidthPx, cfg.NightBoss.Default.HeightPx);
                drawn++;
            }

            Console.WriteLine($"[NightBoss] total={total} matchedIndex={matched} drawn={drawn} missingIcon={missingIcon} notInIndex={notInIndex}");
        }

        private static string NormalizeType(string s)
        {
            s = (s ?? string.Empty).Trim();
            s = s.Replace(" ", "_").Replace("-", "_");
            return s;
        }

        private static string NormalizeIconPath(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw ?? string.Empty;
            if (Path.IsPathRooted(raw)) return raw;
            if (raw.StartsWith("assets/", StringComparison.OrdinalIgnoreCase) ||
                raw.StartsWith("assets\\", StringComparison.OrdinalIgnoreCase))
            {
                return Path.Combine("..", raw);
            }
            return raw;
        }
    }
}

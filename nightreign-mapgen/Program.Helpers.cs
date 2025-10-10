using System;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Linq;
using System.Collections.Generic;
using ImageMagick;
using NightReign.MapGen.Helpers;

namespace NightReign.MapGen
{
    public static partial class Program
    {
        // --- Performance: cache File.Exists results to cut filesystem churn ---
        static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> __existsCache 
            = new System.Collections.Concurrent.ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        internal static bool CachedExists(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            return __existsCache.GetOrAdd(path, p => CachedIO.CachedExists(p));
        }

        // Global verbosity switch (optional, read from config in LoadConfig if present)
        internal static bool Verbose { get; set; } = false;

        internal static AppConfig LoadConfig(string path)
        {
            if (!CachedExists(path))
            throw new FileNotFoundException($"Config file not found: {path}");
            
            var json = File.ReadAllText(path);
            var cfg = JsonSerializer.Deserialize<AppConfig>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Failed to parse appsettings.json.");
            if (string.IsNullOrWhiteSpace(cfg.OutputFolder))
            cfg.OutputFolder = "output";
            // Read optional Verbose flag to gate hot-loop logging
            try {
                using var __doc = System.Text.Json.JsonDocument.Parse(json);
                if (__doc.RootElement.TryGetProperty("Verbose", out var vvv) && vvv.ValueKind == System.Text.Json.JsonValueKind.True) 
                    Verbose = true;
            } catch {}
    
            return cfg;
        }
        
        internal static string ResolvePath(string value, string baseDir, bool allowCreateDir = false)
        {
            var full = Path.IsPathRooted(value) ? value : Path.GetFullPath(Path.Combine(baseDir, value));
            if (allowCreateDir && !Directory.Exists(full))
            Directory.CreateDirectory(full);
            return full;
        }
        
        internal static string? GetPatternId(string patternPath)
        {
            if (!CachedExists(patternPath))
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
        
        internal static string NormalizeId(JsonElement el) =>
        el.ValueKind switch
        {
            JsonValueKind.String => (int.TryParse(el.GetString(), out var n) ? n.ToString("D3") : el.GetString() ?? "unknown"),
            JsonValueKind.Number => el.TryGetInt32(out var i) ? i.ToString("D3") : ((int)Math.Round(el.GetDouble())).ToString("D3"),
            _ => "unknown"
        };
        
        internal static IEnumerable<SummaryPattern> LoadSummary(string summaryPath)
        {
            if (!CachedExists(summaryPath))
            throw new FileNotFoundException($"Summary JSON not found: {summaryPath}");
            
            var json = File.ReadAllText(summaryPath);
            var root = JsonSerializer.Deserialize<SummaryRoot>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return root?.Patterns ?? Array.Empty<SummaryPattern>();
        }
        
        internal static PatternDoc LoadPattern(string patternPath)
        {
            var json = File.ReadAllText(patternPath);
            var doc = JsonSerializer.Deserialize<PatternDoc>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? new PatternDoc { pois = new List<Poi>() };
            doc.pois ??= new List<Poi>();
            return doc;
        }
        
        internal static Dictionary<string, IndexEntry> LoadIndex(string indexPath)
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
                            if (Verbose) Console.WriteLine($"[Index] Loaded {dict.Count} entries (object.entries).");
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
                            if (Verbose) Console.WriteLine($"[Index] Loaded {dict.Count} entries (array).");
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
                    if (Verbose) Console.WriteLine($"[Index] Loaded {dict.Count} entries (object map).");
                }
            }
            
            return dict;
        }
        
        internal static string? MapRawFilenameForSpecial(string special)
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
        
        internal static string? MapShiftingOverlayForSpecial(string special)
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
        
        internal static string? MapFrenzyOverlay(string? dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) return null;
            switch (dir.Trim().ToLowerInvariant())
            {
                case "south": return "frenzy_south.png";
                case "north": return "frenzy_north.png";
                default: return null;
            }
        }
        
        internal static string? MapRotBlessingOverlay(string? dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) return null;
            switch (dir.Trim().ToLowerInvariant())
            {
                case "west":       return "blessing_west.png";
                case "northeast":  return "blessing_northeast.png";
                case "southwest":  return "blessing_southwest.png";
                default: return null;
            }
        }
        
        internal static string? MapSpawnOverlay(string? id)
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
        
        internal static bool TryComputeTreasureCode(string treasure, string special, out int code)
        {
            code = 0;
            if (!int.TryParse(treasure, out var t)) return false;
            if (!int.TryParse(special, out var s)) return false;
            code = t * 10 + s;
            return true;
        }
        
        internal static string? MapNightlordIconFilename(string nightlord)
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
        
        internal static void CompositeFullCanvasIfExists(MagickImage background, string overlayPath)
        {
            if (!CachedExists(overlayPath))
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
            if (Verbose) Console.WriteLine($"[OK] Applied overlay: {System.IO.Path.GetFileName(overlayPath)}");
        }
        
        internal static void CompositeNoResizeIfExists(MagickImage background, string overlayPath)
        {
            if (!CachedExists(overlayPath))
            {
                Console.WriteLine($"[Warn] Overlay not found: {overlayPath}");
                return;
            }
            using var overlay = new MagickImage(overlayPath);
            background.Composite(overlay, 0, 0, CompositeOperator.Over);
            if (Verbose) Console.WriteLine($"[OK] Applied (no-resize) overlay: {System.IO.Path.GetFileName(overlayPath)}");
        }
        
        internal static (double px, double py) MapToPxPyExact1536(double x, double z)
        {
            const double S = 1536.0;
            return (x + S / 2.0, S / 2.0 - z);
        }
        
        internal static void CompositeIconAt(MagickImage background, string iconPath, double x, double z, int targetW, int targetH)
        {
            // Optimized: use in-process icon cache to avoid repeated decode+resize
            if (string.IsNullOrWhiteSpace(iconPath))
            {
                if (Verbose) Console.WriteLine("[Icon] Empty path.");
                return;
            }

            if (!CachedExists(iconPath))
            {
                Console.WriteLine($"[Warn] Icon missing: {iconPath}");
                return;
            }

            // Fetch cached, resized icon. Resizing preserves aspect ratio to fit within target box.
            var icon = NightReign.MapGen.Rendering.ImageCache.GetResized(iconPath, targetW, targetH);

            var (px, py) = MapToPxPyExact1536(x, z);
            int xTopLeft = (int)Math.Round(px - icon.Width / 2.0);
            int yTopLeft = (int)Math.Round(py - icon.Height / 2.0);

            background.Composite(icon, xTopLeft, yTopLeft, CompositeOperator.Over);
            if (Verbose) if (Verbose) Console.WriteLine($"[OK] Anchored '{System.IO.Path.GetFileName(iconPath)}' at {xTopLeft},{yTopLeft} ({icon.Width}x{icon.Height})");
    }
        
        internal static (int x, int y) MeasureAnchor(
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
        
        internal static void CompositeIconAnchored(MagickImage background, string iconPath, IconSettings? settings)
        {
            if (!CachedExists(iconPath))
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
            if (Verbose) Console.WriteLine($"[OK] Anchored '{System.IO.Path.GetFileName(iconPath)}' at {placeX},{placeY} ({newW}x{newH})");
        }
        
        internal static string? ResolveIconPath(string? indexIconPath, string poiName, AppConfig cfg, string cwd)
        {
            if (cfg.IconOverrides != null && poiName != null && cfg.IconOverrides.TryGetValue(poiName.Trim(), out var overridePath) && !string.IsNullOrWhiteSpace(overridePath))
            {
                var normalizedOv = NormalizeIconPath(overridePath.Trim());
                var absOv = ResolvePath(normalizedOv, cwd);
                if (CachedExists(absOv)) return absOv;
                Console.WriteLine($"[IconOverride] Missing override file for '{poiName}': {absOv}");
            }
            
            if (!string.IsNullOrWhiteSpace(indexIconPath))
            {
                var normalized = NormalizeIconPath(indexIconPath.Trim());
                var abs = ResolvePath(normalized, cwd);
                if (CachedExists(abs)) return abs;
                Console.WriteLine($"[Icon] Missing file for '{poiName}': {abs}");
            }
            return null;
        }
        
        internal static string NormalizeType(string s)
        {
            s = (s ?? string.Empty).Trim();
            s = s.Replace(" ", "_").Replace("-", "_");
            return s;
        }
        
        internal static string NormalizeIconPath(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw ?? string.Empty;
            if (System.IO.Path.IsPathRooted(raw)) return raw;
            if (raw.StartsWith("assets/", StringComparison.OrdinalIgnoreCase) ||
            raw.StartsWith("assets\\", StringComparison.OrdinalIgnoreCase))
            {
                return System.IO.Path.Combine("..", raw);
            }
            return raw;
        }
    }
}
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace NightReign.MapGen.Rendering
{
    /// <summary>
    /// Provides per-type label offset lookup from appsettings.json.
    /// Usage:
    ///   var (dx, dy) = LabelOffsets.GetOffsets(
    ///       appsettingsPath: Path.Combine(cwd, "appsettings.json"),
    ///       cwd: cwd,
    ///       sectionPath: "LabelOffsets:MajorBase",
    ///       typeKey: type // e.g., "Camp", "Fort", "Great_Church", "Ruins"
    ///   );
    ///   px += dx; py += dy;
    /// </summary>
    public static class LabelOffsets
    {
        public static (int dx, int dy) GetOffsets(string appsettingsPath, string cwd, string sectionPath, string typeKey)
        {
            try
            {
                // Resolve appsettings.json relative to cwd if not absolute
                string settingsPath = appsettingsPath;
                if (!Path.IsPathRooted(settingsPath))
                    settingsPath = Path.Combine(cwd ?? "", appsettingsPath ?? "appsettings.json");

                if (!File.Exists(settingsPath))
                    return (0, 0);

                // Load JSON
                using var fs = File.OpenRead(settingsPath);
                using var doc = JsonDocument.Parse(fs);

                // Drill into section path (e.g., "LabelOffsets:MajorBase")
                JsonElement node = doc.RootElement;
                if (!string.IsNullOrWhiteSpace(sectionPath))
                {
                    foreach (var part in sectionPath.Split(':', StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty(part, out node))
                            return (0, 0);
                    }
                }

                // Normalize the type key as we store it in JSON
                var key = NormalizeTypeKey(typeKey);

                if (node.ValueKind == JsonValueKind.Object && node.TryGetProperty(key, out var typeNode) && typeNode.ValueKind == JsonValueKind.Object)
                {
                    int dx = 0, dy = 0;
                    if (typeNode.TryGetProperty("dx", out var dxEl) && dxEl.TryGetInt32(out var dxVal)) dx = dxVal;
                    if (typeNode.TryGetProperty("dy", out var dyEl) && dyEl.TryGetInt32(out var dyVal)) dy = dyVal;
                    return (dx, dy);
                }

                return (0, 0);
            }
            catch
            {
                return (0, 0);
            }
        }

        private static string NormalizeTypeKey(string? typeKey)
        {
            if (string.IsNullOrWhiteSpace(typeKey)) return "";
            // Trim and standardize some common variations
            var t = typeKey.Trim();
            // Ensure Great_Church keeps underscore (matches your config style)
            if (t.Equals("Great Church", StringComparison.OrdinalIgnoreCase)) return "Great_Church";
            if (t.Equals("Great_Church", StringComparison.Ordinal)) return "Great_Church";
            if (t.Equals("Camp", StringComparison.OrdinalIgnoreCase)) return "Camp";
            if (t.Equals("Fort", StringComparison.OrdinalIgnoreCase)) return "Fort";
            if (t.Equals("Ruins", StringComparison.OrdinalIgnoreCase)) return "Ruins";
            // Fallback: return as-is (caller may already be normalized)
            return t;
        }
    }
}

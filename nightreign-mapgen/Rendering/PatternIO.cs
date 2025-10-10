using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace NightReign.MapGen.Rendering
{
    // Minimal POI representation for pattern files
    public sealed class PatternPoi
    {
        public string Name { get; }
        public double X { get; }
        public double Z { get; }

        public PatternPoi(string name, double x, double z)
        {
            Name = name;
            X = x;
            Z = z;
        }

        public override string ToString() => $"{Name} @ ({X}, {Z})";
    }

    public static class PatternIO
    {
        /// <summary>
        /// Resolve the absolute path to the data/pattern directory regardless of where the exe runs from.
        /// Walk up from AppContext.BaseDirectory until a folder containing data/pattern is found.
        /// </summary>
        public static string ResolvePatternDir()
        {
            string? dir = AppContext.BaseDirectory;
            while (dir != null)
            {
                var candidate = Path.Combine(dir, "data", "pattern");
                if (Directory.Exists(candidate))
                    return candidate;

                dir = Directory.GetParent(dir)?.FullName;
            }
            throw new DirectoryNotFoundException("Could not locate 'data/pattern' directory by walking up from AppContext.BaseDirectory.");
        }

        public static string ResolvePatternPathById(int patternId)
        {
            var root = ResolvePatternDir();
            var file = Path.Combine(root, $"pattern_{patternId:000}.json");
            if (!File.Exists(file))
                throw new FileNotFoundException($"Pattern file not found: {file}");
            return file;
        }

        public static IReadOnlyList<PatternPoi> LoadById(int patternId)
        {
            var path = ResolvePatternPathById(patternId);
            return LoadByPath(path);
        }

        public static IReadOnlyList<PatternPoi> LoadByPath(string path)
        {
            using var fs = File.OpenRead(path);
            using var doc = JsonDocument.Parse(fs);
            var root = doc.RootElement;

            if (!root.TryGetProperty("pois", out var poisEl) || poisEl.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Pattern JSON is missing 'pois' array.");

            var list = new List<PatternPoi>();

            foreach (var poi in poisEl.EnumerateArray())
            {
                if (poi.ValueKind != JsonValueKind.Object) continue;

                // Accept either top-level x/z or a nested pos.{x,z}
                double x = 0, z = 0;
                string name = "";

                if (poi.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
                    name = nameEl.GetString() ?? "";

                if (poi.TryGetProperty("x", out var xEl) && xEl.TryGetDouble(out var xv))
                    x = xv;
                else if (poi.TryGetProperty("pos", out var posEl) && posEl.ValueKind == JsonValueKind.Object)
                {
                    if (posEl.TryGetProperty("x", out var px) && px.TryGetDouble(out var pxv)) x = pxv;
                    if (posEl.TryGetProperty("z", out var pz) && pz.TryGetDouble(out var pzv)) z = pzv;
                }

                if (poi.TryGetProperty("z", out var zEl) && zEl.TryGetDouble(out var zv))
                    z = zv;

                if (string.IsNullOrWhiteSpace(name))
                    continue;

                list.Add(new PatternPoi(name, x, z));
            }

            return list;
        }
    }
}
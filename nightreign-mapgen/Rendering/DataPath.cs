using System;
using System.IO;

namespace NightReign.MapGen.Rendering
{
    public static class DataPath
    {
        /// <summary>
        /// Resolve a relative path like "data/pattern/pattern_000.json" by walking up from the executable location.
        /// Ensures your code can run from /Rendering/bin/* or your IDE's working dir.
        /// </summary>
        public static string Resolve(string relativePath)
        {
            if (Path.IsPathRooted(relativePath))
                return relativePath;

            string? dir = AppContext.BaseDirectory;
            while (dir != null)
            {
                var candidate = Path.Combine(dir, relativePath);
                if (File.Exists(candidate) || Directory.Exists(candidate))
                    return candidate;
                dir = Directory.GetParent(dir)?.FullName;
            }
            throw new FileNotFoundException($"Could not resolve path: {relativePath} (walked up from AppContext.BaseDirectory)");
        }
    }
}
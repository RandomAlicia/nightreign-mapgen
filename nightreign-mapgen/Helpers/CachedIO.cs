using System;
using System.Collections.Concurrent;
using System.IO;

namespace NightReign.MapGen.Helpers
{
    /// <summary>
    /// Tiny filesystem helper to cache File.Exists results.
    /// Use: CachedIO.CachedExists(path)
    /// </summary>
    public static class CachedIO
    {
        private static readonly ConcurrentDictionary<string, bool> _existsCache =
            new(StringComparer.OrdinalIgnoreCase);

        public static bool CachedExists(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            if (_existsCache.TryGetValue(path!, out var hit)) return hit;

            var ok = File.Exists(path!);
            _existsCache[path!] = ok;
            return ok;
        }
    }
}

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;

namespace NightReign.MapGen.Rendering
{
    /// <summary>
    /// Caches parsed appsettings.json (or other small JSON files) for reuse.
    /// Holds JsonDocument references for the lifetime of the process.
    /// </summary>
    public static class ConfigCache
    {
        private static readonly ConcurrentDictionary<string, JsonDocument> _docs =
            new ConcurrentDictionary<string, JsonDocument>(StringComparer.OrdinalIgnoreCase);

        public static JsonElement GetRoot(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path is null/empty", nameof(path));
            var doc = _docs.GetOrAdd(path, p => JsonDocument.Parse(File.ReadAllText(p)));
            return doc.RootElement;
        }
    
        public static void Clear()
        {
            foreach (var kv in _docs)
            {
                try { kv.Value.Dispose(); } catch {}
            }
            _docs.Clear();
        }

}
}

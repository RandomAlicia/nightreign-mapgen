using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace NightReign.MapGen.Rendering
{
    /// <summary>
    /// Resolves display text for POIs via index.json (i18nKey) and i18n/&lt;lang&gt;/poi.json.
    /// Clean version: no noisy per-item logging; only minimal, silent fallbacks.
    /// </summary>
    public static class LabelTextResolver
    {
        // Cache: lowercase(lang) -> (key -> localized string)
        private static readonly Dictionary<string, Dictionary<string, string>> _i18nCache = new();

        public static string Resolve(
            string rawName,
            IDictionary<string, object> indexLookup,
            string i18nFolder,
            string lang,
            string cwd
        )
        {
            if (string.IsNullOrWhiteSpace(rawName))
                return string.Empty;

            string? i18nKey = TryGetI18nKey(rawName, indexLookup);
            if (!string.IsNullOrWhiteSpace(i18nKey))
            {
                var text = TryResolveI18n(i18nKey!, i18nFolder, lang, cwd)
                           ?? TryResolveI18n(i18nKey!, i18nFolder, "en", cwd);
                if (!string.IsNullOrWhiteSpace(text))
                    return text!;
            }

            // Fallback: strip "Type - Name" -> "Name"
            int dash = rawName.IndexOf(" - ", StringComparison.Ordinal);
            return dash >= 0 && dash + 3 < rawName.Length ? rawName[(dash + 3)..] : rawName;
        }

        private static string? TryResolveI18n(string key, string i18nFolder, string lang, string cwd)
        {
            var langTrim = (lang ?? "").Trim();
            var cacheKey = langTrim.ToLowerInvariant();

            if (!_i18nCache.TryGetValue(cacheKey, out var map))
            {
                map = LoadLangMap(i18nFolder, langTrim, cwd);
                _i18nCache[cacheKey] = map;
            }
            return map != null && map.TryGetValue(key, out var val) ? val : null;
        }

        private static Dictionary<string, string> LoadLangMap(string i18nFolder, string lang, string cwd)
        {
            try
            {
                var folder = i18nFolder ?? "../i18n";
                var path = Path.Combine(cwd, folder, lang, "poi.json");
                if (!File.Exists(path))
                    return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                var json = File.ReadAllText(path);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json, options)
                           ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                return dict;
            }
            catch
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Find i18nKey for a raw pattern name by trying: exact key -> stripped key -> scan values by .name
        /// </summary>
        private static string? TryGetI18nKey(string rawName, IDictionary<string, object> indexLookup)
        {
            if (indexLookup == null || rawName == null) return null;

            // 1) exact rawName
            if (indexLookup.TryGetValue(rawName, out var entry) && entry != null)
            {
                var k = ExtractI18nKey(entry);
                if (!string.IsNullOrWhiteSpace(k)) return k;
            }

            // 2) stripped key ("Type - Name" -> "Name")
            int dash = rawName.IndexOf(" - ", StringComparison.Ordinal);
            if (dash >= 0 && dash + 3 < rawName.Length)
            {
                var stripped = rawName[(dash + 3)..];
                if (indexLookup.TryGetValue(stripped, out var entry2) && entry2 != null)
                {
                    var k2 = ExtractI18nKey(entry2);
                    if (!string.IsNullOrWhiteSpace(k2)) return k2;
                }
            }

            // 3) scan values whose internal 'name' matches rawName
            foreach (var kv in indexLookup)
            {
                var v = kv.Value;
                if (v == null) continue;
                try
                {
                    var nameVal = ExtractName(v);
                    if (!string.IsNullOrWhiteSpace(nameVal) && string.Equals(nameVal, rawName, StringComparison.Ordinal))
                    {
                        var k3 = ExtractI18nKey(v);
                        if (!string.IsNullOrWhiteSpace(k3)) return k3;
                    }
                }
                catch { /* ignore */ }
            }

            return null;
        }

        private static string? ExtractName(object entry)
        {
            if (entry == null) return null;

            if (entry is JsonElement el && el.ValueKind == JsonValueKind.Object)
            {
                if (el.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
                    return nameEl.GetString();
            }

            if (entry is IDictionary<string, object> dict)
            {
                if (dict.TryGetValue("name", out var nv) && nv is string s && !string.IsNullOrWhiteSpace(s))
                    return s;
            }

            // KeyValuePair unwrap
            var tp = entry.GetType();
            if (tp.IsGenericType && tp.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
            {
                var valProp = tp.GetProperty("Value");
                var val = valProp?.GetValue(entry);
                return ExtractName(val!);
            }

            // Case-insensitive property lookup
            var nameProp = GetPropertyIgnoreCase(tp, "name");
            if (nameProp != null)
            {
                var v = nameProp.GetValue(entry) as string;
                if (!string.IsNullOrWhiteSpace(v)) return v;
            }
            return null;
        }

        private static string? ExtractI18nKey(object entry)
        {
            if (entry == null) return null;

            // JsonElement path
            if (entry is JsonElement el && el.ValueKind == JsonValueKind.Object)
            {
                if (el.TryGetProperty("i18nKey", out var keyEl) && keyEl.ValueKind == JsonValueKind.String)
                    return keyEl.GetString();
                // derive from id if present
                if (el.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                    return "poi." + idEl.GetString();
            }

            // Dictionary path
            if (entry is IDictionary<string, object> dict)
            {
                if (dict.TryGetValue("i18nKey", out var kv) && kv is string s && !string.IsNullOrWhiteSpace(s))
                    return s;
                if (dict.TryGetValue("id", out var idv) && idv is string ids && !string.IsNullOrWhiteSpace(ids))
                    return "poi." + ids;
            }

            var t = entry.GetType();

            // Unwrap KeyValuePair<,>
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
            {
                var valProp = t.GetProperty("Value");
                var val = valProp?.GetValue(entry);
                return ExtractI18nKey(val!);
            }

            // Case-insensitive property or field for i18nKey
            var prop = GetPropertyIgnoreCase(t, "i18nKey");
            if (prop != null)
            {
                var v = prop.GetValue(entry) as string;
                if (!string.IsNullOrWhiteSpace(v)) return v;
            }

            var field = GetFieldIgnoreCase(t, "i18nKey");
            if (field != null)
            {
                var v = field.GetValue(entry) as string;
                if (!string.IsNullOrWhiteSpace(v)) return v;
            }

            // Fallback from 'id' property/field ("poi."+id)
            var idProp = GetPropertyIgnoreCase(t, "id");
            if (idProp != null)
            {
                var idv = idProp.GetValue(entry) as string;
                if (!string.IsNullOrWhiteSpace(idv)) return "poi." + idv;
            }
            var idField = GetFieldIgnoreCase(t, "id");
            if (idField != null)
            {
                var idv = idField.GetValue(entry) as string;
                if (!string.IsNullOrWhiteSpace(idv)) return "poi." + idv;
            }

            return null;
        }

        private static PropertyInfo? GetPropertyIgnoreCase(Type t, string name)
            => t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        private static FieldInfo? GetFieldIgnoreCase(Type t, string name)
            => t.GetField(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
    }
}

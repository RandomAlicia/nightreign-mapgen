using System;
using System.Collections.Concurrent;
using ImageMagick;

namespace NightReign.MapGen.Rendering
{
    /// <summary>
    /// Caches original and resized images to avoid repeated disk I/O and resampling.
    /// Returned MagickImage instances MUST NOT be mutated or disposed by callers.
    /// </summary>
    public static class ImageCache
    {
        private static readonly ConcurrentDictionary<string, MagickImage> _originals =
            new(StringComparer.OrdinalIgnoreCase);

        private static readonly ConcurrentDictionary<string, MagickImage> _resized =
            new(StringComparer.OrdinalIgnoreCase);

        public static MagickImage GetOriginal(string path)
        {
            return _originals.GetOrAdd(path, p => new MagickImage(p));
        }

        public static MagickImage GetResized(string path, int w, int h)
        {
            var key = $"{path}::{w}x{h}";
            return _resized.GetOrAdd(key, _ =>
            {
                // NOTE: Clone() returns IMagickImage<byte> in newer Magick.NET,
                // so we cast explicitly to MagickImage to satisfy the dictionary type.
                var original = GetOriginal(path);
                var clone = (MagickImage)original.Clone();

                // Prefer a faster filter; tweak if you want sharper small icons.
                clone.FilterType = FilterType.Triangle;
                clone.Resize((uint)w, (uint)h);
                return clone;
            });
        }

        public static void Clear()
        {
            foreach (var kv in _resized)
                kv.Value?.Dispose();
            foreach (var kv in _originals)
                kv.Value?.Dispose();

            _resized.Clear();
            _originals.Clear();
        }
    }
}
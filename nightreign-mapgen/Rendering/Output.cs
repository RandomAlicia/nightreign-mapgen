using System;
using System.IO;
using ImageMagick;

namespace NightReign.MapGen.Rendering
{
    /// <summary>
    /// Output helpers.
    /// </summary>
    public static class Output
    {
        public static void Save(MagickImage background, string outputFolder, string id)
        {
            background.Format = MagickFormat.Png;
            var outputPath = Path.Combine(outputFolder, $"{id}.png");
            background.Settings.SetDefine(MagickFormat.Png, "compression-level", "4");
            background.Write(outputPath);
            Console.WriteLine($"[Wrote] {outputPath}");
        }
    }
}
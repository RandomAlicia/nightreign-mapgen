using System;
using System.Collections.Generic;
using System.IO;
using ImageMagick;

namespace NightReign.MapGen.Rendering
{
    /// <summary>
    /// All label passes, isolated from Program.Main.
    /// </summary>
    public static class Labels
    {
        public static void RenderAll(
        MagickImage background,
        PatternDoc patternDoc,
        Dictionary<string, IndexEntry> indexLookup,
        AppConfig cfg,
        string cwd,
        string? specialValue,
        string patternId,
        string patternPath)
        {
            try
            {
                // Parse appsettings once
                var appPath = System.IO.Path.Combine(cwd, "appsettings.json");
                using var appFs = System.IO.File.OpenRead(appPath);
                using var appDoc = System.Text.Json.JsonDocument.Parse(appFs);

                var ctx = new LabelContext
                {
                    Background = background,
                    Pattern = patternDoc,
                    IndexLookup = indexLookup,
                    AppSettingsPath = appPath,
                    Cwd = cwd,
                    SpecialValue = specialValue,
                    PatternId = patternId,
                    AppSettingsRoot = appDoc.RootElement
                };


                var pipeline = new System.Collections.Generic.List<ILabelPass>
                {
                    new LegacyLabelerPasses.MajorBase(),
                    new LegacyLabelerPasses.MinorBase(),
                    new LegacyLabelerPasses.Evergaol(),
                    new LegacyLabelerPasses.FieldBoss(),
                    new LegacyLabelerPasses.NightBoss(),
                    new LegacyLabelerPasses.ShiftingEarth(),
                    new LegacyLabelerPasses.SpecialEvent(),
                    new LegacyLabelerPasses.SpecialIcon()
                };
                
                LabelPipeline.Execute(pipeline, ctx);
            }
            catch (Exception e)
            {
                Console.WriteLine($"[Labels] skipped: {e.Message}");
            }
        }
    }
}
 
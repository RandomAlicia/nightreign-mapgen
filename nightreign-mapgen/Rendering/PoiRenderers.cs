using System;
using System.Collections.Generic;
using ImageMagick;

namespace NightReign.MapGen.Rendering
{
    /// <summary>
    /// Groups all POI renderer calls behind one method.
    /// </summary>
    public static class PoiRenderers
    {
        public static void RenderAll(
        MagickImage background,
        PatternDoc patternDoc,
        Dictionary<string, IndexEntry> indexLookup,
        AppConfig cfg,
        string cwd)
        {
            MajorBaseRenderer.Render(background, patternDoc, indexLookup, cfg, cwd);
            MinorBaseRenderer.Render(background, patternDoc, indexLookup, cfg, cwd);
            EventRenderer.Render(background, patternDoc, indexLookup, cfg, cwd);
            EvergaolRenderer.Render(background, patternDoc, indexLookup, cfg, cwd);
            FieldBossRenderer.Render(background, patternDoc, indexLookup, cfg, cwd);
            NightBossRenderer.Render(background, patternDoc, indexLookup, cfg, cwd);
        }
    }
}
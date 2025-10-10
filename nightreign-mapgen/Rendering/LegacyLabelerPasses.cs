using System;
using System.Collections.Generic;

namespace NightReign.MapGen.Rendering
{
    /// <summary>
    /// Adapter passes that call existing static Labeler classes.
    /// This lets us centralize execution & error handling without changing behavior.
    /// </summary>
    public static class LegacyLabelerPasses
    {


        // Helper: box any typed index dictionary to IDictionary<string, object>
        private static IDictionary<string, object> BoxIndex<T>(IDictionary<string, T> src)
        {
            if (src is IDictionary<string, object> already)
                return already;
            var dict = new Dictionary<string, object>(src.Count);
            foreach (var kv in src)
                dict[kv.Key] = (object?)kv.Value!;
            return dict;
        }

        private static IDictionary<string, object> BoxIndex(IDictionary<string, object> src) => src;

        public sealed class MajorBase : ILabelPass
        {
            public void Run(LabelContext ctx) =>
            LabelerMajorBase.Label(
            ctx.Background,
            ctx.Pattern.pois,
            p => ImageHelpers.SelectNameXZ(p),
            BoxIndex(ctx.IndexLookup),
            System.IO.Path.Combine(ctx.Cwd, "appsettings.json"),
            ctx.Cwd,
            (x,z) => ImageHelpers.WorldToPxPy(x, z, (int)ctx.Background.Width, (int)ctx.Background.Height),
            "poiStandard"
            );
        }
        
        public sealed class MinorBase : ILabelPass
        {
            public void Run(LabelContext ctx) =>
            LabelerMinorBase.Label(
            ctx.Background,
            ctx.Pattern.pois,
            p => ImageHelpers.SelectNameXZ(p),
            BoxIndex(ctx.IndexLookup),
            System.IO.Path.Combine(ctx.Cwd, "appsettings.json"),
            ctx.Cwd,
            (x,z) => ImageHelpers.WorldToPxPy(x, z, (int)ctx.Background.Width, (int)ctx.Background.Height),
            "poiStandard"
            );
        }
        
        public sealed class Evergaol : ILabelPass
        {
            public void Run(LabelContext ctx) =>
            LabelerEvergaol.Label(
            ctx.Background,
            ctx.Pattern.pois,
            p => ImageHelpers.SelectNameXZ(p),
            BoxIndex(ctx.IndexLookup),
            System.IO.Path.Combine(ctx.Cwd, "appsettings.json"),
            ctx.Cwd,
            (x,z) => ImageHelpers.WorldToPxPy(x, z, (int)ctx.Background.Width, (int)ctx.Background.Height),
            "poiStandard"
            );
        }
        
        public sealed class FieldBoss : ILabelPass
        {
            public void Run(LabelContext ctx) =>
            LabelerFieldBoss.Label(
            ctx.Background,
            ctx.Pattern.pois,
            p => ImageHelpers.SelectNameXZ(p),
            BoxIndex(ctx.IndexLookup),
            System.IO.Path.Combine(ctx.Cwd, "appsettings.json"),
            ctx.Cwd,
            (x,z) => ImageHelpers.WorldToPxPy(x, z, (int)ctx.Background.Width, (int)ctx.Background.Height),
            "poiStandard"
            );
        }
        
        public sealed class NightBoss : ILabelPass
        {
            public void Run(LabelContext ctx) =>
            LabelerNightBoss.Label(
            ctx.Background,
            ctx.Pattern.pois,
            p => ImageHelpers.SelectNameXZ(p),
            BoxIndex(ctx.IndexLookup),
            System.IO.Path.Combine(ctx.Cwd, "appsettings.json"),
            ctx.Cwd,
            (x,z) => ImageHelpers.WorldToPxPy(x, z, (int)ctx.Background.Width, (int)ctx.Background.Height),
            "poiNightBoss"
            );
        }
        
        public sealed class ShiftingEarth : ILabelPass
        {
            public void Run(LabelContext ctx) =>
            LabelerShiftingEarth.Label(
            ctx.Background,
            specialValue: ctx.SpecialValue,
            appsettingsPath: System.IO.Path.Combine(ctx.Cwd, "appsettings.json"),
            cwd: ctx.Cwd
            );
        }
        
        public sealed class SpecialEvent : ILabelPass
        {
            public void Run(LabelContext ctx) =>
            LabelerSpecialEvent.Label(
            ctx.Background,
            patternId: ctx.PatternId,
            appsettingsPath: System.IO.Path.Combine(ctx.Cwd, "appsettings.json"),
            cwd: ctx.Cwd,
            bottomMarginPx: 24
            );
        }
        
        public sealed class SpecialIcon : ILabelPass
        {
            public void Run(LabelContext ctx) =>
            LabelerSpecialIcon.Label(
            ctx.Background,
            patternId: ctx.PatternId,
            appsettingsPath: System.IO.Path.Combine(ctx.Cwd, "appsettings.json"),
            cwd: ctx.Cwd
            );
        }
    }
}
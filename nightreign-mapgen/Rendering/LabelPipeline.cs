using ImageMagick;
using System.Collections.Generic;
using System.Text.Json;

namespace NightReign.MapGen.Rendering
{
    public interface ILabelPass
    {
        void Run(LabelContext ctx);
    }
    
    public sealed class LabelContext
    {
        public MagickImage Background { get; init; }
        public PatternDoc Pattern { get; init; }
        public IDictionary<string, IndexEntry> IndexLookup { get; init; }
        public string AppSettingsPath { get; init; }
        public string Cwd { get; init; }
        public string? SpecialValue { get; init; }
        public string PatternId { get; init; }
        public JsonElement AppSettingsRoot { get; init; }
    }
    
    public static class LabelPipeline
    {
        public static void Execute(IEnumerable<ILabelPass> passes, LabelContext ctx)
        {
            foreach (var pass in passes)
            {
                try { pass.Run(ctx); }
                catch (System.Exception ex) { System.Console.WriteLine($"[LabelPass:{pass.GetType().Name}] {ex.Message}"); }
            }
        }
    }
}
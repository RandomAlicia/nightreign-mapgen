using System;
using ImageMagick;
using ImageMagick.Drawing;

namespace NightReign.MapGen.Rendering
{
    /// <summary>
    /// Text renderer with exact centering and padded glow layers to avoid clipped/rectangular halos.
    /// Also clamps tiny alpha after blur to eliminate faint box edges.
    /// </summary>
    public static class TextRenderer
    {
        public sealed class GlowStyle
        {
            public int OffsetX { get; set; } = 0;
            public int OffsetY { get; set; } = 0;
            public int WideningRadius { get; set; } = 3;   // morphology iterations
            public double BlurRadius { get; set; } = 5.0;  // gaussian blur sigma
            public MagickColor Color { get; set; } = MagickColors.Black;
            public int OpacityPercent { get; set; } = 100; // 0..100
        }
        
        // Clamp any residual near-transparent alpha (< alphaFloor%) to fully transparent after blur.
        private static readonly Percentage AlphaFloorPercent = new Percentage(7); // 1%
        
        /// <summary>
        /// Simple crisp text (no glow). Centering uses an offscreen layer sized to the text bounds.
        /// </summary>
        public static void DrawSimpleText(
        MagickImage canvas,
        string text,
        string fontPath,
        int fontSizePx,
        int x, int y,
        MagickColor? fill   = null,
        MagickColor? stroke = null,
        double strokeWidth  = 0.0,
        bool centerX = false,
        bool centerY = false
        )
        {
            if (canvas == null || string.IsNullOrWhiteSpace(text)) return;
            
            var textFill   = fill   ?? MagickColors.White;
            var textStroke = stroke;
            
            if (centerX || centerY)
            {
                int width, height; double ascent, descent, lineHeight;
                MeasureSingleLine(fontPath, fontSizePx, text, out width, out height, out ascent, out descent, out lineHeight);
                
                using var layer = new MagickImage(MagickColors.Transparent, (uint)width, (uint)height);
                var d = new Drawables()
                .Font(fontPath)
                .FontPointSize(fontSizePx)
                .FillColor(textFill)
                .StrokeColor(textStroke ?? MagickColors.Transparent)
                .StrokeWidth(strokeWidth)
                .Text(0, (int)Math.Round(ascent), text);
                d.Draw(layer);
                
                int left = x - width / 2;
                int top  = y - height / 2;
                canvas.Composite(layer, left, top, CompositeOperator.Over);
            }
            else
            {
                var draw = new Drawables()
                .Font(fontPath)
                .FontPointSize(fontSizePx)
                .StrokeColor(textStroke ?? MagickColors.Transparent)
                .StrokeWidth(strokeWidth)
                .FillColor(textFill)
                .Text(x, y, text);
                draw.Draw(canvas);
            }
        }
        
        /// <summary>
        /// Single-line text with padded glow to avoid rectangle clipping artifacts, plus alpha clamping.
        /// </summary>
        public static void DrawTextWithGlow(
        MagickImage canvas,
        string text,
        string fontPath,
        int fontSizePx,
        int x, int y,
        GlowStyle? glow,
        MagickColor? textFill,
        MagickColor? textStroke,
        double textStrokeWidth  = 0.0,
        bool centerX = false,
        bool centerY = false
        )
        {
            if (canvas == null || string.IsNullOrWhiteSpace(text)) return;
            glow ??= new GlowStyle();
            var fill = textFill ?? MagickColors.White;
            
            // Measure single line bounds
            int width, height; double ascent, descent, lineHeight;
            MeasureSingleLine(fontPath, fontSizePx, text, out width, out height, out ascent, out descent, out lineHeight);
            
            // Target top-left if centering
            int left = centerX ? x - width / 2 : x;
            int top  = centerY ? y - height / 2 : y;
            
            // Padded glow layer so blur has room
            if (glow.OpacityPercent > 0)
            {
                int pad = ComputeGlowPad(glow);
                int gw = width  + pad * 2;
                int gh = height + pad * 2;
                
                using var glowLayer = new MagickImage(MagickColors.Transparent, (uint)gw, (uint)gh);
                glowLayer.VirtualPixelMethod = VirtualPixelMethod.Transparent;
                
                var gDraw = new Drawables()
                .Font(fontPath)
                .FontPointSize(fontSizePx)
                .FillColor(glow.Color)
                .StrokeColor(MagickColors.Transparent)
                .StrokeWidth(0)
                .Text(pad + glow.OffsetX, pad + (int)Math.Round(ascent) + glow.OffsetY, text);
                gDraw.Draw(glowLayer);
                
                if (glow.WideningRadius > 0)
                {
                    var morph = new MorphologySettings
                    {
                        Method = MorphologyMethod.Dilate,
                        Kernel = Kernel.Diamond,
                        Iterations = glow.WideningRadius
                    };
                    glowLayer.Morphology(morph);
                }
                if (glow.BlurRadius > 0)
                glowLayer.GaussianBlur(0, glow.BlurRadius);
                
                // clamp tiny alpha to fully transparent to avoid faint box edge
                glowLayer.Level(AlphaFloorPercent, new Percentage(100), Channels.Alpha);
                glowLayer.Evaluate(Channels.Alpha, EvaluateOperator.Multiply, glow.OpacityPercent / 100.0);
                
                // Composite centered (or not) with padding accounted
                int glLeft = left - pad;
                int glTop  = top  - pad;
                canvas.Composite(glowLayer, glLeft, glTop, CompositeOperator.Over);
            }
            
            // Crisp text drawn via centered layer when requested
            if (centerX || centerY)
            {
                using var textLayer = new MagickImage(MagickColors.Transparent, (uint)width, (uint)height);
                var d = new Drawables()
                .Font(fontPath)
                .FontPointSize(fontSizePx)
                .FillColor(fill)
                .StrokeColor(textStroke ?? MagickColors.Transparent)
                .StrokeWidth(textStrokeWidth)
                .Text(0, (int)Math.Round(ascent), text);
                d.Draw(textLayer);
                
                canvas.Composite(textLayer, left, top, CompositeOperator.Over);
            }
            else
            {
                // Baseline semantics (non-centered)
                var draw = new Drawables()
                .Font(fontPath)
                .FontPointSize(fontSizePx)
                .FillColor(fill)
                .StrokeColor(textStroke ?? MagickColors.Transparent)
                .StrokeWidth(textStrokeWidth)
                .Text(x, y, text);
                draw.Draw(canvas);
            }
        }
        
        /// <summary>
        /// Multi-line text with padded glow and exact centering on the block bounds, plus alpha clamping.
        /// </summary>
        public static void DrawMultilineWithGlow(
        MagickImage canvas,
        string text,
        string fontPath,
        int fontSizePx,
        int centerX, int centerY,
        GlowStyle? glow,
        MagickColor? textFill   = null,
        MagickColor? textStroke = null,
        double textStrokeWidth  = 0.0,
        int lineSpacingPx       = 4
        )
        {
            if (canvas == null || string.IsNullOrWhiteSpace(text)) return;
            glow ??= new GlowStyle();
            
            var lines = text.Replace("\r\n", "\n").Replace("\\n", "\n").Split('\n');
            if (lines.Length == 0) return;
            
            // Measure lines
            var widths = new int[lines.Length];
            var ascents = new double[lines.Length];
            var descents = new double[lines.Length];
            var lineHeights = new double[lines.Length];
            int maxW = 0;
            double totalH = 0.0;
            
            using (var meas = new MagickImage(MagickColors.Transparent, 1, 1))
            {
                meas.Settings.Font = fontPath;
                meas.Settings.FontPointsize = fontSizePx;
                
                for (int i = 0; i < lines.Length; i++)
                {
                    var m = meas.FontTypeMetrics(lines[i]);
                    widths[i] = (int)Math.Ceiling(m.TextWidth);
                    ascents[i] = m.Ascent;
                    descents[i] = m.Descent;
                    lineHeights[i] = (m.Ascent - m.Descent);
                    if (widths[i] > maxW) maxW = widths[i];
                    totalH += lineHeights[i];
                }
            }
            totalH += lineSpacingPx * (lines.Length - 1);
            
            int w = Math.Max(1, maxW);
            int h = Math.Max(1, (int)Math.Ceiling(totalH));
            
            // Compute top-left for the centered block
            int left = centerX - w / 2;
            int top  = centerY - h / 2;
            
            // Glow: padded to avoid clipped halo
            if (glow.OpacityPercent > 0)
            {
                int pad = ComputeGlowPad(glow);
                int gw = w + pad * 2;
                int gh = h + pad * 2;
                
                using var glowLayer = new MagickImage(MagickColors.Transparent, (uint)gw, (uint)gh);
                glowLayer.VirtualPixelMethod = VirtualPixelMethod.Transparent;
                
                var g = new Drawables()
                .Font(fontPath)
                .FontPointSize(fontSizePx)
                .FillColor(glow.Color)
                .StrokeColor(MagickColors.Transparent)
                .StrokeWidth(0);
                
                int y = pad + (int)Math.Round(ascents[0]);
                for (int i = 0; i < lines.Length; i++)
                {
                    int x = pad + (w - widths[i]) / 2;
                    g = g.Text(x + glow.OffsetX, y + glow.OffsetY, lines[i]);
                    if (i + 1 < lines.Length)
                    y += (int)Math.Round(lineHeights[i]) + lineSpacingPx;
                }
                g.Draw(glowLayer);
                
                if (glow.WideningRadius > 0)
                {
                    var morph = new MorphologySettings
                    {
                        Method = MorphologyMethod.Dilate,
                        Kernel = Kernel.Diamond,
                        Iterations = glow.WideningRadius
                    };
                    glowLayer.Morphology(morph);
                }
                if (glow.BlurRadius > 0)
                glowLayer.GaussianBlur(0, glow.BlurRadius);
                
                glowLayer.Level(AlphaFloorPercent, new Percentage(100), Channels.Alpha);
                glowLayer.Evaluate(Channels.Alpha, EvaluateOperator.Multiply, glow.OpacityPercent / 100.0);
                
                // account for padding when compositing
                canvas.Composite(glowLayer, left - pad, top - pad, CompositeOperator.Over);
            }
            
            // Text layer (no padding needed)
            using var textLayer = new MagickImage(MagickColors.Transparent, (uint)w, (uint)h);
            var d = new Drawables()
            .Font(fontPath)
            .FontPointSize(fontSizePx)
            .FillColor(textFill ?? MagickColors.White)
            .StrokeColor(textStroke ?? MagickColors.Transparent)
            .StrokeWidth(textStrokeWidth);
            
            int y2 = (int)Math.Round(ascents[0]);
            for (int i = 0; i < lines.Length; i++)
            {
                int x = (w - widths[i]) / 2;
                d = d.Text(x, y2, lines[i]);
                if (i + 1 < lines.Length)
                y2 += (int)Math.Round(lineHeights[i]) + lineSpacingPx;
            }
            d.Draw(textLayer);
            
            canvas.Composite(textLayer, left, top, CompositeOperator.Over);
        }
        
        // --- helpers ---
        private static int ComputeGlowPad(GlowStyle glow)
        {
            // generous padding: 4*sigma for blur, + 2*widening, + max offset, + 4 guard
            int blurPad = (int)Math.Ceiling(glow.BlurRadius * 6.0);
            int widenPad = glow.WideningRadius * 2;
            int offsetPad = Math.Max(Math.Abs(glow.OffsetX), Math.Abs(glow.OffsetY));
            return Math.Max(0, blurPad + widenPad + offsetPad + 8);
        }
        
        private static void MeasureSingleLine(string fontPath, int fontSizePx, string text,
        out int width, out int height, out double ascent, out double descent, out double lineHeight)
        {
            using var meas = new MagickImage(MagickColors.Transparent, 1, 1);
            meas.Settings.Font = fontPath;
            meas.Settings.FontPointsize = fontSizePx;
            var m = meas.FontTypeMetrics(text);
            width = (int)Math.Ceiling(m.TextWidth);
            ascent = m.Ascent;
            descent = m.Descent;
            lineHeight = (m.Ascent - m.Descent);
            height = (int)Math.Ceiling(lineHeight);
        }
    }
}
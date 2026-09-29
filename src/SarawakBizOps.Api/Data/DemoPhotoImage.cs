namespace SarawakBizOps.Api.Data;

/// <summary>
/// Draws a small, fictional "site photo" as an RGB PNG for the demo seed (Phase 9): a wall, a floor, a
/// machine cabinet with a gauge and a warning stripe, in colours that vary with the seed text. Nothing is
/// photographed, so the seed carries no personal data, no real site and no image files.
/// </summary>
public static class DemoPhotoImage
{
    private const int Width = 320;
    private const int Height = 240;

    public static byte[] Create(string seedText)
    {
        var seed = seedText.Aggregate(23, (h, c) => unchecked(h * 31 + c));
        var random = new Random(seed);
        var pixels = new byte[Width * Height * 3];

        // Wall (a soft vertical gradient) above a darker floor.
        var wall = (R: 150 + random.Next(80), G: 150 + random.Next(80), B: 150 + random.Next(80));
        var horizon = 150 + random.Next(30);
        for (var y = 0; y < Height; y++)
        {
            var shade = y < horizon ? 1.0 - 0.25 * y / horizon : 0.45;
            for (var x = 0; x < Width; x++)
            {
                Set(pixels, x, y, (int)(wall.R * shade), (int)(wall.G * shade), (int)(wall.B * shade));
            }
        }

        // The machine: a cabinet with a darker panel, a round gauge and a yellow/black warning stripe.
        var left = 40 + random.Next(120);
        var width = 110 + random.Next(40);
        var top = 40 + random.Next(30);
        var body = (R: 40 + random.Next(160), G: 60 + random.Next(120), B: 60 + random.Next(160));
        Rect(pixels, left, top, width, horizon + 30 - top, body.R, body.G, body.B);
        Rect(pixels, left + 12, top + 14, width - 24, 50, body.R / 2, body.G / 2, body.B / 2);
        Circle(pixels, left + width / 2, top + 100, 18, 235, 235, 225);
        Line(pixels, left + width / 2, top + 100, left + width / 2 + 12 - random.Next(24), top + 88, 200, 30, 30);
        for (var x = left; x < left + width; x++)
        {
            for (var y = horizon + 18; y < horizon + 30; y++)
            {
                var yellow = (x + y) / 8 % 2 == 0;
                Set(pixels, x, y, yellow ? 240 : 25, yellow ? 200 : 25, yellow ? 20 : 25);
            }
        }

        return DemoPng.Encode(Width, Height, DemoPng.Rgb, pixels);
    }

    private static void Rect(byte[] pixels, int x0, int y0, int w, int h, int r, int g, int b)
    {
        for (var y = y0; y < y0 + h; y++)
        {
            for (var x = x0; x < x0 + w; x++)
            {
                Set(pixels, x, y, r, g, b);
            }
        }
    }

    private static void Circle(byte[] pixels, int cx, int cy, int radius, int r, int g, int b)
    {
        for (var y = cy - radius; y <= cy + radius; y++)
        {
            for (var x = cx - radius; x <= cx + radius; x++)
            {
                if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= radius * radius)
                {
                    Set(pixels, x, y, r, g, b);
                }
            }
        }
    }

    private static void Line(byte[] pixels, int x0, int y0, int x1, int y1, int r, int g, int b)
    {
        var steps = Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0)) + 1;
        for (var s = 0; s <= steps; s++)
        {
            var x = x0 + (x1 - x0) * s / steps;
            var y = y0 + (y1 - y0) * s / steps;
            Set(pixels, x, y, r, g, b);
            Set(pixels, x + 1, y, r, g, b);
        }
    }

    private static void Set(byte[] pixels, int x, int y, int r, int g, int b)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
        {
            return;
        }

        var i = (y * Width + x) * 3;
        pixels[i] = (byte)Math.Clamp(r, 0, 255);
        pixels[i + 1] = (byte)Math.Clamp(g, 0, 255);
        pixels[i + 2] = (byte)Math.Clamp(b, 0, 255);
    }
}

namespace DeepcoolLm240.Usb;

public static class Lm240Protocol
{
    public const ushort VendorId = 0x3633;
    public const ushort ProductId = 0x0026;
    public const byte BulkOut = 0x01;
    public const int Width = 320;
    public const int Height = 240;
    public const int WriteTimeoutMs = 5000;

    public static readonly byte[] FrameHeader =
    [
        0xaa, 0x08, 0x00, 0x00, 0x01, 0x00, 0x58, 0x02, 0x00, 0x2c, 0x01, 0xbc, 0x11
    ];

    public static readonly byte[] InitQuery = [0xaa, 0x01, 0x00, 0x09, 0x29, 0x91];

    public static byte[] ColorBars()
    {
        (byte r, byte g, byte b)[] bars =
        [
            (255, 0, 0),
            (0, 255, 0),
            (0, 0, 255),
            (255, 255, 255),
            (0, 0, 0),
        ];

        var frame = new byte[Width * Height * 2];
        int barWidth = Width / bars.Length;
        int i = 0;
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                int bar = Math.Min(x / barWidth, bars.Length - 1);
                WritePixel(frame, ref i, bars[bar].r, bars[bar].g, bars[bar].b);
            }
        }

        return frame;
    }

    public static byte[] Solid(byte r, byte g, byte b)
    {
        var frame = new byte[Width * Height * 2];
        int i = 0;
        for (int p = 0; p < Width * Height; p++)
            WritePixel(frame, ref i, r, g, b);
        return frame;
    }

    public static byte[] FromBgra(byte[] bgra, int stride)
    {
        var frame = new byte[Width * Height * 2];
        int i = 0;
        for (int y = 0; y < Height; y++)
        {
            int row = y * stride;
            for (int x = 0; x < Width; x++)
            {
                int px = row + x * 4;
                byte b = bgra[px];
                byte g = bgra[px + 1];
                byte r = bgra[px + 2];
                WritePixel(frame, ref i, r, g, b);
            }
        }

        return frame;
    }

    public static void Rgb565ToBgra(byte[] rgb565, byte[] bgra)
    {
        int src = 0;
        int dst = 0;
        for (int p = 0; p < Width * Height; p++)
        {
            ushort pixel = (ushort)(rgb565[src] | (rgb565[src + 1] << 8));
            src += 2;
            byte r = (byte)(((pixel >> 11) & 0x1F) << 3);
            byte g = (byte)(((pixel >> 5) & 0x3F) << 2);
            byte b = (byte)((pixel & 0x1F) << 3);
            bgra[dst++] = b;
            bgra[dst++] = g;
            bgra[dst++] = r;
            bgra[dst++] = 255;
        }
    }

    static void WritePixel(byte[] frame, ref int i, byte r, byte g, byte b)
    {
        ushort pixel = (ushort)(((r >> 3) << 11) | ((g >> 2) << 5) | (b >> 3));
        frame[i++] = (byte)pixel;
        frame[i++] = (byte)(pixel >> 8);
    }
}

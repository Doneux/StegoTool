using System.Buffers.Binary;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace StegoTool;

/// <summary>
/// LSB steganography: each pixel's R, G and B values give up their lowest bit,
/// so one pixel carries 3 bits and no value changes by more than 1.
///
/// Layout of the hidden data:
///   [4 bytes] magic "STG1"           - tells us an image actually has a message
///   [4 bytes] payload length (big-endian)
///   [N bytes] message as UTF-8
/// The length header replaces the old delimiter approach, so any text
/// (including the delimiter character) can be hidden.
/// </summary>
public static class Stego
{
    static readonly byte[] Magic = "STG1"u8.ToArray();
    const int HeaderBytes = 8;

    /// <summary>How many message bytes fit in an image of this size.</summary>
    public static int CapacityBytes(int width, int height)
    {
        long bytes = (long)width * height * 3 / 8 - HeaderBytes;
        return (int)Math.Clamp(bytes, 0, int.MaxValue);
    }

    /// <summary>Returns a new image with the message hidden in it. The source is not modified.</summary>
    public static Bitmap Encode(Bitmap source, string message)
    {
        byte[] payload = Encoding.UTF8.GetBytes(message);
        int capacity = CapacityBytes(source.Width, source.Height);
        if (payload.Length > capacity)
            throw new InvalidOperationException(
                $"The message is {payload.Length:N0} bytes, but this image can only hold {capacity:N0}.");

        byte[] data = new byte[HeaderBytes + payload.Length];
        Magic.CopyTo(data, 0);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(4), payload.Length);
        payload.CopyTo(data, HeaderBytes);

        byte[] px = ReadPixels(source);
        int slot = 0;
        foreach (byte b in data)
        {
            for (int bit = 7; bit >= 0; bit--, slot++)
            {
                int i = SlotIndex(slot);
                px[i] = (byte)((px[i] & 0xFE) | ((b >> bit) & 1));
            }
        }

        var result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        WritePixels(result, px);
        return result;
    }

    /// <summary>Returns the hidden message, or null if the image doesn't contain one.</summary>
    public static string? Decode(Bitmap image)
    {
        long totalSlots = (long)image.Width * image.Height * 3;
        if (totalSlots < HeaderBytes * 8) return null;

        byte[] px = ReadPixels(image);
        int slot = 0;

        byte NextByte()
        {
            int value = 0;
            for (int bit = 0; bit < 8; bit++, slot++)
                value = (value << 1) | (px[SlotIndex(slot)] & 1);
            return (byte)value;
        }

        byte[] header = new byte[HeaderBytes];
        for (int i = 0; i < HeaderBytes; i++) header[i] = NextByte();

        if (!header.AsSpan(0, 4).SequenceEqual(Magic)) return null;
        int length = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(4));
        if (length < 0 || length > CapacityBytes(image.Width, image.Height)) return null;

        byte[] payload = new byte[length];
        for (int i = 0; i < length; i++) payload[i] = NextByte();
        return Encoding.UTF8.GetString(payload);
    }

    /// <summary>
    /// A dimmed grayscale copy of <paramref name="a"/> with every pixel that differs in <paramref name="b"/> painted red.
    /// </summary>
    public static Bitmap DiffMap(Bitmap a, Bitmap b, out int changedPixels)
    {
        byte[] pa = ReadPixels(a);
        byte[] pb = ReadPixels(b);
        byte[] result = new byte[pa.Length];
        changedPixels = 0;

        for (int i = 0; i < pa.Length; i += 4) // BGRA
        {
            bool changed = pa[i] != pb[i] || pa[i + 1] != pb[i + 1] || pa[i + 2] != pb[i + 2];
            if (changed)
            {
                changedPixels++;
                result[i] = 0; result[i + 1] = 0; result[i + 2] = 255;
            }
            else
            {
                byte gray = (byte)((pa[i] + pa[i + 1] + pa[i + 2]) / 9); // grayscale at 1/3 brightness
                result[i] = result[i + 1] = result[i + 2] = gray;
            }
            result[i + 3] = 255;
        }

        var bmp = new Bitmap(a.Width, a.Height, PixelFormat.Format32bppArgb);
        WritePixels(bmp, result);
        return bmp;
    }

    // Slot n -> pixel n/3, channel n%3. Pixels are stored B,G,R,A so channels 0-2 are B,G,R and alpha is never touched.
    static int SlotIndex(int slot) => (slot / 3) * 4 + slot % 3;

    static byte[] ReadPixels(Bitmap bmp)
    {
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
                                ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int rowBytes = bmp.Width * 4;
            byte[] buffer = new byte[rowBytes * bmp.Height];
            for (int y = 0; y < bmp.Height; y++)
                Marshal.Copy(data.Scan0 + y * data.Stride, buffer, y * rowBytes, rowBytes);
            return buffer;
        }
        finally { bmp.UnlockBits(data); }
    }

    static void WritePixels(Bitmap bmp, byte[] buffer)
    {
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
                                ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            int rowBytes = bmp.Width * 4;
            for (int y = 0; y < bmp.Height; y++)
                Marshal.Copy(buffer, y * rowBytes, data.Scan0 + y * data.Stride, rowBytes);
        }
        finally { bmp.UnlockBits(data); }
    }
}

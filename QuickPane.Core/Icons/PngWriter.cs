using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace QuickPane.Core.Icons;

/// <summary>
/// アイコン画像 (32bit BGRA、アルファは非乗算)。
/// </summary>
/// <param name="Width">幅</param>
/// <param name="Height">高さ</param>
/// <param name="Bgra">ピクセルデータ (上から下、1 ピクセル 4 バイト)</param>
public sealed record IconBitmap(int Width, int Height, byte[] Bgra);

/// <summary>
/// IconBitmap を PNG 形式で書き込む (RGBA 8bit、フィルタなし)。
/// </summary>
public static class PngWriter
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static readonly uint[] CrcTable = CreateCrcTable();

    /// <summary>
    /// PNG を書き込む。
    /// </summary>
    /// <param name="stream">書き込み先</param>
    /// <param name="bitmap">画像</param>
    public static void Write(Stream stream, IconBitmap bitmap)
    {
        stream.Write(Signature);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0), bitmap.Width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), bitmap.Height);
        header[8] = 8; // ビット深度
        header[9] = 6; // カラータイプ: RGBA
        WriteChunk(stream, "IHDR", header);

        using (var data = new MemoryStream())
        {
            using (var zlib = new ZLibStream(data, CompressionLevel.Optimal, leaveOpen: true))
            {
                var row = new byte[1 + (bitmap.Width * 4)];
                for (var y = 0; y < bitmap.Height; y++)
                {
                    row[0] = 0; // フィルタなし
                    var offset = y * bitmap.Width * 4;
                    for (var x = 0; x < bitmap.Width; x++)
                    {
                        var src = offset + (x * 4);
                        var dst = 1 + (x * 4);
                        row[dst] = bitmap.Bgra[src + 2];
                        row[dst + 1] = bitmap.Bgra[src + 1];
                        row[dst + 2] = bitmap.Bgra[src];
                        row[dst + 3] = bitmap.Bgra[src + 3];
                    }

                    zlib.Write(row);
                }
            }

            WriteChunk(stream, "IDAT", data.ToArray());
        }

        WriteChunk(stream, "IEND", []);
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, data.Length);
        stream.Write(buffer);

        var typeBytes = Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);

        var crc = UpdateCrc(0xFFFFFFFFu, typeBytes);
        crc = UpdateCrc(crc, data) ^ 0xFFFFFFFFu;
        BinaryPrimitives.WriteUInt32BigEndian(buffer, crc);
        stream.Write(buffer);
    }

    private static uint UpdateCrc(uint crc, byte[] data)
    {
        foreach (var b in data)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }

    private static uint[] CreateCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}

using System;
using System.IO;
using System.IO.Compression;

namespace AyaHitmarker.LogicTests;

/// <summary>
/// 最小的 PNG 编码器（8 位 RGB，无滤波）。仅供 <c>--render-icons</c> 把程序化图标渲染出来肉眼检查，
/// 不是插件的一部分 —— 仓库刻意不引任何图像库。
/// </summary>
internal static class PngWriter
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    private static uint Crc(byte[] type, byte[] data)
    {
        uint c = 0xFFFFFFFFu;
        foreach (byte b in type) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        foreach (byte b in data) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }

    private static void WriteChunk(Stream s, string type, byte[] data)
    {
        byte[] t = System.Text.Encoding.ASCII.GetBytes(type);
        WriteBe(s, (uint)data.Length);
        s.Write(t, 0, 4);
        s.Write(data, 0, data.Length);
        WriteBe(s, Crc(t, data));
    }

    private static void WriteBe(Stream s, uint v)
    {
        s.WriteByte((byte)(v >> 24));
        s.WriteByte((byte)(v >> 16));
        s.WriteByte((byte)(v >> 8));
        s.WriteByte((byte)v);
    }

    /// <summary><paramref name="rgb"/> 是自上而下的 RGB 像素（每像素 3 字节）。</summary>
    internal static void Write(string path, int width, int height, byte[] rgb)
    {
        using var raw = new MemoryStream();
        for (int y = 0; y < height; y++)
        {
            raw.WriteByte(0);   // filter: none
            raw.Write(rgb, y * width * 3, width * 3);
        }

        using var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            raw.Position = 0;
            raw.CopyTo(z);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var f = File.Create(path);
        f.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);

        var ihdr = new byte[13];
        ihdr[0] = (byte)(width >> 24); ihdr[1] = (byte)(width >> 16); ihdr[2] = (byte)(width >> 8); ihdr[3] = (byte)width;
        ihdr[4] = (byte)(height >> 24); ihdr[5] = (byte)(height >> 16); ihdr[6] = (byte)(height >> 8); ihdr[7] = (byte)height;
        ihdr[8] = 8;   // bit depth
        ihdr[9] = 2;   // colour type: RGB
        WriteChunk(f, "IHDR", ihdr);
        WriteChunk(f, "IDAT", compressed.ToArray());
        WriteChunk(f, "IEND", Array.Empty<byte>());
    }
}

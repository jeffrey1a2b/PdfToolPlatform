using System.Buffers.Binary;
using PdfToolPlatform.Core.Fonts;
using Xunit;

namespace PdfToolPlatform.Tests;

public class TrueTypeCollectionExtractorTests
{
    private const uint TtcTag = 0x74746366; // 'ttcf'

    [Fact]
    public void ExtractFontBytes_PlainTtf_ReturnsUnchanged()
    {
        // 一般.ttf/.otf(非ttc容器),不應該被動到任何bytes
        var original = BuildPlainSfnt();

        var result = TrueTypeCollectionExtractor.ExtractFontBytes(original);

        Assert.Equal(original, result);
    }

    [Fact]
    public void ExtractFontBytes_TtcContainer_ProducesStandaloneSfntWithSameTableData()
    {
        var (ttcBytes, tableA, tableB) = BuildFakeTtc();

        var result = TrueTypeCollectionExtractor.ExtractFontBytes(ttcBytes);

        // 輸出不應該再是ttc容器
        Assert.NotEqual(TtcTag, ReadUInt32(result, 0));

        // sfntVersion / numTables 要對得上原本子字型的表格目錄
        Assert.Equal(0x00010000u, ReadUInt32(result, 0));
        ushort numTables = ReadUInt16(result, 4);
        Assert.Equal(2, numTables);

        var extracted = ReadTables(result, numTables);
        Assert.Equal(tableA, extracted[TagOf("tagA")]);
        Assert.Equal(tableB, extracted[TagOf("tagB")]);
    }

    [Fact]
    public void ExtractFontBytes_TtcWithMultipleFaces_DefaultsToFirstFace()
    {
        var (ttcBytes, tableA, _) = BuildFakeTtc();

        // faceIndex超出範圍時應退回第一個子字型,而不是丟例外
        var result = TrueTypeCollectionExtractor.ExtractFontBytes(ttcBytes, faceIndex: 99);

        ushort numTables = ReadUInt16(result, 4);
        var extracted = ReadTables(result, numTables);
        Assert.Equal(tableA, extracted[TagOf("tagA")]);
    }

    private static (byte[] ttc, byte[] tableA, byte[] tableB) BuildFakeTtc()
    {
        var tableA = System.Text.Encoding.ASCII.GetBytes("Hello"); // 5 bytes,故意不是4的倍數
        var tableB = System.Text.Encoding.ASCII.GetBytes("World!!!"); // 8 bytes

        const int numTables = 2;
        int directoryOffset = 16; // ttc header(12) + offsetTable(1*4)
        int tableDirSize = 12 + numTables * 16;
        int dataStart = directoryOffset + tableDirSize;

        int offsetA = dataStart;
        int offsetB = offsetA + Align4(tableA.Length);

        using var ms = new MemoryStream();
        WriteUInt32(ms, TtcTag);
        WriteUInt16(ms, 1); // majorVersion
        WriteUInt16(ms, 0); // minorVersion
        WriteUInt32(ms, 1); // numFonts
        WriteUInt32(ms, (uint)directoryOffset); // OffsetTable[0]

        // ---- 子字型的table directory ----
        WriteUInt32(ms, 0x00010000); // sfntVersion
        WriteUInt16(ms, numTables);
        WriteUInt16(ms, 0); // searchRange (測試不需要正確值)
        WriteUInt16(ms, 0); // entrySelector
        WriteUInt16(ms, 0); // rangeShift

        WriteUInt32(ms, TagOf("tagA"));
        WriteUInt32(ms, 0); // checkSum
        WriteUInt32(ms, (uint)offsetA);
        WriteUInt32(ms, (uint)tableA.Length);

        WriteUInt32(ms, TagOf("tagB"));
        WriteUInt32(ms, 0);
        WriteUInt32(ms, (uint)offsetB);
        WriteUInt32(ms, (uint)tableB.Length);

        // ---- 實際表格資料 ----
        ms.Write(tableA, 0, tableA.Length);
        PadTo4(ms, tableA.Length);
        ms.Write(tableB, 0, tableB.Length);
        PadTo4(ms, tableB.Length);

        return (ms.ToArray(), tableA, tableB);
    }

    private static byte[] BuildPlainSfnt()
    {
        var tableA = System.Text.Encoding.ASCII.GetBytes("Plain");
        const int numTables = 1;
        int tableDirSize = 12 + numTables * 16;
        int offsetA = tableDirSize;

        using var ms = new MemoryStream();
        WriteUInt32(ms, 0x00010000);
        WriteUInt16(ms, numTables);
        WriteUInt16(ms, 0);
        WriteUInt16(ms, 0);
        WriteUInt16(ms, 0);

        WriteUInt32(ms, TagOf("tagA"));
        WriteUInt32(ms, 0);
        WriteUInt32(ms, (uint)offsetA);
        WriteUInt32(ms, (uint)tableA.Length);

        ms.Write(tableA, 0, tableA.Length);
        PadTo4(ms, tableA.Length);

        return ms.ToArray();
    }

    private static Dictionary<uint, byte[]> ReadTables(byte[] sfnt, int numTables)
    {
        var result = new Dictionary<uint, byte[]>();
        int recordStart = 12;
        for (int i = 0; i < numTables; i++)
        {
            int rec = recordStart + i * 16;
            uint tag = ReadUInt32(sfnt, rec);
            uint offset = ReadUInt32(sfnt, rec + 8);
            uint length = ReadUInt32(sfnt, rec + 12);
            var data = new byte[length];
            Array.Copy(sfnt, offset, data, 0, length);
            result[tag] = data;
        }
        return result;
    }

    private static int Align4(int length) => (length + 3) & ~3;

    private static void PadTo4(Stream s, int length)
    {
        int pad = Align4(length) - length;
        for (int i = 0; i < pad; i++) s.WriteByte(0);
    }

    private static uint TagOf(string fourChars) =>
        BinaryPrimitives.ReadUInt32BigEndian(System.Text.Encoding.ASCII.GetBytes(fourChars));

    private static uint ReadUInt32(byte[] buffer, int offset) =>
        BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(offset, 4));

    private static ushort ReadUInt16(byte[] buffer, int offset) =>
        BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(offset, 2));

    private static void WriteUInt32(Stream s, uint value)
    {
        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buf, value);
        s.Write(buf);
    }

    private static void WriteUInt16(Stream s, ushort value)
    {
        Span<byte> buf = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(buf, value);
        s.Write(buf);
    }
}

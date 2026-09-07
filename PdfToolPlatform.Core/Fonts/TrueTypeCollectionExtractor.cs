using System.Buffers.Binary;

namespace PdfToolPlatform.Core.Fonts;

/// <summary>
/// PDFsharp 的字型解析器只認得單一字型檔(.ttf / .otf)的 SFNT 格式,不支援
/// TrueType Collection(.ttc)這種"一個檔案裡塞多個字型、彼此共用部分表格資料"的容器格式。
/// Windows上許多中文字型(例如 msjh.ttc、mingliu.ttc、simsun.ttc)都是 .ttc 格式。
///
/// 如果把 .ttc 的原始 bytes 直接丟給 PDFsharp,它會把 .ttc 檔頭(ttcf 標籤 + 多字型偏移表)
/// 誤判成一般 SFNT 的表格目錄去解析,讀出來的表格 offset/length 全部錯亂,
/// 最終在量測/繪製文字時(MeasureString / DrawString)才會噴出
/// "Object reference not set to an instance of an object." 這種 NullReferenceException。
///
/// 這裡在讀取字型檔時偵測是否為 .ttc,若是則依 TrueType Collection 規格從中抽取
/// 指定索引的子字型,重新組成一份獨立、合法的 SFNT(.ttf相容)資料,PDFsharp才能正確解析。
/// 若本來就是一般 .ttf/.otf(非 ttc 容器),原樣回傳,不做任何事。
/// </summary>
internal static class TrueTypeCollectionExtractor
{
    private const uint TtcTag = 0x74746366; // 'ttcf'

    public static byte[] ExtractFontBytes(byte[] fileBytes, int faceIndex = 0)
    {
        if (fileBytes.Length < 16 || ReadUInt32(fileBytes, 0) != TtcTag)
        {
            // 不是ttc容器(例如Arial/Times這種一般.ttf),原樣回傳即可
            return fileBytes;
        }

        uint numFonts = ReadUInt32(fileBytes, 8);
        if (numFonts == 0)
            return fileBytes;

        if (faceIndex < 0 || faceIndex >= numFonts)
            faceIndex = 0;

        uint directoryOffset = ReadUInt32(fileBytes, 12 + faceIndex * 4);

        uint sfntVersion = ReadUInt32(fileBytes, (int)directoryOffset);
        ushort numTables = ReadUInt16(fileBytes, (int)directoryOffset + 4);

        var tags = new uint[numTables];
        var checkSums = new uint[numTables];
        var srcOffsets = new uint[numTables];
        var lengths = new uint[numTables];

        int recordStart = (int)directoryOffset + 12;
        for (int i = 0; i < numTables; i++)
        {
            int rec = recordStart + i * 16;
            tags[i] = ReadUInt32(fileBytes, rec);
            checkSums[i] = ReadUInt32(fileBytes, rec + 4);
            srcOffsets[i] = ReadUInt32(fileBytes, rec + 8);
            lengths[i] = ReadUInt32(fileBytes, rec + 12);
        }

        // 依SFNT規格計算searchRange/entrySelector/rangeShift
        ushort entrySelector = 0;
        while ((1 << (entrySelector + 1)) <= numTables) entrySelector++;
        ushort searchRange = (ushort)((1 << entrySelector) * 16);
        ushort rangeShift = (ushort)(numTables * 16 - searchRange);

        int headerSize = 12 + numTables * 16;

        // ttc內的table offset是相對於整個ttc檔案(表格可能在子字型間共用),
        // 抽出獨立字型時要把實際資料複製出來,並在新檔案裡重新配置offset(4-byte對齊)。
        var tableData = new byte[numTables][];
        var newOffsets = new uint[numTables];
        int cursor = headerSize;
        for (int i = 0; i < numTables; i++)
        {
            var data = new byte[lengths[i]];
            Array.Copy(fileBytes, srcOffsets[i], data, 0, lengths[i]);
            tableData[i] = data;
            newOffsets[i] = (uint)cursor;
            cursor += (int)((lengths[i] + 3) & ~3u);
        }

        using var output = new MemoryStream(cursor);
        WriteUInt32(output, sfntVersion);
        WriteUInt16(output, numTables);
        WriteUInt16(output, searchRange);
        WriteUInt16(output, entrySelector);
        WriteUInt16(output, rangeShift);

        for (int i = 0; i < numTables; i++)
        {
            WriteUInt32(output, tags[i]);
            WriteUInt32(output, checkSums[i]);
            WriteUInt32(output, newOffsets[i]);
            WriteUInt32(output, lengths[i]);
        }

        for (int i = 0; i < numTables; i++)
        {
            output.Write(tableData[i], 0, tableData[i].Length);
            int pad = (int)((lengths[i] + 3) & ~3u) - (int)lengths[i];
            for (int p = 0; p < pad; p++) output.WriteByte(0);
        }

        return output.ToArray();
    }

    private static uint ReadUInt32(byte[] buffer, int offset) =>
        BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(offset, 4));

    private static ushort ReadUInt16(byte[] buffer, int offset) =>
        BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(offset, 2));

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buf, value);
        stream.Write(buf);
    }

    private static void WriteUInt16(Stream stream, ushort value)
    {
        Span<byte> buf = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(buf, value);
        stream.Write(buf);
    }
}

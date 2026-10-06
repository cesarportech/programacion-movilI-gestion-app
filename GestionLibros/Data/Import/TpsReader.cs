using System.Text;

namespace GestionLibros.Data.Import;

// Read-only reader for Clarion TopSpeed (.tps) files, enough to extract table definitions and data rows.
public sealed class TpsReader
{
    public sealed record Field(int Type, int Offset, string Name, int Elements, int Length, int Decimals);
    public sealed record Table(int Number, string Name, int RecordLength, List<Field> Fields);
    public sealed record Row(int Table, int RecordNumber, byte[] Data);

    private static readonly Encoding Latin = Encoding.Latin1;
    private readonly Dictionary<int, string> names = [];
    private readonly Dictionary<int, SortedDictionary<int, byte[]>> definitionBlocks = [];
    private readonly Dictionary<(int, int), byte[]> rows = [];
    public Dictionary<int, Table> Tables { get; } = [];
    public IEnumerable<Row> Rows => rows.OrderBy(r => r.Key.Item2).Select(r => new Row(r.Key.Item1, r.Key.Item2, r.Value));

    public TpsReader(byte[] file)
    {
        if (file.Length < 0x200 || Latin.GetString(file, 14, 4) != "tOpS") throw new InvalidDataException("No es un archivo TopSpeed.");
        // Header: 60 block start/end page references, in 0x100 units after the 0x200-byte header.
        var visited = new HashSet<int>();
        for (var i = 0; i < 60; i++)
        {
            var start = (BitConverter.ToInt32(file, 0x20 + i * 4) << 8) + 0x200;
            var end = (BitConverter.ToInt32(file, 0x20 + 240 + i * 4) << 8) + 0x200;
            for (var ofs = start; ofs < end && ofs + 13 <= file.Length;)
            {
                if (BitConverter.ToInt32(file, ofs) != ofs) { ofs += 0x100; continue; }
                var size = BitConverter.ToUInt16(file, ofs + 4);
                if (size < 13) { ofs += 0x100; continue; }
                if (visited.Add(ofs)) ReadPage(file, ofs);
                ofs += (size + 0xFF) & ~0xFF;
            }
        }
        foreach (var (table, blocks) in definitionBlocks)
            Tables[table] = ParseDefinition(table, names.GetValueOrDefault(table, $"T{table}"), blocks.Values.SelectMany(b => b).ToArray());
    }

    private void ReadPage(byte[] file, int ofs)
    {
        var size = BitConverter.ToUInt16(file, ofs + 4);
        var uncompressed = BitConverter.ToUInt16(file, ofs + 6);
        var body = file.AsSpan(ofs + 13, Math.Min(size - 13, file.Length - ofs - 13)).ToArray();
        var data = size == uncompressed ? body : Unrle(body, uncompressed - 13);
        var pos = 0;
        byte[] previous = [];
        int recordLength = 0, headerLength = 0;
        while (pos < data.Length)
        {
            var flags = data[pos++];
            if ((flags & 0x80) != 0) { recordLength = BitConverter.ToUInt16(data, pos); pos += 2; }
            if ((flags & 0x40) != 0) { headerLength = BitConverter.ToUInt16(data, pos); pos += 2; }
            var copy = flags & 0x3F;
            if (recordLength <= 0 || copy > previous.Length || pos + recordLength - copy > data.Length) break;
            var record = new byte[recordLength];
            Array.Copy(previous, record, copy);
            Array.Copy(data, pos, record, copy, recordLength - copy);
            pos += recordLength - copy;
            previous = record;
            Interpret(record, headerLength);
        }
    }

    private static byte[] Unrle(byte[] input, int expected)
    {
        var output = new List<byte>(Math.Max(expected, 0));
        var pos = 0;
        while (pos < input.Length)
        {
            int skip = input[pos++];
            if (skip == 0) break;
            if (skip > 0x7F) skip = (skip & 0x7F) | (input[pos++] << 7);
            for (var i = 0; i < skip && pos < input.Length; i++) output.Add(input[pos++]);
            if (pos >= input.Length) break;
            var repeat = output[^1];
            int count = input[pos++];
            if (count > 0x7F) count = (count & 0x7F) | (input[pos++] << 7);
            for (var i = 0; i < count; i++) output.Add(repeat);
        }
        return output.ToArray();
    }

    private static int BigEndian(byte[] b, int at) => (b[at] << 24) | (b[at + 1] << 16) | (b[at + 2] << 8) | b[at + 3];

    private void Interpret(byte[] record, int headerLength)
    {
        if (headerLength > record.Length || headerLength < 5) return;
        if (record[0] == 0xFE)
        {
            if (record.Length >= headerLength + 4) names[BigEndian(record, headerLength)] = Latin.GetString(record, 1, headerLength - 1);
            return;
        }
        var table = BigEndian(record, 0);
        switch (record[4])
        {
            case 0xF3 when headerLength >= 9:
                rows[(table, BigEndian(record, 5))] = record[headerLength..];
                break;
            case 0xFA when headerLength >= 7:
                if (!definitionBlocks.TryGetValue(table, out var blocks)) definitionBlocks[table] = blocks = [];
                blocks[BitConverter.ToUInt16(record, 5)] = record[headerLength..];
                break;
        }
    }

    private static Table ParseDefinition(int number, string name, byte[] d)
    {
        var pos = 2;
        int Short() { var v = BitConverter.ToUInt16(d, pos); pos += 2; return v; }
        string Text() { var start = pos; while (pos < d.Length && d[pos] != 0) pos++; return Latin.GetString(d, start, pos++ - start); }
        var recordLength = Short(); var fieldCount = Short(); Short(); Short();
        var fields = new List<Field>();
        for (var i = 0; i < fieldCount && pos < d.Length; i++)
        {
            int type = d[pos++]; var offset = Short(); var fieldName = Text(); var elements = Short(); var length = Short(); Short(); Short();
            var decimals = 0;
            if (type == 0x0A) { decimals = d[pos]; pos += 2; }
            else if (type is 0x12 or 0x13 or 0x14) { Short(); if (Text().Length == 0) pos++; }
            fields.Add(new(type, offset, fieldName.Contains(':') ? fieldName[(fieldName.IndexOf(':') + 1)..] : fieldName, elements, length, decimals));
        }
        return new(number, name, recordLength, fields);
    }

    public static object? Value(Field field, byte[] data)
    {
        if (field.Offset + field.Length > data.Length) return null;
        var o = field.Offset;
        return field.Type switch
        {
            0x01 => data[o],
            0x02 => BitConverter.ToInt16(data, o),
            0x03 => BitConverter.ToUInt16(data, o),
            0x04 => DateOrNull(data, o),
            0x06 => BitConverter.ToInt32(data, o),
            0x07 => BitConverter.ToUInt32(data, o),
            0x08 => BitConverter.ToSingle(data, o),
            0x09 => BitConverter.ToDouble(data, o),
            0x0A => Bcd(data, o, field.Length, field.Decimals),
            0x12 => Latin.GetString(data, o, field.Length).TrimEnd(' ', '\0'),
            0x13 => Latin.GetString(data, o, field.Length).Split('\0')[0].TrimEnd(),
            0x14 => Latin.GetString(data, o + 1, Math.Min(data[o], field.Length - 1)),
            _ => null,
        };
    }

    private static DateTime? DateOrNull(byte[] data, int o)
    {
        var year = BitConverter.ToUInt16(data, o + 2); int month = data[o + 1], day = data[o];
        return year is >= 1800 and <= 2200 && month is >= 1 and <= 12 && day is >= 1 and <= 31 && day <= DateTime.DaysInMonth(year, month) ? new DateTime(year, month, day) : null;
    }

    // Clarion LONG dates count days from 28 Dec 1800.
    public static DateTime? ClarionDate(int days) => days is > 4 and < 200000 ? new DateTime(1800, 12, 28).AddDays(days) : null;

    private static decimal Bcd(byte[] data, int o, int length, int decimals)
    {
        var digits = new StringBuilder();
        for (var i = 0; i < length; i++) digits.Append(data[o + i].ToString("X2"));
        var negative = digits[0] != '0';
        var text = digits.ToString(1, digits.Length - 1);
        var value = decimal.Parse(text);
        for (var i = 0; i < decimals; i++) value /= 10;
        return negative ? -value : value;
    }
}

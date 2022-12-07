using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Seismic;

public enum DataFormat
{
    IBMFLT32 = 1,    // big endian IBM float
    INT32 = 2,
    INT16 = 3,
    FIXPTGN32 = 4,   // 4 byte fixed point with gain (obsolete)
    IEEEFLT32 = 5,   // big endian IEEE float
    INT8 = 8         // all integers are big endian for segy (unix, IBM), little endian on Intel (hence in code)
}

public class SegyReader
{
    // private bool _isEbcdic = true;

    public string FileName { get; }
    public string Header { get; private set; }
    public DataFormat Format { get; set; }
    public int SampleSize { get; set; }
    public int SampleInterval { get; set; }
    public int TraceSize { get; set; }    
    public int InLineSize { get; set; }
    public int InLineStep { get; set; } = 1;
    public int InlineBegin { get; set; }
    public int InlineEnd { get; set; }
    public int CrossLineSize { get; set; }
    public int CrossLineStep { get; set; } = 1;
    public int CrossLineBegin { get; set; }
    public int CrossLineEnd { get; set; }
    public float XBegin { get; private set; }
    public float XEnd { get; private set; }
    public float YBegin { get; private set; }
    public float YEnd { get; private set; }
    public float ZBegin { get; private set; }
    public float ZEnd { get; private set; }
    public float MinValue { get; set; }
    public float MaxValue { get; set; }

    private const int MB = 1024 * 1024;
    private const int GB = 1024 * MB;
    private const int COLUMN_SIZE = 80;
    private const int ROW_SIZE = 40;
    private const int HEADER_TEXT_SIZE = 3200;
    private const int HEADER_BINARY_SIZE = 400;
    private const int HEADER_SIZE = HEADER_TEXT_SIZE + HEADER_BINARY_SIZE;
    private const int TRACE_HEADER_SIZE = 240;
    private const int FORMAT_INDEX = 24;
    private const int SAMPLE_SIZE_INDEX = 20;
    private const int MAX_SAMPLE_SIZE = 65536;//2^16
    private int _valueSize = 4;
    private int _traceByteSize;
    private long _fileSize;
    private bool _isLittleEndian = false;
    private DataFormat _format;
    private static readonly byte[] _traceHeaderIndex = new byte[] { 7, 4, 8, 2, 4, 46, 5, 12, 1, 2, 2 };

    private int GetValueSize() => _format switch
    {
        DataFormat.INT8 => 1,
        DataFormat.INT16 => 2,
        _ => 4
    };

    public SegyReader(string fileName)
    {
        FileName = fileName;
        ParseHeader();
    }

    public async Task<float[][]> ReadTraceAsync(int minSize = MB)
    {
        await using var s = new FileStream(FileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var reader = PipeReader.Create(s);
        var data = await reader.ReadAsync();
        reader.AdvanceTo(data.Buffer.GetPosition(HEADER_SIZE));
        var traces = new float[TraceSize][];
        var length = _traceByteSize - TRACE_HEADER_SIZE;
        int i = 0;
        while (true)
        {
            data = await reader.ReadAtLeastAsync(minSize);
            var amount = (int) (data.Buffer.Length / _traceByteSize);
            var size = amount * _traceByteSize;
            var buffer = data.Buffer.Slice(0, size);
            Parallel.For(0, amount, (j) =>
            {
                var start = TRACE_HEADER_SIZE + _traceByteSize * j;
                ReadOnlySpan<byte> bytes = buffer.Slice(start, length).ToArray();
                traces[j + i] = ParseValue(ref bytes);
            });
            i += amount;

            reader.AdvanceTo(buffer.End);
            if (data.IsCompleted)
                break;
            
        }
        await reader.CompleteAsync();
        return traces;
    }

    public float[][] ReadAllTraces()
    {
        if (_fileSize > (2L * GB))
            return Array.Empty<float[]>();
        var buffer = File.ReadAllBytes(FileName);
        var traces = new float[TraceSize][];
        Parallel.For(0, TraceSize, (i) => {
            ReadOnlySpan<byte> bytes = buffer;
            var v = bytes.Slice(HEADER_SIZE + _traceByteSize * i + TRACE_HEADER_SIZE, _traceByteSize - TRACE_HEADER_SIZE);
            traces[i] = ParseValue(ref v);
        });
        return traces;
    }

    private float[] ParseValue(ref ReadOnlySpan<byte> values)
    {
        var trace = new float[SampleSize];
        for (int i = 0; i < trace.Length; i++)
            trace[i] = ToValue(values.Slice(i * 4, 4));
        return trace;
    }

    private float ToValue(ReadOnlySpan<byte> bytes) => _format switch
    {
        DataFormat.INT8 => ToInt16(bytes),
        DataFormat.INT16 => ToInt16(bytes),
        DataFormat.INT32 => ToInt32(bytes),
        DataFormat.IEEEFLT32 => ToSingle(bytes),
        DataFormat.IBMFLT32 => FromIbmSingle(bytes),
        _ => 0
    };


    private void ParseHeader()
    {
        var pool = ArrayPool<byte>.Shared;
        var buffer = pool.Rent(HEADER_SIZE);

        using var s = new FileStream(FileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        _fileSize = s.Length;
        var byteRead = s.Read(buffer, 0, HEADER_SIZE);
        Header = ParseHeaderText(buffer);
        ParseHeaderBinary(buffer);
        pool.Return(buffer);

        buffer = pool.Rent(_traceByteSize * 2);
        byteRead = s.Read(buffer, 0, _traceByteSize * 2);
        ReadOnlySpan<byte> traceBytes = buffer;
        // first 3 trace
        var t0 = ParseTraceHeader(traceBytes.Slice(0, TRACE_HEADER_SIZE));
        var t1 = ParseTraceHeader(traceBytes.Slice(_traceByteSize, TRACE_HEADER_SIZE));

        var inLineIndex = t0[73] > 0 ? 73 : 1;
        var xLineIndex = t0[74] > 0 ? 74 : 5;
        InLineStep = t1[inLineIndex] - t0[inLineIndex];
        CrossLineStep = t1[xLineIndex] - t0[xLineIndex];
        var scalar = Math.Abs(t0[20]);
        SampleInterval = t0[39] > 0 ? t0[39] / 1000 : 0;
        XBegin = (t0[71] > 0 ? t0[71] : t0[21]) / scalar;
        YBegin = (t0[72] > 0 ? t0[72] : t0[22]) / scalar;
        ZBegin = t0[33] != 0 ? Math.Abs(t0[33]) : t0[35];
        ZEnd = ZBegin + (SampleInterval * SampleSize);


        //read last trace
        s.Seek(HEADER_SIZE + (_traceByteSize * (TraceSize - 1L)), SeekOrigin.Begin);
        byteRead = s.Read(buffer, 0, TRACE_HEADER_SIZE);
        var tN = ParseTraceHeader(traceBytes.Slice(0, TRACE_HEADER_SIZE));
        XEnd = (tN[71] > 0 ? tN[71] : tN[21]) / scalar;
        YEnd = (tN[72] > 0 ? tN[72] : tN[22]) / scalar;

        pool.Return(buffer);

        if (InLineStep > 0)
            InLineSize = 1 + (tN[inLineIndex] - t0[inLineIndex]) / InLineStep;
        if (CrossLineStep > 0)
            CrossLineSize = 1 + (tN[xLineIndex] - t0[xLineIndex]) / CrossLineStep;
        if (InLineSize == 0 && CrossLineSize > 0)
        {
            InLineSize = TraceSize / CrossLineSize;
            InLineStep = (1 + tN[inLineIndex] - t0[inLineIndex]) / InLineSize;
        }
        if (InLineSize > 0 && CrossLineSize == 0)
        {
            CrossLineSize = TraceSize / InLineSize;
            CrossLineStep = (1 + tN[xLineIndex] - t0[xLineIndex]) / CrossLineSize;
        }
    }

    private static string ParseHeaderText(byte[] header)
    {
        var text = header[0] == 'C' ? Encoding.Default.GetString(header, 0, HEADER_TEXT_SIZE)
            : ToString(header, 0, HEADER_TEXT_SIZE);
        var sb = new StringBuilder();
        for (int i = 0; i < ROW_SIZE; i++)            
            sb.AppendLine(text.Substring(i * COLUMN_SIZE, COLUMN_SIZE));
        return sb.ToString();
    }

    private void ParseHeaderBinary(ReadOnlySpan<byte> bytes)
    {
        var header = bytes.Slice(HEADER_TEXT_SIZE);
        var byte0 = header[FORMAT_INDEX];
        var byte1 = header[FORMAT_INDEX + 1];
        _isLittleEndian = byte1 == 0;
        _format = (DataFormat)(_isLittleEndian ? byte0 : byte1);
        _valueSize = GetValueSize();
        SampleSize = ToInt16(header.Slice(SAMPLE_SIZE_INDEX, 2));
        _traceByteSize = TRACE_HEADER_SIZE + SampleSize * _valueSize;
        TraceSize = (int)((_fileSize - HEADER_SIZE) / _traceByteSize);
    }

    private int[] ParseTraceHeader(ReadOnlySpan<byte> bytes)
    {
        var header = new int[93];
        for (int i = 0, k = 0, l = 0; i < _traceHeaderIndex.Length; i++)
        {
            for (int j = 0; j < _traceHeaderIndex[i]; j++, k++)
            {
                header[k] = i % 2 == 0 ? ToInt32(bytes.Slice(l, 4)) : ToInt16(bytes.Slice(l, 2));
                l += i % 2 == 0 ? 4 : 2;
            }
        }

        return header;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private short ToInt16(ReadOnlySpan<byte> source) => _isLittleEndian
        ? BinaryPrimitives.ReadInt16LittleEndian(source) : BinaryPrimitives.ReadInt16BigEndian(source);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int ToInt32(ReadOnlySpan<byte> source) => _isLittleEndian
        ? BinaryPrimitives.ReadInt32LittleEndian(source) : BinaryPrimitives.ReadInt32BigEndian(source);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float ToSingle(ReadOnlySpan<byte> source) => _isLittleEndian
        ? BinaryPrimitives.ReadSingleLittleEndian(source) : BinaryPrimitives.ReadSingleBigEndian(source);

    private const int IBM_BASE = 16;
    private const float THREE_BYTE_SHIFT = 16777216;
    private const byte EXPONENT_BIAS = 64;
    /// <summary>
    /// Returns a 32-bit IEEE single precision floating point number from four bytes encoding
    /// a single precision number in IBM System/360 Floating Point format
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float FromIbmSingle(ReadOnlySpan<byte> source)
    {
        if (0 == BinaryPrimitives.ReadInt32LittleEndian(source))
            return 0;

        // The first bit is the sign.  The next 7 bits are the exponent.
        byte exponentBits = source[0];
        var sign = +1.0f;
        // Remove sign from first bit
        if (exponentBits >= 128)
        {
            sign = -1.0f;
            exponentBits -= 128;
        }
        // Remove the bias from the exponent
        exponentBits -= EXPONENT_BIAS;
        var exponent = MathF.Pow(IBM_BASE, exponentBits);

        // The fractional part is Big Endian unsigned int to the right of the radix point
        // So we reverse the bytes and pack them back into an int
        Span<byte> fractionBytes = stackalloc byte[] { source[3], source[2], source[1], 0 };
        // Note: The sign bit for int32 is in the last byte of the array, which is zero, so we don't have to convert to uint
        float mantissa = BinaryPrimitives.ReadInt32LittleEndian(fractionBytes);
        // And divide by 2^(8 * 3) to move the decimal all the way to the left
        var fraction = mantissa / THREE_BYTE_SHIFT;

        return sign * exponent * fraction;
    }


    private static readonly Encoding _unicode = Encoding.Unicode;
    //private static readonly Encoding _ebcdic = Encoding.GetEncoding("IBM037");
    private static readonly Encoding _ebcdic = CodePagesEncodingProvider.Instance.GetEncoding("IBM037");
    public static string ToString(byte[] value, int index)
        => ToString(value, index, value.Length - index);

    public static string ToString(byte[] bytes, int index, int length)
        => _unicode.GetString(Encoding.Convert(_ebcdic, _unicode, bytes, index, length));

}

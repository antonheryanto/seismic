using Cysharp.Collections;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Runtime.CompilerServices;
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
    public DataFormat Format { get; set; } = DataFormat.IEEEFLT32;
    public int Revision { get; set; } = 256;
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
    private static readonly byte[] _traceHeaderIndex = new byte[] { 7, 4, 8, 2, 4, 46, 5, 12, 1, 2, 2 };

    private int GetValueSize() => Format switch
    {
        DataFormat.INT8 => 1,
        DataFormat.INT16 => 2,
        _ => 4
    };

    public SegyReader(string fileName = null)
    {
        if (fileName == null)
            return;
        FileName = fileName;
        ParseHeader();
    }

    public void Write(string fileName, float[][] traces, int index = 0)
    {
        using var r = new FileStream(FileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var w = File.OpenWrite(fileName);
        // write the text and binary header
        var headers = new byte[HEADER_SIZE];
        r.Read(headers, 0, HEADER_SIZE);
        w.Write(headers, 0, HEADER_SIZE);
        var buffer = new byte[TraceSize * _traceByteSize];
        r.Read(buffer, 0, buffer.Length);
        for (int i = 0, offset = 0; i < TraceSize; i++, offset += _traceByteSize)
        {
            // write the trace header
            var traceHeader = new ArraySegment<byte>(buffer, offset, TRACE_HEADER_SIZE).ToArray();
            w.Write(traceHeader, 0, traceHeader.Length);
            // write trace value
            var traceList = new List<byte>();
            for (int j = 0; j < traces[i].Length; j++)
            {
                traceList.AddRange(FromSingle(traces[i][j]));
            }
            var traceData = traceList.ToArray();
            w.Write(traceData, 0, traceData.Length);
        }
        w.Close();
    }

#if NET6_0_OR_GREATER
    public NativeMemoryArray<float> AsNativeMemoryArray()
    {
        using var handle = File.OpenHandle(FileName, FileMode.Open, FileAccess.Read);
        var fileSize = RandomAccess.GetLength(handle);
        var traces = new NativeMemoryArray<float>(TraceSize * SampleSize);
        var size = fileSize < 2L * GB ? fileSize : (2L * GB / _traceByteSize) * _traceByteSize;
        long offset = HEADER_SIZE;
        long i = 0;
        while (offset < fileSize)
        {
            var arraySize = fileSize - offset > size ? size : fileSize - offset;
            using var array = new NativeMemoryArray<byte>(arraySize);
            RandomAccess.Read(handle, array.AsSpan(), offset);
            foreach (ReadOnlySpan<byte> chunk in array.AsSpanSequence(_traceByteSize))
            {
                var v = chunk.Slice(TRACE_HEADER_SIZE, _traceByteSize - TRACE_HEADER_SIZE);
                ReadOnlySpan<float> x = ParseValue(ref v);
                x.CopyTo(traces.AsSpan(i * SampleSize, SampleSize));
                i++;
            }
            offset += size;
        }
        return traces;
    }

    public void Write(string fileName, NativeMemoryArray<float> traces)
    {
        using var readHandler = File.OpenHandle(FileName, FileMode.Open, FileAccess.Read);
        using var writeHandler = File.OpenHandle(fileName, FileMode.OpenOrCreate, FileAccess.ReadWrite);
        var fileSize = RandomAccess.GetLength(readHandler);
        var size = fileSize < 2L * GB ? fileSize : (2L * GB / _traceByteSize) * _traceByteSize;
        long offset = HEADER_SIZE;
        long i = 0;
        while (offset < fileSize)
        {
            var arraySize = fileSize - offset > size ? size : fileSize - offset;
            using var array = new NativeMemoryArray<byte>(arraySize);
            RandomAccess.Read(readHandler, array.AsSpan(), offset);            
            foreach (ReadOnlySpan<byte> chunk in array.AsSpanSequence(_traceByteSize))
            {
                var header = chunk.Slice(0, TRACE_HEADER_SIZE);
                Span<byte> traceByte = new byte[_traceByteSize];
                chunk.Slice(0, TRACE_HEADER_SIZE).CopyTo(traceByte.Slice(0, TRACE_HEADER_SIZE));
                for (int j = 0; j < SampleSize; j++)
                {
                    Span<byte> sample = FromSingle(traces[j]);
                    sample.CopyTo(traceByte.Slice(j * sample.Length, sample.Length));
                    i++;
                }
                traceByte.ToArray();
                RandomAccess.Write(writeHandler, traceByte, offset);
                offset += _traceByteSize;
            }            
        }
    }


    public float[][] ReadBigTrace()
    {
        using var handle = File.OpenHandle(FileName, FileMode.Open, FileAccess.Read);
        var size = RandomAccess.GetLength(handle);
        using var array = new NativeMemoryArray<byte>(size - HEADER_SIZE);
        RandomAccess.Read(handle, array.AsSpan(), HEADER_SIZE);
        int i = 0;
        var traces = new float[TraceSize][];
        foreach (ReadOnlySpan<byte> chunk in array.AsSpanSequence(_traceByteSize))
        {
            var v = chunk.Slice(TRACE_HEADER_SIZE, _traceByteSize - TRACE_HEADER_SIZE);
            traces[i] = ParseValue(ref v);
            i++;
        }
        return traces;
    }

    public async Task ReadAllTraceAsync()
    {
        using var handle = File.OpenHandle(FileName, FileMode.Open, FileAccess.Read, options: FileOptions.Asynchronous);
        var size = RandomAccess.GetLength(handle);
        using var array = new NativeMemoryArray<byte>(size);
        await RandomAccess.ReadAsync(handle, array.AsMemoryList(), 0);
        var option = new ParallelOptions();
#if DEBUG
        option.MaxDegreeOfParallelism = 1;
#endif
        
    }

    public async Task<float[][]> ReadTraceAsync(int minSize = GB)
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
#endif

    public float[][] ReadAllTraces()
    {
        if (_fileSize > (2L * GB))
            return Array.Empty<float[]>();
        var buffer = File.ReadAllBytes(FileName);
        var traces = new float[TraceSize][];
        var option = new ParallelOptions();
#if DEBUG
        option.MaxDegreeOfParallelism = 1;
#endif
        Parallel.For(0, traces.Length, option, i => {            
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
            trace[i] = ToValue(values.Slice(i * _valueSize, _valueSize));
        return trace;
    }

    private float ToValue(ReadOnlySpan<byte> bytes) => Format switch
    {
        DataFormat.INT8 => ToInt16(bytes),
        DataFormat.INT16 => ToInt16(bytes),
        DataFormat.INT32 => ToInt32(bytes),
        DataFormat.IEEEFLT32 => ToSingle(bytes),
        DataFormat.IBMFLT32 => IbmToSingle(bytes),
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

        // first n trace
        var nTrace = 2;
        var t = new int[nTrace][];
        buffer = pool.Rent(_traceByteSize * nTrace);
        byteRead = s.Read(buffer, 0, _traceByteSize * nTrace);
        ReadOnlySpan<byte> traceBytes = buffer;
        for (int i = 0;i < nTrace; i++)
            t[i] = ParseTraceHeader(traceBytes.Slice(_traceByteSize * i, TRACE_HEADER_SIZE));

        var inLineIndex = t[0][73] > 0 ? 73 : 1;
        var xLineIndex = t[0][74] > 0 ? 74 : 5;
        if (inLineIndex == 73 && xLineIndex == 5)
            xLineIndex = 74;
        InLineStep = t[1][inLineIndex] - t[0][inLineIndex];
        CrossLineStep = t[1][xLineIndex] - t[0][xLineIndex];
        var scalar = Math.Abs(t[0][20]);
        if (scalar == 0)
            scalar = 1;
        SampleInterval = t[0][39] > 0 ? t[0][39] / 1000 : 0;
        XBegin = (t[0][71] > 0 ? t[0][71] : t[0][21]) / scalar;
        YBegin = (t[0][72] > 0 ? t[0][72] : t[0][22]) / scalar;
        ZBegin = t[0][33] != 0 ? Math.Abs(t[0][33]) : t[0][35];
        ZEnd = ZBegin + (SampleInterval * SampleSize);


        //read last trace
        s.Seek(HEADER_SIZE + (_traceByteSize * (TraceSize - 1L)), SeekOrigin.Begin);
        byteRead = s.Read(buffer, 0, TRACE_HEADER_SIZE);
        var tN = ParseTraceHeader(traceBytes.Slice(0, TRACE_HEADER_SIZE));
        XEnd = (tN[71] > 0 ? tN[71] : tN[21]) / scalar;
        YEnd = (tN[72] > 0 ? tN[72] : tN[22]) / scalar;

        pool.Return(buffer);

        if (InLineStep > 0)
            InLineSize = 1 + (tN[inLineIndex] - t[0][inLineIndex]) / InLineStep;
        if (CrossLineStep > 0)
            CrossLineSize = 1 + (tN[xLineIndex] - t[0][xLineIndex]) / CrossLineStep;
        if (InLineSize == 0 && CrossLineSize > 0)
        {
            CrossLineSize = Math.Min(CrossLineSize, TraceSize); //handle 2d or incomplete data
            InLineSize = TraceSize / CrossLineSize;
            InLineStep = (1 + tN[inLineIndex] - t[0][inLineIndex]) / InLineSize;
        }
        if (InLineSize > 0 && CrossLineSize == 0)
        {
            InLineSize = Math.Min(InLineSize, TraceSize); //handle 2d or incomplete data
            CrossLineSize = Math.Max(TraceSize / InLineSize, 1);
            CrossLineStep = (1 + tN[xLineIndex] - t[0][xLineIndex]) / CrossLineSize;
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
        var header = bytes.Slice(HEADER_TEXT_SIZE, HEADER_BINARY_SIZE);
        var byte0 = header[FORMAT_INDEX];
        var byte1 = header[FORMAT_INDEX + 1];
        _isLittleEndian = byte1 == 0;
        Format = (DataFormat)(_isLittleEndian ? byte0 : byte1);
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
    private float ToSingle(ReadOnlySpan<byte> source)
    {
#if NET6_0_OR_GREATER
        return _isLittleEndian ? BinaryPrimitives.ReadSingleLittleEndian(source)
            : BinaryPrimitives.ReadSingleBigEndian(source);
#else
        return _isLittleEndian ? BitConverter.ToSingle(source.ToArray(), 0)
            : BitConverter.ToSingle(ReverseByte(source), 0);
#endif
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte[] ReverseByte(ReadOnlySpan<byte> source)
    {
        var x = new byte[source.Length];
        for (int i = 0, j = source.Length - 1; i < source.Length; i++, j--)
            x[i] = source[j];
        return x;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte[] SwapByte(ReadOnlySpan<byte> s) => new byte[] { s[3], s[2], s[1], s[0] };

    private const int IBM_BASE = 16;
    private const byte EXPONENT_BIAS = 64;
    private const float THREE_BYTE_SHIFT = 16777216;
    /// <summary>
    /// Returns a 32-bit IEEE single precision floating point number from four bytes encoding
    /// a single precision number in IBM System/360 Floating Point format
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float IbmToSingle(ReadOnlySpan<byte> s)
    {
        var y = BinaryPrimitives.ReadInt32BigEndian(s);
        if (0 == y || s.Length != 4)
            return 0;
        // The first bit is the sign.
        var sign = s[0] < 128 ? 1: -1;
        // remove sign, The next 7 bits are the exponent.
        var expBit = s[0] & 0x7f;
        // (exp - 64) * 4 + 127 - 1 == exp * 4 - 256 + 126 == (exp << 2) - 130 
        var expIEEE = (expBit << 2) - 130;
        var exp = (byte) (expIEEE >> 1);
        // ieee exp conversion except for 0
        Span<byte> exponentBytes = stackalloc byte[] { 0, 0, 128, exp };
#if NET6_0_OR_GREATER
        var exponent = BinaryPrimitives.ReadSingleLittleEndian(exponentBytes);
#else
        var exponent = BitConverter.ToSingle(exponentBytes.ToArray(), 0);
#endif
        // The fractional part is Big Endian unsigned int to the right of the radix point
        // So we reverse the bytes and pack them back into an int
        // Note: The sign bit for int32 is in the last byte of the array, which is zero, so we don't have to convert to uint
        float fraction = BinaryPrimitives.ReadUInt32LittleEndian(stackalloc byte[] { s[3], s[2], s[1], 0 });
        // And divide by 2^(8 * 3) to move the decimal all the way to the left
        fraction /= THREE_BYTE_SHIFT;
        return sign * exponent * fraction;
    }

    /// <summary>
    /// Given a 32-bit IEEE single precision floating point number, returns four bytes encoding
    /// a single precision number in IBM System/360 Floating Point format
    /// </summary>
    public static byte[] SingleToIbm(float value)
    {
        var bytes = new byte[4];
        if (value == 0)
            return bytes;

        // Sign
        if (value < 0)
            bytes[0] = 128;
        var v = Math.Abs(value);

        // Fraction
        // Find the number of digits (in the IBM base) we need to move the radix point to get a value that is less than 1
        var moveRadix = (int)Math.Log(v, IBM_BASE) + 1;
        var fraction = v / (Math.Pow(IBM_BASE, moveRadix));
        var fractionInt = (int)(THREE_BYTE_SHIFT * fraction);
        var fractionBytes = BitConverter.GetBytes(fractionInt);
        bytes[3] = fractionBytes[0];
        bytes[2] = fractionBytes[1];
        bytes[1] = fractionBytes[2];

        // Exponent
        var exponent = moveRadix + EXPONENT_BIAS;
        bytes[0] += (byte)exponent;
        return bytes;
    }

    private static readonly Encoding _unicode = Encoding.Unicode;
    //private static readonly Encoding _ebcdic = Encoding.GetEncoding("IBM037");
    private static readonly Encoding _ebcdic = CodePagesEncodingProvider.Instance.GetEncoding("IBM037");
    public static string ToString(byte[] value, int index)
        => ToString(value, index, value.Length - index);

    private static string ToString(byte[] bytes, int index, int length)
        => _unicode.GetString(Encoding.Convert(_ebcdic, _unicode, bytes, index, length));

    //[MethodImpl(MethodImplOptions.AggressiveInlining)]
    private byte[] FromSingle(float value)
    {
        if (Format == DataFormat.IBMFLT32)
            return SingleToIbm(value);
        var bytes = BitConverter.GetBytes(value);
        return _isLittleEndian ? bytes : bytes.Reverse().ToArray();
    }
}

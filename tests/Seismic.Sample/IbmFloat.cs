using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Seismic.Sample;

public class IbmFloat
{
    static readonly byte[] source = new byte[] { 56, 235, 225, 134 };
    private const int IBM_BASE = 16;
    private const byte EXPONENT_BIAS = 64;
    private const float THREE_BYTE_SHIFT = 16777216;
    static float IbmToFloat()
    {
        ReadOnlySpan<byte> s = source;
        int sign = (s[0] >> 7) == 0 ? 1 : -1;
        int exp = (((s[0] & 0x7f) << 2) - 130) >> 1;
        Span<byte> exponentBytes = stackalloc byte[] { 0, 0, 128, (byte)exp };
        var exponent = BinaryPrimitives.ReadSingleLittleEndian(exponentBytes);
        Span<byte> fractionBytes = stackalloc byte[] { source[3], source[2], source[1], 0 };
        float mantissa = BinaryPrimitives.ReadInt32LittleEndian(fractionBytes);
        var fracBin = mantissa / THREE_BYTE_SHIFT;

        return sign * exponent * fracBin;
    }

    static float IbmToFloat4()
    {
        ReadOnlySpan<byte> s = source;
        int sign = s[0] < 128 ? 1 : -1;
        float exp = MathF.Pow(16, (s[0] & 0x7f) - EXPONENT_BIAS);
        float fraction = 0;
        float fracHex = 1;
        for (int i = 1; i < s.Length; i++)
        {
            fracHex /= 16;
            fraction += (s[i] >> 4) * fracHex;
            fracHex /= 16;
            fraction += (s[i] & 0x0f) * fracHex;
        }
        return sign * exp * fraction;
    }

    static float IbmToFloat5()
    {
        ReadOnlySpan<byte> s = source;
        int sign = s[0] < 128 ? 1 : -1;
        float exp = MathF.Pow(16, (s[0] & 0x7f) - EXPONENT_BIAS);
        float fraction = 0;
        int fracHex = 0;
        for (int i = 1; i < s.Length; i++)
        {
            fracHex--;
            fraction += (s[i] >> 4) * MathF.Pow(16, fracHex);
            fracHex--;
            fraction += (s[i] & 0x0f) * MathF.Pow(16, fracHex);
        }
        return sign * exp * fraction;
    }

    static float IbmToFloat3()
    {
        float x = 2.14532475e-10f;
        ReadOnlySpan<byte> xb = BitConverter.GetBytes(x);
        uint xi = BinaryPrimitives.ReadUInt32LittleEndian(xb);
        var xo = xi << 8;

        ReadOnlySpan<byte> s = source;
        int sign = s[0] >> 7;
        int exp = (s[0] << 2) - 130;
        var fr = BinaryPrimitives.ReadUInt32BigEndian(stackalloc byte[] { s[3], s[2], s[1], 0 });
        while (fr < 0x80000000)
        {
            --exp;
            fr <<= 1;
        }
        if (exp <= 0)
        { /* underflow */
            if (exp < -24) /* complete underflow - return properly signed zero */
                fr = 0;
            else /* partial underflow - return denormalized number */
                fr >>= -exp;
            exp = 0;
        }
        else if (exp >= 255)
        { /* overflow - return infinity */
            fr = 0;
            exp = 255;
        }
        else
        { /* just a plain old number - remove the assumed high bit */
            fr <<= 1;
        }

        //var to = (uint)((fr >> 9) | (exp << 23) | (sign << 31));
        var fx = fr >> 9;
        Span<byte> f = BitConverter.GetBytes(fx);
        f[0] = (byte)(sign << 7 | (exp >> 1) + 1);
        f[1] = (byte)(((exp & 1) << 7) | f[1]);
        var o = BinaryPrimitives.ReadSingleBigEndian(f);
        return default;
    }

    static float IbmToFloat2()
    {
        ReadOnlySpan<byte> s = source;
        //ReadOnlySpan<byte> rs = stackalloc byte[] { s[3], s[2], s[1], s[0] };
        var fr = BinaryPrimitives.ReadUInt32BigEndian(s); //get int value
        uint sign = fr >> 31; // save sign;
        fr <<= 1; // shift sign out
        uint exp = fr >> 25;
        fr <<= 7; // shift exp out
        if (fr == 0)
            exp = 0;
        // adjust exponent from base 16 offset 64 radix point before first digit
        // to base 2 offset 127 radix point after first digit
        // (exp - 64) * 4 + 127 - 1 == exp * 4 - 256 + 126 == (exp << 2) - 130
        exp = (exp << 2) - 130;
        ///* (re)normalize */
        //while (fr < 0x80000000)
        //{ /* 3 times max for normalized input */
        //    --exp;
        //    fr <<= 1;
        //}
        //if (exp <= 0)
        //{ /* underflow */
        //    if (exp < -24) /* complete underflow - return properly signed zero */
        //        fr = 0;
        //    else /* partial underflow - return denormalized number */
        //        fr >>= -exp;
        //    exp = 0;
        //}
        //else if (exp >= 255)
        //{ /* overflow - return infinity */
        //    fr = 0;
        //    exp = 255;
        //}
        //else
        //{ /* just a plain old number - remove the assumed high bit */
        //    fr <<= 1;
        //}

        //var to = (fr >> 9) | (exp << 23) | (sign << 31);

        return default;
    }

    // https://stackoverflow.com/questions/4550367/convert-from-the-ibm-floating-point-to-the-ieee-floating-point-standard-and-vice

    // https://en.wikipedia.org/wiki/IBM_hexadecimal_floating-point
    //
    // float2ibm(-118.625F) == 0xC276A000
    // 1 100 0010    0111 0110 1010 0000 0000 0000
    //
    // IBM/370 single precision, 4 bytes
    // xxxx.xxxx     xxxx.xxxx xxxx.xxxx xxxx.xxxx
    // s|-exp--|     |--------fraction-----------|
    //    (7)                   (24)
    //
    // value = (-1)**s * 16**(e - 64) * .f   range = 5E-79 ... 7E+75
    //
    static int float2ibm(float fromFormat)
    {
        byte[] bytes = BitConverter.GetBytes(fromFormat);
        int fconv = (bytes[3] << 24) | (bytes[2] << 16) | (bytes[1] << 8) | bytes[0];

        if (fconv == 0)
            return 0;
        int fmant = (0x007fffff & fconv) | 0x00800000;
        int t = (int)((0x7f800000 & fconv) >> 23) - 126;
        while (0 != (t & 0x3))
        {
            ++t;
            fmant >>= 1;
        }
        fconv = (int)(0x80000000 & fconv) | (((t >> 2) + 64) << 24) | fmant;
        return fconv; // Big-endian order
    }
}

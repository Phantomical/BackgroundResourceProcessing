using System;
using System.Runtime.InteropServices;

namespace BackgroundResourceProcessing.Collections.Burst;

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct FixedString32
{
    private struct FixedChars32
    {
        char b00;
        char b01;
        char b02;
        char b03;
        char b04;
        char b05;
        char b06;
        char b07;
        char b08;
        char b09;
        char b10;
        char b11;
        char b12;
        char b13;
        char b14;
        char b15;
        char b16;
        char b17;
        char b18;
        char b19;
        char b20;
        char b21;
        char b22;
        char b23;
        char b24;
        char b25;
        char b26;
        char b27;
        char b28;
        char b29;
        char b30;
        char b31;
    }

    FixedChars32 chars;

    public readonly int Capacity => sizeof(FixedChars32) / sizeof(char);
    public readonly int Length
    {
        get
        {
            fixed (FixedChars32* data = &chars)
            {
                MemorySpan<char> dst = new((char*)data, Capacity);
                int i;
                for (i = 0; i < dst.Length; ++i)
                {
                    if (dst[i] == '\0')
                        break;
                }

                return i;
            }
        }
    }

    public FixedString32(string text)
    {
        fixed (FixedChars32* data = &chars)
        {
            MemorySpan<char> dst = new((char*)data, Capacity);
            var len = Math.Min(Capacity, text.Length);

            for (int i = 0; i < len; ++i)
                dst[i] = text[i];
        }
    }

    public override string ToString()
    {
        fixed (FixedChars32* data = &chars)
            return new string((char*)data, 0, Length);
    }
}

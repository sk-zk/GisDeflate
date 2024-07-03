using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace GisDeflate
{
    internal static class Utils
    {
        public static uint GetUnalignedLe32(byte[] p, int offset)
        {
            return ((uint)(p[3 + offset] << 24))
                 | ((uint)(p[2 + offset] << 16))
                 | ((uint)(p[1 + offset] << 8))
                 | p[0 + offset];
        }

        public static uint Rotr(uint value, int count)
        {
            return (value >> count) | (value << (32 - count));
        }

        public static uint BitScanReverse32(uint value)
        {
            if (value == 0)
                throw new ArgumentOutOfRangeException(nameof(value));

            uint position = 0;
            if ((value & 0xFFFF0000) != 0)
            {
                value >>= 16;
                position += 16;
            }
            if ((value & 0xFF00) != 0)
            {
                value >>= 8;
                position += 8;
            }
            if ((value & 0xF0) != 0)
            {
                value >>= 4;
                position += 4;
            }
            if ((value & 0xC) != 0)
            {
                value >>= 2;
                position += 2;
            }
            if ((value & 0x2) != 0)
            {
                position += 1;
            }
            return position;
        }
    }
}

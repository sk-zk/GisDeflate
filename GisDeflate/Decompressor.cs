using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace GisDeflate
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct Decompressor
    {
        [StructLayout(LayoutKind.Explicit)]
        public struct Union
        {
            [FieldOffset(0)]
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 19)]
            public byte[] PrecodeLens = new byte[19];

            [FieldOffset(0)]
            public InnerStruct L = new();

            [FieldOffset(0)]
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1334)]
            public uint[] LitlenDecodeTable = new uint[1334];

            public Union()
            {
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct InnerStruct
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 288 + 32 + 137)]
            public byte[] Lens = new byte[288 + 32 + 137];

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)]
            public uint[] PrecodeDecodeTable = new uint[128];

            public InnerStruct()
            {
            }
        }

        public Union U = new();

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 402)]
        public uint[] OffsetDecodeTable = new uint[402];

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 288)]
        public ushort[] SortedSyms = new ushort[288];

        public bool StaticCodesLoaded = false;

        public Decompressor()
        {
        }
    }

}

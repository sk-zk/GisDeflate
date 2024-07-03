using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace GisDeflate
{
    /// <summary>
    /// The main DEFLATE decompressor structure. Since this implementation only
    /// supports full buffer decompression, this structure does not store the entire
    /// decompression state, but rather only some arrays that are too large to
    /// comfortably allocate on the stack.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Decompressor
    {
        // Each ENOUGH number is the maximum number of decode table entries that may be
        // required for the corresponding Huffman code, including the main table and all
        // subtables. Each number depends on three parameters:
        // 
        // (1) the maximum number of symbols in the code (DEFLATE_NUM_*_SYMS)
        // (2) the number of main table bits (the TABLEBITS numbers defined above)
        // (3) the maximum allowed codeword length (DEFLATE_MAX_*_CODEWORD_LEN)
        // 
        // The ENOUGH numbers were computed using the utility program 'enough' from
        // zlib. This program enumerates all possible relevant Huffman codes to find
        // the worst-case usage of decode table entries.

        const int PrecodeEnough = 128; // enough 19 7 7
        const int LitlenEnough = 1334; // enough 288 10 15
        const int OffsetEnough = 402; // enough 32 8 15

        // The arrays aren't all needed at the same time.  'PrecodeLens' and
        // 'PrecodeDecodeTable' are unneeded after 'Lens' has been filled.
        // Furthermore, 'Lens' need not be retained after building the litlen
        // and offset decode tables.  In fact, 'Lens' can be in union with
        // 'LitlenDecodeTable' provided that 'OffsetDecodeTable' is separate
        // and is built first.

        [StructLayout(LayoutKind.Explicit)]
        public struct Union
        {
            [FieldOffset(0)]
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = Deflate.DeflateNumPrecodeSyms)]
            public byte[] PrecodeLens = new byte[Deflate.DeflateNumPrecodeSyms];

            [FieldOffset(0)]
            public InnerStruct L = new();

            [FieldOffset(0)]
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = LitlenEnough)]
            public uint[] LitlenDecodeTable = new uint[LitlenEnough];

            public Union()
            {
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct InnerStruct
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 
                Deflate.DeflateNumLitlenSyms 
                + Deflate.DeflateNumOffsetSyms 
                + Deflate.DeflateMaxLensOverrun)]
            public byte[] Lens = new byte[
                Deflate.DeflateNumLitlenSyms 
                + Deflate.DeflateNumOffsetSyms 
                + Deflate.DeflateMaxLensOverrun];

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = PrecodeEnough)]
            public uint[] PrecodeDecodeTable = new uint[PrecodeEnough];

            public InnerStruct()
            {
            }
        }

        public Union U = new();

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = OffsetEnough)]
        public uint[] OffsetDecodeTable = new uint[OffsetEnough];

        // used only during BuildDecodeTable()
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = Deflate.DeflateMaxNumSyms)]
        public ushort[] SortedSyms = new ushort[Deflate.DeflateMaxNumSyms];

        public bool StaticCodesLoaded = false;

        public Decompressor()
        {
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GisDeflate
{
    internal static class DecodeResults
    {
        /// <summary>
        /// Shift a decode result into its position in the decode table entry.
        /// </summary>
        public static uint HuffDecResultEntry(uint result)
        {
            return result << Deflate.HuffDecResultShift;
        }

        public static uint[] GenerateOffsetDecodeResults()
        {
            static uint Entry(int offsetBase, int numExtraBits)
            {
                return HuffDecResultEntry(
                    (uint)((numExtraBits << Deflate.HuffDecExtraOffsetBitsShift) 
                    | offsetBase));
            }

            return [
                Entry(1     , 0)  , Entry(2     , 0)  , Entry(3     , 0)  , Entry(4     , 0)  ,
                Entry(5     , 1)  , Entry(7     , 1)  , Entry(9     , 2)  , Entry(13    , 2)  ,
                Entry(17    , 3)  , Entry(25    , 3)  , Entry(33    , 4)  , Entry(49    , 4)  ,
                Entry(65    , 5)  , Entry(97    , 5)  , Entry(129   , 6)  , Entry(193   , 6)  ,
                Entry(257   , 7)  , Entry(385   , 7)  , Entry(513   , 8)  , Entry(769   , 8)  ,
                Entry(1025  , 9)  , Entry(1537  , 9)  , Entry(2049  , 10) , Entry(3073  , 10) ,
                Entry(4097  , 11) , Entry(6145  , 11) , Entry(8193  , 12) , Entry(12289 , 12) ,
                Entry(16385 , 13) , Entry(24577 , 13) , Entry(32769 , 14) , Entry(49153 , 14) ,
            ];
        }

        public static uint[] GenerateLitlenDecodeResults(uint numLitlenSyms)
        {
            var results = new uint[numLitlenSyms];

            for (uint i = 0; i < 256; i++)
            {
                results[i] = (i << 8) | Deflate.HuffDecLiteral;
            }

            // you didn't see that.
            results[256] = ((0 << 8) | 0) << 8;
            results[257] = ((3 << 8) | 0) << 8;
            results[258] = ((4 << 8) | 0) << 8;
            results[259] = ((5 << 8) | 0) << 8;
            results[260] = ((6 << 8) | 0) << 8;
            results[261] = ((7 << 8) | 0) << 8;
            results[262] = ((8 << 8) | 0) << 8;
            results[263] = ((9 << 8) | 0) << 8;
            results[264] = ((10 << 8) | 0) << 8;
            results[265] = ((11 << 8) | 1) << 8;
            results[266] = ((13 << 8) | 1) << 8;
            results[267] = ((15 << 8) | 1) << 8;
            results[268] = ((17 << 8) | 1) << 8;
            results[269] = ((19 << 8) | 2) << 8;
            results[270] = ((23 << 8) | 2) << 8;
            results[271] = ((27 << 8) | 2) << 8;
            results[272] = ((31 << 8) | 2) << 8;
            results[273] = ((35 << 8) | 3) << 8;
            results[274] = ((43 << 8) | 3) << 8;
            results[275] = ((51 << 8) | 3) << 8;
            results[276] = ((59 << 8) | 3) << 8;
            results[277] = ((67 << 8) | 4) << 8;
            results[278] = ((83 << 8) | 4) << 8;
            results[279] = ((99 << 8) | 4) << 8;
            results[280] = ((115 << 8) | 4) << 8;
            results[281] = ((131 << 8) | 5) << 8;
            results[282] = ((163 << 8) | 5) << 8;
            results[283] = ((195 << 8) | 5) << 8;
            results[284] = ((227 << 8) | 5) << 8;
            results[285] = ((3 << 8) | 16) << 8;
            results[286] = ((3 << 8) | 16) << 8;
            results[287] = ((3 << 8) | 16) << 8;

            return results;
        }

        public static uint[] GeneratePrecodeDecodeResults()
        {
            var results = new uint[Deflate.DeflateNumPrecodeSyms];
            for (uint i = 0; i < Deflate.DeflateNumPrecodeSyms; i++)
            {
                results[i] = HuffDecResultEntry(i);
            }
            return results;
        }
    }
}

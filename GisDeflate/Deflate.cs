using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GisDeflate
{
    internal static class Deflate
    {
        /// <summary>
        /// The number of bits to keep in input buffer.
        /// </summary>
        const int LowWatermarkBits = 32;

        /// <summary>
        /// Number of bits per GDeflate bit-packet.
        /// </summary>
        const int BitsPerPacket = 32;

        /// <summary>
        /// Number of GDeflate streams.
        /// </summary>
        public const int NumStreams = 32;

        /// <summary>
        /// The maximum number of symbols across all codes.
        /// </summary>
        public const int DeflateMaxNumSyms = 288;

        // Number of symbols in each Huffman code. Note: for the literal/length
        // and offset codes, these are actually the maximum values; a given block
        // might use fewer symbols.
        public const int DeflateNumPrecodeSyms = 19;
        public const int DeflateNumLitlenSyms = 288;
        public const int DeflateNumOffsetSyms = 32;

        // Maximum codeword length, in bits, within each Huffman code
        const int DeflateMaxPreCodewordLen = 7;
        const int DeflateMaxCodewordLen = 15;
        const int DeflateMaxLitlenCodewordLen = 15;
        const int DeflateMaxOffsetCodewordLen = 15;

        /// <summary>
        /// Maximum possible overrun when decoding codeword lengths.
        /// </summary>
        public const int DeflateMaxLensOverrun = 137;

        /// <summary>
        /// The order in which precode lengths are stored.
        /// </summary>
        static readonly byte[] DeflatePrecodeLensPermutation = new byte[]
        {
            16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15
        };

        // Each TABLEBITS number is the base-2 logarithm of the number of entries in the
        // main portion of the corresponding decode table. Each number should be large
        // enough to ensure that for typical data, the vast majority of symbols can be
        // decoded by a direct lookup of the next TABLEBITS bits of compressed data.
        // However, this must be balanced against the fact that a larger table requires
        // more memory and requires more time to fill.
        // 
        // Note: you cannot change a TABLEBITS number without also changing the
        // corresponding ENOUGH number!
        const int PrecodeTableBits = 7;
        const int LitlenTableBits = 10;
        const int OffsetTableBits = 8;

        /// <summary>
        /// Shift to extract the decode result from a decode table entry. 
        /// </summary>
        public const int HuffDecResultShift = 8;

        /// <summary>
        /// This flag is set in all main decode table entries that represent subtable pointers.
        /// </summary>
        const uint HuffDecSubtablePointer = 0x80000000;

        /// <summary>
        /// Mask for extracting the codeword length from a decode table entry.
        /// </summary>
        const byte HuffDecLengthMask = 0xFF;

        /// <summary>
        /// This flag is set in all entries in the litlen decode table that represent literals.
        /// </summary>
        public const uint HuffDecLiteral = 0x40000000;
        
        const int HuffDecLengthBaseShift = 8;
        const int HuffDecExtraLengthBitsMask = 0xff;

        const int HuffDecEndOfBlockLength = 0;

        public const int HuffDecExtraOffsetBitsShift = 16;

        const int HuffDecOffsetBaseMask = (1 << HuffDecExtraOffsetBitsShift) - 1;

        /// <summary>
        /// The decode result for each precode symbol. There is no special optimization for the precode;
        /// the decode result is simply the symbol value.
        /// </summary>
        readonly static uint[] PrecodeDecodeResults = DecodeResults.GeneratePrecodeDecodeResults();
        /// <summary>
        /// The decode result for each litlen symbol. For literals, this is the literal value itself and the
        /// HUFFDEC_LITERAL flag. For lengths, this is the length base and the number of extra length bits.
        /// </summary>
        readonly static uint[] LitlenDecodeResults = DecodeResults.GenerateLitlenDecodeResults(DeflateNumLitlenSyms);
        /// <summary>
        /// The decode result for each offset symbol. This is the offset base and the number of extra offset bits.
        /// </summary>
        readonly static uint[] OffsetDecodeResults = DecodeResults.GenerateOffsetDecodeResults();

        public static void TileDecompressionJob(DecompressionContext context, byte[] inData, int[] tileOffsets)
        {
            var decompressor = new Decompressor();

            while (true)
            {
                var tileIndex = (uint)Interlocked.Increment(ref context.GlobalIndex) - 1;

                if (tileIndex >= context.NumItems)
                    break;

                var tileOffset = tileIndex > 0 
                    ? tileOffsets[(int)tileIndex] 
                    : 0;

                var start = tileOffset;
                var length = tileIndex < context.NumItems - 1 
                    ? tileOffsets[(int)tileIndex + 1] - tileOffset 
                    : tileOffsets[0];
                var compressedPage = inData[start..(start + length)];

                var outputOffset = tileIndex * TileStream.KDefaultTileSize;

                Decompress(decompressor, new[] { compressedPage },
                    ref context.Output, outputOffset,
                    TileStream.KDefaultTileSize, out var actual);
            }
        }

        public static void Decompress(Decompressor decompressor, byte[][] inPages,
            ref byte[] out_, uint outputOffset, uint outNBytesAvail, out uint actualOutNBytesRet)
        {
            if (inPages.Length == 0)
            {
                throw new ArgumentException(null, nameof(inPages));
            }

            uint outIdx = outputOffset;
            actualOutNBytesRet = 0;
            for (int npage = 0; npage < inPages.Length; npage++)
            {
                DeflateDecompressDefault(decompressor,
                    inPages[npage],
                    ref out_,
                    outIdx,
                    outNBytesAvail,
                    out uint pageInNBytesRet,
                    out uint pageOutNBytesRet);
                outIdx += pageOutNBytesRet;
                outNBytesAvail -= pageOutNBytesRet;
                actualOutNBytesRet += pageOutNBytesRet;
            }
        }

        public static void DeflateDecompressDefault(Decompressor decompressor, byte[] page,
            ref byte[] out_, uint outIdx, uint outNBytesAvail, 
            out uint actualInNBytesRet, out uint actualOutNBytesRet) 
        {
            uint outNextIdx = outIdx;
            uint outEndIdx = outNextIdx + outNBytesAvail;

            uint inNextIdx = 0;

            var s = new State();

            uint i;
            uint isFinalBlock;
            BlockType blockType;
            uint numLitlenSyms;
            uint numOffsetSyms;
            uint isCopy = 0;

            // Starting to read GDeflate stream.
            Reset();
            for (int n = 0; n < 32; n++)
            {
                Advance();
            }

        next_block:
            // Starting to read the next block.
            Reset();

            // BFINAL: 1 bit
            isFinalBlock = PopBits(s, 1);

            // BTYPE: 2 bits
            blockType = (BlockType)PopBits(s, 2);

            EnsureBits(LowWatermarkBits);

            actualInNBytesRet = 0;
            actualOutNBytesRet = 0;

            if (blockType == BlockType.DynamicHuffman)
            {
                uint numExplicitPrecodeLens;

                // Read the codeword length counts.
                numLitlenSyms = PopBits(s, 5) + 257;
                numOffsetSyms = PopBits(s, 5) + 1;
                numExplicitPrecodeLens = PopBits(s, 4) + 4;

                decompressor.StaticCodesLoaded = false;

                EnsureBits(LowWatermarkBits);

                // Read the precode codeword lengths.
                for (i = 0; i < numExplicitPrecodeLens; i++)
                {
                    decompressor.U.PrecodeLens[DeflatePrecodeLensPermutation[i]] = (byte)PopBits(s, 3);
                    Advance();
                }

                for (; i < DeflateNumPrecodeSyms; i++)
                    decompressor.U.PrecodeLens[DeflatePrecodeLensPermutation[i]] = 0;

                // Build the decode table for the precode.
                BuildPrecodeDecodeTable(decompressor);

                Reset();

                // Expand the literal/length and offset codeword lengths.
                for (i = 0; i < numLitlenSyms + numOffsetSyms;)
                {
                    uint entry;
                    uint presym;
                    byte repVal;
                    uint repCount;

                    // Read the next precode symbol.
                    entry = decompressor.U.L.PrecodeDecodeTable[Bits(s, DeflateMaxPreCodewordLen)];
                    RemoveBits(s, (int)(entry & HuffDecLengthMask));

                    presym = entry >> HuffDecResultShift;

                    if (presym < 16)
                    {
                        // Explicit codeword length
                        decompressor.U.L.Lens[i++] = (byte)presym;
                        Advance();
                        continue;
                    }

                    // Run-length encoded codeword lengths

                    // Note: we don't need verify that the repeat count
                    // doesn't overflow the number of elements, since we
                    // have enough extra spaces to allow for the worst-case
                    // overflow (138 zeroes when only 1 length was
                    // remaining).
                    // 
                    // In the case of the small repeat counts (presyms 16
                    // and 17), it is fastest to always write the maximum
                    // number of entries. That gets rid of branches that
                    // would otherwise be required.
                    // 
                    // It is not just because of the numerical order that
                    // our checks go in the order 'presym < 16', 'presym ==
                    // 16', and 'presym == 17'. For typical data this is
                    // ordered from most frequent to least frequent case.
                    if (presym == 16)
                    {
                        // Repeat the previous length 3 - 6 times
                        repVal = decompressor.U.L.Lens[i - 1];
                        repCount = 3 + PopBits(s, 2);
                        decompressor.U.L.Lens[i + 0] = repVal;
                        decompressor.U.L.Lens[i + 1] = repVal;
                        decompressor.U.L.Lens[i + 2] = repVal;
                        decompressor.U.L.Lens[i + 3] = repVal;
                        decompressor.U.L.Lens[i + 4] = repVal;
                        decompressor.U.L.Lens[i + 5] = repVal;
                        i += repCount;
                    }
                    else if (presym == 17)
                    {
                        // Repeat zero 3 - 10 times
                        repCount = 3 + PopBits(s, 3);
                        decompressor.U.L.Lens[i + 0] = 0;
                        decompressor.U.L.Lens[i + 1] = 0;
                        decompressor.U.L.Lens[i + 2] = 0;
                        decompressor.U.L.Lens[i + 3] = 0;
                        decompressor.U.L.Lens[i + 4] = 0;
                        decompressor.U.L.Lens[i + 5] = 0;
                        decompressor.U.L.Lens[i + 6] = 0;
                        decompressor.U.L.Lens[i + 7] = 0;
                        decompressor.U.L.Lens[i + 8] = 0;
                        decompressor.U.L.Lens[i + 9] = 0;
                        i += repCount;
                    }
                    else
                    {
                        // Repeat zero 11 - 138 times
                        repCount = 11 + PopBits(s, 7);
                        for (uint j = i; j < i + repCount; j++)
                        {
                            decompressor.U.L.Lens[j] = 0;
                        }
                        i += repCount;
                    }

                    Advance();
                }
            }
            else if (blockType == BlockType.Uncompressed)
            {
                throw new NotImplementedException();
            }
            else if (blockType == BlockType.StaticHuffman)
            {
                // Static Huffman block: build the decode tables for the static
                // codes. Skip doing so if the tables are already set up from
                // an earlier static block; this speeds up decompression of
                // degenerate input of many empty or very short static blocks.
                // 
                // Afterwards, the remainder is the same as decompressing a
                // dynamic Huffman block.
                if (decompressor.StaticCodesLoaded)
                {
                    goto have_decode_tables;
                }

                for (i = 0; i < 144; i++)
                    decompressor.U.L.Lens[i] = 8;
                for (; i < 256; i++)
                    decompressor.U.L.Lens[i] = 9;
                for (; i < 280; i++)
                    decompressor.U.L.Lens[i] = 7;
                for (; i < 288; i++)
                    decompressor.U.L.Lens[i] = 8;

                for (; i < 288 + 32; i++)
                    decompressor.U.L.Lens[i] = 5;

                numLitlenSyms = 288;
                numOffsetSyms = 32;
            }
            else
            {
                throw new InvalidDataException();
            }

            // Decompressing a Huffman block (either dynamic or static)
            BuildOffsetDecodeTable(decompressor, numLitlenSyms, numOffsetSyms);
            BuildLitlenDecodeTable(decompressor, numLitlenSyms);

        have_decode_tables:
            Reset();

            // The main GDEFLATE decode loop

            while (true)
            {
                uint entry;
                uint length;

                if ((isCopy & 1) == 0)
                {
                    // Decode a litlen symbol.
                    entry = decompressor.U.LitlenDecodeTable[Bits(s, LitlenTableBits)];
                    if ((entry & HuffDecSubtablePointer) != 0)
                    {
                        // Litlen subtable required (uncommon case)
                        RemoveBits(s, LitlenTableBits);
                        entry = decompressor.U.LitlenDecodeTable[
                          ((entry >> HuffDecResultShift) & 0xFFFF) +
                          Bits(s, (int)(entry & HuffDecLengthMask))];
                    }
                    RemoveBits(s, (int)(entry & HuffDecLengthMask));
                    if ((entry & HuffDecLiteral) != 0)
                    {
                        // Literal
                        if (outNextIdx == outEndIdx)
                            throw new InvalidOperationException("Out of space");

                        out_[outNextIdx] = (byte)(entry >> HuffDecResultShift);
                        outNextIdx++;
                        Advance();
                        continue;
                    }

                    // Match or end-of-block
                    entry >>= HuffDecResultShift;

                    length = (entry >> HuffDecLengthBaseShift)
                        + PopBits(s, (int)(entry & HuffDecExtraLengthBitsMask));

                    // The match destination must not end after the end of the
                    // output buffer. For efficiency, combine this check with the
                    // end-of-block check. We're using 0 for the special
                    // end-of-block length, so subtract 1 and it turn it into
                    // SIZE_MAX.
                    if (length - 1 >= outEndIdx - outNextIdx)
                    {
                        if (length != HuffDecEndOfBlockLength)
                            throw new InvalidOperationException("Out of space");
                        goto block_done;
                    }

                    // Store copy for use later.
                    StoreCopy(length, outNextIdx);

                    // Advance output stream.
                    outNextIdx += length;
                }
                else
                {
                    DoCopy(decompressor, ref s, out_);
                    CopyComplete();
                }

                Advance();
            }

        block_done:
            for (uint n = 0; n < NumStreams; n++)
            {
                if ((isCopy & 1) == 1)
                {
                    DoCopy(decompressor, ref s, out_);
                    CopyComplete();
                }
                Advance();
            }

            // Finished decoding a block.
            if (isFinalBlock == 0)
                goto next_block;

            // That was the last block.

            actualInNBytesRet = inNextIdx;
            actualOutNBytesRet = outNextIdx;


            // Reset GDeflate stream index.
            void Reset()
            {
                s.Idx = 0;
            }

            // Advance GDeflate stream index. Refill bits if necessary.
            void Advance()
            {
                EnsureBits(LowWatermarkBits);
                s.Idx = (s.Idx + 1) % NumStreams;
                AdvanceCopies();
            }

            // Setup copy advance method depending on a number of streams used.
            void AdvanceCopies()
            {
                isCopy = Utils.Rotr(isCopy, 1);
            }

            // Load more bits from the input buffer until the specified number of bits is
            // present in the bitbuffer variable. 'n' cannot be too large; see MAX_ENSURE
            // and CAN_ENSURE().
            void EnsureBits(int n)
            {
                if (s.BitsLeft[s.Idx] < n)
                {
                    s.BitBuf[s.Idx] |= (ulong)Utils.GetUnalignedLe32(page, (int)inNextIdx) << s.BitsLeft[s.Idx];
                    inNextIdx += BitsPerPacket / 8;
                    s.BitsLeft[s.Idx] += BitsPerPacket;
                }
            }

            // Stores a deferred copy in current GDeflate stream.
            void StoreCopy(uint len, uint outNextIdx)
            {
                s.Copies[s.Idx].Length = len;
                s.Copies[s.Idx].OutNextIdx = outNextIdx;
                isCopy |= 1;
            }

            // Marks a copy in current current GDeflate stream as complete.
            void CopyComplete()
            {
                isCopy = (uint)(isCopy & ~1);
            }
        }

        /// <summary>
        /// Return the next 'n' bits from the bitbuffer variable without removing them.
        ///</summary>
        static uint Bits(State s, int n)
        {
            return (uint)s.BitBuf[s.Idx] & (((uint)1 << (n)) - 1);
        }

        /// <summary>
        /// Remove the next 'n' bits from the bitbuffer variable.
        /// </summary>
        static void RemoveBits(State s, int n)
        {
            s.BitBuf[s.Idx] >>= n;
            s.BitsLeft[s.Idx] -= n;
        }

        /// <summary>
        /// Remove and return the next 'n' bits from the bitbuffer variable.
        /// </summary>
        static uint PopBits(State s, int n)
        {
            var ret = Bits(s, n);
            RemoveBits(s, n);
            return ret;
        }

        /// <summary>
        /// Perform a deferred GDeflate copy.
        /// </summary>
        static void DoCopy(Decompressor decompressor, ref State s, byte[] out_)
        {
            uint entry;
            uint offset;

            // Pop match params.
            uint length = s.Copies[s.Idx].Length;
            uint outNextIdx = s.Copies[s.Idx].OutNextIdx;

            // Decode the match offset.
            entry = decompressor.OffsetDecodeTable[Bits(s, OffsetTableBits)];
            if ((entry & HuffDecSubtablePointer) != 0)
            {
                // Offset subtable required (uncommon case)
                RemoveBits(s, OffsetTableBits);
                entry = decompressor.OffsetDecodeTable[
                    ((entry >> HuffDecResultShift) & 0xFFFF) +
                    Bits(s, (int)(entry & HuffDecLengthMask))];
            }
            RemoveBits(s, (int)(entry & HuffDecLengthMask));

            entry >>= HuffDecResultShift;

            // Pop the extra offset bits and add them to the offset base
            // to produce the full offset.
            offset = (entry & HuffDecOffsetBaseMask)
                + PopBits(s, (int)(entry >> HuffDecExtraOffsetBitsShift));

            // Copy the match.
            var srcIdx = outNextIdx - offset;
            var dstIdx = outNextIdx;
            outNextIdx += length;
            do
            {
                out_[dstIdx++] = out_[srcIdx++];
            } while (dstIdx < outNextIdx);
        }

        /// <summary>
        /// Build the decode table for the offset code.
        /// </summary>
        static void BuildOffsetDecodeTable(Decompressor decompressor, uint numLitlenSyms, uint numOffsetSyms)
        {
            BuildDecodeTable(decompressor.OffsetDecodeTable,
                decompressor.U.L.Lens[(int)numLitlenSyms..],
                numOffsetSyms,
                OffsetDecodeResults,
                OffsetTableBits,
                DeflateMaxOffsetCodewordLen,
                decompressor.SortedSyms);
        }

        /// <summary>
        /// Build the decode table for the literal/length code.
        /// </summary>
        static void BuildLitlenDecodeTable(Decompressor decompressor, uint numLitlenSyms)
        {
            BuildDecodeTable(decompressor.U.LitlenDecodeTable,
                decompressor.U.L.Lens,
                numLitlenSyms,
                LitlenDecodeResults,
                LitlenTableBits,
                DeflateMaxLitlenCodewordLen,
                decompressor.SortedSyms);
        }

        /// <summary>
        /// Build the decode table for the precode.
        /// </summary>
        static void BuildPrecodeDecodeTable(Decompressor decompressor)
        {
            BuildDecodeTable(decompressor.U.L.PrecodeDecodeTable,
                decompressor.U.PrecodeLens,
                DeflateNumPrecodeSyms,
                PrecodeDecodeResults,
                PrecodeTableBits,
                DeflateMaxPreCodewordLen,
                decompressor.SortedSyms);
        }
    
        /// <summary>
        /// Build a table for fast decoding of symbols from a Huffman code. As input,
        /// this function takes the codeword length of each symbol which may be used in
        /// the code. As output, it produces a decode table for the canonical Huffman
        /// code described by the codeword lengths. The decode table is built with the
        /// assumption that it will be indexed with "bit-reversed" codewords, where the
        /// low-order bit is the first bit of the codeword. This format is used for all
        /// Huffman codes in DEFLATE.
        /// </summary>
        public static void BuildDecodeTable(uint[] decodeTable, byte[] lens, uint numSyms, uint[] decodeResults,
            uint tableBits, uint maxCodewordLen, ushort[] sortedSyms
            )
        {
            uint[] lenCounts = new uint[DeflateMaxCodewordLen + 1];
            uint[] offsets = new uint[DeflateMaxCodewordLen + 1];
            uint sym; // current symbol
            uint codeword; // current codeword, bit-reversed
            uint len; // current codeword length in bits
            uint count; // num codewords remaining with this length
            uint codespaceUsed; // codespace used out of '2^maxCodewordLen'
            uint curTableEnd; // end index of current table
            uint subtablePrefix; // codeword prefix of current subtable
            uint subtableStart; // start index of current subtable
            uint subtableBits; // log2 of current subtable length
            uint sortedSymsIdx = 0;

            // Count how many codewords have each length, including 0.
            for (len = 0; len <= maxCodewordLen; len++)
                lenCounts[len] = 0;
            for (sym = 0; sym < numSyms; sym++)
                lenCounts[lens[sym]]++;

            offsets[0] = 0;
            offsets[1] = lenCounts[0];
            codespaceUsed = 0;
            for (len = 1; len < maxCodewordLen; len++)
            {
                offsets[len + 1] = offsets[len] + lenCounts[len];
                codespaceUsed = (codespaceUsed << 1) + lenCounts[len];
            }
            codespaceUsed = (codespaceUsed << 1) + lenCounts[len];

            for (sym = 0; sym < numSyms; sym++)
                sortedSyms[offsets[lens[sym]]++] = (ushort)sym;

            sortedSymsIdx += offsets[0]; // Skip unused symbols

            // lens[] is done being used, so we can write to decode_table[] now.

            // Check whether the lengths form a complete code (exactly fills the
            // codespace), an incomplete code (doesn't fill the codespace), or an
            // overfull code (overflows the codespace). A codeword of length 'n'
            // uses proportion '1/(2^n)' of the codespace. An overfull code is
            // nonsensical, so is considered invalid. An incomplete code is
            // considered valid only in two specific cases; see below.

            // overfull code?
            if (codespaceUsed > (uint)(1 << (int)maxCodewordLen))
                throw new InvalidOperationException("Overfull");

            // incomplete code?
            if (codespaceUsed < (uint)(1 << (int)maxCodewordLen))
            {
                throw new NotImplementedException();
            }

            // The lengths form a complete code. Now, enumerate the codewords in
            // lexicographic order and fill the decode table entries for each one.
            //
            // First, process all codewords with len <= table_bits. Each one gets
            // '2^(table_bits-len)' direct entries in the table.
            //
            // Since DEFLATE uses bit-reversed codewords, these entries aren't
            // consecutive but rather are spaced '2^len' entries apart. This makes
            // filling them naively somewhat awkward and inefficient, since strided
            // stores are less cache-friendly and preclude the use of word or
            // vector-at-a-time stores to fill multiple entries per instruction.
            //
            // To optimize this, we incrementally double the table size. When
            // processing codewords with length 'len', the table is treated as
            // having only '2^len' entries, so each codeword uses just one entry.
            // Then, each time 'len' is incremented, the table size is doubled and
            // the first half is copied to the second half. This significantly
            // improves performance over naively doing strided stores.
            //
            // Note that some entries copied for each table doubling may not have
            // been initialized yet, but it doesn't matter since they're guaranteed
            // to be initialized later (because the Huffman code is complete).

            codeword = 0;
            len = 1;
            while ((count = lenCounts[len]) == 0)
                len++;
            curTableEnd = (uint)(1 << (int)len);
            while (len <= tableBits)
            {
                // Process all 'count' codewords with length 'len' bits.
                do
                {
                    uint bit;

                    // Fill the first entry for the current codeword.
                    decodeTable[codeword] = decodeResults[sortedSyms[sortedSymsIdx]] | len;
                    sortedSymsIdx++;

                    if (codeword == curTableEnd - 1)
                    {
                        // Last codeword (all 1's)
                        for (; len < tableBits; len++)
                        {
                            Array.Copy(decodeTable, 0, decodeTable, curTableEnd, curTableEnd);
                            curTableEnd <<= 1;
                        }
                        return;
                    }

                    // To advance to the lexicographically next codeword in
                    // the canonical code, the codeword must be incremented,
                    // then 0's must be appended to the codeword as needed
                    // to match the next codeword's length.
                    // 
                    // Since the codeword is bit-reversed, appending 0's is
                    // a no-op. However, incrementing it is nontrivial.  To
                    // do so efficiently, use the 'bsr' instruction to find
                    // the last (highest order) 0 bit in the codeword, set
                    // it, and clear any later (higher order) 1 bits. But
                    // 'bsr' actually finds the highest order 1 bit, so to
                    // use it first flip all bits in the codeword by XOR'ing
                    // it with (1U << len) - 1 == curTableEnd - 1.

                    bit = (uint)(1 << (int)Utils.BitScanReverse32(codeword ^ (curTableEnd - 1)));
                    codeword &= bit - 1;
                    codeword |= bit;
                } while (--count > 0);

                // Advance to the next codeword length.
                do
                {
                    if (++len <= tableBits)
                    {
                        Array.Copy(decodeTable, 0, decodeTable, curTableEnd, curTableEnd);
                        curTableEnd <<= 1;
                    }
                } while ((count = lenCounts[len]) == 0);
            }

            // Process codewords with len > tableBits. These require subtables.
            curTableEnd = 1U << (int)tableBits;
            subtablePrefix = 0xffffffff;
            subtableStart = 0;
            while (true)
            {
                uint entry;
                uint i;
                uint stride;
                uint bit;

		        // Start a new subtable if the first 'tableBits' bits of the
		        // codeword don't match the prefix of the current subtable.

                if ((codeword & ((1U << (int)tableBits) - 1)) != subtablePrefix)
                {
                    subtablePrefix = codeword & ((1U << (int)tableBits) - 1);
                    subtableStart = curTableEnd;

			        // Calculate the subtable length. If the codeword has
			        // length 'table_bits + n', then the subtable needs
			        // '2^n' entries. But it may need more; if fewer than
			        // '2^n' codewords of length 'table_bits + n' remain,
			        // then the length will need to be incremented to bring
			        // in longer codewords until the subtable can be
			        // completely filled. Note that because the Huffman
			        // code is complete, it will always be possible to fill
			        // the subtable eventually.

                    subtableBits = len - tableBits;
                    codespaceUsed = count;
                    while (codespaceUsed < (1U << (int)subtableBits))
                    {
                        subtableBits++;
                        codespaceUsed = (codespaceUsed << 1) 
                            + lenCounts[tableBits + subtableBits];
                    }
                    curTableEnd = subtableStart + (1U << (int)subtableBits);

                    // Create the entry that points from the main table to
                    // the subtable. This entry contains the index of the
                    // start of the subtable and the number of bits with
                    // which the subtable is indexed (the log base 2 of the
                    // number of entries it contains).

                    decodeTable[subtablePrefix] = HuffDecSubtablePointer
                        | DecodeResults.HuffDecResultEntry(subtableStart)
                        | subtableBits;
                }

                // Fill the subtable entries for the current codeword.
                entry = decodeResults[sortedSyms[sortedSymsIdx]] | (len - tableBits);
                sortedSymsIdx++;
                i = subtableStart + (codeword >> (int)tableBits);
                stride = 1U << (int)(len - tableBits);
                do
                {
                    decodeTable[i] = entry;
                    i += stride;
                } while (i < curTableEnd);

                // Advance to the next codeword.
                if (codeword == (1U << (int)len) - 1) // last codeword (all 1's)?
                    return;

                bit = 1U << (int)Utils.BitScanReverse32(codeword ^ ((1U << (int)len) - 1));
                codeword &= bit - 1;
                codeword |= bit;
                count--;
                while (count == 0)
                    count = lenCounts[++len];
            }
        }
    }
}

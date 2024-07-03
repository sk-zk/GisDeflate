using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace GisDeflate
{
    public static class GDeflate
    {
        /// <summary>
        /// Decompresses data compressed with GDeflate.
        /// </summary>
        /// <param name="data">The compressed data.</param>
        /// <param name="numWorkers">The number of decompression tasks to run in parallel.
        /// If 0 is passed, this parameter is set to <c>Environment.ProcessorCount</c>.</param>
        /// <returns>The uncompressed data.</returns>
        public static byte[] Decompress(byte[] data, uint numWorkers = 0)
        {
            if (numWorkers == 0)
            {
                numWorkers = (uint)Environment.ProcessorCount;
            }

            using var ms = new MemoryStream(data);
            using var r = new BinaryReader(ms);

            var header = new TileStream();
            header.Deserialize(r);
            header.ValidateStream();

            var context = new DecompressionContext
            {
                Input = data,
                Output = new byte[header.UncompressedSize],
                GlobalIndex = 0,
                NumItems = header.NumTiles
            };

            var inData = context.Input[(int)(0x8 + context.NumItems * 4)..];
            var tileOffsets = MemoryMarshal.Cast<byte, int>(
                context.Input[0x8..(0x8 + (int)(context.NumItems * 4))]
                .AsSpan()).ToArray();

            var tasks = new Task[numWorkers];
            for (int i = 0; i < numWorkers; i++)
            {
                tasks[i] = Task.Run(() => Deflate.TileDecompressionJob(context, inData, tileOffsets));
            }
            Task.WaitAll(tasks);

            return context.Output;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GisDeflate
{
    /// <summary>
    /// GDeflate state structure.
    /// </summary>
    internal struct State
    {
        public ulong[] BitBuf = new ulong[Deflate.NumStreams];
        public uint[] BitsLeft = new uint[Deflate.NumStreams];
        public DeferredCopy[] Copies = new DeferredCopy[Deflate.NumStreams];
        public uint Idx = 0;

        public State()
        {
        }
    }
}

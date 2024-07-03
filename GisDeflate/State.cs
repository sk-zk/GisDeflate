using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GisDeflate
{
    internal struct State
    {
        public ulong[] BitBuf = new ulong[32];
        public int[] BitsLeft = new int[32];
        public DeferredCopy[] Copies = new DeferredCopy[32];
        public uint Idx = 0;

        public State()
        {
        }
    }
}

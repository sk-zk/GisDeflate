using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GisDeflate
{
    internal class DecompressionContext
    {
        public byte[] Input;
        public byte[] Output;
        public int GlobalIndex;
        public uint NumItems;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GisDeflate
{
    /// <summary>
    /// Valid block types.
    /// </summary>
    internal enum BlockType
    {
        Uncompressed = 0,
        StaticHuffman = 1,
        DynamicHuffman = 2
    }
}

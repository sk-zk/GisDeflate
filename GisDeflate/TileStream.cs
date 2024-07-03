using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GisDeflate
{
    internal class TileStream
    {
        const byte KDeflateId = 4;
        public const uint KDefaultTileSize = 64 * 1024;

        public byte Id;

        public byte Magic;

        public ushort NumTiles;

        private FlagField bitField;

        public ushort TileSizeIdx
        {
            get => (ushort)bitField.GetBitString(0, 2);
            set => bitField.SetBitString(0, 2, value);
        }

        public uint LastTileSize
        {
            get => bitField.GetBitString(2, 18);
            set => bitField.SetBitString(2, 18, value);
        }

        public bool IsValid =>
            Id == (Magic ^ 0xff);

        public uint UncompressedSize =>
            NumTiles * KDefaultTileSize - (LastTileSize == 0 ? 0 : KDefaultTileSize - LastTileSize);

        public void Deserialize(BinaryReader r)
        {
            Id = r.ReadByte();
            Magic = r.ReadByte();
            NumTiles = r.ReadUInt16();
            bitField = new FlagField(r.ReadUInt32());
        }

        public void ValidateStream()
        {
            if (!IsValid)
                throw new InvalidDataException("Malformed stream encountered.");

            if (Id != KDeflateId)
                throw new InvalidDataException($"Unknown stream format: {Id}");
        }
    }
}

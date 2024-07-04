using GisDeflate;

namespace GisDeflateTests
{
    public class GDeflateTest
    {
        [Test]
        public void Decompress()
        {
            var compressed = File.ReadAllBytes("Data/DynamicBlocks/compressed.bin");
            var expected = File.ReadAllBytes("Data/DynamicBlocks/expected.bin");

            var actual = GDeflate.Decompress(compressed);

            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void DecompressWithUncompBlocks()
        {
            var compressed = File.ReadAllBytes("Data/DynamicAndUncompBlocks/compressed.bin");
            var expected = File.ReadAllBytes("Data/DynamicAndUncompBlocks/expected.bin");

            var actual = GDeflate.Decompress(compressed);

            Assert.That(actual, Is.EqualTo(expected));
        }
    }
}
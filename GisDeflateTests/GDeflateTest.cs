using GisDeflate;

namespace GisDeflateTests
{
    public class GDeflateTest
    {
        [Test]
        public void Decompress()
        {
            var compressed = File.ReadAllBytes("Data/compressed.bin");
            var expected = File.ReadAllBytes("Data/expected.bin");

            var actual = GDeflate.Decompress(compressed);

            Assert.That(actual, Is.EqualTo(expected));
        }
    }
}
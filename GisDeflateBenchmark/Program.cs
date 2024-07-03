using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using GisDeflate;

namespace GisDeflateBenchmark
{
    internal class Program
    {
        static void Main(string[] args)
        {
            var summary = BenchmarkRunner.Run<DecompressionBenchmark>();
        }
    }

    [SimpleJob(RuntimeMoniker.Net80)]
    [RPlotExporter]
    public class DecompressionBenchmark
    {
        private byte[] compressed;

        [Params(1000)]
        public int N;

        [GlobalSetup]
        public void Setup()
        {
            compressed = File.ReadAllBytes("Data/compressed.bin");
        }

        [Benchmark]
        public byte[] Decompress() => GDeflate.Decompress(compressed);

    }
}

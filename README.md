# GisDeflate
GDeflate decompression in C# with zero native dependencies.

## Install
`nuget install GisDeflate`

## Usage
```cs
byte[] compressed = File.ReadAllBytes("foo.bin");
byte[] decompressed = GDeflate.Decompress(compressed);
```

## Credits
Naturally, this code is a port of the reference implementation by [Microsoft](https://github.com/microsoft/DirectStorage/tree/main/GDeflate)
and [NVIDIA](https://github.com/NVIDIA/libdeflate/tree/3bb5c6924b32a91e6e6a8f54ba00a21f037a8db5).

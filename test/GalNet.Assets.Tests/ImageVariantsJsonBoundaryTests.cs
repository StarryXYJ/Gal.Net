using System.Text;
using System.Text.Json;
using GalNet.Assets.Variants;

namespace GeneralTest.Assets;

public sealed class ImageVariantsJsonBoundaryTests
{
    [TestCase("""{"folder":"a","folder":"b"}""")]
    [TestCase("""{"folder":"a","dimensions":[{"key":"pose","key":"mood"}]}""")]
    public void SourceRejectsDuplicatesAtEveryObjectDepth(string json) =>
        Assert.That(() => ImageVariantsCodec.ReadSource(Encoding.UTF8.GetBytes(json)), Throws.TypeOf<JsonException>());

    [TestCase("""{"kind":"image-variants","version":1,"version":1}""")]
    [TestCase("""{"kind":"image-variants","version":1,"default":"a","dimensions":[{"key":"pose"}],"variants":[{"key":"a","assetId":"a","assetId":"b"}]}""")]
    public void CompiledPayloadRejectsDuplicatesAtEveryObjectDepth(string json) =>
        Assert.That(() => ImageVariantsCodec.Decode(Encoding.UTF8.GetBytes(json)), Throws.TypeOf<JsonException>());
}

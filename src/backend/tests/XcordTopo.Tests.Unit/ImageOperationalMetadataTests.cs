using XcordTopo.Infrastructure.Providers;
using XcordTopo.Models;

namespace XcordTopo.Tests.Unit;

public class ImageOperationalMetadataTests
{
    [Fact]
    public void AllImageKinds_HaveMetadataEntry()
    {
        foreach (var kind in Enum.GetValues<ImageKind>())
        {
            Assert.True(
                ImageOperationalMetadata.Images.ContainsKey(kind),
                $"Missing metadata for ImageKind.{kind}");
        }
    }

    [Theory]
    [InlineData(ImageKind.PostgreSQL, 512)]
    [InlineData(ImageKind.Redis, 256)]
    [InlineData(ImageKind.MinIO, 512)]
    [InlineData(ImageKind.HubServer, 512)]
    [InlineData(ImageKind.FederationServer, 192)]
    [InlineData(ImageKind.LiveKit, 1024)]
    [InlineData(ImageKind.Custom, 256)]
    public void MinRamMb_HasExpectedValues(ImageKind kind, int expectedRam)
    {
        var meta = ImageOperationalMetadata.Images[kind];
        Assert.Equal(expectedRam, meta.MinRamMb);
    }
}

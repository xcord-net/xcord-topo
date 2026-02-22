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
    [InlineData(ImageKind.FederationServer, 512)]
    [InlineData(ImageKind.LiveKit, 1024)]
    [InlineData(ImageKind.Custom, 256)]
    public void MinRamMb_HasExpectedValues(ImageKind kind, int expectedRam)
    {
        var meta = ImageOperationalMetadata.Images[kind];
        Assert.Equal(expectedRam, meta.MinRamMb);
    }

    [Fact]
    public void CaddyMetadata_HasCorrectValues()
    {
        Assert.Equal([80, 443], ImageOperationalMetadata.Caddy.Ports);
        Assert.Equal("/data", ImageOperationalMetadata.Caddy.MountPath);
        Assert.Equal(128, ImageOperationalMetadata.Caddy.MinRamMb);
        Assert.Equal("caddy:2-alpine", ImageOperationalMetadata.Caddy.DockerImage);
    }

    [Fact]
    public void PostgreSQL_HasExpectedPorts()
    {
        var meta = ImageOperationalMetadata.Images[ImageKind.PostgreSQL];
        Assert.Equal([5432], meta.Ports);
    }

    [Fact]
    public void LiveKit_HasExpectedPorts()
    {
        var meta = ImageOperationalMetadata.Images[ImageKind.LiveKit];
        Assert.Equal([7880, 7881, 7882], meta.Ports);
    }

    [Fact]
    public void Redis_HasCommandOverride()
    {
        var meta = ImageOperationalMetadata.Images[ImageKind.Redis];
        Assert.NotNull(meta.CommandOverride);
        Assert.Contains("requirepass", meta.CommandOverride);
    }

    [Fact]
    public void MinIO_HasCommandOverride()
    {
        var meta = ImageOperationalMetadata.Images[ImageKind.MinIO];
        Assert.NotNull(meta.CommandOverride);
        Assert.Contains("server /data", meta.CommandOverride);
    }
}

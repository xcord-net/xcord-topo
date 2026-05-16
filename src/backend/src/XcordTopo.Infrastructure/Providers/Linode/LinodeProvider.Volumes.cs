using XcordTopo.Infrastructure.Terraform;
using XcordTopo.Models;

namespace XcordTopo.Infrastructure.Providers;

public sealed partial class LinodeProvider
{
    private static string GenerateVolumes(List<TopologyHelpers.HostEntry> hosts)
    {
        var volumes = new HclBuilder();
        foreach (var entry in hosts)
        {
            var images = TopologyHelpers.CollectImages(entry.Host);
            foreach (var image in images)
            {
                var volumeSize = image.Config.GetValueOrDefault("volumeSize", "");
                if (string.IsNullOrEmpty(volumeSize)) continue;

                var size = int.TryParse(volumeSize, out var s) ? s : 25;
                var resourceName = $"{TopologyHelpers.SanitizeName(entry.Host.Name)}_{TopologyHelpers.SanitizeName(image.Name)}";

                var isReplicated = TopologyHelpers.IsReplicatedHost(entry);
                volumes.Block($"resource \"linode_volume\" \"{resourceName}_vol\"", b =>
                {
                    if (isReplicated)
                    {
                        var countExpr = TopologyHelpers.GetHostCountExpression(entry);
                        b.RawAttribute("count", countExpr!);
                        b.Attribute("label", $"{image.Name}-vol-${{count.index}}");
                    }
                    else
                    {
                        b.Attribute("label", $"{image.Name}-vol");
                    }

                    b.RawAttribute("region", "var.linode_region");
                    b.Attribute("size", size);
                    b.RawAttribute("linode_id", isReplicated
                        ? $"linode_instance.{TopologyHelpers.SanitizeName(entry.Host.Name)}[count.index].id"
                        : $"linode_instance.{TopologyHelpers.SanitizeName(entry.Host.Name)}.id");
                });
                volumes.Line();
            }
        }
        return volumes.ToString();
    }
}

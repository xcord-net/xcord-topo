using XcordTopo.Infrastructure.Terraform;
using XcordTopo.Models;

namespace XcordTopo.Infrastructure.Providers;

public sealed partial class LinodeProvider
{
    private static string? GenerateColdStorage(Topology topology)
    {
        if (topology.BackupTarget is null || topology.BackupTarget.Kind != BackupTargetKind.LinodeObjectStorage)
            return null;

        var hcl = new HclBuilder();

        hcl.Block("resource \"linode_object_storage_bucket\" \"backups\"", b =>
        {
            b.RawAttribute("cluster", "\"${var.linode_region}-1\"");
            b.RawAttribute("label", "var.coldstore_bucket");
        });
        hcl.Line();

        hcl.Block("resource \"linode_object_storage_key\" \"backups\"", b =>
        {
            b.RawAttribute("label", "\"xcord-backups-${var.domain}\"");
            b.Line();
            b.Block("bucket_access", ba =>
            {
                ba.RawAttribute("cluster", "linode_object_storage_bucket.backups.cluster");
                ba.RawAttribute("bucket_name", "linode_object_storage_bucket.backups.label");
                ba.Attribute("permissions", "read_write");
            });
        });
        hcl.Line();

        hcl.Block("output \"coldstore_endpoint\"", b =>
        {
            b.RawAttribute("value", "linode_object_storage_bucket.backups.hostname");
            b.Attribute("description", "Cold storage endpoint for backups");
        });
        hcl.Line();

        hcl.Block("output \"coldstore_access_key\"", b =>
        {
            b.RawAttribute("value", "linode_object_storage_key.backups.access_key");
            b.Attribute("sensitive", true);
            b.Attribute("description", "Cold storage access key");
        });
        hcl.Line();

        hcl.Block("output \"coldstore_secret_key\"", b =>
        {
            b.RawAttribute("value", "linode_object_storage_key.backups.secret_key");
            b.Attribute("sensitive", true);
            b.Attribute("description", "Cold storage secret key");
        });

        return hcl.ToString();
    }

    // --- File generators ---

}

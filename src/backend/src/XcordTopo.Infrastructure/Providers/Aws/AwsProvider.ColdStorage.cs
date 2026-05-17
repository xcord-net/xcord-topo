using XcordTopo.Infrastructure.Terraform;
using XcordTopo.Models;

namespace XcordTopo.Infrastructure.Providers;

public sealed partial class AwsProvider
{
    // --- Cold storage (S3) generation ---

    private string? GenerateColdStorage(Topology topology)
    {
        if (topology.BackupTarget is null || topology.BackupTarget.Kind != BackupTargetKind.AwsS3)
            return null;

        var glacierDays = topology.BackupTarget.GlacierTransitionDays ?? 30;
        var hcl = new HclBuilder();

        hcl.Block("resource \"aws_s3_bucket\" \"backups\"", b =>
        {
            b.RawAttribute("bucket", "var.coldstore_bucket");
            // Defaults to false; spelled out to make the protection explicit. AWS will refuse
            // to delete the bucket if it contains any objects.
            b.RawAttribute("force_destroy", "false");
            b.Line();
            // Backups are the last line of defence for stateful infrastructure. Refuse
            // destroy via terraform; intentional teardown requires removing this block first.
            b.Block("lifecycle", lb =>
            {
                lb.RawAttribute("prevent_destroy", "true");
            });
        });
        hcl.Line();

        hcl.Block("resource \"aws_s3_bucket_versioning\" \"backups\"", b =>
        {
            b.RawAttribute("bucket", "aws_s3_bucket.backups.id");
            b.Line();
            b.Block("versioning_configuration", inner =>
            {
                inner.Attribute("status", "Enabled");
            });
        });
        hcl.Line();

        hcl.Block("resource \"aws_s3_bucket_lifecycle_configuration\" \"backups\"", b =>
        {
            b.RawAttribute("bucket", "aws_s3_bucket.backups.id");
            b.Line();
            b.Block("rule", rule =>
            {
                rule.Attribute("id", "glacier-transition");
                rule.Attribute("status", "Enabled");
                rule.Line();
                rule.Block("transition", t =>
                {
                    t.Attribute("days", glacierDays);
                    t.Attribute("storage_class", "GLACIER");
                });
            });
        });
        hcl.Line();

        hcl.Block("output \"coldstore_endpoint\"", b =>
        {
            b.RawAttribute("value", "\"s3.${var.aws_region}.amazonaws.com\"");
            b.Attribute("description", "Cold storage endpoint for backups");
        });
        hcl.Line();

        hcl.Block("output \"coldstore_bucket\"", b =>
        {
            b.RawAttribute("value", "aws_s3_bucket.backups.id");
            b.Attribute("description", "Cold storage bucket name");
        });

        return hcl.ToString();
    }
}

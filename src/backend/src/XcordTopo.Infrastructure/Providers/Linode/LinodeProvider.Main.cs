using XcordTopo.Infrastructure.Terraform;
using XcordTopo.Models;

namespace XcordTopo.Infrastructure.Providers;

public sealed partial class LinodeProvider
{
    private static string GenerateMain()
    {
        var main = new HclBuilder();
        main.Block("terraform", b =>
        {
            b.Block("required_providers", p =>
            {
                p.MapBlock("linode", lp =>
                {
                    lp.Attribute("source", "linode/linode");
                    lp.Attribute("version", "~> 2.0");
                });
                p.MapBlock("random", rp =>
                {
                    rp.Attribute("source", "hashicorp/random");
                    rp.Attribute("version", "~> 3.0");
                });
                p.MapBlock("tls", tp =>
                {
                    tp.Attribute("source", "hashicorp/tls");
                    tp.Attribute("version", "~> 4.0");
                });
            });
        });
        main.Line();
        main.Block("provider \"linode\"", b =>
        {
            b.RawAttribute("token", "var.linode_token");
        });
        main.Line();
        main.Block("resource \"tls_private_key\" \"deploy\"", b =>
        {
            b.Attribute("algorithm", "ED25519");
        });
        return main.ToString();
    }

}

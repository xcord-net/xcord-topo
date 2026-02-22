export interface HelpLink {
  label: string;
  url: string;
  description: string;
}

export const PROVIDER_HELP_LINKS: Record<string, HelpLink[]> = {
  linode: [
    {
      label: 'Create a Linode API Token',
      url: 'https://techdocs.akamai.com/linode-api/reference/get-started',
      description: 'Generate a Personal Access Token with read/write scopes for Linodes, Domains, Firewalls, and Volumes.',
    },
    {
      label: 'DNS Manager',
      url: 'https://techdocs.akamai.com/cloud-computing/docs/dns-manager',
      description: 'Add your domain to the Linode DNS Manager and configure A/AAAA records.',
    },
    {
      label: 'Manage Firewall Rules',
      url: 'https://techdocs.akamai.com/cloud-computing/docs/manage-firewall-rules',
      description: 'Configure firewall rules to allow HTTP (80), HTTPS (443), and SSH (22) traffic.',
    },
    {
      label: 'SSH Key Authentication',
      url: 'https://techdocs.akamai.com/cloud-computing/docs/use-public-key-authentication-with-ssh',
      description: 'Generate an SSH key pair and add the public key to your Linode account.',
    },
    {
      label: 'Getting Started with Linode',
      url: 'https://techdocs.akamai.com/cloud-computing/docs/getting-started',
      description: 'Create a Linode account and set up your first compute instance.',
    },
  ],
};

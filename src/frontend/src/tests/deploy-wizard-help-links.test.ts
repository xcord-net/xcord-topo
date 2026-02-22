import { describe, it, expect } from 'vitest';
import { PROVIDER_HELP_LINKS } from '../lib/provider-help-links';

describe('PROVIDER_HELP_LINKS', () => {
  it('has entries for linode covering API token, DNS, firewall, SSH, and getting started', () => {
    const linode = PROVIDER_HELP_LINKS['linode'];
    expect(linode).toBeDefined();
    expect(linode.length).toBe(5);

    const labels = linode.map((l) => l.label);
    expect(labels).toContain('Create a Linode API Token');
    expect(labels).toContain('DNS Manager');
    expect(labels).toContain('Manage Firewall Rules');
    expect(labels).toContain('SSH Key Authentication');
    expect(labels).toContain('Getting Started with Linode');
  });

  it('all help link URLs are HTTPS', () => {
    for (const [, links] of Object.entries(PROVIDER_HELP_LINKS)) {
      for (const link of links) {
        expect(link.url).toMatch(/^https:\/\//);
      }
    }
  });

  it('all help links have non-empty label, url, and description', () => {
    for (const [, links] of Object.entries(PROVIDER_HELP_LINKS)) {
      for (const link of links) {
        expect(link.label.length).toBeGreaterThan(0);
        expect(link.url.length).toBeGreaterThan(0);
        expect(link.description.length).toBeGreaterThan(0);
      }
    }
  });

  it('returns undefined for an unknown provider key', () => {
    expect(PROVIDER_HELP_LINKS['unknown-provider']).toBeUndefined();
  });
});

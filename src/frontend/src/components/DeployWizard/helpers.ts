import { imageDefinitions } from '../../catalog/images';
import { type Topology, type Container, resolveTypeId } from '../../types/topology';

/** Collect all distinct provider keys from container overrides + topology-level provider. */
export function collectActiveProviders(topology: Topology): string[] {
  const keys = new Set<string>([topology.provider]);
  function walk(containers: Container[]) {
    for (const c of containers) {
      const override = c.config?.provider;
      if (override) keys.add(override);
      if (c.children) walk(c.children);
    }
  }
  walk(topology.containers);
  return Array.from(keys);
}

/** Collect all application image kinds present in the topology that require a private registry. */
export function collectAppImageKinds(topology: Topology): string[] {
  const catalog = imageDefinitions();
  const kinds = new Set<string>();
  function walk(containers: Container[]) {
    for (const c of containers) {
      for (const img of c.images) {
        const def = catalog.find(d => d.kind === resolveTypeId(img));
        if (def?.dockerBehavior?.requiresPrivateRegistry) kinds.add(resolveTypeId(img));
      }
      if (c.children) walk(c.children);
    }
  }
  walk(topology.containers);
  return Array.from(kinds);
}

/** Derive the GitHub repo name for an image kind from the catalog. */
export function getRepoName(kind: string): string | null {
  const catalog = imageDefinitions();
  const def = catalog.find(d => d.kind === kind);
  const gitUrl = def?.dockerBehavior?.gitRepoUrl;
  if (gitUrl) {
    // Extract repo name from URL: "https://github.com/xcord-net/xcord-hub.git" -> "xcord-hub"
    const lastSegment = gitUrl.split('/').pop() ?? '';
    return lastSegment.replace(/\.git$/, '');
  }
  return null;
}

export function formatRelativeTime(iso: string): string {
  const diff = Date.now() - new Date(iso).getTime();
  const seconds = Math.floor(diff / 1000);
  if (seconds < 60) return 'just now';
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return `${minutes}m ago`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours}h ago`;
  const days = Math.floor(hours / 24);
  return `${days}d ago`;
}

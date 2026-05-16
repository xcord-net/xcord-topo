import type { DeployStep } from '../../types/deploy';

export interface ProviderInfo {
  key: string;
  name: string;
  description: string;
  supportedContainerKinds: string[];
}

export interface Region {
  id: string;
  label: string;
  country: string;
}

export const STEPS: { key: DeployStep; label: string }[] = [
  { key: 'provider', label: 'Provider' },
  { key: 'configure', label: 'Configure' },
  { key: 'validate', label: 'Validate' },
  { key: 'hosting', label: 'Hosting' },
  { key: 'review', label: 'Review' },
  { key: 'migrate', label: 'Migrate' },
  { key: 'execute', label: 'Execute' },
];

export const matchKindBadge: Record<string, { label: string; class: string }> = {
  Unchanged: { label: 'Unchanged', class: 'bg-topo-text-muted/20 text-topo-text-muted' },
  Modified: { label: 'Modified', class: 'bg-topo-warning/20 text-topo-warning' },
  Relocated: { label: 'Relocated', class: 'bg-topo-brand/20 text-topo-brand' },
  Split: { label: 'Split', class: 'bg-purple-500/20 text-purple-400' },
  Added: { label: 'Added', class: 'bg-topo-success/20 text-topo-success' },
  Removed: { label: 'Removed', class: 'bg-topo-error/20 text-topo-error' },
};

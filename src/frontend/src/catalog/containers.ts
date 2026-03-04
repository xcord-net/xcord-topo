import type { ContainerDefinition } from '../types/catalog';

export const containerDefinitions: ContainerDefinition[] = [
  {
    kind: 'Host',
    label: 'Host',
    color: '#7aa2f7',
    defaultWidth: 400,
    defaultHeight: 300,
    defaultPorts: [
      { id: '', name: 'ssh', type: 'Control', direction: 'In', side: 'Left', offset: 0.5 },
      { id: '', name: 'public', type: 'Network', direction: 'InOut', side: 'Right', offset: 0.3 },
      { id: '', name: 'private', type: 'Network', direction: 'InOut', side: 'Right', offset: 0.7 },
    ],
    configFields: [
      { key: 'provider', label: 'Provider Override', type: 'select', options: [
        { value: '', label: 'Default' },
        { value: 'linode', label: 'Linode' },
        { value: 'aws', label: 'AWS' },
      ] },
      { key: 'replicas', label: 'Replicas', placeholder: '1' },
      { key: 'minReplicas', label: 'Min Replicas', placeholder: '' },
      { key: 'maxReplicas', label: 'Max Replicas', placeholder: '' },
      { key: 'backupFrequency', label: 'Backup Frequency', placeholder: 'daily' },
      { key: 'backupRetention', label: 'Backup Retention', placeholder: '7' },
    ],
    description: 'Server or VM — deployment decides the architecture',
  },
  {
    kind: 'Caddy',
    label: 'Caddy',
    color: '#22d3ee',
    defaultWidth: 400,
    defaultHeight: 300,
    defaultPorts: [
      { id: '', name: 'http_in', type: 'Network', direction: 'In', side: 'Top', offset: 0.3 },
      { id: '', name: 'https_in', type: 'Network', direction: 'In', side: 'Top', offset: 0.7 },
      { id: '', name: 'upstream', type: 'Network', direction: 'Out', side: 'Bottom', offset: 0.5 },
    ],
    configFields: [
      { key: 'domain', label: 'Domain', placeholder: 'example.com' },
    ],
    description: 'Caddy reverse proxy with auto-TLS',
  },
  {
    kind: 'ComputePool',
    label: 'Compute Pool',
    color: '#e0af68',
    defaultWidth: 500,
    defaultHeight: 350,
    defaultPorts: [
      { id: '', name: 'public', type: 'Network', direction: 'InOut', side: 'Right', offset: 0.5 },
      { id: '', name: 'control', type: 'Control', direction: 'In', side: 'Left', offset: 0.5 },
    ],
    configFields: [
      { key: 'provider', label: 'Provider Override', type: 'select', options: [
        { value: '', label: 'Default' },
        { value: 'linode', label: 'Linode' },
        { value: 'aws', label: 'AWS' },
      ] },
      { key: 'tierProfile', label: 'Tier Profile', type: 'select', options: [
        { value: 'free', label: 'Free' },
        { value: 'basic', label: 'Basic' },
        { value: 'pro', label: 'Pro' },
        { value: 'enterprise', label: 'Enterprise' },
      ] },
      { key: 'targetTenants', label: 'Target Tenants', placeholder: '100', tooltip: 'Total tenants this pool should support. The number of hosts is calculated automatically: each host fits as many tenants as its RAM allows after shared infrastructure overhead (PG, Redis, MinIO, Caddy), based on the selected tier profile.' },
    ],
    description: 'Shared-infrastructure host pool with tier-based tenant packing',
  },
  {
    kind: 'Dns',
    label: 'DNS Zone',
    color: '#bb9af7',
    defaultWidth: 300,
    defaultHeight: 160,
    defaultPorts: [
      { id: '', name: 'records', type: 'Network', direction: 'In', side: 'Left', offset: 0.5 },
    ],
    configFields: [
      { key: 'provider', label: 'Provider Override', type: 'select', options: [
        { value: '', label: 'Default' },
        { value: 'linode', label: 'Linode' },
        { value: 'aws', label: 'AWS' },
      ] },
      { key: 'domain', label: 'Domain', placeholder: 'example.com' },
    ],
    description: 'DNS zone — wire hosts here to create A records',
  },
];

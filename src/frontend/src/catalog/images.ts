import type { ImageDefinition } from '../types/catalog';

const subdomainRegex = /^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$/;

function validateSubdomain(value: string): string | null {
  if (!value) return null; // empty is ok (optional)
  if (value.length > 63) return 'Max 63 characters';
  if (!subdomainRegex.test(value)) return 'Lowercase letters, numbers, and hyphens only';
  return null;
}

export const imageDefinitions: ImageDefinition[] = [
  {
    kind: 'HubServer',
    label: 'Hub Server',
    color: '#7aa2f7',
    defaultWidth: 140,
    defaultHeight: 60,
    defaultPorts: [
      { id: '', name: 'http', type: 'Network', direction: 'In', side: 'Left', offset: 0.5 },
      { id: '', name: 'pg', type: 'Database', direction: 'Out', side: 'Right', offset: 0.33 },
      { id: '', name: 'redis', type: 'Database', direction: 'Out', side: 'Right', offset: 0.67 },
    ],
    defaultDockerImage: 'ghcr.io/xcord/hub:latest',
    configFields: [
      { key: 'scaling', label: 'Scaling', type: 'select', options: [
        { value: 'Shared', label: 'Shared (1 per host)' },
        { value: 'PerTenant', label: 'Per Tenant' },
      ], parentKinds: ['ComputePool'] },
      { key: 'replicas', label: 'Replicas', placeholder: '1' },
    ],
    description: 'xcord hub control plane',
  },
  {
    kind: 'FederationServer',
    label: 'Federation Server',
    color: '#bb9af7',
    defaultWidth: 140,
    defaultHeight: 60,
    defaultPorts: [
      { id: '', name: 'http', type: 'Network', direction: 'In', side: 'Left', offset: 0.5 },
      { id: '', name: 'pg', type: 'Database', direction: 'Out', side: 'Right', offset: 0.25 },
      { id: '', name: 'redis', type: 'Database', direction: 'Out', side: 'Right', offset: 0.5 },
      { id: '', name: 'minio', type: 'Storage', direction: 'Out', side: 'Right', offset: 0.75 },
    ],
    defaultDockerImage: 'ghcr.io/xcord/fed:latest',
    configFields: [
      { key: 'scaling', label: 'Scaling', type: 'select', options: [
        { value: 'Shared', label: 'Shared (1 per host)' },
        { value: 'PerTenant', label: 'Per Tenant' },
      ], parentKinds: ['ComputePool'] },
      { key: 'replicas', label: 'Replicas', placeholder: '1' },
    ],
    defaultScaling: 'PerTenant',
    description: 'xcord federation instance',
  },
  {
    kind: 'Redis',
    label: 'Redis',
    color: '#dc382d',
    defaultWidth: 120,
    defaultHeight: 50,
    defaultPorts: [
      { id: '', name: 'redis', type: 'Database', direction: 'In', side: 'Left', offset: 0.5 },
    ],
    defaultDockerImage: 'redis:7-alpine',
    configFields: [
      { key: 'scaling', label: 'Scaling', type: 'select', options: [
        { value: 'Shared', label: 'Shared (1 per host)' },
        { value: 'PerTenant', label: 'Per Tenant' },
      ], parentKinds: ['ComputePool'] },
      { key: 'replicas', label: 'Replicas', placeholder: '1' },
      { key: 'volumeSize', label: 'Volume (GB)', placeholder: '25' },
      { key: 'backupFrequency', label: 'Backup Frequency', placeholder: 'daily' },
      { key: 'backupRetention', label: 'Backup Retention', placeholder: '7' },
    ],
    description: 'Redis in-memory data store',
  },
  {
    kind: 'PostgreSQL',
    label: 'PostgreSQL',
    color: '#336791',
    defaultWidth: 120,
    defaultHeight: 50,
    defaultPorts: [
      { id: '', name: 'postgres', type: 'Database', direction: 'In', side: 'Left', offset: 0.5 },
    ],
    defaultDockerImage: 'postgres:17-alpine',
    configFields: [
      { key: 'scaling', label: 'Scaling', type: 'select', options: [
        { value: 'Shared', label: 'Shared (1 per host)' },
        { value: 'PerTenant', label: 'Per Tenant' },
      ], parentKinds: ['ComputePool'] },
      { key: 'replicas', label: 'Replicas', placeholder: '1' },
      { key: 'volumeSize', label: 'Volume (GB)', placeholder: '25' },
      { key: 'backupFrequency', label: 'Backup Frequency', placeholder: 'daily' },
      { key: 'backupRetention', label: 'Backup Retention', placeholder: '7' },
    ],
    description: 'PostgreSQL database',
  },
  {
    kind: 'MinIO',
    label: 'MinIO',
    color: '#c72e49',
    defaultWidth: 120,
    defaultHeight: 50,
    defaultPorts: [
      { id: '', name: 's3', type: 'Storage', direction: 'In', side: 'Left', offset: 0.5 },
    ],
    defaultDockerImage: 'minio/minio:latest',
    configFields: [
      { key: 'scaling', label: 'Scaling', type: 'select', options: [
        { value: 'Shared', label: 'Shared (1 per host)' },
        { value: 'PerTenant', label: 'Per Tenant' },
      ], parentKinds: ['ComputePool'] },
      { key: 'replicas', label: 'Replicas', placeholder: '1' },
      { key: 'volumeSize', label: 'Volume (GB)', placeholder: '25' },
      { key: 'backupFrequency', label: 'Backup Frequency', placeholder: 'daily' },
      { key: 'backupRetention', label: 'Backup Retention', placeholder: '7' },
    ],
    description: 'S3-compatible object storage',
  },
  {
    kind: 'LiveKit',
    label: 'LiveKit',
    color: '#ff6b6b',
    defaultWidth: 120,
    defaultHeight: 50,
    defaultPorts: [
      { id: '', name: 'rtc', type: 'Network', direction: 'In', side: 'Left', offset: 0.33 },
      { id: '', name: 'api', type: 'Network', direction: 'In', side: 'Left', offset: 0.67 },
      { id: '', name: 'redis', type: 'Database', direction: 'Out', side: 'Right', offset: 0.5 },
    ],
    defaultDockerImage: 'livekit/livekit-server:latest',
    configFields: [
      { key: 'scaling', label: 'Scaling', type: 'select', options: [
        { value: 'Shared', label: 'Shared (1 per host)' },
        { value: 'PerTenant', label: 'Per Tenant' },
      ], parentKinds: ['ComputePool'] },
      { key: 'replicas', label: 'Replicas', placeholder: '1' },
    ],
    description: 'LiveKit WebRTC SFU',
  },
  {
    kind: 'Custom',
    label: 'Custom Image',
    color: '#a9b1d6',
    defaultWidth: 120,
    defaultHeight: 50,
    defaultPorts: [
      { id: '', name: 'port', type: 'Generic', direction: 'InOut', side: 'Left', offset: 0.5 },
    ],
    configFields: [
      { key: 'subdomain', label: 'Subdomain', placeholder: 'myapp', validate: validateSubdomain },
      { key: 'scaling', label: 'Scaling', type: 'select', options: [
        { value: 'Shared', label: 'Shared (1 per host)' },
        { value: 'PerTenant', label: 'Per Tenant' },
      ], parentKinds: ['ComputePool'] },
      { key: 'replicas', label: 'Replicas', placeholder: '1' },
    ],
    description: 'Custom Docker image',
  },
];

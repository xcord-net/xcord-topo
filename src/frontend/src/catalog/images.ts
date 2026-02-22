import type { ImageDefinition } from '../types/catalog';

export const imageDefinitions: ImageDefinition[] = [
  {
    kind: 'HubServer',
    label: 'Hub Server',
    color: '#7aa2f7',
    defaultWidth: 140,
    defaultHeight: 60,
    defaultPorts: [
      { id: '', name: 'http', type: 'Network', direction: 'In', side: 'Top', offset: 0.5 },
      { id: '', name: 'pg_connection', type: 'Database', direction: 'Out', side: 'Bottom', offset: 0.3 },
      { id: '', name: 'redis_connection', type: 'Database', direction: 'Out', side: 'Bottom', offset: 0.7 },
    ],
    defaultDockerImage: 'ghcr.io/xcord/hub:latest',
    configFields: [
      { key: 'replicas', label: 'Replicas', placeholder: '1' },
      { key: 'upstreamPath', label: 'Upstream Path', placeholder: '/hub/*', parentKinds: ['Caddy'] },
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
      { id: '', name: 'http', type: 'Network', direction: 'In', side: 'Top', offset: 0.5 },
      { id: '', name: 'pg_connection', type: 'Database', direction: 'Out', side: 'Bottom', offset: 0.2 },
      { id: '', name: 'redis_connection', type: 'Database', direction: 'Out', side: 'Bottom', offset: 0.5 },
      { id: '', name: 'minio_connection', type: 'Storage', direction: 'Out', side: 'Bottom', offset: 0.8 },
    ],
    defaultDockerImage: 'ghcr.io/xcord/fed:latest',
    configFields: [
      { key: 'replicas', label: 'Replicas', placeholder: '1' },
      { key: 'upstreamPath', label: 'Upstream Path', placeholder: '/*', parentKinds: ['Caddy'] },
    ],
    description: 'xcord federation instance',
  },
  {
    kind: 'Redis',
    label: 'Redis',
    color: '#dc382d',
    defaultWidth: 120,
    defaultHeight: 50,
    defaultPorts: [
      { id: '', name: 'redis', type: 'Database', direction: 'In', side: 'Top', offset: 0.5 },
    ],
    defaultDockerImage: 'redis:7-alpine',
    configFields: [
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
      { id: '', name: 'postgres', type: 'Database', direction: 'In', side: 'Top', offset: 0.5 },
    ],
    defaultDockerImage: 'postgres:17-alpine',
    configFields: [
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
      { id: '', name: 's3', type: 'Storage', direction: 'In', side: 'Top', offset: 0.5 },
    ],
    defaultDockerImage: 'minio/minio:latest',
    configFields: [
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
      { id: '', name: 'rtc', type: 'Network', direction: 'In', side: 'Top', offset: 0.3 },
      { id: '', name: 'api', type: 'Network', direction: 'In', side: 'Top', offset: 0.7 },
      { id: '', name: 'redis_connection', type: 'Database', direction: 'Out', side: 'Bottom', offset: 0.5 },
    ],
    defaultDockerImage: 'livekit/livekit-server:latest',
    configFields: [
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
      { id: '', name: 'port', type: 'Generic', direction: 'InOut', side: 'Top', offset: 0.5 },
    ],
    configFields: [
      { key: 'replicas', label: 'Replicas', placeholder: '1' },
      { key: 'upstreamPath', label: 'Upstream Path', placeholder: '/service/*', parentKinds: ['Caddy'] },
    ],
    description: 'Custom Docker image',
  },
];

import { createSignal } from 'solid-js';
import type { ImageDefinition, ConfigField } from '../types/catalog';

const subdomainRegex = /^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$/;

function validateSubdomain(value: string): string | null {
  if (!value) return null; // empty is ok (optional)
  if (value.length > 63) return 'Max 63 characters';
  if (!subdomainRegex.test(value)) return 'Lowercase letters, numbers, and hyphens only';
  return null;
}

/** Convert a validateRegex/validateMessage from the API into a validate function. */
function buildValidator(field: { validateRegex?: string; validateMessage?: string }): ((value: string) => string | null) | undefined {
  if (!field.validateRegex) return undefined;
  const regex = new RegExp(field.validateRegex);
  const message = field.validateMessage ?? 'Invalid value';
  return (value: string) => {
    if (!value) return null;
    return regex.test(value) ? null : message;
  };
}

/** Map API catalog response to ImageDefinition[]. */
function mapApiCatalog(entries: any[]): ImageDefinition[] {
  return entries.map(entry => ({
    kind: entry.typeId,
    label: entry.label,
    color: entry.color,
    defaultWidth: entry.defaultWidth,
    defaultHeight: entry.defaultHeight,
    defaultPorts: (entry.defaultPorts ?? []).map((p: any) => ({
      id: '',
      name: p.name,
      type: p.type,
      direction: p.direction,
      side: p.side,
      offset: p.offset,
    })),
    defaultDockerImage: entry.defaultDockerImage,
    configFields: (entry.configFields ?? []).map((f: any) => ({
      key: f.key,
      label: f.label,
      type: f.type ?? 'text',
      placeholder: f.placeholder,
      options: f.options,
      optionsFrom: f.optionsFrom,
      parentKinds: f.parentKinds,
      validate: buildValidator(f),
      validateRegex: f.validateRegex,
      validateMessage: f.validateMessage,
    })),
    defaultScaling: entry.defaultScaling === 'PerTenant' ? 'PerTenant' : 'Shared',
    description: entry.description,
    wireRequirements: entry.wireRequirements,
    dockerBehavior: entry.dockerBehavior,
  }));
}

// Built-in fallback catalog (used before API is available)
const BUILTIN_CATALOG: ImageDefinition[] = [
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
    defaultDockerImage: '{registry}/hub:latest',
    configFields: [
      { key: 'scaling', label: 'Scaling', type: 'select', options: [
        { value: 'Shared', label: 'Shared (1 per host)' },
        { value: 'PerTenant', label: 'Per Tenant' },
      ], parentKinds: ['ComputePool'] },
      { key: 'replicas', label: 'Replicas', placeholder: '1' },
    ],
    description: 'xcord hub control plane',
    wireRequirements: [
      { portName: 'pg', targetTypeLabel: 'PostgreSQL', required: true },
      { portName: 'redis', targetTypeLabel: 'Redis', required: true },
    ],
    dockerBehavior: { requiresPrivateRegistry: true, versionVariableName: 'hub_version', dbNameWhenConsuming: 'xcord_hub' },
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
    defaultDockerImage: '{registry}/fed:latest',
    configFields: [
      { key: 'tier', label: 'Tier', type: 'select', optionsFrom: 'tierProfiles', parentKinds: ['ComputePool'] },
      { key: 'scaling', label: 'Scaling', type: 'select', options: [
        { value: 'Shared', label: 'Shared (1 per host)' },
        { value: 'PerTenant', label: 'Per Tenant' },
      ], parentKinds: ['ComputePool'] },
      { key: 'replicas', label: 'Replicas', placeholder: '1' },
    ],
    defaultScaling: 'PerTenant',
    description: 'xcord federation instance',
    wireRequirements: [
      { portName: 'pg', targetTypeLabel: 'PostgreSQL', required: true },
      { portName: 'redis', targetTypeLabel: 'Redis', required: true },
      { portName: 'minio', targetTypeLabel: 'MinIO', required: true },
    ],
    dockerBehavior: { requiresPrivateRegistry: true, versionVariableName: 'fed_version', dbNameWhenConsuming: 'xcord' },
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
    wireRequirements: [],
    dockerBehavior: { requiresPrivateRegistry: false },
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
    wireRequirements: [],
    dockerBehavior: { requiresPrivateRegistry: false },
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
    defaultDockerImage: 'minio/minio:RELEASE.2025-02-28T09-55-16Z',
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
    wireRequirements: [],
    dockerBehavior: { requiresPrivateRegistry: false },
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
    defaultDockerImage: 'livekit/livekit-server:v1.8.3',
    configFields: [
      { key: 'scaling', label: 'Scaling', type: 'select', options: [
        { value: 'Shared', label: 'Shared (1 per host)' },
        { value: 'PerTenant', label: 'Per Tenant' },
      ], parentKinds: ['ComputePool'] },
      { key: 'replicas', label: 'Replicas', placeholder: '1' },
    ],
    description: 'LiveKit WebRTC SFU',
    wireRequirements: [],
    dockerBehavior: { requiresPrivateRegistry: false },
  },
  {
    kind: 'Registry',
    label: 'Docker Registry',
    color: '#2ac3de',
    defaultWidth: 140,
    defaultHeight: 60,
    defaultPorts: [],
    defaultDockerImage: 'registry:2',
    configFields: [
      { key: 'domain', label: 'Domain', placeholder: 'docker.xcord.net' },
      { key: 'volumeSize', label: 'Volume (GB)', placeholder: '50' },
    ],
    description: 'Private Docker registry with auto-TLS via Caddy',
    wireRequirements: [],
    dockerBehavior: { requiresPrivateRegistry: false },
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
    wireRequirements: [],
    dockerBehavior: { requiresPrivateRegistry: false },
  },
];

const [catalog, setCatalog] = createSignal<ImageDefinition[]>(BUILTIN_CATALOG);

/** Reactive accessor for the image catalog. Call as imageDefinitions() to get the array. */
export const imageDefinitions = catalog;

/** Fetch image catalog from the API and update the reactive store. Falls back to built-in catalog on error. */
export async function loadImageCatalog(): Promise<void> {
  try {
    const res = await fetch('/api/v1/catalog/images');
    if (res.ok) {
      const data = await res.json();
      const mapped = mapApiCatalog(data);
      if (mapped.length > 0) {
        setCatalog(mapped);
      }
    }
  } catch {
    // Keep built-in fallback
  }
}

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
      { key: 'replicas', label: 'Replicas', placeholder: '1' },
      { key: 'minReplicas', label: 'Min Replicas', placeholder: '' },
      { key: 'maxReplicas', label: 'Max Replicas', placeholder: '' },
      { key: 'backupFrequency', label: 'Backup Frequency', placeholder: 'daily' },
      { key: 'backupRetention', label: 'Backup Retention', placeholder: '7' },
    ],
    description: 'Server or VM — deployment decides the architecture',
  },
  {
    kind: 'Network',
    label: 'Network',
    color: '#0db7ed',
    defaultWidth: 350,
    defaultHeight: 200,
    defaultPorts: [
      { id: '', name: 'gateway', type: 'Network', direction: 'InOut', side: 'Left', offset: 0.5 },
    ],
    configFields: [],
    description: 'Logical network grouping',
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
    kind: 'FederationGroup',
    label: 'Federation Group',
    color: '#9ece6a',
    defaultWidth: 500,
    defaultHeight: 350,
    defaultPorts: [
      { id: '', name: 'federation', type: 'Network', direction: 'InOut', side: 'Right', offset: 0.5 },
      { id: '', name: 'control', type: 'Control', direction: 'In', side: 'Left', offset: 0.5 },
    ],
    configFields: [
      { key: 'domain', label: 'Domain', placeholder: 'chat.example.com' },
      { key: 'instanceCount', label: 'Instance Count', placeholder: '3' },
    ],
    description: 'Group of federated xcord instances',
  },
];

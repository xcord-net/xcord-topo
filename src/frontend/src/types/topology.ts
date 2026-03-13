export type PortType = 'Network' | 'Database' | 'Storage' | 'Control' | 'Generic';
export type PortDirection = 'In' | 'Out' | 'InOut';
export type PortSide = 'Top' | 'Right' | 'Bottom' | 'Left';

export type ContainerKind =
  | 'Host'
  | 'Caddy'
  | 'ComputePool'
  | 'Dns'
  | 'DataPool';

export type ImageKind =
  | 'HubServer'
  | 'FederationServer'
  | 'Redis'
  | 'PostgreSQL'
  | 'MinIO'
  | 'LiveKit'
  | 'Registry'
  | 'Custom';

export type ImageScaling = 'Shared' | 'PerTenant';

export interface Port {
  id: string;
  name: string;
  type: PortType;
  direction: PortDirection;
  side: PortSide;
  offset: number;
}

export interface Image {
  id: string;
  name: string;
  kind: ImageKind;
  x: number;
  y: number;
  width: number;
  height: number;
  ports: Port[];
  dockerImage?: string;
  config: Record<string, string>;
  scaling: ImageScaling;
}

export interface Container {
  id: string;
  name: string;
  kind: ContainerKind;
  x: number;
  y: number;
  width: number;
  height: number;
  ports: Port[];
  images: Image[];
  children: Container[];
  config: Record<string, string>;
}

export interface Wire {
  id: string;
  fromNodeId: string;
  fromPortId: string;
  toNodeId: string;
  toPortId: string;
}

export type BackupTargetKind = 'LinodeObjectStorage' | 'AwsS3' | 'S3Compatible';

export interface BackupTarget {
  label: string;
  kind: BackupTargetKind;
  region: string;
  bucketName: string;
  endpoint?: string;
  glacierTransitionDays?: number;
}

export interface ImageResourceSpec {
  memoryMb: number;
  cpuMillicores: number;
  diskMb: number;
}

export interface TierProfile {
  id: string;
  name: string;
  imageSpecs: Record<string, ImageResourceSpec>;
}

export type DeployStatus = 'Succeeded' | 'Failed';

export interface Topology {
  id: string;
  name: string;
  description?: string;
  provider: string;
  providerConfig: Record<string, string>;
  serviceKeys: Record<string, string>;
  containers: Container[];
  wires: Wire[];
  tierProfiles: TierProfile[];
  registry: string;
  schemaVersion: number;
  createdAt: string;
  updatedAt: string;
  lastDeployStatus?: DeployStatus;
  lastDeployedAt?: string;
  deployedResourceCount: number;
  backupTarget?: BackupTarget;
}

export interface TopologySummary {
  id: string;
  name: string;
  description?: string;
  provider: string;
  containerCount: number;
  wireCount: number;
  createdAt: string;
  updatedAt: string;
}

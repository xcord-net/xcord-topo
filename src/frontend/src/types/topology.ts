export type PortType = 'Network' | 'Database' | 'Storage' | 'Control' | 'Generic';
export type PortDirection = 'In' | 'Out' | 'InOut';
export type PortSide = 'Top' | 'Right' | 'Bottom' | 'Left';

export type ContainerKind =
  | 'Host'
  | 'Network'
  | 'Caddy'
  | 'FederationGroup';

export type ImageKind =
  | 'HubServer'
  | 'FederationServer'
  | 'Redis'
  | 'PostgreSQL'
  | 'MinIO'
  | 'LiveKit'
  | 'Custom';

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

export interface Topology {
  id: string;
  name: string;
  description?: string;
  provider: string;
  providerConfig: Record<string, string>;
  containers: Container[];
  wires: Wire[];
  schemaVersion: number;
  createdAt: string;
  updatedAt: string;
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

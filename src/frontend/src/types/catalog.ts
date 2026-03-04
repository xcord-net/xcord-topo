import type { ContainerKind, ImageKind, ImageScaling, Port, PortType } from './topology';

export interface ConfigField {
  key: string;
  label: string;
  type?: 'text' | 'select';
  placeholder?: string;
  tooltip?: string;
  options?: { value: string; label: string }[];
  parentKinds?: ContainerKind[];
}

export interface ContainerDefinition {
  kind: ContainerKind;
  label: string;
  color: string;
  defaultWidth: number;
  defaultHeight: number;
  defaultPorts: Port[];
  configFields: ConfigField[];
  description: string;
}

export interface ImageDefinition {
  kind: ImageKind;
  label: string;
  color: string;
  defaultWidth: number;
  defaultHeight: number;
  defaultPorts: Port[];
  defaultDockerImage?: string;
  configFields?: ConfigField[];
  defaultScaling?: ImageScaling;
  description: string;
}

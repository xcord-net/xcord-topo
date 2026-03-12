import type { ContainerKind, ImageKind, ImageScaling, Port, PortType } from './topology';

export interface ConfigField {
  key: string;
  label: string;
  type?: 'text' | 'select';
  placeholder?: string;
  tooltip?: string;
  options?: { value: string; label: string }[];
  /** Build options dynamically from topology data instead of static `options`. */
  optionsFrom?: 'tierProfiles';
  parentKinds?: ContainerKind[];
  /** Returns an error message if the value is invalid, or null/undefined if valid. */
  validate?: (value: string) => string | null | undefined;
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

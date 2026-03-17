import type { ContainerKind, ImageScaling, Port, PortType } from './topology';

export interface WireRequirement {
  portName: string;
  targetTypeLabel: string;
  required?: boolean;
}

export interface ImageDockerBehavior {
  requiresPrivateRegistry: boolean;
  versionVariableName?: string | null;
  dbNameWhenConsuming?: string | null;
  registryName?: string | null;
  gitRepoUrl?: string | null;
}

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
  validateRegex?: string;
  validateMessage?: string;
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
  kind: string;
  label: string;
  color: string;
  defaultWidth: number;
  defaultHeight: number;
  defaultPorts: Port[];
  defaultDockerImage?: string;
  configFields?: ConfigField[];
  defaultScaling?: ImageScaling;
  description: string;
  wireRequirements?: WireRequirement[];
  dockerBehavior?: ImageDockerBehavior;
}

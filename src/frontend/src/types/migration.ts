import type { ImageKind } from './topology';

export type ImageMatchKind = 'Unchanged' | 'Modified' | 'Relocated' | 'Split' | 'Added' | 'Removed';
export type ContainerMatchKind = 'Matched' | 'Added' | 'Removed' | 'SplitHost';
export type MigrationPhaseType = 'PreCheck' | 'Infrastructure' | 'DataMigration' | 'Provisioning' | 'Cutover' | 'Cleanup';
export type MigrationStepType =
  | 'Validate' | 'TerraformApply' | 'TerraformDestroy'
  | 'DatabaseDump' | 'DatabaseRestore'
  | 'RedisSnapshot' | 'RedisRestore'
  | 'ObjectStorageMirror'
  | 'DockerInstall' | 'DockerRun'
  | 'DnsUpdate' | 'CaddyReload' | 'HealthCheck'
  | 'SecretGenerate' | 'SecretImport'
  | 'Manual';
export type DecisionKind =
  | 'HubDatabaseMigration' | 'HubRedisMigration'
  | 'SecretHandling' | 'DnsCutover'
  | 'DowntimeTolerance' | 'VariableValue';

export interface DecisionOption {
  key: string;
  label: string;
  description: string;
}

export interface MigrationDecision {
  id: string;
  kind: DecisionKind;
  title: string;
  description: string;
  required: boolean;
  options: DecisionOption[];
  selectedOptionKey?: string;
  customValue?: string;
}

export interface ImageMatch {
  sourceImageId?: string;
  sourceImageName?: string;
  sourceImageKind?: ImageKind;
  sourceHostId?: string;
  sourceHostName?: string;
  targetImageId?: string;
  targetImageName?: string;
  targetImageKind?: ImageKind;
  targetHostId?: string;
  targetHostName?: string;
  kind: ImageMatchKind;
  splitConsumerId?: string;
  targetIsFederation: boolean;
}

export interface ContainerMatch {
  sourceContainerId?: string;
  sourceContainerName?: string;
  targetContainerId?: string;
  targetContainerName?: string;
  kind: ContainerMatchKind;
  matchedImageIds: string[];
}

export interface MigrationStep {
  order: number;
  type: MigrationStepType;
  description: string;
  script?: string;
  causesDowntime: boolean;
  estimatedDuration?: string;
}

export interface MigrationPhase {
  type: MigrationPhaseType;
  name: string;
  description: string;
  steps: MigrationStep[];
}

export interface MigrationDiffResult {
  summary: string;
  hostsAdded: number;
  hostsRemoved: number;
  imagesRelocated: number;
  imagesAdded: number;
  imagesRemoved: number;
  splitsDetected: number;
  imageMatches: ImageMatch[];
  containerMatches: ContainerMatch[];
  decisions: MigrationDecision[];
}

export interface MigrationPlan {
  id: string;
  sourceTopologyId: string;
  sourceTopologyName: string;
  targetTopologyId: string;
  targetTopologyName: string;
  diff: MigrationDiffResult;
  decisions: MigrationDecision[];
  phases: MigrationPhase[];
  createdAt: string;
}

export interface MigrationHclResult {
  targetHclFiles: Record<string, string>;
  migrationScripts: Record<string, string>;
}

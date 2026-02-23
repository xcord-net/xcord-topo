export type DeployStep = 'provider' | 'configure' | 'review' | 'migrate' | 'execute';
export type DeployMode = 'fresh' | 'update' | 'migrate' | 'destroy';

export interface CredentialStatus {
  hasCredentials: boolean;
  setVariables: string[];
  nonSensitiveValues: Record<string, string>;
}

export interface DeployedTopology {
  topologyId: string;
  topologyName: string;
  hasState: boolean;
  resourceCount: number;
}

export interface HostCostEntry {
  hostName: string;
  planId: string;
  planLabel: string;
  ramMb: number;
  count: number;
  pricePerMonth: number;
}

export interface CostEstimate {
  hosts: HostCostEntry[];
  totalMonthly: number;
}

export interface TerraformOutputLine {
  text: string;
  isError: boolean;
}

export interface CredentialFieldHelp {
  summary: string;
  steps: string[];
  url?: string;
  permissions?: string;
}

export interface CredentialField {
  key: string;
  label: string;
  type: 'password' | 'text' | 'select' | 'textarea';
  sensitive: boolean;
  required: boolean;
  placeholder?: string;
  help?: CredentialFieldHelp;
}

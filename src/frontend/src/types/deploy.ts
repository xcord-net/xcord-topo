export type DeployStep = 'provider' | 'configure' | 'validate' | 'hosting' | 'review' | 'migrate' | 'execute';
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
  tierProfileId?: string;
  tenantsPerHost?: number;
  targetTenants?: number;
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

export interface ValidationRule {
  type: 'minLength' | 'maxLength' | 'pattern';
  value?: string;
  message: string;
}

export interface CredentialField {
  key: string;
  label: string;
  type: 'password' | 'text' | 'select' | 'textarea';
  sensitive: boolean;
  required: boolean;
  placeholder?: string;
  help?: CredentialFieldHelp;
  validation: ValidationRule[];
}

// --- Hosting options ---

export interface PoolHostingOption {
  planId: string;
  planLabel: string;
  memoryMb: number;
  vCpus: number;
  priceMonthly: number;
  tenantsPerHost: number;
  costPerTenant: number;
}

export interface PoolHostingEntry {
  poolName: string;
  tierProfileId: string;
  tierProfileName: string;
  options: PoolHostingOption[];
}

export interface HostingOptions {
  pools: PoolHostingEntry[];
}

export interface PoolSelection {
  poolName: string;
  planId: string;
  targetTenants: number;
}

// --- Topology validation ---

export type ValidationSeverity = 'Error' | 'Warning';

export interface ValidationItem {
  severity: ValidationSeverity;
  message: string;
  nodeId?: string;
  field?: string;
}

export interface TopologyValidationResult {
  isValid: boolean;
  errors: string[];
  canDeploy: boolean;
  items: ValidationItem[];
}

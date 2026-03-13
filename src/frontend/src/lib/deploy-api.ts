import type { CredentialStatus, CredentialField, DeployedTopology, TerraformOutputLine, HostingOptions, TopologyValidationResult } from '../types/deploy';

const API_BASE = '/api/v1';

// --- Credentials ---

export async function getCredentialStatus(topologyId: string, providerKey: string): Promise<CredentialStatus> {
  const res = await fetch(`${API_BASE}/topologies/${topologyId}/credentials/${providerKey}`);
  if (!res.ok) throw new Error(`Failed to get credential status: ${res.statusText}`);
  return res.json();
}

export async function saveCredentials(topologyId: string, providerKey: string, variables: Record<string, string>): Promise<CredentialStatus> {
  const res = await fetch(`${API_BASE}/topologies/${topologyId}/credentials/${providerKey}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ variables }),
  });
  if (!res.ok) throw new Error(`Failed to save credentials: ${res.statusText}`);
  const data = await res.json();
  return data.updatedStatus;
}

// --- Credential schema ---

export async function getCredentialSchema(providerKey: string): Promise<CredentialField[]> {
  const res = await fetch(`${API_BASE}/providers/${providerKey}/credential-schema`);
  if (!res.ok) throw new Error(`Failed to get credential schema: ${res.statusText}`);
  const data = await res.json();
  return data.fields;
}

// --- Service keys ---

export async function getServiceKeySchema(topologyId?: string): Promise<CredentialField[]> {
  const params = topologyId ? `?topologyId=${topologyId}` : '';
  const res = await fetch(`${API_BASE}/service-keys/schema${params}`);
  if (!res.ok) throw new Error(`Failed to get service key schema: ${res.statusText}`);
  const data = await res.json();
  return data.fields;
}

export async function getServiceKeyStatus(topologyId: string): Promise<CredentialStatus> {
  const res = await fetch(`${API_BASE}/topologies/${topologyId}/service-keys`);
  if (!res.ok) throw new Error(`Failed to get service key status: ${res.statusText}`);
  return res.json();
}

export async function saveServiceKeys(topologyId: string, variables: Record<string, string>): Promise<CredentialStatus> {
  const res = await fetch(`${API_BASE}/topologies/${topologyId}/service-keys`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ variables }),
  });
  if (!res.ok) throw new Error(`Failed to save service keys: ${res.statusText}`);
  const data = await res.json();
  return data.updatedStatus;
}

// --- SSH keypair ---

export async function generateSshKeypair(): Promise<{ publicKey: string; privateKey: string }> {
  const res = await fetch(`${API_BASE}/ssh/generate-keypair`, { method: 'POST' });
  if (!res.ok) throw new Error(`Failed to generate SSH keypair: ${res.statusText}`);
  return res.json();
}

// --- Topology validation ---

export async function validateTopology(topologyId: string): Promise<TopologyValidationResult> {
  const res = await fetch(`${API_BASE}/topologies/${topologyId}/validate`, { method: 'POST' });
  if (!res.ok) {
    const err = await res.json().catch(() => null);
    throw new Error(err?.detail ?? `Validation failed: ${res.statusText}`);
  }
  return res.json();
}

// --- Deploy status ---

export async function getActiveDeployments(): Promise<DeployedTopology[]> {
  const res = await fetch(`${API_BASE}/deploy/active`);
  if (!res.ok) throw new Error(`Failed to get active deployments: ${res.statusText}`);
  return res.json();
}

// --- Hosting options ---

export async function getHostingOptions(topologyId: string): Promise<HostingOptions> {
  const res = await fetch(`${API_BASE}/topologies/${topologyId}/hosting-options`);
  if (!res.ok) throw new Error(`Failed to get hosting options: ${res.statusText}`);
  return res.json();
}

// --- Image tags ---

export interface ImageTagInfo {
  name: string;
  sha: string;
}

export async function getImageTags(repoName: string): Promise<ImageTagInfo[]> {
  const res = await fetch(`${API_BASE}/images/${repoName}/tags`);
  if (!res.ok) throw new Error(`Failed to fetch image tags: ${res.statusText}`);
  const data = await res.json();
  return data.tags;
}

// --- Image push ---

export interface ImageVersionSpec {
  kind: string;
  version: string;
}

export async function executeImagePush(topologyId: string, images: ImageVersionSpec[]): Promise<void> {
  const res = await fetch(`${API_BASE}/topologies/${topologyId}/images/push`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ images }),
  });
  if (!res.ok) {
    const err = await res.json().catch(() => null);
    throw new Error(err?.detail ?? `Failed to start image push: ${res.statusText}`);
  }
}

export function connectImagePushStream(
  topologyId: string,
  onLine: (line: TerraformOutputLine) => void,
  onDone: () => void,
): { close: () => void } {
  const eventSource = new EventSource(`${API_BASE}/topologies/${topologyId}/images/stream`);

  eventSource.onmessage = (event) => {
    if (event.data === '[DONE]') {
      eventSource.close();
      onDone();
      return;
    }
    try {
      const line: TerraformOutputLine = JSON.parse(event.data);
      onLine(line);
    } catch {
      onLine({ text: event.data, isError: false });
    }
  };

  eventSource.onerror = () => {
    eventSource.close();
    onDone();
  };

  return { close: () => eventSource.close() };
}

// --- SSE stream ---

export function connectTerraformStream(
  topologyId: string,
  onLine: (line: TerraformOutputLine) => void,
  onDone: () => void,
): { close: () => void } {
  const eventSource = new EventSource(`${API_BASE}/topologies/${topologyId}/terraform/stream`);

  eventSource.onmessage = (event) => {
    if (event.data === '[DONE]') {
      eventSource.close();
      onDone();
      return;
    }
    try {
      const line: TerraformOutputLine = JSON.parse(event.data);
      onLine(line);
    } catch {
      onLine({ text: event.data, isError: false });
    }
  };

  eventSource.onerror = () => {
    eventSource.close();
    onDone();
  };

  return { close: () => eventSource.close() };
}

// --- Re-exports from existing APIs ---

export { generateHcl, executeTerraform, cancelTerraform, fetchTopologies } from './serialization';
export { diffTopologies, createMigrationPlan } from './migration-api';

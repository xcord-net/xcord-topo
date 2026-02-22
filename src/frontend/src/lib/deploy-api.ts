import type { CredentialStatus, CostEstimate, DeployedTopology, TerraformOutputLine } from '../types/deploy';

const API_BASE = '/api/v1';

// --- Credentials ---

export async function getCredentialStatus(providerKey: string): Promise<CredentialStatus> {
  const res = await fetch(`${API_BASE}/providers/${providerKey}/credentials`);
  if (!res.ok) throw new Error(`Failed to get credential status: ${res.statusText}`);
  return res.json();
}

export async function saveCredentials(providerKey: string, variables: Record<string, string>): Promise<void> {
  const res = await fetch(`${API_BASE}/providers/${providerKey}/credentials`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ variables }),
  });
  if (!res.ok) throw new Error(`Failed to save credentials: ${res.statusText}`);
}

// --- Deploy status ---

export async function getActiveDeployments(): Promise<DeployedTopology[]> {
  const res = await fetch(`${API_BASE}/deploy/active`);
  if (!res.ok) throw new Error(`Failed to get active deployments: ${res.statusText}`);
  return res.json();
}

// --- Cost estimate ---

export async function estimateCost(topologyId: string): Promise<CostEstimate> {
  const res = await fetch(`${API_BASE}/topologies/${topologyId}/terraform/estimate`, { method: 'POST' });
  if (!res.ok) throw new Error(`Failed to estimate cost: ${res.statusText}`);
  return res.json();
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
export { diffTopologies, createMigrationPlan, generateMigrationHcl } from './migration-api';

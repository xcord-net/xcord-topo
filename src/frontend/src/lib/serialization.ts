import type { Topology } from '../types/topology';

const API_BASE = '/api/v1';

export async function fetchTopologies(): Promise<{ topologies: Array<{ id: string; name: string; description?: string; provider: string; containerCount: number; wireCount: number; createdAt: string; updatedAt: string }> }> {
  const res = await fetch(`${API_BASE}/topologies`);
  if (!res.ok) throw new Error(`Failed to fetch topologies: ${res.statusText}`);
  return res.json();
}

export async function fetchTopology(id: string): Promise<Topology> {
  const res = await fetch(`${API_BASE}/topologies/${id}`);
  if (!res.ok) throw new Error(`Failed to fetch topology: ${res.statusText}`);
  return res.json();
}

export async function createTopology(name: string, description?: string): Promise<Topology> {
  const res = await fetch(`${API_BASE}/topologies`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ name, description }),
  });
  if (!res.ok) throw new Error(`Failed to create topology: ${res.statusText}`);
  return res.json();
}

export async function saveTopology(topology: Topology): Promise<Topology> {
  const res = await fetch(`${API_BASE}/topologies/${topology.id}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(topology),
  });
  if (!res.ok) throw new Error(`Failed to save topology: ${res.statusText}`);
  return res.json();
}

export async function deleteTopology(id: string): Promise<void> {
  const res = await fetch(`${API_BASE}/topologies/${id}`, { method: 'DELETE' });
  if (!res.ok) throw new Error(`Failed to delete topology: ${res.statusText}`);
}

export async function generateHcl(
  topologyId: string,
  poolSelections?: import('../types/deploy').PoolSelection[],
  infraSelections?: import('../types/deploy').InfraSelection[],
): Promise<{ files: Record<string, string>; summary: import('../types/deploy').ResourceSummary }> {
  const hasPoolBody = poolSelections && poolSelections.length > 0;
  const hasInfraBody = infraSelections && infraSelections.length > 0;
  const hasBody = hasPoolBody || hasInfraBody;
  const res = await fetch(`${API_BASE}/topologies/${topologyId}/terraform/generate`, {
    method: 'POST',
    ...(hasBody ? {
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        ...(hasPoolBody ? { poolSelections } : {}),
        ...(hasInfraBody ? { infraSelections } : {}),
      }),
    } : {}),
  });
  if (!res.ok) throw new Error(`Failed to generate HCL: ${res.statusText}`);
  return res.json();
}

export async function executeTerraform(topologyId: string, command: string, deployApps?: boolean): Promise<void> {
  const params = deployApps ? '?deployApps=true' : '';
  const res = await fetch(`${API_BASE}/topologies/${topologyId}/terraform/${command}${params}`, { method: 'POST' });
  if (!res.ok) throw new Error(`Failed to execute terraform ${command}: ${res.statusText}`);
}

export async function cancelTerraform(topologyId: string): Promise<void> {
  const res = await fetch(`${API_BASE}/topologies/${topologyId}/terraform/cancel`, { method: 'POST' });
  if (!res.ok) throw new Error(`Failed to cancel terraform: ${res.statusText}`);
}

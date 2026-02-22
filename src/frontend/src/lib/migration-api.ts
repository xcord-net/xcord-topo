import type { MigrationDecision, MigrationDiffResult, MigrationHclResult, MigrationPlan } from '../types/migration';

const API_BASE = '/api/v1';

export async function diffTopologies(sourceTopologyId: string, targetTopologyId: string): Promise<MigrationDiffResult> {
  const res = await fetch(`${API_BASE}/migrations/diff`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ sourceTopologyId, targetTopologyId }),
  });
  if (!res.ok) throw new Error(`Failed to compute diff: ${res.statusText}`);
  return res.json();
}

export async function createMigrationPlan(
  sourceTopologyId: string,
  targetTopologyId: string,
  decisions: MigrationDecision[],
): Promise<MigrationPlan> {
  const res = await fetch(`${API_BASE}/migrations/plan`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ sourceTopologyId, targetTopologyId, decisions }),
  });
  if (!res.ok) throw new Error(`Failed to create migration plan: ${res.statusText}`);
  return res.json();
}

export async function getMigrationPlan(id: string): Promise<MigrationPlan> {
  const res = await fetch(`${API_BASE}/migrations/${id}`);
  if (!res.ok) throw new Error(`Failed to fetch migration plan: ${res.statusText}`);
  return res.json();
}

export async function generateMigrationHcl(id: string): Promise<MigrationHclResult> {
  const res = await fetch(`${API_BASE}/migrations/${id}/hcl`, { method: 'POST' });
  if (!res.ok) throw new Error(`Failed to generate migration HCL: ${res.statusText}`);
  return res.json();
}

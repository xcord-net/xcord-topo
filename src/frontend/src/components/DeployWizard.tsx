import { Component, createSignal, createEffect, For, Show, onCleanup } from 'solid-js';
import { useTopology } from '../stores/topology.store';
import * as deployApi from '../lib/deploy-api';
import type { DeployStep, DeployMode, CredentialStatus, DeployedTopology, CostEstimate, TerraformOutputLine } from '../types/deploy';
import type { MigrationDiffResult, MigrationDecision, MigrationPlan } from '../types/migration';

interface ProviderInfo {
  key: string;
  name: string;
  description: string;
  supportedContainerKinds: string[];
}

interface Region {
  id: string;
  label: string;
  country: string;
}

const STEPS: { key: DeployStep; label: string }[] = [
  { key: 'provider', label: 'Provider' },
  { key: 'configure', label: 'Configure' },
  { key: 'review', label: 'Review' },
  { key: 'migrate', label: 'Migrate' },
  { key: 'execute', label: 'Execute' },
];

const matchKindBadge: Record<string, { label: string; class: string }> = {
  Unchanged: { label: 'Unchanged', class: 'bg-topo-text-muted/20 text-topo-text-muted' },
  Modified: { label: 'Modified', class: 'bg-topo-warning/20 text-topo-warning' },
  Relocated: { label: 'Relocated', class: 'bg-topo-brand/20 text-topo-brand' },
  Split: { label: 'Split', class: 'bg-purple-500/20 text-purple-400' },
  Added: { label: 'Added', class: 'bg-topo-success/20 text-topo-success' },
  Removed: { label: 'Removed', class: 'bg-topo-error/20 text-topo-error' },
};

const DeployWizard: Component<{ onClose: () => void }> = (props) => {
  const topo = useTopology();

  // --- Wizard state ---
  const [step, setStep] = createSignal<DeployStep>('provider');
  const [provider, setProvider] = createSignal('');
  const [providers, setProviders] = createSignal<ProviderInfo[]>([]);
  const [regions, setRegions] = createSignal<Region[]>([]);
  const [credentialStatus, setCredentialStatus] = createSignal<CredentialStatus | null>(null);
  const [credentialValues, setCredentialValues] = createSignal<Record<string, string>>({});
  const [costEstimate, setCostEstimate] = createSignal<CostEstimate | null>(null);
  const [hclFiles, setHclFiles] = createSignal<Record<string, string>>({});
  const [activeDeployments, setActiveDeployments] = createSignal<DeployedTopology[]>([]);
  const [deployMode, setDeployMode] = createSignal<DeployMode>('fresh');
  const [loading, setLoading] = createSignal(false);
  const [error, setError] = createSignal('');

  // Migration state
  const [migrationSourceId, setMigrationSourceId] = createSignal('');
  const [migrationDiff, setMigrationDiff] = createSignal<MigrationDiffResult | null>(null);
  const [migrationDecisions, setMigrationDecisions] = createSignal<MigrationDecision[]>([]);
  const [migrationPlan, setMigrationPlan] = createSignal<MigrationPlan | null>(null);

  // Execute state
  const [outputLines, setOutputLines] = createSignal<TerraformOutputLine[]>([]);
  const [executing, setExecuting] = createSignal(false);
  const [executePhase, setExecutePhase] = createSignal('');
  const [executeResult, setExecuteResult] = createSignal<'success' | 'failure' | null>(null);
  let outputRef: HTMLDivElement | undefined;
  let streamHandle: { close: () => void } | null = null;

  onCleanup(() => { streamHandle?.close(); });

  // Auto-scroll output
  createEffect(() => {
    outputLines();
    if (outputRef) outputRef.scrollTop = outputRef.scrollHeight;
  });

  // --- Step 1: Provider ---
  const loadProviders = async () => {
    setLoading(true);
    setError('');
    try {
      const res = await fetch('/api/v1/providers');
      if (!res.ok) throw new Error('Failed to fetch providers');
      const data = await res.json();
      setProviders(data.providers);

      // Pre-select from topology
      const topoProv = topo.topology.provider;
      if (topoProv) setProvider(topoProv);

      // Auto-advance if only one provider
      if (data.providers.length === 1) {
        setProvider(data.providers[0].key);
        await onProviderSelected(data.providers[0].key);
      }
    } catch (e: any) {
      setError(e.message);
    } finally {
      setLoading(false);
    }
  };

  const onProviderSelected = async (key: string) => {
    setProvider(key);
    setLoading(true);
    setError('');
    try {
      const [regRes, credRes] = await Promise.all([
        fetch(`/api/v1/providers/${key}/regions`).then(r => { if (!r.ok) throw new Error('Failed'); return r.json(); }),
        deployApi.getCredentialStatus(key),
      ]);
      setRegions(regRes.regions);
      setCredentialStatus(credRes);

      // Pre-fill credential values from saved non-sensitive values
      const prefill: Record<string, string> = {};
      if (credRes.nonSensitiveValues) {
        for (const [k, v] of Object.entries(credRes.nonSensitiveValues)) {
          prefill[k] = v;
        }
      }
      // Keep token field empty if already saved — we don't expose it
      setCredentialValues(prefill);
      setStep('configure');
    } catch (e: any) {
      setError(e.message);
    } finally {
      setLoading(false);
    }
  };

  // --- Step 2: Configure ---
  const setCredVal = (key: string, value: string) => {
    setCredentialValues(prev => ({ ...prev, [key]: value }));
  };

  const handleSaveAndNext = async () => {
    setLoading(true);
    setError('');
    try {
      // Only send non-empty values
      const vars: Record<string, string> = {};
      for (const [k, v] of Object.entries(credentialValues())) {
        if (v) vars[k] = v;
      }
      if (Object.keys(vars).length > 0) {
        await deployApi.saveCredentials(provider(), vars);
      }

      // Generate HCL
      const hclResult = await deployApi.generateHcl(topo.topology.id);
      setHclFiles(hclResult.files);

      // Fetch cost estimate and active deployments in parallel
      const [cost, deployments] = await Promise.all([
        deployApi.estimateCost(topo.topology.id),
        deployApi.getActiveDeployments(),
      ]);
      setCostEstimate(cost);
      setActiveDeployments(deployments);

      // Determine deploy mode
      const currentDeploy = deployments.find(d => d.topologyId === topo.topology.id);
      const otherDeploy = deployments.find(d => d.topologyId !== topo.topology.id);
      if (currentDeploy) {
        setDeployMode('update');
      } else if (otherDeploy) {
        setDeployMode('migrate');
        setMigrationSourceId(otherDeploy.topologyId);
      } else {
        setDeployMode('fresh');
      }

      setStep('review');
    } catch (e: any) {
      setError(e.message);
    } finally {
      setLoading(false);
    }
  };

  // --- Step 3: Review ---
  const handleStartMigration = async () => {
    const sourceId = migrationSourceId();
    if (!sourceId) return;
    setLoading(true);
    setError('');
    try {
      const diff = await deployApi.diffTopologies(sourceId, topo.topology.id);
      setMigrationDiff(diff);
      setMigrationDecisions(diff.decisions.map(d => ({ ...d })));
      setStep('migrate');
    } catch (e: any) {
      setError(e.message);
    } finally {
      setLoading(false);
    }
  };

  const handleDeployFromReview = () => {
    setOutputLines([]);
    setExecuteResult(null);
    setStep('execute');
  };

  const handleDestroyFromReview = () => {
    setDeployMode('destroy');
    setOutputLines([]);
    setExecuteResult(null);
    setStep('execute');
  };

  // --- Step 4: Migrate ---
  const updateDecision = (id: string, key: string) => {
    setMigrationDecisions(prev => prev.map(d => d.id === id ? { ...d, selectedOptionKey: key } : d));
  };

  const allRequiredAnswered = () =>
    migrationDecisions().filter(d => d.required).every(d => d.selectedOptionKey);

  const handleDeployWithMigration = async () => {
    const sourceId = migrationSourceId();
    if (!sourceId) return;
    setLoading(true);
    setError('');
    try {
      const plan = await deployApi.createMigrationPlan(sourceId, topo.topology.id, migrationDecisions());
      setMigrationPlan(plan);
      setOutputLines([]);
      setExecuteResult(null);
      setStep('execute');
    } catch (e: any) {
      setError(e.message);
    } finally {
      setLoading(false);
    }
  };

  // --- Step 5: Execute ---
  const runTerraformCommand = (command: string): Promise<boolean> => {
    return new Promise(async (resolve) => {
      try {
        await deployApi.executeTerraform(topo.topology.id, command);
        // Small delay to let the server set up the stream
        await new Promise(r => setTimeout(r, 200));

        streamHandle = deployApi.connectTerraformStream(
          topo.topology.id,
          (line) => {
            setOutputLines(prev => [...prev, line]);
          },
          () => {
            streamHandle = null;
            // Check if last line indicates failure
            const lines = outputLines();
            const lastLine = lines[lines.length - 1];
            const failed = lastLine?.isError && lastLine.text.includes('exited with code') && !lastLine.text.includes('code 0');
            resolve(!failed);
          },
        );
      } catch (e: any) {
        setOutputLines(prev => [...prev, { text: `Error: ${e.message}`, isError: true }]);
        resolve(false);
      }
    });
  };

  const handleExecute = async () => {
    setExecuting(true);
    setExecuteResult(null);
    setOutputLines([]);

    const mode = deployMode();

    if (mode === 'destroy') {
      setExecutePhase('destroy');
      setOutputLines(prev => [...prev, { text: '=== Terraform Destroy ===', isError: false }]);
      const ok = await runTerraformCommand('destroy');
      setExecuteResult(ok ? 'success' : 'failure');
      setExecuting(false);
      return;
    }

    // Init
    setExecutePhase('init');
    setOutputLines(prev => [...prev, { text: '=== Terraform Init ===', isError: false }]);
    let ok = await runTerraformCommand('init');
    if (!ok) { setExecuteResult('failure'); setExecuting(false); return; }

    // Plan
    setExecutePhase('plan');
    setOutputLines(prev => [...prev, { text: '\n=== Terraform Plan ===', isError: false }]);
    ok = await runTerraformCommand('plan');
    if (!ok) { setExecuteResult('failure'); setExecuting(false); return; }

    // Apply
    setExecutePhase('apply');
    setOutputLines(prev => [...prev, { text: '\n=== Terraform Apply ===', isError: false }]);
    ok = await runTerraformCommand('apply');
    setExecuteResult(ok ? 'success' : 'failure');
    setExecuting(false);
  };

  const handleCancel = async () => {
    streamHandle?.close();
    streamHandle = null;
    try {
      await deployApi.cancelTerraform(topo.topology.id);
      setOutputLines(prev => [...prev, { text: '\n--- Cancelled by user ---', isError: true }]);
    } catch { }
    setExecuting(false);
    setExecuteResult('failure');
  };

  // --- Init ---
  loadProviders();

  // --- Step navigation ---
  const stepIndex = () => STEPS.findIndex(s => s.key === step());

  const visibleSteps = () => {
    if (deployMode() === 'migrate') return STEPS;
    return STEPS.filter(s => s.key !== 'migrate');
  };

  const canGoBack = () => {
    const idx = stepIndex();
    return idx > 0 && !executing();
  };

  const handleBack = () => {
    const currentIdx = stepIndex();
    if (currentIdx <= 0) return;
    const prev = STEPS[currentIdx - 1];
    // Skip migrate step going backwards if not in migrate mode
    if (prev.key === 'migrate' && deployMode() !== 'migrate') {
      setStep(STEPS[currentIdx - 2].key);
    } else {
      setStep(prev.key);
    }
  };

  const canClickStep = (targetStep: DeployStep) => {
    if (executing()) return false;
    const targetIdx = STEPS.findIndex(s => s.key === targetStep);
    const currentIdx = stepIndex();
    return targetIdx < currentIdx;
  };

  // --- Render ---
  return (
    <div class="absolute bottom-0 left-0 right-0 h-[28rem] bg-topo-bg-secondary border-t border-topo-border flex flex-col">
      {/* Header + Step indicator */}
      <div class="flex items-center justify-between px-4 py-2 border-b border-topo-border shrink-0">
        <div class="flex items-center gap-4">
          <span class="text-sm font-semibold text-topo-text-primary">Deploy</span>
          <div class="flex items-center gap-1">
            <For each={visibleSteps()}>
              {(s, i) => (
                <>
                  <Show when={i() > 0}>
                    <div class="w-4 h-px bg-topo-border mx-0.5" />
                  </Show>
                  <button
                    class={`px-2 py-0.5 text-xs rounded transition-colors ${
                      step() === s.key
                        ? 'bg-topo-brand text-white'
                        : canClickStep(s.key)
                          ? 'text-topo-text-secondary hover:text-topo-text-primary cursor-pointer'
                          : 'text-topo-text-muted cursor-default'
                    }`}
                    onClick={() => canClickStep(s.key) && setStep(s.key)}
                  >
                    {s.label}
                  </button>
                </>
              )}
            </For>
          </div>
        </div>
        <button
          class="px-2 py-1 text-xs rounded text-topo-text-muted hover:text-topo-text-primary"
          onClick={props.onClose}
        >
          Close
        </button>
      </div>

      {/* Error banner */}
      <Show when={error()}>
        <div class="px-4 py-2 text-xs text-topo-error bg-topo-error/10 border-b border-topo-error/20 shrink-0">
          {error()}
        </div>
      </Show>

      {/* Content */}
      <div class="flex-1 overflow-auto p-4">
        {/* Step 1: Provider */}
        <Show when={step() === 'provider'}>
          <div class="space-y-4">
            <p class="text-xs text-topo-text-muted">Select a cloud provider for deployment.</p>
            <Show when={loading()}>
              <p class="text-xs text-topo-text-muted">Loading providers...</p>
            </Show>
            <div class="grid grid-cols-3 gap-3">
              <For each={providers()}>
                {(p) => (
                  <button
                    class={`p-4 rounded-lg border text-left transition-colors ${
                      provider() === p.key
                        ? 'border-topo-brand bg-topo-brand/10'
                        : 'border-topo-border bg-topo-bg-primary hover:border-topo-text-muted'
                    }`}
                    onClick={() => onProviderSelected(p.key)}
                  >
                    <div class="text-sm font-medium text-topo-text-primary">{p.name}</div>
                    <div class="text-xs text-topo-text-muted mt-1">{p.description}</div>
                  </button>
                )}
              </For>
            </div>
          </div>
        </Show>

        {/* Step 2: Configure */}
        <Show when={step() === 'configure'}>
          <div class="flex gap-6">
            {/* Form */}
            <div class="flex-1 space-y-3">
              <p class="text-xs text-topo-text-muted">Enter credentials and configuration for {provider()}.</p>

              {/* API Token */}
              <div>
                <label class="block text-xs text-topo-text-muted mb-1">
                  API Token
                  <Show when={credentialStatus()?.setVariables.includes('linode_token')}>
                    <span class="ml-2 text-topo-success">(saved)</span>
                  </Show>
                </label>
                <input
                  type="password"
                  class="w-full bg-topo-bg-primary border border-topo-border rounded px-2 py-1.5 text-sm text-topo-text-primary focus:outline-none focus:border-topo-brand"
                  placeholder={credentialStatus()?.setVariables.includes('linode_token') ? '••••••••' : 'Enter API token'}
                  value={credentialValues()['linode_token'] ?? ''}
                  onInput={(e) => setCredVal('linode_token', e.currentTarget.value)}
                />
              </div>

              {/* Region */}
              <div>
                <label class="block text-xs text-topo-text-muted mb-1">Region</label>
                <select
                  class="w-full bg-topo-bg-primary border border-topo-border rounded px-2 py-1.5 text-sm text-topo-text-primary focus:outline-none focus:border-topo-brand"
                  value={credentialValues()['region'] ?? ''}
                  onChange={(e) => setCredVal('region', e.currentTarget.value)}
                >
                  <option value="">Select region...</option>
                  <For each={regions()}>
                    {(r) => <option value={r.id}>{r.label} ({r.id})</option>}
                  </For>
                </select>
              </div>

              {/* Domain */}
              <div>
                <label class="block text-xs text-topo-text-muted mb-1">Domain</label>
                <input
                  type="text"
                  class="w-full bg-topo-bg-primary border border-topo-border rounded px-2 py-1.5 text-sm text-topo-text-primary focus:outline-none focus:border-topo-brand"
                  placeholder="example.com"
                  value={credentialValues()['domain'] ?? ''}
                  onInput={(e) => setCredVal('domain', e.currentTarget.value)}
                />
              </div>

              {/* SSH Public Key */}
              <div>
                <label class="block text-xs text-topo-text-muted mb-1">SSH Public Key</label>
                <textarea
                  class="w-full bg-topo-bg-primary border border-topo-border rounded px-2 py-1.5 text-sm text-topo-text-primary focus:outline-none focus:border-topo-brand font-mono h-16 resize-none"
                  placeholder="ssh-rsa AAAA..."
                  value={credentialValues()['ssh_public_key'] ?? ''}
                  onInput={(e) => setCredVal('ssh_public_key', e.currentTarget.value)}
                />
              </div>
            </div>

            {/* Cost estimate sidebar */}
            <Show when={costEstimate()}>
              {(cost) => (
                <div class="w-56 shrink-0">
                  <div class="bg-topo-bg-primary border border-topo-border rounded-md p-3">
                    <h3 class="text-xs font-semibold text-topo-text-primary mb-2">Estimated Cost</h3>
                    <div class="space-y-1.5">
                      <For each={cost().hosts}>
                        {(h) => (
                          <div class="flex justify-between text-xs">
                            <span class="text-topo-text-secondary truncate mr-2">
                              {h.hostName}
                              <Show when={h.count > 1}>
                                <span class="text-topo-text-muted"> x{h.count}</span>
                              </Show>
                            </span>
                            <span class="text-topo-text-muted whitespace-nowrap">${h.pricePerMonth}/mo</span>
                          </div>
                        )}
                      </For>
                    </div>
                    <div class="border-t border-topo-border mt-2 pt-2 flex justify-between text-xs font-semibold">
                      <span class="text-topo-text-primary">Total</span>
                      <span class="text-topo-brand">${cost().totalMonthly}/mo</span>
                    </div>
                  </div>
                </div>
              )}
            </Show>
          </div>
        </Show>

        {/* Step 3: Review */}
        <Show when={step() === 'review'}>
          <div class="flex gap-6">
            <div class="flex-1 space-y-4">
              {/* Deploy mode banner */}
              <div class={`px-3 py-2 rounded text-xs ${
                deployMode() === 'fresh' ? 'bg-topo-success/10 text-topo-success' :
                deployMode() === 'update' ? 'bg-topo-brand/10 text-topo-brand' :
                'bg-topo-warning/10 text-topo-warning'
              }`}>
                <Show when={deployMode() === 'fresh'}>
                  Fresh deployment — no existing infrastructure detected.
                </Show>
                <Show when={deployMode() === 'update'}>
                  Update — this topology is already deployed ({activeDeployments().find(d => d.topologyId === topo.topology.id)?.resourceCount ?? 0} resources).
                </Show>
                <Show when={deployMode() === 'migrate'}>
                  Migration available — "{activeDeployments().find(d => d.topologyId !== topo.topology.id)?.topologyName}" is currently deployed.
                </Show>
              </div>

              {/* Resource summary */}
              <div>
                <h3 class="text-xs font-semibold text-topo-text-primary mb-2">Resources</h3>
                <div class="text-xs text-topo-text-secondary">
                  {Object.keys(hclFiles()).length} Terraform files generated
                </div>
              </div>

              {/* Cost breakdown */}
              <Show when={costEstimate()}>
                {(cost) => (
                  <div>
                    <h3 class="text-xs font-semibold text-topo-text-primary mb-2">Cost Breakdown</h3>
                    <table class="w-full text-left text-xs">
                      <thead>
                        <tr class="border-b border-topo-border text-topo-text-muted">
                          <th class="py-1 px-2 font-medium">Host</th>
                          <th class="py-1 px-2 font-medium">Plan</th>
                          <th class="py-1 px-2 font-medium">RAM</th>
                          <th class="py-1 px-2 font-medium">Count</th>
                          <th class="py-1 px-2 font-medium text-right">$/mo</th>
                        </tr>
                      </thead>
                      <tbody>
                        <For each={cost().hosts}>
                          {(h) => (
                            <tr class="border-b border-topo-border/50">
                              <td class="py-1 px-2 text-topo-text-secondary">{h.hostName}</td>
                              <td class="py-1 px-2 text-topo-text-muted">{h.planLabel}</td>
                              <td class="py-1 px-2 text-topo-text-muted">{h.ramMb}MB</td>
                              <td class="py-1 px-2 text-topo-text-muted">{h.count}</td>
                              <td class="py-1 px-2 text-topo-text-secondary text-right">${h.pricePerMonth}</td>
                            </tr>
                          )}
                        </For>
                        <tr>
                          <td colspan="4" class="py-1.5 px-2 text-topo-text-primary font-semibold">Total</td>
                          <td class="py-1.5 px-2 text-topo-brand font-semibold text-right">${cost().totalMonthly}/mo</td>
                        </tr>
                      </tbody>
                    </table>
                  </div>
                )}
              </Show>

              {/* HCL file preview */}
              <details class="group">
                <summary class="text-xs font-semibold text-topo-text-primary cursor-pointer hover:text-topo-brand">
                  HCL Files ({Object.keys(hclFiles()).length})
                </summary>
                <div class="mt-2 space-y-3 max-h-48 overflow-y-auto">
                  <For each={Object.entries(hclFiles())}>
                    {([name, content]) => (
                      <div>
                        <div class="text-topo-brand text-xs mb-1">{name}</div>
                        <pre class="bg-topo-bg-primary p-2 rounded overflow-x-auto text-[10px] text-topo-text-secondary font-mono">{content}</pre>
                      </div>
                    )}
                  </For>
                </div>
              </details>
            </div>
          </div>
        </Show>

        {/* Step 4: Migrate */}
        <Show when={step() === 'migrate'}>
          <div class="space-y-4">
            <p class="text-xs text-topo-text-muted">
              Migrating from "{activeDeployments().find(d => d.topologyId === migrationSourceId())?.topologyName}" to "{topo.topology.name}".
            </p>

            {/* Diff summary */}
            <Show when={migrationDiff()}>
              {(d) => (
                <div class="space-y-3">
                  <div class="flex gap-3 text-xs">
                    <span class="text-topo-text-muted">Summary:</span>
                    <span class="text-topo-text-secondary">{d().summary}</span>
                  </div>
                  <div class="flex gap-4 text-xs">
                    <Show when={d().hostsAdded > 0}><span class="text-topo-success">+{d().hostsAdded} hosts</span></Show>
                    <Show when={d().hostsRemoved > 0}><span class="text-topo-error">-{d().hostsRemoved} hosts</span></Show>
                    <Show when={d().imagesRelocated > 0}><span class="text-topo-brand">{d().imagesRelocated} relocated</span></Show>
                    <Show when={d().splitsDetected > 0}><span class="text-purple-400">{d().splitsDetected} splits</span></Show>
                    <Show when={d().imagesAdded > 0}><span class="text-topo-success">+{d().imagesAdded} images</span></Show>
                    <Show when={d().imagesRemoved > 0}><span class="text-topo-error">-{d().imagesRemoved} images</span></Show>
                  </div>

                  {/* Image matches table */}
                  <table class="w-full text-left">
                    <thead>
                      <tr class="border-b border-topo-border text-xs text-topo-text-muted">
                        <th class="py-1 px-2 font-medium">Source Image</th>
                        <th class="py-1 px-2 font-medium">Source Host</th>
                        <th class="py-1 px-2 font-medium">Target Image</th>
                        <th class="py-1 px-2 font-medium">Target Host</th>
                        <th class="py-1 px-2 font-medium">Status</th>
                      </tr>
                    </thead>
                    <tbody>
                      <For each={d().imageMatches}>
                        {(m) => {
                          const badge = matchKindBadge[m.kind] ?? matchKindBadge.Unchanged;
                          return (
                            <tr class="border-b border-topo-border/50">
                              <td class="py-1.5 px-2 text-xs text-topo-text-secondary">{m.sourceImageName ?? '-'}</td>
                              <td class="py-1.5 px-2 text-xs text-topo-text-muted">{m.sourceHostName ?? '-'}</td>
                              <td class="py-1.5 px-2 text-xs text-topo-text-secondary">{m.targetImageName ?? '-'}</td>
                              <td class="py-1.5 px-2 text-xs text-topo-text-muted">{m.targetHostName ?? '-'}</td>
                              <td class="py-1.5 px-2">
                                <span class={`px-1.5 py-0.5 rounded text-[10px] font-medium ${badge.class}`}>{badge.label}</span>
                                {m.targetIsFederation && (
                                  <span class="ml-1 px-1.5 py-0.5 rounded text-[10px] font-medium bg-topo-text-muted/10 text-topo-text-muted">fresh</span>
                                )}
                              </td>
                            </tr>
                          );
                        }}
                      </For>
                    </tbody>
                  </table>
                </div>
              )}
            </Show>

            {/* Decisions */}
            <Show when={migrationDecisions().length > 0}>
              <div class="space-y-3">
                <h3 class="text-xs font-semibold text-topo-text-primary">Migration Decisions</h3>
                <For each={migrationDecisions()}>
                  {(decision) => (
                    <div class="bg-topo-bg-primary border border-topo-border rounded-md p-3">
                      <div class="flex items-center gap-2 mb-1">
                        <span class="text-sm font-medium text-topo-text-primary">{decision.title}</span>
                        <span class={`px-1.5 py-0.5 rounded text-[10px] font-medium ${decision.required ? 'bg-topo-error/20 text-topo-error' : 'bg-topo-text-muted/20 text-topo-text-muted'}`}>
                          {decision.required ? 'Required' : 'Optional'}
                        </span>
                      </div>
                      <p class="text-xs text-topo-text-muted mb-2">{decision.description}</p>
                      <div class="space-y-1.5">
                        <For each={decision.options}>
                          {(option) => (
                            <label class="flex items-start gap-2 cursor-pointer group">
                              <input
                                type="radio"
                                name={decision.id}
                                checked={decision.selectedOptionKey === option.key}
                                onChange={() => updateDecision(decision.id, option.key)}
                                class="mt-0.5 accent-topo-brand"
                              />
                              <div>
                                <span class="text-xs font-medium text-topo-text-secondary group-hover:text-topo-text-primary">
                                  {option.label}
                                </span>
                                <p class="text-[10px] text-topo-text-muted">{option.description}</p>
                              </div>
                            </label>
                          )}
                        </For>
                      </div>
                    </div>
                  )}
                </For>
              </div>
            </Show>
          </div>
        </Show>

        {/* Step 5: Execute */}
        <Show when={step() === 'execute'}>
          <div class="flex flex-col h-full">
            {/* Status bar */}
            <div class="flex items-center justify-between mb-2 shrink-0">
              <div class="text-xs text-topo-text-muted">
                <Show when={executing()}>
                  Running terraform {executePhase()}...
                </Show>
                <Show when={!executing() && !executeResult()}>
                  Ready to {deployMode() === 'destroy' ? 'destroy' : 'deploy'}.
                </Show>
                <Show when={executeResult() === 'success'}>
                  <span class="text-topo-success font-medium">
                    {deployMode() === 'destroy' ? 'Destroy' : 'Deployment'} completed successfully.
                  </span>
                </Show>
                <Show when={executeResult() === 'failure'}>
                  <span class="text-topo-error font-medium">
                    {deployMode() === 'destroy' ? 'Destroy' : 'Deployment'} failed.
                  </span>
                </Show>
              </div>
              <div class="flex gap-2">
                <Show when={!executing() && !executeResult()}>
                  <button
                    class={`px-3 py-1 text-xs rounded font-medium ${
                      deployMode() === 'destroy'
                        ? 'bg-topo-error/20 text-topo-error hover:bg-topo-error/30'
                        : 'bg-topo-success/20 text-topo-success hover:bg-topo-success/30'
                    }`}
                    onClick={handleExecute}
                  >
                    {deployMode() === 'destroy' ? 'Destroy' : 'Start Deploy'}
                  </button>
                </Show>
                <Show when={executing()}>
                  <button
                    class="px-3 py-1 text-xs rounded bg-topo-error/20 text-topo-error hover:bg-topo-error/30 font-medium"
                    onClick={handleCancel}
                  >
                    Cancel
                  </button>
                </Show>
              </div>
            </div>

            {/* Output log */}
            <div
              ref={outputRef}
              class="flex-1 bg-topo-bg-primary rounded p-3 font-mono text-xs overflow-auto min-h-0"
            >
              <Show when={outputLines().length === 0}>
                <div class="text-topo-text-muted">
                  {deployMode() === 'destroy'
                    ? 'Click "Destroy" to tear down infrastructure.'
                    : 'Click "Start Deploy" to begin the deployment pipeline (init -> plan -> apply).'}
                </div>
              </Show>
              <For each={outputLines()}>
                {(line) => (
                  <div class={line.isError ? 'text-topo-error' : 'text-topo-text-secondary'}>
                    {line.text}
                  </div>
                )}
              </For>
            </div>
          </div>
        </Show>
      </div>

      {/* Footer */}
      <div class="flex items-center justify-between px-4 py-2 border-t border-topo-border shrink-0">
        <div>
          <Show when={canGoBack()}>
            <button
              class="px-3 py-1 text-xs rounded text-topo-text-secondary hover:text-topo-text-primary hover:bg-topo-bg-tertiary"
              onClick={handleBack}
            >
              Back
            </button>
          </Show>
        </div>
        <div class="flex items-center gap-2">
          <Show when={step() === 'configure'}>
            <button
              class="px-3 py-1 text-xs rounded bg-topo-brand hover:bg-topo-brand-hover text-white font-medium disabled:opacity-30"
              onClick={handleSaveAndNext}
              disabled={loading()}
            >
              {loading() ? 'Saving...' : 'Next'}
            </button>
          </Show>
          <Show when={step() === 'review'}>
            <Show when={deployMode() === 'migrate'}>
              <button
                class="px-3 py-1 text-xs rounded bg-topo-warning/20 text-topo-warning hover:bg-topo-warning/30 font-medium disabled:opacity-30"
                onClick={handleStartMigration}
                disabled={loading()}
              >
                {loading() ? 'Loading...' : 'Migrate from existing'}
              </button>
            </Show>
            <Show when={activeDeployments().find(d => d.topologyId === topo.topology.id)}>
              <button
                class="px-3 py-1 text-xs rounded bg-topo-error/20 text-topo-error hover:bg-topo-error/30 font-medium"
                onClick={handleDestroyFromReview}
              >
                Destroy
              </button>
            </Show>
            <button
              class="px-3 py-1 text-xs rounded bg-topo-brand hover:bg-topo-brand-hover text-white font-medium disabled:opacity-30"
              onClick={handleDeployFromReview}
              disabled={loading()}
            >
              {deployMode() === 'update' ? 'Update' : 'Deploy'}
            </button>
          </Show>
          <Show when={step() === 'migrate'}>
            <button
              class="px-3 py-1 text-xs rounded bg-topo-brand hover:bg-topo-brand-hover text-white font-medium disabled:opacity-30"
              onClick={handleDeployWithMigration}
              disabled={loading() || !allRequiredAnswered()}
            >
              {loading() ? 'Generating plan...' : 'Deploy with Migration'}
            </button>
          </Show>
        </div>
      </div>
    </div>
  );
};

export default DeployWizard;

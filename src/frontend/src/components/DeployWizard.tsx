import { Component, createSignal, createEffect, createMemo, For, Show, onCleanup } from 'solid-js';
import { useTopology } from '../stores/topology.store';
import { useDeployWizardStore } from '../stores/deploy-wizard.store';
import * as deployApi from '../lib/deploy-api';
import { saveTopology } from '../lib/serialization';
import { validateField, validateAllFields } from '../lib/credential-validation';
import type { DeployStep, DeployMode, CredentialStatus, CredentialField, DeployedTopology, CostEstimate, TerraformOutputLine, HostingOptions, PoolSelection, InfraSelection, TopologyValidationResult, ValidationItem } from '../types/deploy';
import type { MigrationDiffResult, MigrationDecision, MigrationPlan } from '../types/migration';
import type { Topology, Container } from '../types/topology';

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

/** Collect all distinct provider keys from container overrides + topology-level provider. */
function collectActiveProviders(topology: Topology): string[] {
  const keys = new Set<string>([topology.provider]);
  function walk(containers: Container[]) {
    for (const c of containers) {
      const override = c.config?.provider;
      if (override) keys.add(override);
      if (c.children) walk(c.children);
    }
  }
  walk(topology.containers);
  return Array.from(keys);
}

const STEPS: { key: DeployStep; label: string }[] = [
  { key: 'provider', label: 'Provider' },
  { key: 'configure', label: 'Configure' },
  { key: 'validate', label: 'Validate' },
  { key: 'hosting', label: 'Hosting' },
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

// --- Help Popover Component ---
const FieldHelp: Component<{ help: NonNullable<CredentialField['help']> }> = (props) => {
  const [open, setOpen] = createSignal(false);
  let containerRef: HTMLDivElement | undefined;

  const handleClickOutside = (e: MouseEvent) => {
    if (containerRef && !containerRef.contains(e.target as Node)) {
      setOpen(false);
    }
  };

  createEffect(() => {
    if (open()) {
      document.addEventListener('mousedown', handleClickOutside);
    } else {
      document.removeEventListener('mousedown', handleClickOutside);
    }
  });

  onCleanup(() => document.removeEventListener('mousedown', handleClickOutside));

  return (
    <div class="relative inline-block" ref={containerRef}>
      <button
        type="button"
        class="ml-1.5 w-4 h-4 rounded-full bg-topo-text-muted/20 text-topo-text-muted hover:bg-topo-brand/20 hover:text-topo-brand text-[10px] font-bold leading-none inline-flex items-center justify-center"
        onClick={(e) => { e.preventDefault(); setOpen(v => !v); }}
      >
        ?
      </button>
      <Show when={open()}>
        <div class="absolute left-6 top-0 z-50 w-80 bg-topo-bg-primary border border-topo-border rounded-lg shadow-xl p-3 text-xs">
          <div class="font-semibold text-topo-text-primary mb-2">{props.help.summary}</div>
          <ol class="list-decimal list-inside space-y-1 text-topo-text-secondary mb-2">
            <For each={props.help.steps}>
              {(step) => <li>{step}</li>}
            </For>
          </ol>
          <Show when={props.help.permissions}>
            <div class="bg-topo-warning/10 border border-topo-warning/20 rounded px-2 py-1.5 mb-2">
              <span class="font-medium text-topo-warning">Required permissions: </span>
              <span class="text-topo-text-secondary">{props.help.permissions}</span>
            </div>
          </Show>
          <Show when={props.help.note}>
            <p class="text-topo-text-muted italic mb-2">{props.help.note}</p>
          </Show>
          <Show when={props.help.url}>
            <a
              href={props.help.url!}
              target="_blank"
              rel="noopener noreferrer"
              class="text-topo-brand hover:underline font-medium"
            >
              View docs &rarr;
            </a>
          </Show>
        </div>
      </Show>
    </div>
  );
};

function formatRelativeTime(iso: string): string {
  const diff = Date.now() - new Date(iso).getTime();
  const seconds = Math.floor(diff / 1000);
  if (seconds < 60) return 'just now';
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return `${minutes}m ago`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours}h ago`;
  const days = Math.floor(hours / 24);
  return `${days}d ago`;
}

const DeployWizard: Component<{ onClose: () => void }> = (props) => {
  const topo = useTopology();
  const wizardStore = useDeployWizardStore();

  // Reset stored wizard state if topology changed
  const currentTopoId = topo.topology.id;
  if (wizardStore.state.topologyId !== currentTopoId) {
    wizardStore.reset();
    wizardStore.setTopologyId(currentTopoId);
  }

  // Restore persisted state from store
  const saved = wizardStore.state;

  // --- Wizard state ---
  // Don't render data-dependent steps until restoreWizardState() loads their data
  const safeInitialStep: DeployStep = (['hosting', 'review', 'migrate', 'execute'] as DeployStep[]).includes(saved.step)
    ? 'validate' : saved.step;
  const [step, _setStep] = createSignal<DeployStep>(safeInitialStep);
  const setStep = (s: DeployStep) => { _setStep(s); wizardStore.setStep(s); };
  const [provider, _setProvider] = createSignal(saved.provider);
  const setProvider = (p: string) => { _setProvider(p); wizardStore.setProvider(p); };
  const [providers, setProviders] = createSignal<ProviderInfo[]>([]);
  const [regions, setRegions] = createSignal<Region[]>([]);
  const [credentialStatus, setCredentialStatus] = createSignal<CredentialStatus | null>(null);
  const [credentialValues, _setCredentialValues] = createSignal<Record<string, string>>({});
  const setCredentialValues = (v: Record<string, string> | ((prev: Record<string, string>) => Record<string, string>)) => {
    if (typeof v === 'function') {
      _setCredentialValues(prev => { const next = v(prev); wizardStore.setProviderValues('__single__', next); return next; });
    } else {
      _setCredentialValues(v); wizardStore.setProviderValues('__single__', v);
    }
  };
  const [credentialSchema, setCredentialSchema] = createSignal<CredentialField[]>([]);
  const [costEstimate, setCostEstimate] = createSignal<CostEstimate | null>(null);
  const [hclFiles, setHclFiles] = createSignal<Record<string, string>>({});
  const hclResources = createMemo(() => {
    const grouped = new Map<string, string[]>();
    for (const [, content] of Object.entries(hclFiles())) {
      const re = /^resource\s+"([^"]+)"\s+"([^"]+)"/gm;
      let m;
      while ((m = re.exec(content)) !== null) {
        const list = grouped.get(m[1]) ?? [];
        list.push(m[2]);
        grouped.set(m[1], list);
      }
    }
    return grouped;
  });
  const hclResourceCount = createMemo(() => {
    let count = 0;
    for (const names of hclResources().values()) count += names.length;
    return count;
  });
  const [activeDeployments, setActiveDeployments] = createSignal<DeployedTopology[]>([]);
  const [deployMode, _setDeployMode] = createSignal<DeployMode>(saved.deployMode);
  const setDeployMode = (m: DeployMode) => { _setDeployMode(m); wizardStore.setDeployMode(m); };

  // Validation step state
  const [validationResult, setValidationResult] = createSignal<TopologyValidationResult | null>(null);

  // Hosting step state
  const [hostingOptions, setHostingOptions] = createSignal<HostingOptions | null>(null);
  const [poolSelections, _setPoolSelections] = createSignal<Record<string, PoolSelection>>(saved.poolSelections);
  const setPoolSelections = (v: Record<string, PoolSelection> | ((prev: Record<string, PoolSelection>) => Record<string, PoolSelection>)) => {
    if (typeof v === 'function') {
      _setPoolSelections(prev => { const next = v(prev); wizardStore.setPoolSelections(next); return next; });
    } else {
      _setPoolSelections(v); wizardStore.setPoolSelections(v);
    }
  };
  const [infraSelections, _setInfraSelections] = createSignal<Record<string, InfraSelection>>(saved.infraSelections ?? {});
  const setInfraSelections = (v: Record<string, InfraSelection> | ((prev: Record<string, InfraSelection>) => Record<string, InfraSelection>)) => {
    if (typeof v === 'function') {
      _setInfraSelections(prev => { const next = v(prev); wizardStore.setInfraSelections(next); return next; });
    } else {
      _setInfraSelections(v); wizardStore.setInfraSelections(v);
    }
  };
  const [loading, setLoading] = createSignal(false);
  const [error, setError] = createSignal('');
  const [fieldErrors, setFieldErrors] = createSignal<Record<string, string>>({});
  const [touchedFields, setTouchedFields] = createSignal<Set<string>>(new Set());

  // Multi-provider state
  const [activeProviderKeys, setActiveProviderKeys] = createSignal<string[]>([]);
  const [activeProviderTab, setActiveProviderTab] = createSignal('');
  const [providerRegions, setProviderRegions] = createSignal<Record<string, Region[]>>({});
  const [providerSchemas, setProviderSchemas] = createSignal<Record<string, CredentialField[]>>({});
  const [providerStatuses, setProviderStatuses] = createSignal<Record<string, CredentialStatus>>({});
  const [providerValues, _setProviderValues] = createSignal<Record<string, Record<string, string>>>({});
  const setProviderValues = (v: Record<string, Record<string, string>> | ((prev: Record<string, Record<string, string>>) => Record<string, Record<string, string>>)) => {
    if (typeof v === 'function') {
      _setProviderValues(prev => { const next = v(prev); wizardStore.setAllProviderValues(next); return next; });
    } else {
      _setProviderValues(v); wizardStore.setAllProviderValues(v);
    }
  };
  const [providerFieldErrors, setProviderFieldErrors] = createSignal<Record<string, Record<string, string>>>({});
  const [providerTouchedFields, setProviderTouchedFields] = createSignal<Record<string, Set<string>>>({});

  const isMultiProvider = () => activeProviderKeys().length > 1;

  // Service key state
  const [serviceKeySchema, setServiceKeySchema] = createSignal<CredentialField[]>([]);
  const [serviceKeyStatus, setServiceKeyStatus] = createSignal<CredentialStatus | null>(null);
  const [serviceKeyValues, _setServiceKeyValues] = createSignal<Record<string, string>>(saved.serviceKeyValues);
  const setServiceKeyValues = (v: Record<string, string> | ((prev: Record<string, string>) => Record<string, string>)) => {
    if (typeof v === 'function') {
      _setServiceKeyValues(prev => { const next = v(prev); wizardStore.setServiceKeyValues(next); return next; });
    } else {
      _setServiceKeyValues(v); wizardStore.setServiceKeyValues(v);
    }
  };
  const [serviceKeyErrors, setServiceKeyErrors] = createSignal<Record<string, string>>({});
  const [serviceKeyTouched, setServiceKeyTouched] = createSignal<Set<string>>(new Set());

  const missingRequiredServiceKeys = () => {
    const status = serviceKeyStatus();
    const localValues = serviceKeyValues();
    return serviceKeySchema()
      .filter(f => f.required)
      .some(f => {
        const isSetOnServer = status?.setVariables?.includes(f.key) ?? false;
        const hasLocalValue = (localValues[f.key] ?? '').trim().length > 0;
        return !isSetOnServer && !hasLocalValue;
      });
  };

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

      // Restore saved provider or pre-select from topology
      const savedProv = saved.provider;
      const topoProv = topo.topology.provider;
      if (savedProv) {
        _setProvider(savedProv);
        await onProviderSelected(savedProv);
      } else if (topoProv) {
        setProvider(topoProv);
        // Auto-advance if only one provider
        if (data.providers.length === 1) {
          setProvider(data.providers[0].key);
          await onProviderSelected(data.providers[0].key);
        }
      } else if (data.providers.length === 1) {
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
      // Detect all active providers from topology container overrides
      const allKeys = collectActiveProviders(topo.topology);
      // Ensure the selected provider is included
      if (!allKeys.includes(key)) allKeys.unshift(key);
      setActiveProviderKeys(allKeys);
      setActiveProviderTab(allKeys[0]);

      if (allKeys.length > 1) {
        // Multi-provider: load schemas/statuses for all providers in parallel
        const schemaResults: Record<string, CredentialField[]> = {};
        const statusResults: Record<string, CredentialStatus> = {};
        const valResults: Record<string, Record<string, string>> = {};
        const touchResults: Record<string, Set<string>> = {};
        const errResults: Record<string, Record<string, string>> = {};
        const regionResults: Record<string, Region[]> = {};

        await Promise.all(allKeys.map(async (pk) => {
          const [credRes, schema, regRes] = await Promise.all([
            deployApi.getCredentialStatus(topo.topology.id, pk),
            deployApi.getCredentialSchema(pk),
            fetch(`/api/v1/providers/${pk}/regions`).then(r => { if (!r.ok) throw new Error('Failed'); return r.json(); }),
          ]);
          regionResults[pk] = regRes.regions;
          schemaResults[pk] = schema;
          statusResults[pk] = credRes;
          touchResults[pk] = new Set();
          errResults[pk] = {};

          const prefill: Record<string, string> = {};
          if (credRes.nonSensitiveValues) {
            for (const [k, v] of Object.entries(credRes.nonSensitiveValues)) {
              prefill[k] = v;
            }
          }
          // Merge saved values from wizard store
          const savedVals = saved.providerValues[pk];
          if (savedVals) {
            for (const [k, v] of Object.entries(savedVals)) {
              if (v) prefill[k] = v;
            }
          }
          valResults[pk] = prefill;
        }));

        setProviderSchemas(schemaResults);
        setProviderStatuses(statusResults);
        setProviderValues(valResults);
        setProviderTouchedFields(touchResults);
        setProviderFieldErrors(errResults);
        setProviderRegions(regionResults);

        // Also load primary provider data for single-provider compat
        const regRes = await fetch(`/api/v1/providers/${key}/regions`).then(r => { if (!r.ok) throw new Error('Failed'); return r.json(); });
        setRegions(regRes.regions);
        setCredentialSchema(schemaResults[key] ?? []);
        setCredentialStatus(statusResults[key] ?? null);
        setCredentialValues(valResults[key] ?? {});
      } else {
        // Single provider: original path
        const [regRes, credRes, schema] = await Promise.all([
          fetch(`/api/v1/providers/${key}/regions`).then(r => { if (!r.ok) throw new Error('Failed'); return r.json(); }),
          deployApi.getCredentialStatus(topo.topology.id, key),
          deployApi.getCredentialSchema(key),
        ]);
        setRegions(regRes.regions);
        setCredentialStatus(credRes);
        setCredentialSchema(schema);

        const prefill: Record<string, string> = {};
        if (credRes.nonSensitiveValues) {
          for (const [k, v] of Object.entries(credRes.nonSensitiveValues)) {
            prefill[k] = v;
          }
        }
        const topoConfig = topo.topology.providerConfig;
        if (topoConfig) {
          for (const [k, v] of Object.entries(topoConfig)) {
            prefill[k] = v;
          }
        }
        // Merge saved values from wizard store
        const savedSingle = saved.providerValues['__single__'];
        if (savedSingle) {
          for (const [k, v] of Object.entries(savedSingle)) {
            if (v) prefill[k] = v;
          }
        }
        setCredentialValues(prefill);
      }

      // Fetch service key schema and status in parallel
      const [skSchema, skStatus] = await Promise.all([
        deployApi.getServiceKeySchema(),
        deployApi.getServiceKeyStatus(topo.topology.id),
      ]);
      setServiceKeySchema(skSchema);
      setServiceKeyStatus(skStatus);

      // Pre-fill non-sensitive values from saved status
      const skPrefill: Record<string, string> = {};
      if (skStatus.nonSensitiveValues) {
        for (const [k, v] of Object.entries(skStatus.nonSensitiveValues)) {
          skPrefill[k] = v;
        }
      }
      // Merge saved service key values from wizard store
      if (saved.serviceKeyValues) {
        for (const [k, v] of Object.entries(saved.serviceKeyValues)) {
          if (v) skPrefill[k] = v;
        }
      }
      setServiceKeyValues(skPrefill);

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
    setFieldErrors(prev => { const next = { ...prev }; delete next[key]; return next; });
  };

  const handleFieldBlur = (field: CredentialField) => {
    setTouchedFields(prev => new Set(prev).add(field.key));
    const value = credentialValues()[field.key] ?? '';
    const isSaved = credentialStatus()?.setVariables.includes(field.key) ?? false;
    const err = validateField(field, value, isSaved);
    setFieldErrors(prev => {
      const next = { ...prev };
      if (err) next[field.key] = err; else delete next[field.key];
      return next;
    });
  };

  const handleSaveAndNext = async () => {
    if (isMultiProvider()) {
      // Validate all provider tabs
      const allProvKeys = activeProviderKeys();
      const schemas = providerSchemas();
      const vals = providerValues();
      const statuses = providerStatuses();
      let hasErrors = false;
      const newErrors: Record<string, Record<string, string>> = {};
      const newTouched: Record<string, Set<string>> = {};

      for (const pk of allProvKeys) {
        const schema = schemas[pk] ?? [];
        const fieldKeys = new Set(schema.map(f => f.key));
        newTouched[pk] = fieldKeys;
        const savedKeys = new Set(statuses[pk]?.setVariables ?? []);
        const errors = validateAllFields(schema, vals[pk] ?? {}, savedKeys);
        newErrors[pk] = errors;
        if (Object.keys(errors).length > 0) hasErrors = true;
      }

      setProviderFieldErrors(newErrors);
      setProviderTouchedFields(newTouched);

      if (hasErrors) {
        // Switch to the first tab with errors
        const firstError = allProvKeys.find(pk => Object.keys(newErrors[pk] ?? {}).length > 0);
        if (firstError) setActiveProviderTab(firstError);
        setError('Please fix the validation errors on all provider tabs');
        return;
      }
    } else {
      // Single provider validation
      const schema = credentialSchema();
      const allKeys = new Set(schema.map(f => f.key));
      setTouchedFields(allKeys);
      const savedKeys = new Set(credentialStatus()?.setVariables ?? []);
      const errors = validateAllFields(schema, credentialValues(), savedKeys);
      setFieldErrors(errors);
      if (Object.keys(errors).length > 0) {
        setError('Please fix the validation errors below');
        return;
      }
    }

    // Validate service keys
    const skSchema = serviceKeySchema();
    const allSkKeys = new Set(skSchema.map(f => f.key));
    setServiceKeyTouched(allSkKeys);
    const skSavedKeys = new Set(serviceKeyStatus()?.setVariables ?? []);
    const skErrors = validateAllFields(skSchema, serviceKeyValues(), skSavedKeys);
    setServiceKeyErrors(skErrors);
    if (Object.keys(skErrors).length > 0) {
      setError('Please fix the service key validation errors below');
      return;
    }

    setLoading(true);
    setError('');
    try {
      // Persist provider selection to topology and save to backend
      if (topo.topology.provider !== provider()) {
        topo.updateProvider(provider());
      }
      await saveTopology(topo.topology);

      if (isMultiProvider()) {
        // Save credentials for each provider
        const allProvKeys = activeProviderKeys();
        const vals = providerValues();
        const updatedStatuses = { ...providerStatuses() };
        for (const pk of allProvKeys) {
          const vars: Record<string, string> = {};
          for (const [k, v] of Object.entries(vals[pk] ?? {})) {
            if (v) vars[k] = v;
          }
          if (Object.keys(vars).length > 0) {
            const status = await deployApi.saveCredentials(topo.topology.id, pk, vars);
            updatedStatuses[pk] = status;
          }
        }
        setProviderStatuses(updatedStatuses);
      } else {
        // Only send non-empty values
        const vars: Record<string, string> = {};
        for (const [k, v] of Object.entries(credentialValues())) {
          if (v) vars[k] = v;
        }
        if (Object.keys(vars).length > 0) {
          const updatedCredStatus = await deployApi.saveCredentials(topo.topology.id, provider(), vars);
          setCredentialStatus(updatedCredStatus);
        }

        // Persist non-sensitive config to topology's providerConfig (per-topology, not shared)
        const schema = credentialSchema();
        const nonSensitive: Record<string, string> = {};
        for (const [k, v] of Object.entries(vars)) {
          const field = schema.find(f => f.key === k);
          if (field && !field.sensitive) {
            nonSensitive[k] = v;
          }
        }
        topo.updateProviderConfig(nonSensitive);
      }

      // Save service keys
      const skVars: Record<string, string> = {};
      for (const [k, v] of Object.entries(serviceKeyValues())) {
        if (v) skVars[k] = v;
      }
      if (Object.keys(skVars).length > 0) {
        const updatedSkStatus = await deployApi.saveServiceKeys(topo.topology.id, skVars);
        setServiceKeyStatus(updatedSkStatus);
      }

      // Persist non-sensitive service keys to topology
      const skNonSensitive: Record<string, string> = {};
      for (const [k, v] of Object.entries(skVars)) {
        const field = serviceKeySchema().find(f => f.key === k);
        if (field && !field.sensitive) {
          skNonSensitive[k] = v;
        }
      }
      topo.updateServiceKeys(skNonSensitive);

      // Run topology validation before proceeding
      const validationRes = await deployApi.validateTopology(topo.topology.id);
      setValidationResult(validationRes);
      setStep('validate');
    } catch (e: any) {
      setError(e.message);
    } finally {
      setLoading(false);
    }
  };

  // --- Validate step: proceed to hosting/review ---
  const handleValidateNext = async () => {
    const vr = validationResult();
    if (!vr || !vr.canDeploy) {
      setError('Fix all validation errors before continuing.');
      return;
    }
    setLoading(true);
    setError('');
    try {
      const hosting = await deployApi.getHostingOptions(topo.topology.id);
      setHostingOptions(hosting);

      if (hosting.pools.length > 0 || hosting.infraImages.length > 0) {
        const defaults: Record<string, PoolSelection> = {};
        const savedPools = poolSelections();
        for (const pool of hosting.pools) {
          if (pool.options.length > 0) {
            const key = pool.tierProfileId ? `${pool.poolName}_${pool.tierProfileId}` : pool.poolName;
            const savedSel = savedPools[key];
            const validPlan = savedSel && pool.options.some(o => o.planId === savedSel.planId);
            defaults[key] = validPlan ? savedSel : {
              poolName: pool.poolName,
              planId: pool.options[0].planId,
              targetTenants: 10,
              tierProfileId: pool.tierProfileId || undefined,
            };
          }
        }
        setPoolSelections(defaults);

        // Initialize infra selections with defaults (auto-selected plan from backend)
        const infraDefaults: Record<string, InfraSelection> = {};
        const savedInfra = infraSelections();
        for (const img of hosting.infraImages) {
          const savedSel = savedInfra[img.imageName];
          const validPlan = savedSel && img.availablePlans.some(p => p.planId === savedSel.planId);
          infraDefaults[img.imageName] = validPlan ? savedSel : {
            imageName: img.imageName,
            planId: img.planId,
          };
        }
        setInfraSelections(infraDefaults);

        setStep('hosting');
      } else {
        await generateAndEstimate();
      }
    } catch (e: any) {
      setError(e.message);
    } finally {
      setLoading(false);
    }
  };

  // --- Re-run validation (for retry) ---
  const handleRevalidate = async () => {
    setLoading(true);
    setError('');
    try {
      const validationRes = await deployApi.validateTopology(topo.topology.id);
      setValidationResult(validationRes);
    } catch (e: any) {
      setError(e.message);
    } finally {
      setLoading(false);
    }
  };

  // --- Generate HCL + estimate cost helper ---
  const generateAndEstimate = async (selections?: PoolSelection[]) => {
    const selArr = selections && selections.length > 0 ? selections : undefined;
    const [hclResult, cost, deployments] = await Promise.all([
      deployApi.generateHcl(topo.topology.id, selArr),
      deployApi.estimateCost(topo.topology.id, selArr),
      deployApi.getActiveDeployments(),
    ]);
    setHclFiles(hclResult.files);
    setCostEstimate(cost);
    setActiveDeployments(deployments);

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
  };

  // --- Step 2.5: Hosting ---
  const handleHostingNext = async () => {
    const sels = poolSelections();
    const options = hostingOptions();
    if (!options) return;

    // Validate all pools with viable options have a plan selected
    for (const pool of options.pools) {
      if (pool.options.length === 0) continue;
      const key = pool.tierProfileId ? `${pool.poolName}_${pool.tierProfileId}` : pool.poolName;
      const sel = sels[key];
      if (!sel || !sel.planId) {
        setError(`Please select a hosting plan for "${pool.tierProfileName || pool.poolName}"`);
        return;
      }
    }

    setLoading(true);
    setError('');
    try {
      const selArr = Object.values(sels);
      await generateAndEstimate(selArr);
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
      if (ok) {
        topo.updateDeployStatus(undefined, 0);
      } else {
        topo.updateDeployStatus('Failed', topo.topology.deployedResourceCount);
      }
      await saveTopology(topo.topology);
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

    // Persist deploy status
    if (ok) {
      // Estimate resource count from output lines that mention "created" or "modified"
      const lines = outputLines();
      const summaryLine = lines.findLast(l => /\d+\s+added/.test(l.text));
      let resourceCount = 0;
      if (summaryLine) {
        const match = summaryLine.text.match(/(\d+)\s+added/);
        if (match) resourceCount = parseInt(match[1], 10);
      }
      // Fall back to active deployment resource count if available
      if (!resourceCount) {
        const activeDeploy = activeDeployments().find(d => d.topologyId === topo.topology.id);
        resourceCount = activeDeploy?.resourceCount ?? 0;
      }
      topo.updateDeployStatus('Succeeded', resourceCount);
    } else {
      topo.updateDeployStatus('Failed', topo.topology.deployedResourceCount);
    }
    await saveTopology(topo.topology);

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
  // If restoring to a step beyond 'provider', reload API data for that step
  const restoreWizardState = async () => {
    let restoredStep = saved.step;
    // Don't restore to execute or migrate steps — fall back to review
    if (restoredStep === 'execute' || restoredStep === 'migrate') {
      restoredStep = 'review';
    }
    if (restoredStep === 'provider' || !saved.provider) {
      // Start fresh from provider step
      loadProviders();
      return;
    }

    // Load providers list first, then load provider data to populate schemas/statuses
    await loadProviders();

    // If loadProviders auto-advanced past provider selection, the step is already set.
    // If we had a saved step beyond 'configure', re-advance to it by reloading intermediate data.
    if (restoredStep !== 'configure') {
      const stepOrder: DeployStep[] = ['provider', 'configure', 'validate', 'hosting', 'review', 'migrate', 'execute'];
      const restoredIdx = stepOrder.indexOf(restoredStep);

      // Save current canvas state before validating — the topology may have changed since last save
      if (restoredIdx >= stepOrder.indexOf('validate')) {
        try {
          await saveTopology(topo.topology);
        } catch { /* continue to validation, it will show errors */ }
        try {
          const validationRes = await deployApi.validateTopology(topo.topology.id);
          setValidationResult(validationRes);
          // If validation now fails, stop at the validate step
          if (!validationRes.canDeploy) {
            _setStep('validate');
            return;
          }
        } catch { _setStep('validate'); return; }
      }

      // For steps beyond validate, reload hosting options
      if (restoredIdx >= stepOrder.indexOf('hosting')) {
        try {
          const hosting = await deployApi.getHostingOptions(topo.topology.id);
          setHostingOptions(hosting);
        } catch {
          _setStep('validate');
          return;
        }
      }

      // For review step, reload HCL + cost
      if (restoredIdx >= stepOrder.indexOf('review')) {
        try {
          const selArr = Object.keys(saved.poolSelections).length > 0
            ? Object.values(saved.poolSelections) : undefined;
          const [hclResult, cost, deployments] = await Promise.all([
            deployApi.generateHcl(topo.topology.id, selArr),
            deployApi.estimateCost(topo.topology.id, selArr),
            deployApi.getActiveDeployments(),
          ]);
          setHclFiles(hclResult.files);
          setCostEstimate(cost);
          setActiveDeployments(deployments);
        } catch { /* will show on review step */ }
      }

      // Restore to saved step (setStep already persists via wrapper)
      _setStep(restoredStep);
    }
  };
  restoreWizardState();

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

  // --- SSH key generation ---
  const [generatingKey, setGeneratingKey] = createSignal(false);

  const handleGenerateSshKey = async () => {
    setGeneratingKey(true);
    setError('');
    try {
      const result = await deployApi.generateSshKeypair();
      setCredVal('ssh_public_key', result.publicKey);

      // Auto-download private key
      const blob = new Blob([result.privateKey + '\n'], { type: 'application/octet-stream' });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = 'xcord_topo_id_ed25519';
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
    } catch (e: any) {
      setError(e.message);
    } finally {
      setGeneratingKey(false);
    }
  };

  // --- Dynamic field rendering ---
  const renderField = (field: CredentialField) => {
    const isSaved = () => credentialStatus()?.setVariables.includes(field.key);
    const isSshKey = field.key === 'ssh_public_key';
    const hasError = () => touchedFields().has(field.key) && fieldErrors()[field.key];
    const borderClass = () => hasError() ? 'border-topo-error' : 'border-topo-border';

    return (
      <div>
        <label class="flex items-center text-xs text-topo-text-muted mb-1">
          {field.label}
          <Show when={field.required}>
            <span class="ml-0.5 text-topo-error">*</span>
          </Show>
          <Show when={!field.required}>
            <span class="ml-1 text-topo-text-muted/50">(optional)</span>
          </Show>
          <Show when={field.help}>
            <FieldHelp help={field.help!} />
          </Show>
          <Show when={isSshKey}>
            <button
              type="button"
              class="ml-2 text-xs text-topo-brand hover:underline disabled:opacity-30"
              onClick={handleGenerateSshKey}
              disabled={generatingKey()}
            >
              {generatingKey() ? 'Generating...' : 'Generate'}
            </button>
          </Show>
        </label>
        {field.type === 'select' && field.key === 'region' ? (
          <select
            class={`w-full bg-topo-bg-primary border ${borderClass()} rounded px-2 py-1.5 text-sm text-topo-text-primary focus:outline-none focus:border-topo-brand`}
            value={credentialValues()[field.key] ?? ''}
            onChange={(e) => setCredVal(field.key, e.currentTarget.value)}
            onBlur={() => handleFieldBlur(field)}
          >
            <option value="">{field.placeholder || 'Select...'}</option>
            <For each={regions()}>
              {(r) => <option value={r.id}>{r.label} ({r.id})</option>}
            </For>
          </select>
        ) : field.type === 'textarea' ? (
          <textarea
            class={`w-full bg-topo-bg-primary border ${borderClass()} rounded px-2 py-1.5 text-sm text-topo-text-primary focus:outline-none focus:border-topo-brand font-mono h-16 resize-none`}
            placeholder={field.placeholder}
            value={credentialValues()[field.key] ?? ''}
            onInput={(e) => setCredVal(field.key, e.currentTarget.value)}
            onBlur={() => handleFieldBlur(field)}
          />
        ) : (
          <input
            type={field.type === 'password' ? 'password' : 'text'}
            class={`w-full bg-topo-bg-primary border ${borderClass()} rounded px-2 py-1.5 text-sm text-topo-text-primary focus:outline-none focus:border-topo-brand`}
            placeholder={isSaved() && field.sensitive ? '••••••••' : (field.placeholder ?? '')}
            value={credentialValues()[field.key] ?? ''}
            onInput={(e) => setCredVal(field.key, e.currentTarget.value)}
            onBlur={() => handleFieldBlur(field)}
          />
        )}
        <Show when={hasError()}>
          <p class="text-xs text-topo-error mt-0.5">{fieldErrors()[field.key]}</p>
        </Show>
      </div>
    );
  };

  // --- Service key field rendering ---
  const setServiceKeyVal = (key: string, value: string) => {
    setServiceKeyValues(prev => ({ ...prev, [key]: value }));
    setServiceKeyErrors(prev => { const next = { ...prev }; delete next[key]; return next; });
  };

  const handleServiceKeyBlur = (field: CredentialField) => {
    setServiceKeyTouched(prev => new Set(prev).add(field.key));
    const value = serviceKeyValues()[field.key] ?? '';
    const isSaved = serviceKeyStatus()?.setVariables.includes(field.key) ?? false;
    const err = validateField(field, value, isSaved);
    setServiceKeyErrors(prev => {
      const next = { ...prev };
      if (err) next[field.key] = err; else delete next[field.key];
      return next;
    });
  };

  const renderServiceKeyField = (field: CredentialField) => {
    const isSaved = () => serviceKeyStatus()?.setVariables.includes(field.key);
    const hasError = () => serviceKeyTouched().has(field.key) && serviceKeyErrors()[field.key];
    const borderClass = () => hasError() ? 'border-topo-error' : 'border-topo-border';

    return (
      <div>
        <label class="flex items-center text-xs text-topo-text-muted mb-1">
          {field.label}
          <Show when={field.required}>
            <span class="ml-0.5 text-topo-error">*</span>
          </Show>
          <Show when={!field.required}>
            <span class="ml-1 text-topo-text-muted/50">(optional)</span>
          </Show>
          <Show when={field.help}>
            <FieldHelp help={field.help!} />
          </Show>
        </label>
        {field.type === 'textarea' ? (
          <textarea
            class={`w-full bg-topo-bg-primary border ${borderClass()} rounded px-2 py-1.5 text-sm text-topo-text-primary focus:outline-none focus:border-topo-brand font-mono h-16 resize-none`}
            placeholder={field.placeholder}
            value={serviceKeyValues()[field.key] ?? ''}
            onInput={(e) => setServiceKeyVal(field.key, e.currentTarget.value)}
            onBlur={() => handleServiceKeyBlur(field)}
          />
        ) : (
          <input
            type={field.type === 'password' ? 'password' : 'text'}
            class={`w-full bg-topo-bg-primary border ${borderClass()} rounded px-2 py-1.5 text-sm text-topo-text-primary focus:outline-none focus:border-topo-brand`}
            placeholder={isSaved() && field.sensitive ? '••••••••' : (field.placeholder ?? '')}
            value={serviceKeyValues()[field.key] ?? ''}
            onInput={(e) => setServiceKeyVal(field.key, e.currentTarget.value)}
            onBlur={() => handleServiceKeyBlur(field)}
          />
        )}
        <Show when={hasError()}>
          <p class="text-xs text-topo-error mt-0.5">{serviceKeyErrors()[field.key]}</p>
        </Show>
      </div>
    );
  };

  // --- Multi-provider field rendering ---
  const renderMultiProviderField = (
    providerKey: string,
    field: CredentialField,
    vals: () => Record<string, string>,
    errs: () => Record<string, string>,
    touched: () => Set<string>,
    status: () => CredentialStatus | undefined,
  ) => {
    const isSaved = () => status()?.setVariables?.includes(field.key);
    const hasError = () => touched().has(field.key) && errs()[field.key];
    const borderClass = () => hasError() ? 'border-topo-error' : 'border-topo-border';

    const setValue = (key: string, value: string) => {
      setProviderValues(prev => ({
        ...prev,
        [providerKey]: { ...(prev[providerKey] ?? {}), [key]: value },
      }));
      setProviderFieldErrors(prev => {
        const next = { ...prev };
        const pErrors = { ...(next[providerKey] ?? {}) };
        delete pErrors[key];
        next[providerKey] = pErrors;
        return next;
      });
    };

    const handleBlur = () => {
      setProviderTouchedFields(prev => {
        const next = { ...prev };
        const s = new Set(next[providerKey] ?? []);
        s.add(field.key);
        next[providerKey] = s;
        return next;
      });
      const value = vals()[field.key] ?? '';
      const isSavedNow = status()?.setVariables?.includes(field.key) ?? false;
      const err = validateField(field, value, isSavedNow);
      setProviderFieldErrors(prev => {
        const next = { ...prev };
        const pErrors = { ...(next[providerKey] ?? {}) };
        if (err) pErrors[field.key] = err; else delete pErrors[field.key];
        next[providerKey] = pErrors;
        return next;
      });
    };

    return (
      <div>
        <label class="flex items-center text-xs text-topo-text-muted mb-1">
          {field.label}
          <Show when={field.required}>
            <span class="ml-0.5 text-topo-error">*</span>
          </Show>
          <Show when={!field.required}>
            <span class="ml-1 text-topo-text-muted/50">(optional)</span>
          </Show>
          <Show when={field.help}>
            <FieldHelp help={field.help!} />
          </Show>
        </label>
        {field.type === 'select' && field.key === 'region' ? (
          <select
            class={`w-full bg-topo-bg-primary border ${borderClass()} rounded px-2 py-1.5 text-sm text-topo-text-primary focus:outline-none focus:border-topo-brand`}
            value={vals()[field.key] ?? ''}
            onChange={(e) => setValue(field.key, e.currentTarget.value)}
            onBlur={handleBlur}
          >
            <option value="">{field.placeholder || 'Select...'}</option>
            <For each={providerRegions()[providerKey] ?? []}>
              {(r) => <option value={r.id}>{r.label} ({r.id})</option>}
            </For>
          </select>
        ) : field.type === 'textarea' ? (
          <textarea
            class={`w-full bg-topo-bg-primary border ${borderClass()} rounded px-2 py-1.5 text-sm text-topo-text-primary focus:outline-none focus:border-topo-brand font-mono h-16 resize-none`}
            placeholder={field.placeholder}
            value={vals()[field.key] ?? ''}
            onInput={(e) => setValue(field.key, e.currentTarget.value)}
            onBlur={handleBlur}
          />
        ) : (
          <input
            type={field.type === 'password' ? 'password' : 'text'}
            class={`w-full bg-topo-bg-primary border ${borderClass()} rounded px-2 py-1.5 text-sm text-topo-text-primary focus:outline-none focus:border-topo-brand`}
            placeholder={isSaved() && field.sensitive ? '••••••••' : (field.placeholder ?? '')}
            value={vals()[field.key] ?? ''}
            onInput={(e) => setValue(field.key, e.currentTarget.value)}
            onBlur={handleBlur}
          />
        )}
        <Show when={hasError()}>
          <p class="text-xs text-topo-error mt-0.5">{errs()[field.key]}</p>
        </Show>
      </div>
    );
  };

  // --- Inline navigation buttons ---
  const renderNav = (children: any) => (
    <div class="flex items-center justify-between mt-6 pt-4 border-t border-topo-border">
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
        {children}
      </div>
    </div>
  );

  // --- Render ---
  return (
    <div class="fixed inset-0 z-50 bg-topo-bg-secondary flex flex-col">
      {/* Header + Step indicator */}
      <div class="flex items-center justify-between px-6 py-3 border-b border-topo-border shrink-0">
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
        <div class="px-6 py-2 text-xs text-topo-error bg-topo-error/10 border-b border-topo-error/20 shrink-0">
          {error()}
        </div>
      </Show>

      {/* Content */}
      <div class="flex-1 overflow-auto p-6">
        <div class="max-w-4xl mx-auto">
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
                <Show when={isMultiProvider()}>
                  <div class="bg-topo-brand/10 border border-topo-brand/20 rounded px-3 py-2 text-xs text-topo-brand">
                    This topology uses multiple providers. Configure credentials for each.
                  </div>

                  {/* Provider tabs */}
                  <div class="flex gap-1 border-b border-topo-border">
                    <For each={activeProviderKeys()}>
                      {(pk) => {
                        const hasProvErrors = () => Object.keys(providerFieldErrors()[pk] ?? {}).length > 0;
                        const provStatus = () => providerStatuses()[pk];
                        const allSaved = () => {
                          const schema = providerSchemas()[pk] ?? [];
                          const savedVars = provStatus()?.setVariables ?? [];
                          return schema.filter(f => f.required).every(f => savedVars.includes(f.key));
                        };

                        return (
                          <button
                            class={`px-3 py-1.5 text-xs font-medium border-b-2 transition-colors ${
                              activeProviderTab() === pk
                                ? 'border-topo-brand text-topo-brand'
                                : 'border-transparent text-topo-text-muted hover:text-topo-text-secondary'
                            }`}
                            onClick={() => setActiveProviderTab(pk)}
                          >
                            {pk}
                            <Show when={allSaved() && !hasProvErrors()}>
                              <span class="ml-1 text-topo-success">&#10003;</span>
                            </Show>
                            <Show when={hasProvErrors()}>
                              <span class="ml-1 text-topo-error">&#9679;</span>
                            </Show>
                          </button>
                        );
                      }}
                    </For>
                  </div>

                  {/* Per-provider credential form */}
                  <For each={activeProviderKeys()}>
                    {(pk) => (
                      <Show when={activeProviderTab() === pk}>
                        <div class="space-y-3">
                          <p class="text-xs text-topo-text-muted">Enter credentials for {pk}.</p>
                          <For each={providerSchemas()[pk] ?? []}>
                            {(field) => {
                              const vals = () => providerValues()[pk] ?? {};
                              const errs = () => providerFieldErrors()[pk] ?? {};
                              const touched = () => providerTouchedFields()[pk] ?? new Set();
                              const status = () => providerStatuses()[pk];

                              return renderMultiProviderField(pk, field, vals, errs, touched, status);
                            }}
                          </For>
                        </div>
                      </Show>
                    )}
                  </For>
                </Show>

                <Show when={!isMultiProvider()}>
                  <p class="text-xs text-topo-text-muted">Enter credentials and configuration for {provider()}.</p>

                  <For each={credentialSchema()}>
                    {(field) => renderField(field)}
                  </For>
                </Show>

                {/* Service Configuration */}
                <Show when={serviceKeySchema().length > 0}>
                  <div class="border-t border-topo-border mt-4 pt-4">
                    <h3 class="text-sm font-semibold text-topo-text-primary mb-1">Service Configuration</h3>
                    <p class="text-xs text-topo-text-muted mb-3">Registry and third-party service credentials distributed to all provisioned instances.</p>

                    {/* Docker Registry */}
                    <Show when={serviceKeySchema().some(f => f.key.startsWith('registry_'))}>
                      <div class="mb-3">
                        <h4 class="text-xs font-medium text-topo-text-secondary mb-2">Docker Registry</h4>
                        <div class="space-y-2">
                          <For each={serviceKeySchema().filter(f => f.key.startsWith('registry_'))}>
                            {(field) => renderServiceKeyField(field)}
                          </For>
                        </div>
                      </div>
                    </Show>

                    {/* Stripe */}
                    <Show when={serviceKeySchema().some(f => f.key.startsWith('stripe_'))}>
                      <div class="mb-3">
                        <h4 class="text-xs font-medium text-topo-text-secondary mb-2">Stripe</h4>
                        <div class="space-y-2">
                          <For each={serviceKeySchema().filter(f => f.key.startsWith('stripe_'))}>
                            {(field) => renderServiceKeyField(field)}
                          </For>
                        </div>
                      </div>
                    </Show>

                    {/* SMTP */}
                    <Show when={serviceKeySchema().some(f => f.key.startsWith('smtp_'))}>
                      <div class="mb-3">
                        <h4 class="text-xs font-medium text-topo-text-secondary mb-2">Email (SMTP)</h4>
                        <div class="space-y-2">
                          <For each={serviceKeySchema().filter(f => f.key.startsWith('smtp_'))}>
                            {(field) => renderServiceKeyField(field)}
                          </For>
                        </div>
                      </div>
                    </Show>

                    {/* Tenor */}
                    <Show when={serviceKeySchema().some(f => f.key.startsWith('tenor_'))}>
                      <div class="mb-3">
                        <h4 class="text-xs font-medium text-topo-text-secondary mb-2">GIF Search (Optional)</h4>
                        <div class="space-y-2">
                          <For each={serviceKeySchema().filter(f => f.key.startsWith('tenor_'))}>
                            {(field) => renderServiceKeyField(field)}
                          </For>
                        </div>
                      </div>
                    </Show>
                  </div>
                </Show>

                {renderNav(
                  <button
                    class="px-3 py-1 text-xs rounded bg-topo-brand hover:bg-topo-brand-hover text-white font-medium disabled:opacity-30"
                    onClick={handleSaveAndNext}
                    disabled={loading()}
                  >
                    {loading() ? 'Saving...' : 'Next'}
                  </button>
                )}
              </div>

              {/* Cost estimate sidebar */}
              <Show when={costEstimate()}>
                {(cost) => (
                  <div class="w-56 shrink-0">
                    <div class="bg-topo-bg-primary border border-topo-border rounded-md p-3">
                      <h3 class="text-xs font-semibold text-topo-text-primary mb-2">Estimated Cost</h3>
                      <div class="space-y-1.5">
                        <For each={cost().hosts.filter(h => !h.tierProfileId)}>
                          {(h) => (
                            <div class="text-xs flex justify-between">
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
                        <span class="text-topo-text-primary">Infrastructure</span>
                        <span class="text-topo-brand">
                          ${cost().hosts.filter(h => !h.tierProfileId).reduce((s, h) => s + h.pricePerMonth, 0)}/mo
                        </span>
                      </div>
                      <Show when={cost().hosts.some(h => !!h.tierProfileId)}>
                        <div class="text-[10px] text-topo-text-muted mt-2">
                          + compute pools (scale with instances)
                        </div>
                      </Show>
                    </div>
                  </div>
                )}
              </Show>
            </div>
          </Show>

          {/* Step 2.5: Validate */}
          <Show when={step() === 'validate'}>
            <div class="space-y-4">
              <h3 class="text-sm font-semibold text-topo-text-primary">Topology Validation</h3>
              <Show when={validationResult()} fallback={
                <div class="text-sm text-topo-text-secondary">Running validation...</div>
              }>
                {(vr) => (
                  <>
                    <Show when={vr().canDeploy}>
                      <div class="flex items-center gap-2 p-3 rounded-lg bg-emerald-500/10 border border-emerald-500/20">
                        <svg class="w-4 h-4 text-emerald-400 shrink-0" viewBox="0 0 20 20" fill="currentColor">
                          <path fill-rule="evenodd" d="M16.707 5.293a1 1 0 010 1.414l-8 8a1 1 0 01-1.414 0l-4-4a1 1 0 011.414-1.414L8 12.586l7.293-7.293a1 1 0 011.414 0z" clip-rule="evenodd" />
                        </svg>
                        <span class="text-emerald-400 text-sm font-medium">Topology is valid and ready to deploy.</span>
                        <Show when={vr().items.filter((i: ValidationItem) => i.severity === 'Warning').length > 0}>
                          <span class="text-amber-400 text-xs">
                            ({vr().items.filter((i: ValidationItem) => i.severity === 'Warning').length} warning{vr().items.filter((i: ValidationItem) => i.severity === 'Warning').length !== 1 ? 's' : ''})
                          </span>
                        </Show>
                      </div>
                    </Show>
                    <Show when={!vr().canDeploy}>
                      <div class="p-3 rounded-lg bg-red-500/10 border border-red-500/20">
                        <div class="text-red-400 text-sm font-medium">
                          {vr().errors.length} error{vr().errors.length !== 1 ? 's' : ''} must be fixed before deployment
                        </div>
                      </div>
                    </Show>
                    <Show when={vr().items.length > 0}>
                      <div class="space-y-1.5">
                        <For each={vr().items}>
                          {(item: ValidationItem) => (
                            <div class={`p-2.5 rounded border text-xs flex gap-2 items-start ${
                              item.severity === 'Error'
                                ? 'bg-red-500/10 border-red-500/20 text-red-400'
                                : 'bg-amber-500/10 border-amber-500/20 text-amber-400'
                            }`}>
                              <span class="font-bold shrink-0 uppercase text-[10px] mt-px">{item.severity}</span>
                              <span>{item.message}</span>
                            </div>
                          )}
                        </For>
                      </div>
                    </Show>
                    <div class="flex gap-2 justify-end pt-2">
                      <button
                        class="px-4 py-1.5 rounded text-sm text-topo-text-secondary hover:text-topo-text-primary transition-colors"
                        onClick={() => setStep('configure')}
                      >Back</button>
                      <Show when={!vr().canDeploy}>
                        <button
                          class="px-4 py-1.5 rounded text-sm font-medium bg-topo-brand/20 text-topo-brand hover:bg-topo-brand/30 transition-colors"
                          disabled={loading()}
                          onClick={handleRevalidate}
                        >
                          {loading() ? 'Validating...' : 'Re-validate'}
                        </button>
                      </Show>
                      <button
                        class={`px-4 py-1.5 rounded text-sm font-medium transition-colors ${
                          vr().canDeploy
                            ? 'bg-topo-brand text-white hover:bg-topo-brand/90'
                            : 'bg-topo-text-muted/20 text-topo-text-muted cursor-not-allowed'
                        }`}
                        disabled={!vr().canDeploy || loading()}
                        onClick={handleValidateNext}
                      >
                        {loading() ? 'Loading...' : vr().canDeploy ? 'Continue' : 'Fix errors to continue'}
                      </button>
                    </div>
                  </>
                )}
              </Show>
            </div>
          </Show>

          {/* Step 3: Hosting */}
          <Show when={step() === 'hosting'}>
            <div class="space-y-5">
              <p class="text-xs text-topo-text-muted">
                Infrastructure cost breakdown and compute pool configuration.
              </p>

              {/* Section 1: Infrastructure Costs */}
              <Show when={(hostingOptions()?.infraImages?.length ?? 0) > 0}>
                <div class="space-y-2">
                  <h3 class="text-xs font-semibold text-topo-text-primary uppercase tracking-wide">Infrastructure</h3>
                  <div class="bg-topo-bg-secondary rounded-lg border border-topo-border overflow-hidden">
                    <table class="w-full text-xs">
                      <thead>
                        <tr class="text-topo-text-muted border-b border-topo-border bg-topo-bg-tertiary/50">
                          <th class="text-left py-1.5 px-3 font-medium">Image</th>
                          <th class="text-right py-1.5 px-3 font-medium">RAM</th>
                          <th class="text-left py-1.5 px-3 font-medium">Plan</th>
                          <th class="text-right py-1.5 px-3 font-medium">Storage</th>
                          <th class="text-right py-1.5 px-3 font-medium">$/mo</th>
                          <th class="text-right py-1.5 px-3 font-medium">Replicas</th>
                          <th class="text-right py-1.5 px-3 font-medium">Cost Range</th>
                        </tr>
                      </thead>
                      <tbody>
                        <For each={hostingOptions()?.infraImages ?? []}>
                          {(img) => {
                            const sel = () => infraSelections()[img.imageName];
                            const selectedPlan = () => img.availablePlans.find(p => p.planId === sel()?.planId) ?? img.availablePlans[0];
                            const plan = () => selectedPlan();
                            const price = () => plan()?.priceMonthly ?? img.priceMonthly;
                            const disk = () => plan()?.diskGb ?? img.diskGb;
                            const minCost = () => price() * img.minReplicas;
                            const maxCost = () => price() * img.maxReplicas;

                            return (<>
                              <tr class="border-b border-topo-border/50">
                                <td class="py-1.5 px-3">
                                  <span class="text-topo-text-primary font-medium">{img.imageName}</span>
                                  <span class="text-topo-text-muted ml-1.5 text-[10px]">{img.imageKind}</span>
                                </td>
                                <td class="py-1.5 px-3 text-right text-topo-text-secondary">
                                  {img.ramMb >= 1024 ? `${(img.ramMb / 1024).toFixed(0)} GB` : `${img.ramMb} MB`}
                                </td>
                                <td class="py-1.5 px-3">
                                  <Show when={img.availablePlans.length > 1} fallback={
                                    <span class="text-topo-text-secondary">{img.planLabel}</span>
                                  }>
                                    <select
                                      class="bg-topo-bg-tertiary text-topo-text-primary text-xs rounded border border-topo-border px-1.5 py-0.5 w-full"
                                      value={sel()?.planId ?? img.planId}
                                      onChange={(e) => {
                                        setInfraSelections(prev => ({
                                          ...prev,
                                          [img.imageName]: { imageName: img.imageName, planId: e.currentTarget.value },
                                        }));
                                      }}
                                    >
                                      <For each={img.availablePlans}>
                                        {(p) => (
                                          <option value={p.planId}>
                                            {p.planLabel} ({p.vCpus} vCPU)
                                          </option>
                                        )}
                                      </For>
                                    </select>
                                  </Show>
                                </td>
                                <td class="py-1.5 px-3 text-right text-topo-text-secondary">
                                  {disk() >= 1000 ? `${(disk() / 1000).toFixed(1)} TB` : `${disk()} GB`}
                                </td>
                                <td class="py-1.5 px-3 text-right text-topo-text-secondary">${price().toFixed(2)}</td>
                                <td class="py-1.5 px-3 text-right text-topo-text-secondary">
                                  {img.minReplicas === img.maxReplicas
                                    ? img.minReplicas
                                    : <>{img.minReplicas}{'\u2013'}{img.maxReplicas}</>}
                                </td>
                                <td class="py-1.5 px-3 text-right text-topo-text-primary font-medium">
                                  {minCost() === maxCost()
                                    ? <>${minCost().toFixed(2)}/mo</>
                                    : <>${minCost().toFixed(2)}{'\u2013'}${maxCost().toFixed(2)}/mo</>}
                                </td>
                              </tr>
                              <Show when={img.services?.length}>
                                <tr class="border-b border-topo-border/30">
                                  <td colSpan={7} class="px-2 py-1">
                                    <details class="group">
                                      <summary class="text-[10px] text-topo-text-secondary cursor-pointer hover:text-topo-brand">
                                        Services ({img.services!.length})
                                      </summary>
                                      <div class="pl-4 py-1 space-y-0.5">
                                        <For each={img.services!}>
                                          {(svc) => (
                                            <div class="flex justify-between text-[10px] text-topo-text-secondary">
                                              <span>{svc.name} <span class="opacity-50">({svc.kind})</span></span>
                                              <span>{svc.ramMb >= 1024 ? `${(svc.ramMb / 1024).toFixed(1)} GB` : `${svc.ramMb} MB`}</span>
                                            </div>
                                          )}
                                        </For>
                                      </div>
                                    </details>
                                  </td>
                                </tr>
                              </Show>
                            </>);
                          }}
                        </For>
                      </tbody>
                      <tfoot>
                        <tr class="bg-topo-bg-tertiary/30">
                          <td colSpan={6} class="py-1.5 px-3 text-right text-xs text-topo-text-muted font-medium">Total</td>
                          <td class="py-1.5 px-3 text-right text-xs text-topo-brand font-semibold">
                            {(() => {
                              const imgs = hostingOptions()?.infraImages ?? [];
                              const sels = infraSelections();
                              let minTotal = 0;
                              let maxTotal = 0;
                              for (const img of imgs) {
                                const sel = sels[img.imageName];
                                const plan = sel ? img.availablePlans.find(p => p.planId === sel.planId) : undefined;
                                const price = plan?.priceMonthly ?? img.priceMonthly;
                                minTotal += price * img.minReplicas;
                                maxTotal += price * img.maxReplicas;
                              }
                              return minTotal === maxTotal
                                ? <>${minTotal.toFixed(2)}/mo</>
                                : <>${minTotal.toFixed(2)}{'\u2013'}${maxTotal.toFixed(2)}/mo</>;
                            })()}
                          </td>
                        </tr>
                      </tfoot>
                    </table>
                  </div>
                </div>
              </Show>

              {/* Section 2: Pools */}
              <Show when={(hostingOptions()?.pools?.length ?? 0) > 0}>
                <div class="space-y-2">
                  <h3 class="text-xs font-semibold text-topo-text-primary uppercase tracking-wide">Pools</h3>
                  <p class="text-[11px] text-topo-text-muted bg-amber-500/5 border border-amber-500/20 rounded px-2.5 py-1.5">
                    Pool infrastructure is deferred — created when the hub provisions the first tenant or data service.
                  </p>
                </div>

                <For each={hostingOptions()?.pools ?? []}>
                  {(pool) => {
                    const poolKey = () => pool.tierProfileId ? `${pool.poolName}_${pool.tierProfileId}` : pool.poolName;
                    const sel = () => poolSelections()[poolKey()];
                    const hasTenants = () => pool.options.some(o => o.tenantsPerHost > 0);
                    const selectedOption = () => pool.options.find(o => o.planId === sel()?.planId);
                    const hostsRequired = () => {
                      const s = sel();
                      const opt = selectedOption();
                      if (!s || !opt || !opt.tenantsPerHost || !s.targetTenants || s.targetTenants < 1) return 0;
                      return Math.ceil(s.targetTenants / opt.tenantsPerHost);
                    };

                    return (
                      <div class="bg-topo-bg-secondary rounded-lg border border-topo-border p-4 space-y-3">
                        <div class="flex items-center gap-2">
                          <span class="text-sm font-semibold text-topo-text-primary">{pool.poolName}</span>
                          <Show when={pool.tierProfileName}>
                            <span class="text-[10px] px-1.5 py-0.5 rounded bg-topo-brand/10 text-topo-brand">
                              {pool.tierProfileName}
                            </span>
                          </Show>
                        </div>

                        <Show when={pool.options.length > 0} fallback={
                          <p class="text-xs text-red-400 italic">No viable plans for this pool.</p>
                        }>
                          <div class="overflow-x-auto">
                            <table class="w-full text-xs">
                              <thead>
                                <tr class="text-topo-text-muted border-b border-topo-border">
                                  <th class="text-left py-1.5 pr-3 font-medium w-6"></th>
                                  <th class="text-left py-1.5 pr-3 font-medium">Plan</th>
                                  <th class="text-right py-1.5 pr-3 font-medium">RAM</th>
                                  <th class="text-right py-1.5 pr-3 font-medium">Storage</th>
                                  <th class="text-right py-1.5 pr-3 font-medium">vCPUs</th>
                                  <th class="text-right py-1.5 pr-3 font-medium">$/mo</th>
                                  <Show when={hasTenants()}>
                                    <th class="text-right py-1.5 pr-3 font-medium">Tenants/Host</th>
                                    <th class="text-right py-1.5 font-medium">$/Tenant</th>
                                  </Show>
                                </tr>
                              </thead>
                              <tbody>
                                <For each={pool.options}>
                                  {(option) => {
                                    const isSelected = () => sel()?.planId === option.planId;
                                    return (
                                      <tr
                                        class={`border-b border-topo-border/50 cursor-pointer transition-colors ${
                                          isSelected() ? 'bg-topo-brand/10' : 'hover:bg-topo-bg-tertiary'
                                        }`}
                                        onClick={() => {
                                          const key = poolKey();
                                          setPoolSelections(prev => ({
                                            ...prev,
                                            [key]: {
                                              ...prev[key],
                                              poolName: pool.poolName,
                                              planId: option.planId,
                                              tierProfileId: pool.tierProfileId || undefined,
                                            },
                                          }));
                                        }}
                                      >
                                        <td class="py-1.5 pr-3">
                                          <div class={`w-3 h-3 rounded-full border-2 flex items-center justify-center ${
                                            isSelected() ? 'border-topo-brand' : 'border-topo-text-muted/40'
                                          }`}>
                                            <Show when={isSelected()}>
                                              <div class="w-1.5 h-1.5 rounded-full bg-topo-brand" />
                                            </Show>
                                          </div>
                                        </td>
                                        <td class="py-1.5 pr-3 text-topo-text-primary font-medium">{option.planLabel}</td>
                                        <td class="py-1.5 pr-3 text-right text-topo-text-secondary">
                                          {option.memoryMb >= 1024 ? `${(option.memoryMb / 1024).toFixed(0)} GB` : `${option.memoryMb} MB`}
                                        </td>
                                        <td class="py-1.5 pr-3 text-right text-topo-text-secondary">
                                          {option.diskGb >= 1000 ? `${(option.diskGb / 1000).toFixed(1)} TB` : `${option.diskGb} GB`}
                                        </td>
                                        <td class="py-1.5 pr-3 text-right text-topo-text-secondary">{option.vCpus}</td>
                                        <td class="py-1.5 pr-3 text-right text-topo-text-secondary">${option.priceMonthly.toFixed(2)}</td>
                                        <Show when={hasTenants()}>
                                          <td class="py-1.5 pr-3 text-right text-topo-text-secondary">{option.tenantsPerHost}</td>
                                          <td class="py-1.5 text-right text-topo-text-secondary">${option.costPerTenant}</td>
                                        </Show>
                                      </tr>
                                    );
                                  }}
                                </For>
                              </tbody>
                            </table>
                          </div>

                          <Show when={hasTenants()}>
                            <div class="flex items-center gap-4 pt-1">
                              <label class="flex items-center gap-2 text-xs text-topo-text-muted">
                                Estimate tenants
                                <input
                                  type="number"
                                  min="0"
                                  class="w-20 px-2 py-1 text-xs rounded border border-topo-border bg-topo-bg-primary text-topo-text-primary focus:border-topo-brand focus:outline-none"
                                  value={sel()?.targetTenants ?? ''}
                                  onInput={(e) => {
                                    const val = parseInt(e.currentTarget.value, 10);
                                    const key = poolKey();
                                    setPoolSelections(prev => ({
                                      ...prev,
                                      [key]: {
                                        ...prev[key],
                                        poolName: pool.poolName,
                                        targetTenants: isNaN(val) ? 0 : val,
                                        tierProfileId: pool.tierProfileId || undefined,
                                      },
                                    }));
                                  }}
                                />
                              </label>
                              <Show when={hostsRequired() > 0}>
                                <span class="text-xs text-topo-text-muted">
                                  = <span class="text-topo-brand font-semibold">{hostsRequired()}</span>
                                  {' '}{hostsRequired() === 1 ? 'host' : 'hosts'}
                                </span>
                              </Show>
                            </div>
                          </Show>
                        </Show>
                      </div>
                    );
                  }}
                </For>
              </Show>

              {renderNav(
                <button
                  class="px-3 py-1 text-xs rounded font-medium bg-topo-brand text-white hover:bg-topo-brand-hover disabled:opacity-50"
                  disabled={loading()}
                  onClick={handleHostingNext}
                >
                  {loading() ? 'Generating...' : 'Next'}
                </button>
              )}
            </div>
          </Show>

          {/* Step 3: Review */}
          <Show when={step() === 'review'}>
            <div class="flex gap-6 min-w-0">
              <div class="flex-1 min-w-0 space-y-4">
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
                  <Show when={topo.topology.lastDeployedAt}>
                    <div class="mt-1 text-topo-text-muted">
                      Last deployed: {formatRelativeTime(topo.topology.lastDeployedAt!)} ({topo.topology.lastDeployStatus === 'Succeeded' ? 'succeeded' : 'failed'})
                    </div>
                  </Show>
                </div>

                {/* Resource summary */}
                <div>
                  <h3 class="text-xs font-semibold text-topo-text-primary mb-2">Resources</h3>
                  <div class="text-xs text-topo-text-muted mb-2">
                    {hclResourceCount()} resources across {Object.keys(hclFiles()).length} files
                  </div>
                  <Show when={hclResourceCount() > 0}>
                    <div class="space-y-0.5">
                      <For each={[...hclResources().entries()]}>
                        {([type, names]) => (
                          <div class="flex items-center gap-2 text-xs">
                            <span class="text-topo-brand font-mono">{type}</span>
                            <span class="text-topo-text-secondary">
                              {names.length === 1 ? names[0] : `\u00d7${names.length}`}
                            </span>
                          </div>
                        )}
                      </For>
                    </div>
                  </Show>
                </div>

                {/* Service key status */}
                <Show when={serviceKeySchema().length > 0}>
                  <div>
                    <h3 class="text-xs font-semibold text-topo-text-primary mb-2">Service Keys</h3>
                    <div class="space-y-1">
                      <For each={serviceKeySchema()}>
                        {(field) => {
                          const isSet = () => {
                            const onServer = serviceKeyStatus()?.setVariables?.includes(field.key) ?? false;
                            const hasLocal = (serviceKeyValues()[field.key] ?? '').trim().length > 0;
                            return onServer || hasLocal;
                          };
                          return (
                            <div class="flex items-center gap-2 text-xs">
                              <Show when={isSet()} fallback={
                                <Show when={field.required} fallback={
                                  <span class="text-topo-text-muted">&#8212;</span>
                                }>
                                  <span class="text-topo-error">&#10007;</span>
                                </Show>
                              }>
                                <span class="text-topo-success">&#10003;</span>
                              </Show>
                              <span class={isSet() ? 'text-topo-text-secondary' : field.required ? 'text-topo-error' : 'text-topo-text-muted'}>
                                {field.label}
                                <Show when={!field.required}>
                                  <span class="text-topo-text-muted ml-1">(optional)</span>
                                </Show>
                              </span>
                            </div>
                          );
                        }}
                      </For>
                    </div>
                  </div>
                </Show>

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
                          <For each={cost().hosts.filter(h => !h.tierProfileId)}>
                            {(h) => (<>
                              <tr class="border-b border-topo-border/50">
                                <td class="py-1 px-2 text-topo-text-secondary">{h.hostName}</td>
                                <td class="py-1 px-2 text-topo-text-muted">{h.planLabel}</td>
                                <td class="py-1 px-2 text-topo-text-muted">{h.ramMb}MB</td>
                                <td class="py-1 px-2 text-topo-text-muted">{h.count}</td>
                                <td class="py-1 px-2 text-topo-text-secondary text-right">${h.pricePerMonth}</td>
                              </tr>
                              <Show when={h.services?.length}>
                                <tr class="border-b border-topo-border/30">
                                  <td colSpan={5} class="px-2 py-1">
                                    <details class="group">
                                      <summary class="text-[10px] text-topo-text-secondary cursor-pointer hover:text-topo-brand">
                                        Services ({h.services!.length})
                                      </summary>
                                      <div class="pl-4 py-1 space-y-0.5">
                                        <For each={h.services!}>
                                          {(svc) => (
                                            <div class="flex justify-between text-[10px] text-topo-text-secondary">
                                              <span>{svc.name} <span class="opacity-50">({svc.kind})</span></span>
                                              <span>{svc.ramMb >= 1024 ? `${(svc.ramMb / 1024).toFixed(1)} GB` : `${svc.ramMb} MB`}</span>
                                            </div>
                                          )}
                                        </For>
                                      </div>
                                    </details>
                                  </td>
                                </tr>
                              </Show>
                            </>)}
                          </For>
                          <tr>
                            <td colspan="4" class="py-1.5 px-2 text-topo-text-primary font-semibold">Infrastructure Total</td>
                            <td class="py-1.5 px-2 text-topo-brand font-semibold text-right">
                              ${cost().hosts.filter(h => !h.tierProfileId).reduce((s, h) => s + h.pricePerMonth, 0).toFixed(2)}/mo
                            </td>
                          </tr>
                          <Show when={cost().hosts.some(h => !!h.tierProfileId)}>
                            <tr>
                              <td colspan="5" class="pt-3 pb-1 px-2">
                                <span class="text-[10px] font-semibold text-topo-text-primary uppercase tracking-wide">Compute Pools</span>
                                <span class="text-[10px] text-topo-text-muted ml-2">costs scale with provisioned instances</span>
                              </td>
                            </tr>
                            <For each={cost().hosts.filter(h => !!h.tierProfileId)}>
                              {(h) => (
                                <tr class="border-b border-topo-border/50">
                                  <td class="py-1 px-2 text-topo-text-secondary">{h.hostName}</td>
                                  <td class="py-1 px-2 text-topo-text-muted">{h.planLabel}</td>
                                  <td class="py-1 px-2 text-topo-text-muted">{h.ramMb}MB</td>
                                  <td class="py-1 px-2 text-topo-text-muted">{h.tenantsPerHost} tenants/host</td>
                                  <td class="py-1 px-2 text-topo-text-muted text-right">${h.pricePerMonth}/host</td>
                                </tr>
                              )}
                            </For>
                          </Show>
                        </tbody>
                      </table>
                    </div>
                  )}
                </Show>

                {/* HCL file preview */}
                <details class="group">
                  <summary class="text-xs font-semibold text-topo-text-primary cursor-pointer hover:text-topo-brand">
                    <span class="inline-flex items-center gap-2">
                      HCL Files ({Object.keys(hclFiles()).length})
                      <button
                        class="text-[10px] text-topo-text-secondary hover:text-topo-brand"
                        onClick={(e) => {
                          e.preventDefault();
                          const all = Object.entries(hclFiles())
                            .map(([name, content]) => `// === ${name} ===\n${content}`)
                            .join('\n\n');
                          navigator.clipboard.writeText(all);
                        }}
                      >
                        copy
                      </button>
                    </span>
                  </summary>
                  <div class="mt-2 space-y-3 max-h-96 overflow-y-auto">
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

                {renderNav(<>
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
                    disabled={loading() || missingRequiredServiceKeys()}
                  >
                    {deployMode() === 'update' ? 'Update' : 'Deploy'}
                  </button>
                </>)}
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

              {renderNav(
                <button
                  class="px-3 py-1 text-xs rounded bg-topo-brand hover:bg-topo-brand-hover text-white font-medium disabled:opacity-30"
                  onClick={handleDeployWithMigration}
                  disabled={loading() || !allRequiredAnswered()}
                >
                  {loading() ? 'Generating plan...' : 'Deploy with Migration'}
                </button>
              )}
            </div>
          </Show>

          {/* Step 5: Execute */}
          <Show when={step() === 'execute'}>
            <div class="flex flex-col h-[calc(100vh-10rem)]">
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
      </div>

    </div>
  );
};

export default DeployWizard;

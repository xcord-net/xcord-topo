import { createRoot } from 'solid-js';
import { createStore, produce } from 'solid-js/store';
import type { DeployStep, DeployMode, PoolSelection, InfraSelection } from '../types/deploy';

export interface DeployWizardState {
  /** Current wizard step */
  step: DeployStep;
  /** Selected provider key */
  provider: string;
  /** Deploy mode */
  deployMode: DeployMode;
  /** Non-sensitive credential values (keyed by provider → field key → value) */
  providerValues: Record<string, Record<string, string>>;
  /** Pool hosting selections */
  poolSelections: Record<string, PoolSelection>;
  /** Infrastructure image plan selections (keyed by imageName) */
  infraSelections: Record<string, InfraSelection>;
  /** Non-sensitive service key values */
  serviceKeyValues: Record<string, string>;
  /** ID of the topology this wizard state belongs to */
  topologyId: string;
}

const STORAGE_KEY = 'xcord-topo:deploy-wizard';
let saveTimer: ReturnType<typeof setTimeout> | null = null;

function createEmptyState(): DeployWizardState {
  return {
    step: 'provider',
    provider: '',
    deployMode: 'fresh',
    providerValues: {},
    poolSelections: {},
    infraSelections: {},
    serviceKeyValues: {},
    topologyId: '',
  };
}

function loadFromStorage(): DeployWizardState {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (raw) {
      const parsed = JSON.parse(raw) as DeployWizardState;
      // Ensure all fields exist (backfill for older saved state)
      if (!parsed.providerValues) parsed.providerValues = {};
      if (!parsed.poolSelections) parsed.poolSelections = {};
      if (!parsed.infraSelections) parsed.infraSelections = {};
      if (!parsed.serviceKeyValues) parsed.serviceKeyValues = {};
      return parsed;
    }
  } catch { /* ignore corrupt data */ }
  return createEmptyState();
}

function saveToStorage(state: DeployWizardState): void {
  if (saveTimer) clearTimeout(saveTimer);
  saveTimer = setTimeout(() => {
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(state));
    } catch { /* quota exceeded, ignore */ }
  }, 300);
}

const store = createRoot(() => {
  const [state, setState] = createStore<DeployWizardState>(loadFromStorage());

  const update: typeof setState = ((...args: any[]) => {
    (setState as any)(...args);
    saveToStorage(JSON.parse(JSON.stringify(state)));
  }) as any;

  return { state, setState: update };
});

export function useDeployWizardStore() {
  return {
    get state() { return store.state; },

    setStep(step: DeployStep): void {
      store.setState(produce(s => { s.step = step; }));
    },

    setProvider(provider: string): void {
      store.setState(produce(s => { s.provider = provider; }));
    },

    setDeployMode(mode: DeployMode): void {
      store.setState(produce(s => { s.deployMode = mode; }));
    },

    setProviderValues(providerKey: string, values: Record<string, string>): void {
      store.setState(produce(s => {
        s.providerValues[providerKey] = { ...values };
      }));
    },

    setAllProviderValues(values: Record<string, Record<string, string>>): void {
      store.setState(produce(s => {
        s.providerValues = { ...values };
      }));
    },

    setPoolSelections(selections: Record<string, PoolSelection>): void {
      store.setState(produce(s => {
        s.poolSelections = { ...selections };
      }));
    },

    setInfraSelections(selections: Record<string, InfraSelection>): void {
      store.setState(produce(s => {
        s.infraSelections = { ...selections };
      }));
    },

    setServiceKeyValues(values: Record<string, string>): void {
      store.setState(produce(s => {
        s.serviceKeyValues = { ...values };
      }));
    },

    setTopologyId(id: string): void {
      store.setState(produce(s => { s.topologyId = id; }));
    },

    /** Reset wizard state (e.g. when topology changes) */
    reset(): void {
      store.setState(produce(s => {
        Object.assign(s, createEmptyState());
      }));
    },
  };
}

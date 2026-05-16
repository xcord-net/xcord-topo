import { createRoot } from 'solid-js';
import { produce } from 'solid-js/store';
import type { DeployStep, DeployMode, PoolSelection, InfraSelection } from '../types/deploy';
import { createPersistentStore } from './createPersistentStore';

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
  /** Selected version per application image kind (e.g. { "HubServer": "v0.1.5" }) */
  imageVersions: Record<string, string>;
  /** ID of the topology this wizard state belongs to */
  topologyId: string;
}

const STORAGE_KEY = 'xcord-topo:deploy-wizard';

function createEmptyState(): DeployWizardState {
  return {
    step: 'provider',
    provider: '',
    deployMode: 'fresh',
    providerValues: {},
    poolSelections: {},
    infraSelections: {},
    serviceKeyValues: {},
    imageVersions: {},
    topologyId: '',
  };
}

/** Backfill missing fields for state saved by older versions of the wizard. */
function migrateState(loaded: DeployWizardState): DeployWizardState {
  if (!loaded.providerValues) loaded.providerValues = {};
  if (!loaded.poolSelections) loaded.poolSelections = {};
  if (!loaded.infraSelections) loaded.infraSelections = {};
  if (!loaded.serviceKeyValues) loaded.serviceKeyValues = {};
  if (!loaded.imageVersions) loaded.imageVersions = {};
  return loaded;
}

const store = createRoot(() => {
  const [state, setState] = createPersistentStore<DeployWizardState>({
    key: STORAGE_KEY,
    initial: createEmptyState,
    migrate: migrateState,
  });

  return { state, setState };
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

    setImageVersions(versions: Record<string, string>): void {
      store.setState(produce(s => {
        s.imageVersions = { ...versions };
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

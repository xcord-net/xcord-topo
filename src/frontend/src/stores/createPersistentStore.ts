import { createStore, type SetStoreFunction } from 'solid-js/store';

export interface PersistentStoreOptions<T extends object> {
  /** localStorage key used for persistence */
  key: string;
  /** Factory producing the initial state when nothing is persisted yet */
  initial: () => T;
  /** Optional migration applied to data loaded from localStorage before use */
  migrate?: (loaded: T) => T;
  /** Debounce window (ms) for writes back to localStorage. Defaults to 300ms. */
  debounceMs?: number;
}

/**
 * Wrap a SolidJS `createStore` with localStorage persistence. Returns the
 * familiar `[state, setState]` tuple; `setState` is fully typed (same shape as
 * the underlying `SetStoreFunction<T>`) and transparently persists each
 * mutation, replacing the brittle `as any` cast pattern that previously
 * appeared in `topology.store.ts` and `deploy-wizard.store.ts`.
 */
export function createPersistentStore<T extends object>(
  options: PersistentStoreOptions<T>,
): [T, SetStoreFunction<T>] {
  const { key, initial, migrate, debounceMs = 300 } = options;

  const load = (): T => {
    try {
      const raw = localStorage.getItem(key);
      if (raw) {
        const parsed = JSON.parse(raw) as T;
        return migrate ? migrate(parsed) : parsed;
      }
    } catch {
      // Corrupt JSON in storage - fall through to a fresh initial state.
    }
    return initial();
  };

  const [state, setState] = createStore<T>(load());

  let saveTimer: ReturnType<typeof setTimeout> | null = null;
  const save = () => {
    if (saveTimer) clearTimeout(saveTimer);
    saveTimer = setTimeout(() => {
      try {
        localStorage.setItem(key, JSON.stringify(state));
      } catch {
        // Quota exceeded or storage unavailable - drop the write silently;
        // the in-memory state remains authoritative for the session.
      }
    }, debounceMs);
  };

  // Fully-typed wrapper preserving the SetStoreFunction overload signature.
  const persistentSetState: SetStoreFunction<T> = ((...args: unknown[]) => {
    (setState as (...a: unknown[]) => void)(...args);
    save();
  }) as SetStoreFunction<T>;

  return [state, persistentSetState];
}

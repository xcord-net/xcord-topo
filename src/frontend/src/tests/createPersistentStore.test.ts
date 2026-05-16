import { describe, it, expect, beforeEach, vi } from 'vitest';
import { createPersistentStore } from '../stores/createPersistentStore';
import { createRoot } from 'solid-js';
import { produce } from 'solid-js/store';

interface SampleState {
  count: number;
  label: string;
  nested: { value: number };
}

const KEY = 'test:persistent-store';

describe('createPersistentStore', () => {
  beforeEach(() => {
    localStorage.clear();
    vi.useFakeTimers();
  });

  it('returns the initial state when nothing is persisted', () => {
    createRoot(dispose => {
      const [state] = createPersistentStore<SampleState>({
        key: KEY,
        initial: () => ({ count: 0, label: 'init', nested: { value: 1 } }),
      });
      expect(state.count).toBe(0);
      expect(state.label).toBe('init');
      expect(state.nested.value).toBe(1);
      dispose();
    });
  });

  it('rehydrates from localStorage when present', () => {
    localStorage.setItem(KEY, JSON.stringify({ count: 42, label: 'saved', nested: { value: 7 } }));
    createRoot(dispose => {
      const [state] = createPersistentStore<SampleState>({
        key: KEY,
        initial: () => ({ count: 0, label: 'init', nested: { value: 1 } }),
      });
      expect(state.count).toBe(42);
      expect(state.label).toBe('saved');
      expect(state.nested.value).toBe(7);
      dispose();
    });
  });

  it('persists mutations through setState (debounced)', () => {
    createRoot(dispose => {
      const [, setState] = createPersistentStore<SampleState>({
        key: KEY,
        initial: () => ({ count: 0, label: 'init', nested: { value: 1 } }),
        debounceMs: 300,
      });

      setState(produce(s => { s.count = 5; }));
      // Write is debounced; nothing yet.
      expect(localStorage.getItem(KEY)).toBeNull();

      vi.advanceTimersByTime(300);
      const stored = JSON.parse(localStorage.getItem(KEY)!) as SampleState;
      expect(stored.count).toBe(5);
      dispose();
    });
  });

  it('coalesces rapid mutations into a single debounced write', () => {
    createRoot(dispose => {
      const [, setState] = createPersistentStore<SampleState>({
        key: KEY,
        initial: () => ({ count: 0, label: 'init', nested: { value: 1 } }),
      });

      setState(produce(s => { s.count = 1; }));
      setState(produce(s => { s.count = 2; }));
      setState(produce(s => { s.count = 3; }));
      vi.advanceTimersByTime(300);

      const stored = JSON.parse(localStorage.getItem(KEY)!) as SampleState;
      expect(stored.count).toBe(3);
      dispose();
    });
  });

  it('applies the migrate function before exposing loaded state', () => {
    localStorage.setItem(KEY, JSON.stringify({ count: 10, label: 'old' }));
    createRoot(dispose => {
      const [state] = createPersistentStore<SampleState>({
        key: KEY,
        initial: () => ({ count: 0, label: 'init', nested: { value: 1 } }),
        migrate: loaded => {
          if (!loaded.nested) loaded.nested = { value: 99 };
          return loaded;
        },
      });
      expect(state.count).toBe(10);
      expect(state.nested.value).toBe(99);
      dispose();
    });
  });

  it('falls back to initial when persisted data is corrupt JSON', () => {
    localStorage.setItem(KEY, '{not json');
    createRoot(dispose => {
      const [state] = createPersistentStore<SampleState>({
        key: KEY,
        initial: () => ({ count: 17, label: 'fresh', nested: { value: 1 } }),
      });
      expect(state.count).toBe(17);
      expect(state.label).toBe('fresh');
      dispose();
    });
  });

  it('survives a full round-trip across a fresh store instance', () => {
    createRoot(dispose => {
      const [, setState] = createPersistentStore<SampleState>({
        key: KEY,
        initial: () => ({ count: 0, label: 'init', nested: { value: 1 } }),
      });
      setState(produce(s => { s.count = 100; s.label = 'after'; s.nested.value = 50; }));
      vi.advanceTimersByTime(300);
      dispose();
    });

    createRoot(dispose => {
      const [state] = createPersistentStore<SampleState>({
        key: KEY,
        initial: () => ({ count: 0, label: 'init', nested: { value: 1 } }),
      });
      expect(state.count).toBe(100);
      expect(state.label).toBe('after');
      expect(state.nested.value).toBe(50);
      dispose();
    });
  });
});

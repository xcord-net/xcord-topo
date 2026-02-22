import { createRoot, createSignal } from 'solid-js';
import type { Topology } from '../types/topology';

const MAX_HISTORY = 50;

const store = createRoot(() => {
  const [undoStack, setUndoStack] = createSignal<string[]>([]);
  const [redoStack, setRedoStack] = createSignal<string[]>([]);

  return { undoStack, setUndoStack, redoStack, setRedoStack };
});

export function useHistory() {
  return {
    get canUndo() { return store.undoStack().length > 0; },
    get canRedo() { return store.redoStack().length > 0; },

    push(snapshot: Topology): void {
      const serialized = JSON.stringify(snapshot);
      store.setUndoStack(prev => {
        const next = [...prev, serialized];
        if (next.length > MAX_HISTORY) next.shift();
        return next;
      });
      store.setRedoStack([]);
    },

    undo(currentSnapshot: Topology): Topology | null {
      const stack = store.undoStack();
      if (stack.length === 0) return null;

      const previous = stack[stack.length - 1];
      store.setUndoStack(stack.slice(0, -1));
      store.setRedoStack(prev => [...prev, JSON.stringify(currentSnapshot)]);

      return JSON.parse(previous);
    },

    redo(currentSnapshot: Topology): Topology | null {
      const stack = store.redoStack();
      if (stack.length === 0) return null;

      const next = stack[stack.length - 1];
      store.setRedoStack(stack.slice(0, -1));
      store.setUndoStack(prev => [...prev, JSON.stringify(currentSnapshot)]);

      return JSON.parse(next);
    },

    clear(): void {
      store.setUndoStack([]);
      store.setRedoStack([]);
    },
  };
}

import { createRoot, createMemo } from 'solid-js';
import { useTopology } from './topology.store';
import { validateTopology, getNodeErrors } from '../lib/topology-validator';
import type { ValidationItem } from '../lib/topology-validator';

const store = createRoot(() => {
  const topo = useTopology();

  // Recompute validation whenever topology changes (reactive via Solid.js tracking)
  const items = createMemo<ValidationItem[]>(() => {
    // Access the full topology to set up tracking on all fields
    const t = topo.topology;
    // Deep-access key reactive properties so Solid tracks changes
    const _ = [t.name, t.containers.length, t.wires.length, JSON.stringify(t.containers)];
    return validateTopology(t);
  });

  const nodeErrors = createMemo(() => getNodeErrors(items()));

  const errorCount = createMemo(() => items().filter(i => i.severity === 'Error').length);
  const warningCount = createMemo(() => items().filter(i => i.severity === 'Warning').length);

  return { items, nodeErrors, errorCount, warningCount };
});

export function useValidation() {
  return {
    /** All current validation items. */
    get items() { return store.items(); },

    /** Map of nodeId -> ValidationItem[] for nodes with issues. */
    get nodeErrors() { return store.nodeErrors(); },

    /** Total error count. */
    get errorCount() { return store.errorCount(); },

    /** Total warning count. */
    get warningCount() { return store.warningCount(); },

    /** Check if a specific node has errors. */
    hasErrors(nodeId: string): boolean {
      const errs = store.nodeErrors().get(nodeId);
      return errs ? errs.some(e => e.severity === 'Error') : false;
    },

    /** Check if a specific node has warnings (but no errors). */
    hasWarnings(nodeId: string): boolean {
      const errs = store.nodeErrors().get(nodeId);
      if (!errs) return false;
      return errs.some(e => e.severity === 'Warning') && !errs.some(e => e.severity === 'Error');
    },

    /** Get error/warning count for a node. */
    nodeErrorCount(nodeId: string): number {
      return store.nodeErrors().get(nodeId)?.length ?? 0;
    },

    /** Get tooltip text for a node's issues. */
    nodeTooltip(nodeId: string): string {
      const errs = store.nodeErrors().get(nodeId);
      if (!errs) return '';
      return errs.map(e => e.message).join('\n');
    },
  };
}

import { Component, createSignal, createResource, Show, For } from 'solid-js';
import { useTopology } from '../stores/topology.store';
import { useCanvas } from '../stores/canvas.store';
import { useHistory } from '../stores/history.store';
import * as api from '../lib/serialization';

const Toolbar: Component<{ onToggleDeploy: () => void }> = (props) => {
  const topo = useTopology();
  const canvas = useCanvas();
  const history = useHistory();
  const [saving, setSaving] = createSignal(false);
  const [showTopologyList, setShowTopologyList] = createSignal(false);

  const [topologies, { refetch }] = createResource(
    () => showTopologyList(),
    async (open) => {
      if (!open) return [];
      const result = await api.fetchTopologies();
      return result.topologies;
    },
  );

  const handleSave = async () => {
    setSaving(true);
    try {
      await api.saveTopology(topo.topology);
    } catch (e) {
      console.error('Save failed:', e);
    } finally {
      setSaving(false);
    }
  };

  const handleNew = async () => {
    try {
      const topology = await api.createTopology('Untitled Topology');
      topo.load(topology);
      history.clear();
    } catch (e) {
      console.error('Create failed:', e);
    }
  };

  const handleOpen = async (id: string) => {
    try {
      const topology = await api.fetchTopology(id);
      topo.load(topology);
      history.clear();
      setShowTopologyList(false);
    } catch (e) {
      console.error('Open failed:', e);
    }
  };

  const handleDelete = async (id: string) => {
    try {
      await api.deleteTopology(id);
      refetch();
    } catch (e) {
      console.error('Delete failed:', e);
    }
  };

  const handleUndo = () => {
    const prev = history.undo(topo.getSnapshot());
    if (prev) topo.load(prev);
  };

  const handleRedo = () => {
    const next = history.redo(topo.getSnapshot());
    if (next) topo.load(next);
  };

  return (
    <div class="h-12 bg-topo-bg-secondary border-b border-topo-border flex items-center px-4 gap-2 relative">
      {/* Logo / Title */}
      <span class="text-topo-brand font-bold text-sm mr-4">xcord-topo</span>

      {/* Topology name */}
      <input
        class="bg-transparent border border-topo-border rounded px-2 py-1 text-sm text-topo-text-primary w-48 focus:outline-none focus:border-topo-brand"
        value={topo.topology.name}
        onInput={(e) => topo.updateMeta(e.currentTarget.value, topo.topology.description)}
      />

      <div class="h-6 w-px bg-topo-border mx-2" />

      {/* File actions */}
      <button
        class="px-3 py-1 text-xs rounded hover:bg-topo-bg-tertiary text-topo-text-secondary"
        onClick={handleNew}
      >
        New
      </button>
      <button
        class="px-3 py-1 text-xs rounded hover:bg-topo-bg-tertiary text-topo-text-secondary"
        onClick={handleSave}
        disabled={saving()}
      >
        {saving() ? 'Saving...' : 'Save'}
      </button>
      <div class="relative">
        <button
          class="px-3 py-1 text-xs rounded hover:bg-topo-bg-tertiary text-topo-text-secondary"
          onClick={() => setShowTopologyList(p => !p)}
        >
          Open
        </button>

        {/* Topology list dropdown */}
        <Show when={showTopologyList()}>
          <div class="absolute top-full left-0 mt-1 w-72 bg-topo-bg-secondary border border-topo-border rounded-md shadow-lg z-50 max-h-80 overflow-y-auto">
            <Show when={topologies.loading}>
              <p class="px-3 py-2 text-xs text-topo-text-muted">Loading...</p>
            </Show>
            <Show when={!topologies.loading && (topologies()?.length ?? 0) === 0}>
              <p class="px-3 py-2 text-xs text-topo-text-muted italic">No saved topologies</p>
            </Show>
            <For each={topologies()}>
              {(t) => (
                <div class="flex items-center gap-1 px-3 py-2 hover:bg-topo-bg-tertiary group">
                  <button
                    class="flex-1 text-left"
                    onClick={() => handleOpen(t.id)}
                  >
                    <div class="text-sm text-topo-text-primary">{t.name}</div>
                    <div class="text-xs text-topo-text-muted">
                      {t.containerCount} containers · {t.wireCount} wires
                    </div>
                  </button>
                  <button
                    class="text-xs text-topo-text-muted hover:text-red-400 opacity-0 group-hover:opacity-100 px-1"
                    onClick={() => handleDelete(t.id)}
                    title="Delete"
                  >
                    ✕
                  </button>
                </div>
              )}
            </For>
          </div>
        </Show>
      </div>

      <div class="h-6 w-px bg-topo-border mx-2" />

      {/* Undo/Redo */}
      <button
        class="px-2 py-1 text-xs rounded hover:bg-topo-bg-tertiary text-topo-text-secondary disabled:opacity-30"
        onClick={handleUndo}
        disabled={!history.canUndo}
        title="Undo (Ctrl+Z)"
      >
        Undo
      </button>
      <button
        class="px-2 py-1 text-xs rounded hover:bg-topo-bg-tertiary text-topo-text-secondary disabled:opacity-30"
        onClick={handleRedo}
        disabled={!history.canRedo}
        title="Redo (Ctrl+Shift+Z)"
      >
        Redo
      </button>

      <div class="h-6 w-px bg-topo-border mx-2" />

      {/* Zoom controls */}
      <button
        class="px-2 py-1 text-xs rounded hover:bg-topo-bg-tertiary text-topo-text-secondary"
        onClick={() => canvas.zoom(100, window.innerWidth / 2, window.innerHeight / 2)}
      >
        -
      </button>
      <span class="text-xs text-topo-text-muted w-12 text-center">
        {Math.round(canvas.transform.scale * 100)}%
      </span>
      <button
        class="px-2 py-1 text-xs rounded hover:bg-topo-bg-tertiary text-topo-text-secondary"
        onClick={() => canvas.zoom(-100, window.innerWidth / 2, window.innerHeight / 2)}
      >
        +
      </button>
      <button
        class="px-2 py-1 text-xs rounded hover:bg-topo-bg-tertiary text-topo-text-secondary"
        onClick={() => canvas.resetView()}
      >
        Reset
      </button>

      <div class="flex-1" />

      {/* Deploy */}
      <button
        class="px-3 py-1 text-xs rounded bg-topo-brand hover:bg-topo-brand-hover text-white font-medium"
        onClick={props.onToggleDeploy}
      >
        Deploy
      </button>

      {/* Click-away overlay to close dropdown */}
      <Show when={showTopologyList()}>
        <div
          class="fixed inset-0 z-40"
          onClick={() => setShowTopologyList(false)}
        />
      </Show>
    </div>
  );
};

export default Toolbar;

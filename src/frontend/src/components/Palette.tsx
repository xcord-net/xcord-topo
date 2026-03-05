import { Component, For } from 'solid-js';
import { useTopology } from '../stores/topology.store';
import { useHistory } from '../stores/history.store';
import { useInteraction } from '../stores/interaction.store';
import { containerDefinitions } from '../catalog/containers';
import { imageDefinitions } from '../catalog/images';
import type { Container, Image, Port } from '../types/topology';

export type PaletteTab = 'containers' | 'images' | 'source';

function createPort(template: Port): Port {
  return { ...template, id: crypto.randomUUID() };
}

const Palette: Component<{ tab: PaletteTab; onTabChange: (t: PaletteTab) => void }> = (props) => {
  const topo = useTopology();
  const history = useHistory();
  const interaction = useInteraction();

  const addContainer = (kind: string) => {
    const def = containerDefinitions.find(d => d.kind === kind);
    if (!def) return;

    history.push(topo.getSnapshot());
    const container: Container = {
      id: crypto.randomUUID(),
      name: def.label,
      kind: def.kind,
      x: 100 + Math.random() * 200,
      y: 100 + Math.random() * 200,
      width: def.defaultWidth,
      height: def.defaultHeight,
      ports: def.defaultPorts.map(createPort),
      images: [],
      children: [],
      config: {},
    };
    topo.addContainer(container);
    interaction.select(container.id);
  };

  const addImage = (kind: string) => {
    const def = imageDefinitions.find(d => d.kind === kind);
    if (!def) return;

    // Add to first selected container, or first container
    const containers = topo.topology.containers;
    if (containers.length === 0) return;

    const targetId = containers[0].id;
    history.push(topo.getSnapshot());

    const image: Image = {
      id: crypto.randomUUID(),
      name: def.label,
      kind: def.kind,
      x: 20 + Math.random() * 50,
      y: 20 + Math.random() * 50,
      width: def.defaultWidth,
      height: def.defaultHeight,
      ports: def.defaultPorts.map(createPort),
      dockerImage: def.defaultDockerImage,
      config: {},
      scaling: def.defaultScaling ?? 'Shared',
    };
    topo.addImage(targetId, image);
    interaction.select(image.id);
  };

  return (
    <div class="w-56 bg-topo-bg-secondary border-r border-topo-border flex flex-col overflow-hidden">
      <div class="flex border-b border-topo-border">
        <button
          class={`flex-1 py-2 text-xs font-medium transition-colors ${
            props.tab === 'containers' ? 'text-topo-brand border-b-2 border-topo-brand' : 'text-topo-text-muted hover:text-topo-text-secondary'
          }`}
          onClick={() => props.onTabChange('containers')}
        >
          Containers
        </button>
        <button
          class={`flex-1 py-2 text-xs font-medium transition-colors ${
            props.tab === 'images' ? 'text-topo-brand border-b-2 border-topo-brand' : 'text-topo-text-muted hover:text-topo-text-secondary'
          }`}
          onClick={() => props.onTabChange('images')}
        >
          Images
        </button>
        <button
          class={`flex-1 py-2 text-xs font-medium transition-colors ${
            props.tab === 'source' ? 'text-topo-brand border-b-2 border-topo-brand' : 'text-topo-text-muted hover:text-topo-text-secondary'
          }`}
          onClick={() => props.onTabChange('source')}
        >
          Source
        </button>
      </div>

      {props.tab !== 'source' && (
        <div class="flex-1 overflow-y-auto p-2 space-y-1">
          {props.tab === 'containers' ? (
            <For each={containerDefinitions}>
              {(def) => (
                <button
                  class="w-full text-left px-3 py-2 rounded-md text-sm hover:bg-topo-bg-tertiary transition-colors group cursor-pointer"
                  onClick={() => addContainer(def.kind)}
                >
                  <div class="flex items-center gap-2">
                    <div class="w-3 h-3 rounded-sm" style={{ background: def.color }} />
                    <span class="text-topo-text-primary group-hover:text-white">{def.label}</span>
                  </div>
                  <p class="text-xs text-topo-text-muted mt-0.5 ml-5">{def.description}</p>
                </button>
              )}
            </For>
          ) : (
            <For each={imageDefinitions}>
              {(def) => (
                <button
                  class="w-full text-left px-3 py-2 rounded-md text-sm hover:bg-topo-bg-tertiary transition-colors group cursor-pointer"
                  onClick={() => addImage(def.kind)}
                >
                  <div class="flex items-center gap-2">
                    <div class="w-3 h-3 rounded-full" style={{ background: def.color }} />
                    <span class="text-topo-text-primary group-hover:text-white">{def.label}</span>
                  </div>
                  <p class="text-xs text-topo-text-muted mt-0.5 ml-5">{def.description}</p>
                </button>
              )}
            </For>
          )}
        </div>
      )}
    </div>
  );
};

export default Palette;

import { Component, For } from 'solid-js';
import { useInteraction } from '../stores/interaction.store';
import { containerDefinitions } from '../catalog/containers';
import { imageDefinitions } from '../catalog/images';

export type PaletteTab = 'containers' | 'images' | 'source';

const Palette: Component<{ tab: PaletteTab; onTabChange: (t: PaletteTab) => void }> = (props) => {
  const interaction = useInteraction();

  const startContainerDrag = (kind: string, e: PointerEvent) => {
    e.preventDefault();
    (e.target as Element)?.setPointerCapture?.(e.pointerId);
    interaction.startDrag({
      source: { type: 'palette', itemType: 'container', kind },
      origin: { x: 0, y: 0 },
      current: { x: 0, y: 0 },
      dropTargetId: null,
    });
  };

  const startImageDrag = (kind: string, e: PointerEvent) => {
    e.preventDefault();
    (e.target as Element)?.setPointerCapture?.(e.pointerId);
    interaction.startDrag({
      source: { type: 'palette', itemType: 'image', kind },
      origin: { x: 0, y: 0 },
      current: { x: 0, y: 0 },
      dropTargetId: null,
    });
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
                  class="w-full text-left px-3 py-2 rounded-md text-sm hover:bg-topo-bg-tertiary transition-colors group cursor-grab"
                  onPointerDown={(e: PointerEvent) => startContainerDrag(def.kind, e)}
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
            <For each={imageDefinitions()}>
              {(def) => (
                <button
                  class="w-full text-left px-3 py-2 rounded-md text-sm hover:bg-topo-bg-tertiary transition-colors group cursor-grab"
                  onPointerDown={(e: PointerEvent) => startImageDrag(def.kind, e)}
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

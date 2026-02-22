import { Component, Show, For, createMemo } from 'solid-js';
import { useTopology } from '../stores/topology.store';
import { useInteraction } from '../stores/interaction.store';
import { useHistory } from '../stores/history.store';
import { containerDefinitions } from '../catalog/containers';
import { imageDefinitions } from '../catalog/images';
import type { Container, ContainerKind, Image } from '../types/topology';

function findContainerDeep(containers: Container[], id: string): Container | null {
  for (const c of containers) {
    if (c.id === id) return c;
    const found = findContainerDeep(c.children, id);
    if (found) return found;
  }
  return null;
}

function findImageDeep(containers: Container[], id: string): { image: Image; containerId: string } | null {
  for (const c of containers) {
    const img = c.images.find(i => i.id === id);
    if (img) return { image: img, containerId: c.id };
    const found = findImageDeep(c.children, id);
    if (found) return found;
  }
  return null;
}

/** Walk the topology to find the ContainerKind of the container that owns the given image */
function findParentKind(containers: Container[], imageId: string): ContainerKind | null {
  for (const c of containers) {
    if (c.images.some(i => i.id === imageId)) return c.kind;
    const found = findParentKind(c.children, imageId);
    if (found) return found;
  }
  return null;
}

const PropertiesPanel: Component = () => {
  const topo = useTopology();
  const interaction = useInteraction();
  const history = useHistory();

  const selectedContainer = createMemo(() => {
    const id = interaction.selectedNodeId;
    if (!id) return null;
    return findContainerDeep(topo.topology.containers, id);
  });

  const selectedImage = createMemo(() => {
    const id = interaction.selectedNodeId;
    if (!id) return null;
    return findImageDeep(topo.topology.containers, id);
  });

  const configFields = createMemo(() => {
    const c = selectedContainer();
    if (!c) return [];
    return containerDefinitions.find(d => d.kind === c.kind)?.configFields ?? [];
  });

  const imageConfigFields = createMemo(() => {
    const imgData = selectedImage();
    if (!imgData) return [];
    const def = imageDefinitions.find(d => d.kind === imgData.image.kind);
    if (!def?.configFields) return [];
    const parentKind = findParentKind(topo.topology.containers, imgData.image.id);
    return def.configFields.filter(f =>
      !f.parentKinds || (parentKind && f.parentKinds.includes(parentKind))
    );
  });

  const updateContainerProp = (key: keyof Container, value: string | number) => {
    const c = selectedContainer();
    if (!c) return;
    history.push(topo.getSnapshot());
    topo.updateContainer(c.id, { [key]: value } as Partial<Container>);
  };

  const updateContainerConfig = (key: string, value: string) => {
    const c = selectedContainer();
    if (!c) return;
    history.push(topo.getSnapshot());
    topo.updateContainer(c.id, { config: { ...c.config, [key]: value } });
  };

  const updateImageProp = (key: keyof Image, value: string | number) => {
    const img = selectedImage();
    if (!img) return;
    history.push(topo.getSnapshot());
    topo.updateImage(img.containerId, img.image.id, { [key]: value } as Partial<Image>);
  };

  const updateImageConfig = (key: string, value: string) => {
    const img = selectedImage();
    if (!img) return;
    history.push(topo.getSnapshot());
    topo.updateImage(img.containerId, img.image.id, { config: { ...img.image.config, [key]: value } });
  };

  return (
    <div class="w-64 bg-topo-bg-secondary border-l border-topo-border p-4 overflow-y-auto">
      <h3 class="text-xs font-semibold text-topo-text-muted uppercase tracking-wider mb-4">Properties</h3>

      <Show when={selectedContainer()}>
        {(container) => (
          <div class="space-y-3">
            <div>
              <label class="text-xs text-topo-text-muted">Name</label>
              <input
                class="w-full bg-topo-bg-tertiary border border-topo-border rounded px-2 py-1 text-sm text-topo-text-primary mt-1"
                value={container().name}
                onInput={(e) => updateContainerProp('name', e.currentTarget.value)}
              />
            </div>
            <div>
              <label class="text-xs text-topo-text-muted">Kind</label>
              <p class="text-sm text-topo-text-secondary">{container().kind}</p>
            </div>

            {/* Kind-specific config fields */}
            <Show when={configFields().length > 0}>
              <div class="border-t border-topo-border pt-3 mt-3">
                <h4 class="text-xs font-semibold text-topo-text-muted uppercase tracking-wider mb-2">Configuration</h4>
                <div class="space-y-2">
                  <For each={configFields()}>
                    {(field) => (
                      <div>
                        <label class="text-xs text-topo-text-muted">{field.label}</label>
                        <input
                          class="w-full bg-topo-bg-tertiary border border-topo-border rounded px-2 py-1 text-sm text-topo-text-primary mt-1"
                          value={container().config[field.key] ?? ''}
                          placeholder={field.placeholder}
                          onInput={(e) => updateContainerConfig(field.key, e.currentTarget.value)}
                        />
                      </div>
                    )}
                  </For>
                </div>
              </div>
            </Show>

            <div class="border-t border-topo-border pt-3 mt-3">
              <h4 class="text-xs font-semibold text-topo-text-muted uppercase tracking-wider mb-2">Layout</h4>
              <div class="grid grid-cols-2 gap-2">
                <div>
                  <label class="text-xs text-topo-text-muted">Width</label>
                  <input
                    type="number"
                    class="w-full bg-topo-bg-tertiary border border-topo-border rounded px-2 py-1 text-sm text-topo-text-primary mt-1"
                    value={container().width}
                    onInput={(e) => updateContainerProp('width', Number(e.currentTarget.value))}
                  />
                </div>
                <div>
                  <label class="text-xs text-topo-text-muted">Height</label>
                  <input
                    type="number"
                    class="w-full bg-topo-bg-tertiary border border-topo-border rounded px-2 py-1 text-sm text-topo-text-primary mt-1"
                    value={container().height}
                    onInput={(e) => updateContainerProp('height', Number(e.currentTarget.value))}
                  />
                </div>
              </div>
            </div>

            <div>
              <label class="text-xs text-topo-text-muted">Ports</label>
              <p class="text-sm text-topo-text-secondary">{container().ports.length} ports</p>
            </div>
            <div>
              <label class="text-xs text-topo-text-muted">Images</label>
              <p class="text-sm text-topo-text-secondary">{container().images.length} images</p>
            </div>
            <Show when={container().children.length > 0}>
              <div>
                <label class="text-xs text-topo-text-muted">Children</label>
                <p class="text-sm text-topo-text-secondary">{container().children.length} containers</p>
              </div>
            </Show>
          </div>
        )}
      </Show>

      <Show when={selectedImage()}>
        {(imgData) => (
          <div class="space-y-3">
            <div>
              <label class="text-xs text-topo-text-muted">Name</label>
              <input
                class="w-full bg-topo-bg-tertiary border border-topo-border rounded px-2 py-1 text-sm text-topo-text-primary mt-1"
                value={imgData().image.name}
                onInput={(e) => updateImageProp('name', e.currentTarget.value)}
              />
            </div>
            <div>
              <label class="text-xs text-topo-text-muted">Kind</label>
              <p class="text-sm text-topo-text-secondary">{imgData().image.kind}</p>
            </div>
            <div>
              <label class="text-xs text-topo-text-muted">Docker Image</label>
              <input
                class="w-full bg-topo-bg-tertiary border border-topo-border rounded px-2 py-1 text-sm text-topo-text-primary mt-1"
                value={imgData().image.dockerImage ?? ''}
                onInput={(e) => updateImageProp('dockerImage', e.currentTarget.value)}
              />
            </div>

            {/* Image config fields (filtered by parent container kind) */}
            <Show when={imageConfigFields().length > 0}>
              <div class="border-t border-topo-border pt-3 mt-3">
                <h4 class="text-xs font-semibold text-topo-text-muted uppercase tracking-wider mb-2">Configuration</h4>
                <div class="space-y-2">
                  <For each={imageConfigFields()}>
                    {(field) => (
                      <div>
                        <label class="text-xs text-topo-text-muted">{field.label}</label>
                        <input
                          class="w-full bg-topo-bg-tertiary border border-topo-border rounded px-2 py-1 text-sm text-topo-text-primary mt-1"
                          value={imgData().image.config[field.key] ?? ''}
                          placeholder={field.placeholder}
                          onInput={(e) => updateImageConfig(field.key, e.currentTarget.value)}
                        />
                      </div>
                    )}
                  </For>
                </div>
              </div>
            </Show>
          </div>
        )}
      </Show>

      <Show when={!selectedContainer() && !selectedImage()}>
        <p class="text-sm text-topo-text-muted italic">Select a node to view properties</p>
      </Show>
    </div>
  );
};

export default PropertiesPanel;

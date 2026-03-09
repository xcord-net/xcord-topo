import { Component, Show, For, createMemo, createSignal } from 'solid-js';
import type { ConfigField } from '../types/catalog';
import { useTopology } from '../stores/topology.store';
import { useInteraction } from '../stores/interaction.store';
import { useHistory } from '../stores/history.store';
import { useValidation } from '../stores/validation.store';
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

const FieldTooltip: Component<{ text: string }> = (props) => {
  const [open, setOpen] = createSignal(false);
  return (
    <span class="relative inline-block ml-1 align-middle">
      <button
        type="button"
        class="w-3.5 h-3.5 rounded-full bg-topo-text-muted/20 text-topo-text-muted hover:bg-topo-brand/20 hover:text-topo-brand text-[9px] font-bold leading-none inline-flex items-center justify-center"
        onMouseEnter={() => setOpen(true)}
        onMouseLeave={() => setOpen(false)}
        onClick={() => setOpen(v => !v)}
      >
        ?
      </button>
      <Show when={open()}>
        <div class="absolute left-5 top-0 z-50 w-52 bg-topo-bg-primary border border-topo-border rounded-lg shadow-xl p-2 text-xs text-topo-text-secondary">
          {props.text}
        </div>
      </Show>
    </span>
  );
};

const FieldLabel: Component<{ field: ConfigField }> = (props) => (
  <label class="text-xs text-topo-text-muted">
    {props.field.label}
    <Show when={props.field.tooltip}>
      <FieldTooltip text={props.field.tooltip!} />
    </Show>
  </label>
);

const PropertiesPanel: Component = () => {
  const topo = useTopology();
  const interaction = useInteraction();
  const history = useHistory();
  const validation = useValidation();

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

  const nodeErrors = createMemo(() => {
    const id = interaction.selectedNodeId;
    if (!id) return [];
    return validation.nodeErrors.get(id) ?? [];
  });

  const fieldHasError = (field: string) => {
    return nodeErrors().some(e => e.field === field && e.severity === 'Error');
  };

  const fieldHasWarning = (field: string) => {
    return nodeErrors().some(e => e.field === field && e.severity === 'Warning');
  };

  /** Errors that don't have a specific field (structural issues like missing wires). */
  const generalErrors = createMemo(() => nodeErrors().filter(e => !e.field));

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
            {/* Validation errors */}
            <Show when={nodeErrors().length > 0}>
              <div class="space-y-1">
                <For each={nodeErrors()}>
                  {(err) => (
                    <div class={`px-2 py-1.5 rounded text-[11px] leading-tight ${
                      err.severity === 'Error'
                        ? 'bg-red-500/10 border border-red-500/20 text-red-400'
                        : 'bg-amber-500/10 border border-amber-500/20 text-amber-400'
                    }`}>
                      {err.message}
                    </div>
                  )}
                </For>
              </div>
            </Show>

            <div>
              <label class="text-xs text-topo-text-muted">Name</label>
              <input
                class={`w-full bg-topo-bg-tertiary border rounded px-2 py-1 text-sm text-topo-text-primary mt-1 ${fieldHasError('name') ? 'border-red-500' : 'border-topo-border'}`}
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
                        <FieldLabel field={field} />
                        {field.type === 'select' ? (
                          <select
                            class={`w-full bg-topo-bg-tertiary border rounded px-2 py-1 text-sm text-topo-text-primary mt-1 ${fieldHasError(field.key) ? 'border-red-500' : 'border-topo-border'}`}
                            value={container().config[field.key] ?? ''}
                            onChange={(e) => updateContainerConfig(field.key, e.currentTarget.value)}
                          >
                            <For each={field.options ?? []}>
                              {(opt) => <option value={opt.value}>{opt.label}</option>}
                            </For>
                          </select>
                        ) : (
                          <input
                            class={`w-full bg-topo-bg-tertiary border rounded px-2 py-1 text-sm text-topo-text-primary mt-1 ${fieldHasError(field.key) ? 'border-red-500' : 'border-topo-border'}`}
                            value={container().config[field.key] ?? ''}
                            placeholder={field.placeholder}
                            onInput={(e) => updateContainerConfig(field.key, e.currentTarget.value)}
                          />
                        )}
                      </div>
                    )}
                  </For>
                </div>
              </div>
            </Show>

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
            {/* Validation errors */}
            <Show when={nodeErrors().length > 0}>
              <div class="space-y-1">
                <For each={nodeErrors()}>
                  {(err) => (
                    <div class={`px-2 py-1.5 rounded text-[11px] leading-tight ${
                      err.severity === 'Error'
                        ? 'bg-red-500/10 border border-red-500/20 text-red-400'
                        : 'bg-amber-500/10 border border-amber-500/20 text-amber-400'
                    }`}>
                      {err.message}
                    </div>
                  )}
                </For>
              </div>
            </Show>

            <div>
              <label class="text-xs text-topo-text-muted">Name</label>
              <input
                class={`w-full bg-topo-bg-tertiary border rounded px-2 py-1 text-sm text-topo-text-primary mt-1 ${fieldHasError('name') ? 'border-red-500' : 'border-topo-border'}`}
                value={imgData().image.name}
                onInput={(e) => updateImageProp('name', e.currentTarget.value)}
              />
            </div>
            <div>
              <label class="text-xs text-topo-text-muted">Kind</label>
              <p class="text-sm text-topo-text-secondary">{imgData().image.kind}</p>
            </div>

            {/* Image config fields (filtered by parent container kind) */}
            <Show when={imageConfigFields().length > 0}>
              <div class="border-t border-topo-border pt-3 mt-3">
                <h4 class="text-xs font-semibold text-topo-text-muted uppercase tracking-wider mb-2">Configuration</h4>
                <div class="space-y-2">
                  <For each={imageConfigFields()}>
                    {(field) => {
                      const isTopLevel = field.key === 'scaling';
                      const getValue = () => isTopLevel
                        ? (imgData().image as unknown as Record<string, unknown>)[field.key] as string ?? ''
                        : imgData().image.config[field.key] ?? '';
                      const setValue = (v: string) => isTopLevel
                        ? updateImageProp(field.key as keyof Image, v)
                        : updateImageConfig(field.key, v);
                      const error = () => field.validate?.(getValue()) ?? null;
                      const hasFieldErr = () => error() || fieldHasError(field.key);
                      return (
                        <div>
                          <FieldLabel field={field} />
                          {field.type === 'select' ? (
                            <select
                              class={`w-full bg-topo-bg-tertiary border rounded px-2 py-1 text-sm text-topo-text-primary mt-1 ${hasFieldErr() ? 'border-red-500' : 'border-topo-border'}`}
                              value={getValue()}
                              onChange={(e) => setValue(e.currentTarget.value)}
                            >
                              <For each={field.options ?? []}>
                                {(opt) => <option value={opt.value}>{opt.label}</option>}
                              </For>
                            </select>
                          ) : (
                            <input
                              class={`w-full bg-topo-bg-tertiary border rounded px-2 py-1 text-sm text-topo-text-primary mt-1 ${hasFieldErr() ? 'border-red-500' : 'border-topo-border'}`}
                              value={getValue()}
                              placeholder={field.placeholder}
                              onInput={(e) => setValue(e.currentTarget.value)}
                            />
                          )}
                          <Show when={error()}>
                            <p class="text-[10px] text-red-400 mt-0.5">{error()}</p>
                          </Show>
                        </div>
                      );
                    }}
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

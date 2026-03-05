import { createRoot } from 'solid-js';
import { createStore, produce, reconcile } from 'solid-js/store';
import type { Topology, Container, Image, Wire, Port, DeployStatus } from '../types/topology';
import { imageDefinitions } from '../catalog/images';

const HEADER_HEIGHT = 32;

function createId(): string {
  return crypto.randomUUID();
}

function createEmptyTopology(): Topology {
  return {
    id: createId(),
    name: 'Untitled Topology',
    description: undefined,
    provider: 'linode',
    providerConfig: {},
    serviceKeys: {},
    containers: [],
    wires: [],
    tierProfiles: [],
    schemaVersion: 1,
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
    deployedResourceCount: 0,
  };
}

/** Recursively find a container by id, returning [container, parent-array, index] */
function findContainerDeep(containers: Container[], id: string): { container: Container; siblings: Container[]; index: number } | null {
  for (let i = 0; i < containers.length; i++) {
    if (containers[i].id === id) return { container: containers[i], siblings: containers, index: i };
    const found = findContainerDeep(containers[i].children, id);
    if (found) return found;
  }
  return null;
}

/** Find the parent container of a child by id (works on mutable draft inside produce) */
function findParentIn(containers: Container[], childId: string): Container | null {
  for (const c of containers) {
    if (c.children.some(ch => ch.id === childId)) return c;
    const found = findParentIn(c.children, childId);
    if (found) return found;
  }
  return null;
}

/** Compute absolute canvas position of a container (for use inside produce). */
function absoluteContainerPos(containers: Container[], targetId: string, offX = 0, offY = 0): { x: number; y: number } {
  for (const c of containers) {
    const ax = offX + c.x;
    const ay = offY + c.y;
    if (c.id === targetId) return { x: ax, y: ay };
    const found = absoluteContainerPos(c.children, targetId, ax, ay + HEADER_HEIGHT);
    if (found.x !== -Infinity) return found;
  }
  return { x: -Infinity, y: -Infinity };
}

const STORAGE_KEY = 'xcord-topo:topology';
let saveTimer: ReturnType<typeof setTimeout> | null = null;

function loadFromStorage(): Topology {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (raw) {
      const parsed = JSON.parse(raw) as Topology;
      // Backfill children arrays for topologies saved before nesting support
      const backfill = (containers: Container[]) => {
        for (const c of containers) {
          if (!c.children) c.children = [];
          backfill(c.children);
        }
      };
      backfill(parsed.containers);
      // Backfill serviceKeys for topologies saved before service key support
      if (!parsed.serviceKeys) parsed.serviceKeys = {};
      // Migrate image ports to current catalog definitions (names, sides, offsets)
      const migrateImagePorts = (containers: Container[]) => {
        for (const c of containers) {
          for (const img of c.images) {
            const def = imageDefinitions.find(d => d.kind === img.kind);
            if (!def) continue;
            // Replace ports with catalog defaults, preserving IDs by position
            img.ports = def.defaultPorts.map((dp, i) => ({
              ...dp,
              id: img.ports[i]?.id || crypto.randomUUID(),
            }));
          }
          migrateImagePorts(c.children);
        }
      };
      migrateImagePorts(parsed.containers);
      return parsed;
    }
  } catch { /* ignore corrupt data */ }
  return createEmptyTopology();
}

function saveToStorage(topology: Topology): void {
  if (saveTimer) clearTimeout(saveTimer);
  saveTimer = setTimeout(() => {
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(topology));
    } catch { /* quota exceeded, ignore */ }
  }, 300);
}

const store = createRoot(() => {
  const [topology, setTopology] = createStore<Topology>(loadFromStorage());

  /** Wrapper that persists after every mutation */
  const update: typeof setTopology = ((...args: any[]) => {
    (setTopology as any)(...args);
    saveToStorage(JSON.parse(JSON.stringify(topology)));
  }) as any;

  return { topology, setTopology: update };
});

export function useTopology() {
  return {
    get topology() { return store.topology; },

    load(topology: Topology): void {
      store.setTopology(reconcile(topology));
    },

    reset(): void {
      store.setTopology(reconcile(createEmptyTopology()));
    },

    updateMeta(name: string, description?: string): void {
      store.setTopology(produce(t => {
        t.name = name;
        t.description = description;
        t.updatedAt = new Date().toISOString();
      }));
    },

    updateProvider(providerKey: string): void {
      store.setTopology(produce(t => {
        t.provider = providerKey;
        t.updatedAt = new Date().toISOString();
      }));
    },

    addContainer(container: Container): void {
      store.setTopology(produce(t => {
        t.containers.push(container);
        t.updatedAt = new Date().toISOString();
      }));
    },

    updateContainer(id: string, updates: Partial<Container>): void {
      store.setTopology(produce(t => {
        const found = findContainerDeep(t.containers, id);
        if (found) Object.assign(found.container, updates);
        t.updatedAt = new Date().toISOString();
      }));
    },

    removeContainer(id: string): void {
      store.setTopology(produce(t => {
        const found = findContainerDeep(t.containers, id);
        if (found) {
          found.siblings.splice(found.index, 1);
          // Collect all descendant IDs for wire cleanup
          const removedIds = new Set<string>();
          const collectIds = (c: Container) => {
            removedIds.add(c.id);
            c.images.forEach(img => removedIds.add(img.id));
            c.children.forEach(collectIds);
          };
          collectIds(found.container);
          t.wires = t.wires.filter(w =>
            !removedIds.has(w.fromNodeId) && !removedIds.has(w.toNodeId)
          );
        }
        t.updatedAt = new Date().toISOString();
      }));
    },

    /** Find the container that owns an image by image ID */
    findImageOwner(imageId: string): string | null {
      const search = (containers: Container[]): string | null => {
        for (const c of containers) {
          if (c.images.some(i => i.id === imageId)) return c.id;
          const found = search(c.children);
          if (found) return found;
        }
        return null;
      };
      return search(store.topology.containers);
    },

    /** Expand a container so all children/images fit, then propagate up through ancestors */
    growToFit(containerId: string): void {
      store.setTopology(produce(t => {
        const PAD = 20;
        let id: string | null = containerId;
        while (id) {
          const found = findContainerDeep(t.containers, id);
          if (!found) break;
          const c = found.container;
          let maxRight = 0;
          let maxBottom = 0;
          for (const child of c.children) {
            maxRight = Math.max(maxRight, child.x + child.width + PAD);
            maxBottom = Math.max(maxBottom, HEADER_HEIGHT + child.y + child.height + PAD);
          }
          for (const img of c.images) {
            maxRight = Math.max(maxRight, img.x + img.width + PAD);
            maxBottom = Math.max(maxBottom, HEADER_HEIGHT + img.y + img.height + PAD);
          }
          if (maxRight > c.width) c.width = maxRight;
          if (maxBottom > c.height) c.height = maxBottom;
          const parent = findParentIn(t.containers, id);
          id = parent?.id ?? null;
        }
        t.updatedAt = new Date().toISOString();
      }));
    },

    /** Resize a container to tightly fit its children/images (shrink + grow) */
    fitToContents(containerId: string): void {
      store.setTopology(produce(t => {
        const found = findContainerDeep(t.containers, containerId);
        if (!found) return;
        const parent = found.container;
        const PAD = 20;
        let maxRight = 0;
        let maxBottom = 0;

        for (const child of parent.children) {
          maxRight = Math.max(maxRight, child.x + child.width + PAD);
          maxBottom = Math.max(maxBottom, HEADER_HEIGHT + child.y + child.height + PAD);
        }
        for (const img of parent.images) {
          maxRight = Math.max(maxRight, img.x + img.width + PAD);
          maxBottom = Math.max(maxBottom, HEADER_HEIGHT + img.y + img.height + PAD);
        }

        parent.width = Math.max(200, maxRight);
        parent.height = Math.max(120, maxBottom);
        t.updatedAt = new Date().toISOString();
      }));
    },

    /** Fit all containers to their contents (recursive, bottom-up) */
    fitAllToContents(): void {
      store.setTopology(produce(t => {
        const PAD = 20;
        function fitRecursive(containers: Container[]) {
          for (const c of containers) {
            fitRecursive(c.children);
            let maxRight = 0;
            let maxBottom = 0;
            for (const child of c.children) {
              maxRight = Math.max(maxRight, child.x + child.width + PAD);
              maxBottom = Math.max(maxBottom, HEADER_HEIGHT + child.y + child.height + PAD);
            }
            for (const img of c.images) {
              maxRight = Math.max(maxRight, img.x + img.width + PAD);
              maxBottom = Math.max(maxBottom, HEADER_HEIGHT + img.y + img.height + PAD);
            }
            c.width = Math.max(200, maxRight);
            c.height = Math.max(120, maxBottom);
          }
        }
        fitRecursive(t.containers);
        t.updatedAt = new Date().toISOString();
      }));
    },

    addImage(containerId: string, image: Image): void {
      store.setTopology(produce(t => {
        const found = findContainerDeep(t.containers, containerId);
        if (found) found.container.images.push(image);
        t.updatedAt = new Date().toISOString();
      }));
    },

    updateImage(containerId: string, imageId: string, updates: Partial<Image>): void {
      store.setTopology(produce(t => {
        const found = findContainerDeep(t.containers, containerId);
        if (found) {
          const img = found.container.images.find(i => i.id === imageId);
          if (img) Object.assign(img, updates);
        }
        t.updatedAt = new Date().toISOString();
      }));
    },

    removeImage(containerId: string, imageId: string): void {
      store.setTopology(produce(t => {
        const found = findContainerDeep(t.containers, containerId);
        if (found) {
          found.container.images = found.container.images.filter(i => i.id !== imageId);
        }
        t.wires = t.wires.filter(w => w.fromNodeId !== imageId && w.toNodeId !== imageId);
        t.updatedAt = new Date().toISOString();
      }));
    },

    addWire(wire: Wire): void {
      store.setTopology(produce(t => {
        // Prevent duplicate wires
        const exists = t.wires.some(w =>
          (w.fromPortId === wire.fromPortId && w.toPortId === wire.toPortId) ||
          (w.fromPortId === wire.toPortId && w.toPortId === wire.fromPortId)
        );
        if (!exists) {
          t.wires.push(wire);
          t.updatedAt = new Date().toISOString();
        }
      }));
    },

    removeWire(wireId: string): void {
      store.setTopology(produce(t => {
        t.wires = t.wires.filter(w => w.id !== wireId);
        t.updatedAt = new Date().toISOString();
      }));
    },

    /** Batch-move nodes by delta. Handles both containers and images. */
    moveNodes(nodeIds: string[], dx: number, dy: number): void {
      store.setTopology(produce(t => {
        for (const id of nodeIds) {
          // Try as container first
          const found = findContainerDeep(t.containers, id);
          if (found) {
            found.container.x += dx;
            found.container.y += dy;
            continue;
          }
          // Try as image — search recursively through all containers
          const searchImages = (containers: Container[]): boolean => {
            for (const c of containers) {
              const img = c.images.find(i => i.id === id);
              if (img) {
                img.x += dx;
                img.y += dy;
                return true;
              }
              if (searchImages(c.children)) return true;
            }
            return false;
          };
          searchImages(t.containers);
        }
        t.updatedAt = new Date().toISOString();
      }));
    },

    /** Move an image from one container to another, converting coordinates. */
    reparentImage(imageId: string, fromContainerId: string, toContainerId: string, absoluteX: number, absoluteY: number): void {
      store.setTopology(produce(t => {
        // Find and remove from source
        const src = findContainerDeep(t.containers, fromContainerId);
        if (!src) return;
        const imgIdx = src.container.images.findIndex(i => i.id === imageId);
        if (imgIdx === -1) return;
        const [img] = src.container.images.splice(imgIdx, 1);

        // Find target and compute relative coords
        const tgt = findContainerDeep(t.containers, toContainerId);
        if (!tgt) {
          // Put it back if target not found
          src.container.images.push(img);
          return;
        }

        // Compute target's absolute content origin
        const targetAbs = absoluteContainerPos(t.containers, toContainerId);
        img.x = absoluteX - targetAbs.x;
        img.y = absoluteY - (targetAbs.y + HEADER_HEIGHT);

        tgt.container.images.push(img);
        t.updatedAt = new Date().toISOString();
      }));
    },

    /** Move a child container from one parent to another (or to top-level). */
    reparentContainer(containerId: string, newParentId: string | null, absoluteX: number, absoluteY: number): void {
      store.setTopology(produce(t => {
        const found = findContainerDeep(t.containers, containerId);
        if (!found) return;
        // Remove from current location
        const [container] = found.siblings.splice(found.index, 1);

        if (newParentId) {
          const parent = findContainerDeep(t.containers, newParentId);
          if (!parent) {
            // Restore if target not found
            found.siblings.splice(found.index, 0, container);
            return;
          }
          const parentAbs = absoluteContainerPos(t.containers, newParentId);
          container.x = absoluteX - parentAbs.x;
          container.y = absoluteY - (parentAbs.y + HEADER_HEIGHT);
          parent.container.children.push(container);
        } else {
          // Move to top level
          container.x = absoluteX;
          container.y = absoluteY;
          t.containers.push(container);
        }
        t.updatedAt = new Date().toISOString();
      }));
    },

    updateProviderConfig(config: Record<string, string>): void {
      store.setTopology(produce(t => {
        t.providerConfig = { ...config };
        t.updatedAt = new Date().toISOString();
      }));
    },

    updateServiceKeys(keys: Record<string, string>): void {
      store.setTopology(produce(t => {
        t.serviceKeys = { ...t.serviceKeys, ...keys };
        t.updatedAt = new Date().toISOString();
      }));
    },

    updateDeployStatus(status: DeployStatus | undefined, resourceCount: number): void {
      store.setTopology(produce(t => {
        t.lastDeployStatus = status;
        t.lastDeployedAt = status ? new Date().toISOString() : undefined;
        t.deployedResourceCount = resourceCount;
        t.updatedAt = new Date().toISOString();
      }));
    },

    getSnapshot(): Topology {
      return JSON.parse(JSON.stringify(store.topology));
    },
  };
}

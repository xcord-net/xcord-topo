import { createRoot } from 'solid-js';
import { createStore, produce, reconcile } from 'solid-js/store';
import type { Topology, Container, Image, Wire, Port, DeployStatus } from '../types/topology';

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

/** Get absolute position of a container accounting for parent nesting */
function getAbsolutePos(containers: Container[], id: string, offX = 0, offY = 0): { x: number; y: number } | null {
  for (const c of containers) {
    if (c.id === id) return { x: offX + c.x, y: offY + c.y };
    const found = getAbsolutePos(c.children, id, offX + c.x, offY + c.y + HEADER_HEIGHT);
    if (found) return found;
  }
  return null;
}

/** Walk all containers recursively */
function forEachContainer(containers: Container[], fn: (c: Container, absX: number, absY: number) => void, offX = 0, offY = 0): void {
  for (const c of containers) {
    const ax = offX + c.x;
    const ay = offY + c.y;
    fn(c, ax, ay);
    forEachContainer(c.children, fn, ax, ay + HEADER_HEIGHT);
  }
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

    moveContainer(id: string, x: number, y: number): void {
      store.setTopology(produce(t => {
        const found = findContainerDeep(t.containers, id);
        if (found) {
          found.container.x = x;
          found.container.y = y;
        }
      }));
    },

    resizeContainer(id: string, width: number, height: number): void {
      store.setTopology(produce(t => {
        const found = findContainerDeep(t.containers, id);
        if (found) {
          found.container.width = Math.max(200, width);
          found.container.height = Math.max(120, height);
        }
        t.updatedAt = new Date().toISOString();
      }));
    },

    /** Reparent a container as a child of another. Converts coordinates to relative. */
    reparentContainer(childId: string, parentId: string): void {
      store.setTopology(produce(t => {
        const childResult = findContainerDeep(t.containers, childId);
        const parentResult = findContainerDeep(t.containers, parentId);
        if (!childResult || !parentResult) return;
        // Don't allow nesting into self or into a descendant
        const isDescendant = (root: Container, targetId: string): boolean => {
          if (root.id === targetId) return true;
          return root.children.some(c => isDescendant(c, targetId));
        };
        if (isDescendant(childResult.container, parentId)) return;

        // Get absolute positions before reparenting
        const childAbs = getAbsolutePos(t.containers, childId)!;
        const parentAbs = getAbsolutePos(t.containers, parentId)!;

        // Remove from current location
        childResult.siblings.splice(childResult.index, 1);

        // Convert to relative coordinates (relative to parent content area)
        const child = childResult.container;
        child.x = childAbs.x - parentAbs.x;
        child.y = childAbs.y - (parentAbs.y + HEADER_HEIGHT);

        // Re-find parent (indices may have shifted)
        const newParent = findContainerDeep(t.containers, parentId);
        if (newParent) {
          newParent.container.children.push(child);
        }
        t.updatedAt = new Date().toISOString();
      }));
    },

    /** Unparent a child container back to top-level. Converts coordinates to absolute. */
    unparentContainer(childId: string): void {
      store.setTopology(produce(t => {
        const childAbs = getAbsolutePos(t.containers, childId);
        const found = findContainerDeep(t.containers, childId);
        if (!found || !childAbs) return;
        // Only unparent if it's actually nested (not already top-level)
        if (found.siblings === t.containers) return;

        found.siblings.splice(found.index, 1);
        const child = found.container;
        child.x = childAbs.x;
        child.y = childAbs.y;
        t.containers.push(child);
        t.updatedAt = new Date().toISOString();
      }));
    },

    /** Find the innermost container at an absolute canvas point (excluding a given id) */
    containerAtPoint(absX: number, absY: number, excludeId?: string): Container | null {
      let best: Container | null = null;
      let bestArea = Infinity;
      forEachContainer(store.topology.containers, (c, cx, cy) => {
        if (c.id === excludeId) return;
        if (absX >= cx && absX <= cx + c.width && absY >= cy && absY <= cy + c.height) {
          const area = c.width * c.height;
          if (area < bestArea) {
            best = c;
            bestArea = area;
          }
        }
      });
      return best;
    },

    /** Check if a container is currently nested (not top-level) */
    isNested(id: string): boolean {
      const found = findContainerDeep(store.topology.containers, id);
      return !!found && found.siblings !== store.topology.containers;
    },

    /** Get the absolute position of a container (accounting for parent nesting) */
    getAbsolutePosition(id: string): { x: number; y: number } | null {
      return getAbsolutePos(store.topology.containers, id);
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

    /** Expand a container so all children/images fit with padding */
    growToFit(parentId: string): void {
      store.setTopology(produce(t => {
        const found = findContainerDeep(t.containers, parentId);
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

        if (maxRight > parent.width) parent.width = maxRight;
        if (maxBottom > parent.height) parent.height = maxBottom;
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

    moveImage(containerId: string, imageId: string, x: number, y: number): void {
      store.setTopology(produce(t => {
        const found = findContainerDeep(t.containers, containerId);
        if (found) {
          const img = found.container.images.find(i => i.id === imageId);
          if (img) {
            img.x = x;
            img.y = y;
          }
        }
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

    updateProviderConfig(config: Record<string, string>): void {
      store.setTopology(produce(t => {
        t.providerConfig = { ...config };
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

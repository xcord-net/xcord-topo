import { Component, For, Show, onMount, onCleanup, createMemo } from 'solid-js';
import { useTopology } from '../stores/topology.store';
import { useCanvas } from '../stores/canvas.store';
import { useInteraction } from '../stores/interaction.store';
import { useHistory } from '../stores/history.store';
import { screenToCanvas } from '../lib/geometry';
import { containerDefinitions } from '../catalog/containers';
import { imageDefinitions } from '../catalog/images';
import type { Container, Image, Port, Wire as WireType } from '../types/topology';
import ContainerNode from './ContainerNode';
import ImageNode from './ImageNode';
import Wire from './Wire';
import WirePreview from './WirePreview';
import SelectionBox from './SelectionBox';
import DotGrid from './DotGrid';

function createPort(template: Port): Port {
  return { ...template, id: crypto.randomUUID() };
}

/** Deep-clone a container tree, generating fresh IDs. Returns the clone and an old→new ID map. */
function cloneContainer(c: Container): { clone: Container; idMap: Map<string, string> } {
  const idMap = new Map<string, string>();
  const cloneC = (src: Container): Container => {
    const newId = crypto.randomUUID();
    idMap.set(src.id, newId);
    return {
      ...src,
      id: newId,
      ports: src.ports.map(p => {
        const newPid = crypto.randomUUID();
        idMap.set(p.id, newPid);
        return { ...p, id: newPid };
      }),
      images: src.images.map(img => {
        const newImgId = crypto.randomUUID();
        idMap.set(img.id, newImgId);
        return {
          ...img,
          id: newImgId,
          ports: img.ports.map(p => {
            const newPid = crypto.randomUUID();
            idMap.set(p.id, newPid);
            return { ...p, id: newPid };
          }),
          config: { ...img.config },
        };
      }),
      children: src.children.map(cloneC),
      config: { ...src.config },
    };
  };
  return { clone: cloneC(c), idMap };
}

/** Deep-clone an image, generating fresh IDs. */
function cloneImage(img: Image): Image {
  return {
    ...img,
    id: crypto.randomUUID(),
    ports: img.ports.map(p => ({ ...p, id: crypto.randomUUID() })),
    config: { ...img.config },
  };
}

type Clipboard =
  | { type: 'container'; data: Container; wires: WireType[] }
  | { type: 'image'; data: Image; parentId: string };

const Canvas: Component = () => {
  let svgRef: SVGSVGElement | undefined;
  let clipboard: Clipboard | null = null;
  const topo = useTopology();
  const canvas = useCanvas();
  const interaction = useInteraction();
  const history = useHistory();

  /** Convert viewport clientX/clientY to canvas coordinates, accounting for SVG element offset */
  const clientToCanvas = (clientX: number, clientY: number) => {
    const rect = svgRef!.getBoundingClientRect();
    return screenToCanvas({ x: clientX - rect.left, y: clientY - rect.top }, canvas.transform);
  };

  /** Reorder top-level containers so the one containing the dragged element renders last (on top in SVG).
   *  Always returns a new array to avoid SolidJS <For> reconciliation issues
   *  when switching between store proxy and plain array. */
  const sortedContainers = createMemo(() => {
    const containers = [...topo.topology.containers];
    if (interaction.mode !== 'dragging') return containers;
    const dragId = interaction.selectedNodeId;
    if (!dragId) return containers;
    const containsDragged = (c: Container): boolean => {
      if (c.id === dragId) return true;
      if (c.images.some(i => i.id === dragId)) return true;
      return c.children.some(containsDragged);
    };
    return containers.sort((a, b) =>
      (containsDragged(a) ? 1 : 0) - (containsDragged(b) ? 1 : 0)
    );
  });

  /** During image drag, find the image data + parent absolute position for the overlay */
  const draggedImageOverlay = createMemo(() => {
    if (interaction.mode !== 'dragging') return null;
    const imageId = interaction.selectedNodeId;
    const parentId = interaction.dragParentId;
    if (!imageId || !parentId) return null;

    const parentAbs = topo.getAbsolutePosition(parentId);
    if (!parentAbs) return null;

    const findImage = (containers: readonly Container[]): Image | undefined => {
      for (const c of containers) {
        if (c.id === parentId) return c.images.find(i => i.id === imageId);
        const found = findImage(c.children);
        if (found) return found;
      }
      return undefined;
    };
    const image = findImage(topo.topology.containers);
    if (!image) return null;

    return { image, containerX: parentAbs.x, containerY: parentAbs.y + 32 };
  });

  const handleWheel = (e: WheelEvent) => {
    e.preventDefault();
    const rect = svgRef!.getBoundingClientRect();
    canvas.zoom(e.deltaY, e.clientX - rect.left, e.clientY - rect.top);
  };

  const handlePointerDown = (e: PointerEvent) => {
    if (e.button === 1 || (e.button === 0 && e.shiftKey && interaction.mode === 'idle')) {
      // Middle click or shift+left click = pan
      interaction.setMode('panning');
      (e.target as Element)?.setPointerCapture?.(e.pointerId);
      return;
    }

    if (e.button === 0 && interaction.mode === 'idle' && e.target === svgRef) {
      // Click on empty canvas = start selection box or deselect
      const canvasPos = clientToCanvas(e.clientX, e.clientY);
      interaction.startSelectionBox(canvasPos);
      interaction.deselect();
    }
  };

  const handlePointerMove = (e: PointerEvent) => {
    const mode = interaction.mode;

    if (mode === 'panning') {
      canvas.pan(e.movementX, e.movementY);
      return;
    }

    if (mode === 'wiring') {
      const canvasPos = clientToCanvas(e.clientX, e.clientY);
      interaction.updateWiringCursor(canvasPos);
      return;
    }

    if (mode === 'selecting') {
      const canvasPos = clientToCanvas(e.clientX, e.clientY);
      interaction.updateSelectionBox(canvasPos);
      return;
    }

    if (mode === 'dragging') {
      const canvasPos = clientToCanvas(e.clientX, e.clientY);
      const offset = interaction.dragOffset;
      const nodeId = interaction.selectedNodeId;
      const parentId = interaction.dragParentId;
      if (nodeId && parentId) {
        // Dragging an image — convert to parent-relative coordinates
        const parentAbs = topo.getAbsolutePosition(parentId);
        if (parentAbs) {
          topo.moveImage(
            parentId,
            nodeId,
            canvasPos.x - offset.x - parentAbs.x,
            canvasPos.y - offset.y - (parentAbs.y + 32),
          );
        }
        // Drop target highlight for image transfer
        const target = topo.containerAtPoint(canvasPos.x, canvasPos.y);
        interaction.setDropTarget(target && target.id !== parentId ? target.id : null);
      } else if (nodeId) {
        topo.moveContainer(nodeId, canvasPos.x - offset.x, canvasPos.y - offset.y);
        // Drop target highlight for container reparenting
        const target = topo.containerAtPoint(canvasPos.x, canvasPos.y, nodeId);
        interaction.setDropTarget(target ? target.id : null);
      }
      return;
    }

    if (mode === 'resizing') {
      const canvasPos = clientToCanvas(e.clientX, e.clientY);
      const nodeId = interaction.selectedNodeId;
      if (nodeId) {
        const absPos = topo.getAbsolutePosition(nodeId);
        if (absPos) {
          topo.resizeContainer(nodeId, canvasPos.x - absPos.x, canvasPos.y - absPos.y);
        }
      }
      return;
    }
  };

  const handlePointerUp = (e: PointerEvent) => {
    const mode = interaction.mode;

    if (mode === 'panning') {
      interaction.setMode('idle');
      return;
    }

    if (mode === 'dragging') {
      const nodeId = interaction.selectedNodeId;
      const parentId = interaction.dragParentId;
      const dropTarget = interaction.dropTargetId;

      if (nodeId && parentId) {
        // Image drag — transfer to the highlighted container
        if (dropTarget) {
          topo.transferImage(nodeId, parentId, dropTarget);
          topo.growToFit(dropTarget);
        }
        topo.growToFit(parentId);
      } else if (nodeId) {
        // Container drag — reparent to the highlighted container
        if (dropTarget) {
          topo.reparentContainer(nodeId, dropTarget);
          topo.growToFit(dropTarget);
        } else if (topo.isNested(nodeId)) {
          topo.unparentContainer(nodeId);
        }
      }

      interaction.setDropTarget(null);
      interaction.setDragParentId(null);
      interaction.setMode('idle');
      history.push(topo.getSnapshot());
      return;
    }

    if (mode === 'resizing') {
      const nodeId = interaction.selectedNodeId;
      if (nodeId) topo.growToFit(nodeId);
      interaction.setMode('idle');
      history.push(topo.getSnapshot());
      return;
    }

    if (mode === 'wiring') {
      // Check if we're over a port to complete the wire
      const hovered = interaction.hoveredPortId;
      const wiring = interaction.wiringState;
      if (hovered && wiring && hovered !== wiring.fromPortId) {
        // Recursively search all containers/images for the hovered port
        const findOwner = (containers: typeof topo.topology.containers): string | null => {
          for (const c of containers) {
            if (c.ports.some(p => p.id === hovered)) return c.id;
            for (const img of c.images) {
              if (img.ports.some(p => p.id === hovered)) return img.id;
            }
            const found = findOwner(c.children);
            if (found) return found;
          }
          return null;
        };
        const targetNodeId = findOwner(topo.topology.containers);
        if (targetNodeId) {
          topo.addWire({
            id: crypto.randomUUID(),
            fromNodeId: wiring.fromNodeId,
            fromPortId: wiring.fromPortId,
            toNodeId: targetNodeId,
            toPortId: hovered,
          });
          history.push(topo.getSnapshot());
        }
      }
      interaction.endWiring();
      return;
    }

    if (mode === 'selecting') {
      interaction.endSelectionBox();
      return;
    }
  };

  const handleDragOver = (e: DragEvent) => {
    if (e.dataTransfer?.types.includes('application/xcord-topo')) {
      e.preventDefault();
      e.dataTransfer.dropEffect = 'copy';
      const canvasPos = clientToCanvas(e.clientX, e.clientY);
      const target = topo.containerAtPoint(canvasPos.x, canvasPos.y);
      interaction.setDropTarget(target?.id ?? null);
    }
  };

  const handleDragLeave = (e: DragEvent) => {
    // Only clear when leaving the SVG entirely (not when entering a child element)
    if (e.currentTarget === e.target) {
      interaction.setDropTarget(null);
    }
  };

  const handleDrop = (e: DragEvent) => {
    e.preventDefault();
    interaction.setDropTarget(null);
    const raw = e.dataTransfer?.getData('application/xcord-topo');
    if (!raw) return;

    const { type, kind } = JSON.parse(raw) as { type: 'container' | 'image'; kind: string };
    const canvasPos = clientToCanvas(e.clientX, e.clientY);

    if (type === 'container') {
      const def = containerDefinitions.find(d => d.kind === kind);
      if (!def) return;
      history.push(topo.getSnapshot());
      const container: Container = {
        id: crypto.randomUUID(),
        name: def.label,
        kind: def.kind,
        x: canvasPos.x - def.defaultWidth / 2,
        y: canvasPos.y - def.defaultHeight / 2,
        width: def.defaultWidth,
        height: def.defaultHeight,
        ports: def.defaultPorts.map(createPort),
        images: [],
        children: [],
        config: {},
      };
      topo.addContainer(container);
      interaction.select(container.id);

      // If dropped on an existing container, nest it immediately
      const target = topo.containerAtPoint(canvasPos.x, canvasPos.y, container.id);
      if (target) {
        topo.reparentContainer(container.id, target.id);
        topo.growToFit(target.id);
      }
    } else if (type === 'image') {
      const def = imageDefinitions.find(d => d.kind === kind);
      if (!def) return;
      // Find the innermost container under the drop point (recursive)
      const target = topo.containerAtPoint(canvasPos.x, canvasPos.y);
      if (!target) return;
      const targetAbs = topo.getAbsolutePosition(target.id);
      if (!targetAbs) return;
      history.push(topo.getSnapshot());
      const image: Image = {
        id: crypto.randomUUID(),
        name: def.label,
        kind: def.kind,
        x: canvasPos.x - targetAbs.x - def.defaultWidth / 2,
        y: canvasPos.y - (targetAbs.y + 32) - def.defaultHeight / 2,
        width: def.defaultWidth,
        height: def.defaultHeight,
        ports: def.defaultPorts.map(createPort),
        dockerImage: def.defaultDockerImage,
        config: {},
        scaling: def.defaultScaling ?? 'Shared',
      };
      topo.addImage(target.id, image);
      interaction.select(image.id);
      topo.growToFit(target.id);
    }
  };

  const handleKeyDown = (e: KeyboardEvent) => {
    // Don't intercept keys when editing text in input fields
    const tag = (e.target as HTMLElement)?.tagName;
    if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') return;

    if (e.key === 'Delete' || e.key === 'Backspace') {
      const nodeId = interaction.selectedNodeId;
      if (nodeId) {
        history.push(topo.getSnapshot());
        const imageOwner = topo.findImageOwner(nodeId);
        if (imageOwner) {
          topo.removeImage(imageOwner, nodeId);
        } else {
          topo.removeContainer(nodeId);
        }
        interaction.deselect();
      }
      const selectedWire = topo.topology.wires.find(w => interaction.selectedNodeIds.has(w.id));
      if (selectedWire) {
        history.push(topo.getSnapshot());
        topo.removeWire(selectedWire.id);
        interaction.deselect();
      }
      return;
    }

    if (e.ctrlKey || e.metaKey) {
      if (e.key === 'z' && !e.shiftKey) {
        e.preventDefault();
        const prev = history.undo(topo.getSnapshot());
        if (prev) topo.load(prev);
      } else if ((e.key === 'z' && e.shiftKey) || e.key === 'y') {
        e.preventDefault();
        const next = history.redo(topo.getSnapshot());
        if (next) topo.load(next);
      } else if (e.key === 'a') {
        e.preventDefault();
        const ids = topo.topology.containers.map(c => c.id);
        interaction.selectAll(ids);
      } else if (e.key === 'f' && e.shiftKey) {
        e.preventDefault();
        history.push(topo.getSnapshot());
        topo.fitAllToContents();
      } else if (e.key === 'c') {
        e.preventDefault();
        const nodeId = interaction.selectedNodeId;
        if (!nodeId) return;
        const imageOwner = topo.findImageOwner(nodeId);
        if (imageOwner) {
          const findImg = (containers: readonly Container[]): Image | undefined => {
            for (const c of containers) {
              if (c.id === imageOwner) return c.images.find(i => i.id === nodeId);
              const found = findImg(c.children);
              if (found) return found;
            }
            return undefined;
          };
          const img = findImg(topo.topology.containers);
          if (img) clipboard = { type: 'image', data: JSON.parse(JSON.stringify(img)), parentId: imageOwner };
        } else {
          const findC = (containers: readonly Container[]): Container | undefined => {
            for (const c of containers) {
              if (c.id === nodeId) return c;
              const found = findC(c.children);
              if (found) return found;
            }
            return undefined;
          };
          const container = findC(topo.topology.containers);
          if (container) {
            // Collect internal wires (both endpoints inside this container tree)
            const collectIds = (c: Container): Set<string> => {
              const ids = new Set<string>([c.id]);
              c.images.forEach(i => ids.add(i.id));
              c.children.forEach(ch => { for (const id of collectIds(ch)) ids.add(id); });
              return ids;
            };
            const nodeIds = collectIds(container);
            const wires = topo.topology.wires.filter(
              w => nodeIds.has(w.fromNodeId) && nodeIds.has(w.toNodeId)
            );
            clipboard = {
              type: 'container',
              data: JSON.parse(JSON.stringify(container)),
              wires: JSON.parse(JSON.stringify(wires)),
            };
          }
        }
      } else if (e.key === 'v') {
        e.preventDefault();
        if (!clipboard) return;
        history.push(topo.getSnapshot());
        const OFFSET = 30;
        if (clipboard.type === 'image') {
          const copy = cloneImage(clipboard.data);
          copy.x += OFFSET;
          copy.y += OFFSET;
          topo.addImage(clipboard.parentId, copy);
          topo.growToFit(clipboard.parentId);
          interaction.select(copy.id);
        } else {
          const { clone, idMap } = cloneContainer(clipboard.data);
          clone.x += OFFSET;
          clone.y += OFFSET;
          topo.addContainer(clone);
          // Recreate internal wires with mapped IDs
          for (const w of clipboard.wires) {
            const fromNode = idMap.get(w.fromNodeId);
            const fromPort = idMap.get(w.fromPortId);
            const toNode = idMap.get(w.toNodeId);
            const toPort = idMap.get(w.toPortId);
            if (fromNode && fromPort && toNode && toPort) {
              topo.addWire({
                id: crypto.randomUUID(),
                fromNodeId: fromNode,
                fromPortId: fromPort,
                toNodeId: toNode,
                toPortId: toPort,
              });
            }
          }
          interaction.select(clone.id);
        }
      }
    }

    if (e.key === 'Escape') {
      interaction.deselect();
    }
  };

  onMount(() => {
    window.addEventListener('keydown', handleKeyDown);
  });

  onCleanup(() => {
    window.removeEventListener('keydown', handleKeyDown);
  });

  const transformStr = () => {
    const t = canvas.transform;
    return `translate(${t.x}, ${t.y}) scale(${t.scale})`;
  };

  return (
    <svg
      ref={svgRef}
      class="w-full h-full bg-topo-bg-canvas"
      onWheel={handleWheel}
      onPointerDown={handlePointerDown}
      onPointerMove={handlePointerMove}
      onPointerUp={handlePointerUp}
      onDragOver={handleDragOver}
      onDragLeave={handleDragLeave}
      onDrop={handleDrop}
      style={{ cursor: interaction.mode === 'panning' ? 'grabbing' : 'default', 'touch-action': 'none' }}
    >
      <DotGrid />
      <g transform={transformStr()}>
        <For each={sortedContainers()}>
          {(container) => <ContainerNode container={container} />}
        </For>
        <For each={topo.topology.wires}>
          {(wire) => <Wire wire={wire} />}
        </For>
        <Show when={interaction.wiringState}>
          <WirePreview />
        </Show>
        {/* Drag overlay — dragged image rendered above all containers */}
        <Show when={draggedImageOverlay()}>
          {(info) => (
            <g style={{ 'pointer-events': 'none' }}>
              <ImageNode
                image={info().image}
                containerX={info().containerX}
                containerY={info().containerY}
                containerId=""
              />
            </g>
          )}
        </Show>
      </g>
      <Show when={interaction.selectionBox}>
        <SelectionBox />
      </Show>
    </svg>
  );
};

export default Canvas;

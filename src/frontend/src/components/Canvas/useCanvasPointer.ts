import type { useTopology } from '../../stores/topology.store';
import type { useCanvas } from '../../stores/canvas.store';
import type { useInteraction } from '../../stores/interaction.store';
import type { useHistory } from '../../stores/history.store';
import type { Container, Image } from '../../types/topology';
import { screenToCanvas, distance, findDeepestContainerAt, absoluteContainerPosition } from '../../lib/geometry';
import { containerDefinitions } from '../../catalog/containers';
import { imageDefinitions } from '../../catalog/images';

type Topo = ReturnType<typeof useTopology>;
type Canvas = ReturnType<typeof useCanvas>;
type Interaction = ReturnType<typeof useInteraction>;
type History = ReturnType<typeof useHistory>;

export interface CanvasPointerHandlers {
  handleWheel: (e: WheelEvent) => void;
  handlePointerDown: (e: PointerEvent) => void;
  handlePointerMove: (e: PointerEvent) => void;
  handlePointerUp: (e: PointerEvent) => void;
  clientToCanvas: (clientX: number, clientY: number) => { x: number; y: number };
}

/**
 * Builds the wheel/pointer event handlers for the Canvas SVG.
 * Takes a getter for svgRef because the ref may not be assigned yet when this hook runs.
 */
export function useCanvasPointer(
  getSvgRef: () => SVGSVGElement | undefined,
  topo: Topo,
  canvas: Canvas,
  interaction: Interaction,
  history: History,
): CanvasPointerHandlers {
  /** Convert viewport clientX/clientY to canvas coordinates, accounting for SVG element offset */
  const clientToCanvas = (clientX: number, clientY: number) => {
    const rect = getSvgRef()!.getBoundingClientRect();
    return screenToCanvas({ x: clientX - rect.left, y: clientY - rect.top }, canvas.transform);
  };

  const handleWheel = (e: WheelEvent) => {
    e.preventDefault();
    const rect = getSvgRef()!.getBoundingClientRect();
    canvas.zoom(e.deltaY, e.clientX - rect.left, e.clientY - rect.top);
  };

  const handlePointerDown = (e: PointerEvent) => {
    if (e.button === 1 || (e.button === 0 && e.shiftKey && interaction.mode === 'idle')) {
      // Middle click or shift+left click = pan
      interaction.setMode('panning');
      (e.target as Element)?.setPointerCapture?.(e.pointerId);
      return;
    }

    if (e.button === 0 && interaction.mode === 'idle' && e.target === getSvgRef()) {
      // Click on empty canvas = start selection box or deselect
      const canvasPos = clientToCanvas(e.clientX, e.clientY);
      interaction.startSelectionBox(canvasPos);
      interaction.deselect();
    }
  };

  const handlePointerMove = (e: PointerEvent) => {
    // Check drag intent threshold (5px in screen space)
    const intent = interaction.dragIntent;
    if (intent && interaction.mode !== 'dragging') {
      const dist = distance({ x: e.clientX, y: e.clientY }, intent.startPos);
      if (dist >= 5) {
        // Exceeded threshold - start move drag
        history.push(topo.getSnapshot());
        const nodeIds = interaction.selectedNodeIds.has(intent.nodeId)
          ? [...interaction.selectedNodeIds]
          : [intent.nodeId];
        const canvasPos = clientToCanvas(e.clientX, e.clientY);
        interaction.startDrag({
          source: { type: 'move', nodeIds },
          origin: canvasPos,
          current: canvasPos,
          dropTargetId: null,
        });
      }
      return;
    }

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

    if (mode === 'resizing') {
      const resize = interaction.resizeState;
      if (!resize) return;
      const canvasPos = clientToCanvas(e.clientX, e.clientY);
      const dx = canvasPos.x - resize.startCanvasPos.x;
      const dy = canvasPos.y - resize.startCanvasPos.y;
      const MIN_W = 80;
      const MIN_H = 60;
      const updates: Partial<Container> = {};
      if (resize.edge === 'right' || resize.edge === 'bottom-right')
        updates.width = Math.max(MIN_W, resize.startWidth + dx);
      if (resize.edge === 'bottom' || resize.edge === 'bottom-right')
        updates.height = Math.max(MIN_H, resize.startHeight + dy);
      topo.updateContainer(resize.containerId, updates);
      return;
    }

    if (mode === 'dragging') {
      const drag = interaction.dragState;
      if (!drag) return;

      const canvasPos = clientToCanvas(e.clientX, e.clientY);

      if (drag.source.type === 'move') {
        const dx = canvasPos.x - drag.origin.x;
        const dy = canvasPos.y - drag.origin.y;
        topo.moveNodes(drag.source.nodeIds, dx, dy);

        // Hit-test for drop target (exclude dragged containers + their descendants)
        const exclude = new Set<string>();
        for (const nodeId of drag.source.nodeIds) {
          // Only exclude if it's a container (images can't contain things)
          if (!topo.findImageOwner(nodeId)) {
            exclude.add(nodeId);
            // Also exclude descendants of dragged containers
            const collectDescendants = (containers: typeof topo.topology.containers) => {
              for (const c of containers) {
                if (c.id === nodeId) {
                  const addAll = (children: typeof topo.topology.containers) => {
                    for (const ch of children) { exclude.add(ch.id); addAll(ch.children); }
                  };
                  addAll(c.children);
                }
                collectDescendants(c.children);
              }
            };
            collectDescendants(topo.topology.containers);
          }
        }

        const dropTarget = findDeepestContainerAt(topo.topology.containers, canvasPos, exclude);
        interaction.updateDrag(canvasPos, dropTarget);
      } else if (drag.source.type === 'palette') {
        const dropTarget = findDeepestContainerAt(topo.topology.containers, canvasPos);
        interaction.updateDrag(canvasPos, dropTarget);
      }
      return;
    }
  };

  const handlePointerUp = (e: PointerEvent) => {
    if (interaction.dragIntent) {
      interaction.clearDragIntent();
    }

    const mode = interaction.mode;

    if (mode === 'panning') {
      interaction.setMode('idle');
      return;
    }

    if (mode === 'resizing') {
      // Propagate resize to parent containers
      const resize = interaction.resizeState;
      if (resize) {
        const findParent = (containers: typeof topo.topology.containers): string | null => {
          for (const c of containers) {
            if (c.children.some(ch => ch.id === resize.containerId)) return c.id;
            const found = findParent(c.children);
            if (found) return found;
          }
          return null;
        };
        const parentId = findParent(topo.topology.containers);
        if (parentId) topo.growToFit(parentId);
      }
      interaction.endResize();
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

    if (mode === 'dragging') {
      const drag = interaction.dragState;
      if (!drag) { interaction.endDrag(); return; }

      if (drag.source.type === 'move') {
        // Helper to find a node's parent container
        const findNodeParent = (nodeId: string): string | null => {
          const imageOwner = topo.findImageOwner(nodeId);
          if (imageOwner) return imageOwner;
          const findContainerParent = (containers: typeof topo.topology.containers): string | null => {
            for (const c of containers) {
              if (c.children.some(ch => ch.id === nodeId)) return c.id;
              const found = findContainerParent(c.children);
              if (found) return found;
            }
            return null;
          };
          return findContainerParent(topo.topology.containers);
        };

        // Reparent if dropping on a different container (single node only)
        if (drag.dropTargetId && drag.source.nodeIds.length === 1) {
          const nodeId = drag.source.nodeIds[0];
          const currentParent = findNodeParent(nodeId);

          if (currentParent !== drag.dropTargetId) {
            const imageOwner = topo.findImageOwner(nodeId);
            if (imageOwner) {
              const ownerAbs = absoluteContainerPosition(topo.topology.containers, imageOwner);
              if (ownerAbs) {
                const findImg = (containers: typeof topo.topology.containers): { x: number; y: number } | null => {
                  for (const c of containers) {
                    if (c.id === imageOwner) {
                      const img = c.images.find(i => i.id === nodeId);
                      if (img) return { x: ownerAbs.x + img.x, y: ownerAbs.y + 32 + img.y };
                    }
                    const found = findImg(c.children);
                    if (found) return found;
                  }
                  return null;
                };
                const absPos = findImg(topo.topology.containers);
                if (absPos) {
                  topo.reparentImage(nodeId, imageOwner, drag.dropTargetId, absPos.x, absPos.y);
                }
              }
            } else {
              const absPos = absoluteContainerPosition(topo.topology.containers, nodeId);
              if (absPos) {
                topo.reparentContainer(nodeId, drag.dropTargetId, absPos.x, absPos.y);
              }
            }
          }
        }

        // Always growToFit parent containers of moved nodes
        const parentIds = new Set<string>();
        for (const nodeId of drag.source.nodeIds) {
          const parentId = findNodeParent(nodeId);
          if (parentId) parentIds.add(parentId);
        }
        // Also grow the drop target if reparenting happened
        if (drag.dropTargetId) parentIds.add(drag.dropTargetId);
        for (const pid of parentIds) {
          topo.growToFit(pid);
        }
      } else if (drag.source.type === 'palette') {
        const canvasPos = clientToCanvas(e.clientX, e.clientY);
        const src = drag.source;

        if (src.itemType === 'container') {
          const def = containerDefinitions.find(d => d.kind === src.kind);
          if (def) {
            history.push(topo.getSnapshot());
            const container: Container = {
              id: crypto.randomUUID(),
              name: def.label,
              kind: def.kind as any,
              x: canvasPos.x - def.defaultWidth / 2,
              y: canvasPos.y - def.defaultHeight / 2,
              width: def.defaultWidth,
              height: def.defaultHeight,
              ports: def.defaultPorts.map(p => ({ ...p, id: crypto.randomUUID() })),
              images: [],
              children: [],
              config: {},
            };

            if (drag.dropTargetId) {
              // Dropping on a container = make it a child
              const parentAbs = absoluteContainerPosition(topo.topology.containers, drag.dropTargetId);
              if (parentAbs) {
                container.x = canvasPos.x - def.defaultWidth / 2 - parentAbs.x;
                container.y = canvasPos.y - def.defaultHeight / 2 - (parentAbs.y + 32);
              }
              // Add to the target container's children
              topo.addContainer(container);
              topo.reparentContainer(container.id, drag.dropTargetId, canvasPos.x - def.defaultWidth / 2, canvasPos.y - def.defaultHeight / 2);
              topo.growToFit(drag.dropTargetId);
            } else {
              topo.addContainer(container);
            }
            interaction.select(container.id);
          }
        } else {
          // Image - must drop on a container
          if (drag.dropTargetId) {
            const def = imageDefinitions().find(d => d.kind === src.kind);
            if (def) {
              history.push(topo.getSnapshot());
              const parentAbs = absoluteContainerPosition(topo.topology.containers, drag.dropTargetId);
              const image: Image = {
                id: crypto.randomUUID(),
                name: def.label,
                kind: def.kind as any,
                typeId: def.kind,
                x: parentAbs ? canvasPos.x - def.defaultWidth / 2 - parentAbs.x : 20,
                y: parentAbs ? canvasPos.y - def.defaultHeight / 2 - (parentAbs.y + 32) : 20,
                width: def.defaultWidth,
                height: def.defaultHeight,
                ports: def.defaultPorts.map(p => ({ ...p, id: crypto.randomUUID() })),
                dockerImage: def.defaultDockerImage,
                config: {},
                scaling: def.defaultScaling ?? 'Shared',
              };
              topo.addImage(drag.dropTargetId, image);
              topo.growToFit(drag.dropTargetId);
              interaction.select(image.id);
            }
          }
          // else: dropped on empty canvas with image - do nothing
        }
      }
      interaction.endDrag();
      return;
    }
  };

  return { handleWheel, handlePointerDown, handlePointerMove, handlePointerUp, clientToCanvas };
}

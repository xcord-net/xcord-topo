import { onMount, onCleanup } from 'solid-js';
import type { useTopology } from '../../stores/topology.store';
import type { useInteraction } from '../../stores/interaction.store';
import type { useHistory } from '../../stores/history.store';
import type { Container, Image } from '../../types/topology';
import { cloneContainer, cloneImage, type Clipboard } from './clipboard';

type Topo = ReturnType<typeof useTopology>;
type Interaction = ReturnType<typeof useInteraction>;
type History = ReturnType<typeof useHistory>;

interface ClipboardRef {
  current: Clipboard | null;
}

/**
 * Wires up the Canvas keyboard event handler (delete/copy/paste/undo/redo/select-all/escape/fit).
 * Returns a clipboard ref so handlers in other modules can inspect/clear the clipboard if needed.
 */
export function useCanvasKeyboard(topo: Topo, interaction: Interaction, history: History): ClipboardRef {
  const clipboardRef: ClipboardRef = { current: null };

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
          if (img) clipboardRef.current = { type: 'image', data: JSON.parse(JSON.stringify(img)), parentId: imageOwner };
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
            clipboardRef.current = {
              type: 'container',
              data: JSON.parse(JSON.stringify(container)),
              wires: JSON.parse(JSON.stringify(wires)),
            };
          }
        }
      } else if (e.key === 'v') {
        e.preventDefault();
        if (!clipboardRef.current) return;
        history.push(topo.getSnapshot());
        const OFFSET = 30;
        if (clipboardRef.current.type === 'image') {
          const copy = cloneImage(clipboardRef.current.data);
          copy.x += OFFSET;
          copy.y += OFFSET;
          topo.addImage(clipboardRef.current.parentId, copy);
          topo.growToFit(clipboardRef.current.parentId);
          interaction.select(copy.id);
        } else {
          const { clone, idMap } = cloneContainer(clipboardRef.current.data);
          clone.x += OFFSET;
          clone.y += OFFSET;
          topo.addContainer(clone);
          // Recreate internal wires with mapped IDs
          for (const w of clipboardRef.current.wires) {
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

    if (e.key === 'Escape' && interaction.mode === 'resizing') {
      const prev = history.undo(topo.getSnapshot());
      if (prev) topo.load(prev);
      interaction.endResize();
      return;
    }

    if (e.key === 'Escape' && interaction.mode === 'dragging') {
      const prev = history.undo(topo.getSnapshot());
      if (prev) topo.load(prev);
      interaction.cancelDrag();
      return;
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

  return clipboardRef;
}

import { createRoot, createSignal } from 'solid-js';
import type { Point } from '../types/geometry';

export type InteractionMode =
  | 'idle'
  | 'panning'
  | 'dragging'
  | 'wiring'
  | 'resizing'
  | 'selecting'
  | 'palette-drag';

export interface WiringState {
  fromNodeId: string;
  fromPortId: string;
  fromSide: 'Top' | 'Right' | 'Bottom' | 'Left';
  fromPos: Point;
  cursorPos: Point;
}

const store = createRoot(() => {
  const [mode, setMode] = createSignal<InteractionMode>('idle');
  const [selectedNodeId, setSelectedNodeId] = createSignal<string | null>(null);
  const [selectedNodeIds, setSelectedNodeIds] = createSignal<Set<string>>(new Set());
  const [hoveredPortId, setHoveredPortId] = createSignal<string | null>(null);
  const [wiringState, setWiringState] = createSignal<WiringState | null>(null);
  const [selectionBox, setSelectionBox] = createSignal<{ start: Point; end: Point } | null>(null);
  const [paletteDragKind, setPaletteDragKind] = createSignal<string | null>(null);
  const [paletteDragType, setPaletteDragType] = createSignal<'container' | 'image' | null>(null);
  const [dragOffset, setDragOffset] = createSignal<Point>({ x: 0, y: 0 });
  const [dropTargetId, setDropTargetId] = createSignal<string | null>(null);
  const [dragParentId, setDragParentId] = createSignal<string | null>(null);

  return {
    mode, setMode,
    selectedNodeId, setSelectedNodeId,
    selectedNodeIds, setSelectedNodeIds,
    hoveredPortId, setHoveredPortId,
    wiringState, setWiringState,
    selectionBox, setSelectionBox,
    paletteDragKind, setPaletteDragKind,
    paletteDragType, setPaletteDragType,
    dragOffset, setDragOffset,
    dropTargetId, setDropTargetId,
    dragParentId, setDragParentId,
  };
});

export function useInteraction() {
  return {
    get mode() { return store.mode(); },
    get selectedNodeId() { return store.selectedNodeId(); },
    get selectedNodeIds() { return store.selectedNodeIds(); },
    get hoveredPortId() { return store.hoveredPortId(); },
    get wiringState() { return store.wiringState(); },
    get selectionBox() { return store.selectionBox(); },
    get paletteDragKind() { return store.paletteDragKind(); },
    get paletteDragType() { return store.paletteDragType(); },
    get dragOffset() { return store.dragOffset(); },
    get dropTargetId() { return store.dropTargetId(); },
    get dragParentId() { return store.dragParentId(); },

    setMode: store.setMode,
    setDragOffset: store.setDragOffset,
    setDropTarget(id: string | null): void {
      store.setDropTargetId(id);
    },
    setDragParentId: store.setDragParentId,

    select(nodeId: string, additive = false): void {
      store.setSelectedNodeId(nodeId);
      if (additive) {
        const next = new Set(store.selectedNodeIds());
        if (next.has(nodeId)) next.delete(nodeId);
        else next.add(nodeId);
        store.setSelectedNodeIds(next);
      } else {
        store.setSelectedNodeIds(new Set([nodeId]));
      }
    },

    deselect(): void {
      store.setSelectedNodeId(null);
      store.setSelectedNodeIds(new Set<string>());
      store.setMode('idle');
    },

    selectAll(nodeIds: string[]): void {
      store.setSelectedNodeIds(new Set(nodeIds));
      store.setSelectedNodeId(nodeIds[0] ?? null);
    },

    setHoveredPort(portId: string | null): void {
      store.setHoveredPortId(portId);
    },

    startWiring(fromNodeId: string, fromPortId: string, fromSide: 'Top' | 'Right' | 'Bottom' | 'Left', fromPos: Point): void {
      store.setMode('wiring');
      store.setWiringState({ fromNodeId, fromPortId, fromSide, fromPos, cursorPos: fromPos });
    },

    updateWiringCursor(pos: Point): void {
      const current = store.wiringState();
      if (current) store.setWiringState({ ...current, cursorPos: pos });
    },

    endWiring(): void {
      store.setMode('idle');
      store.setWiringState(null);
    },

    startSelectionBox(start: Point): void {
      store.setMode('selecting');
      store.setSelectionBox({ start, end: start });
    },

    updateSelectionBox(end: Point): void {
      const current = store.selectionBox();
      if (current) store.setSelectionBox({ ...current, end });
    },

    endSelectionBox(): void {
      store.setMode('idle');
      store.setSelectionBox(null);
    },

    startPaletteDrag(kind: string, type: 'container' | 'image'): void {
      store.setMode('palette-drag');
      store.setPaletteDragKind(kind);
      store.setPaletteDragType(type);
    },

    endPaletteDrag(): void {
      store.setMode('idle');
      store.setPaletteDragKind(null);
      store.setPaletteDragType(null);
    },

    reset(): void {
      store.setMode('idle');
      store.setSelectedNodeId(null);
      store.setSelectedNodeIds(new Set<string>());
      store.setHoveredPortId(null);
      store.setWiringState(null);
      store.setSelectionBox(null);
      store.setPaletteDragKind(null);
      store.setPaletteDragType(null);
      store.setDropTargetId(null);
      store.setDragParentId(null);
    },
  };
}

import { createRoot, createSignal } from 'solid-js';
import type { Point } from '../types/geometry';

export type InteractionMode =
  | 'idle'
  | 'panning'
  | 'wiring'
  | 'selecting'
  | 'dragging'
  | 'resizing';

export interface WiringState {
  fromNodeId: string;
  fromPortId: string;
  fromSide: 'Top' | 'Right' | 'Bottom' | 'Left';
  fromPos: Point;
  cursorPos: Point;
}

export interface DragState {
  source:
    | { type: 'move'; nodeIds: string[] }
    | { type: 'palette'; itemType: 'container' | 'image'; kind: string };
  origin: Point;
  current: Point;
  dropTargetId: string | null;
}

export interface DragIntent {
  nodeId: string;
  startPos: Point;
}

export type ResizeEdge = 'right' | 'bottom' | 'bottom-right';

export interface ResizeState {
  containerId: string;
  edge: ResizeEdge;
  startCanvasPos: Point;
  startWidth: number;
  startHeight: number;
}

const store = createRoot(() => {
  const [mode, setMode] = createSignal<InteractionMode>('idle');
  const [selectedNodeId, setSelectedNodeId] = createSignal<string | null>(null);
  const [selectedNodeIds, setSelectedNodeIds] = createSignal<Set<string>>(new Set());
  const [hoveredPortId, setHoveredPortId] = createSignal<string | null>(null);
  const [wiringState, setWiringState] = createSignal<WiringState | null>(null);
  const [selectionBox, setSelectionBox] = createSignal<{ start: Point; end: Point } | null>(null);
  const [dragState, setDragState] = createSignal<DragState | null>(null);
  const [dragIntent, setDragIntent] = createSignal<DragIntent | null>(null);
  const [resizeState, setResizeState] = createSignal<ResizeState | null>(null);
  return {
    mode, setMode,
    selectedNodeId, setSelectedNodeId,
    selectedNodeIds, setSelectedNodeIds,
    hoveredPortId, setHoveredPortId,
    wiringState, setWiringState,
    selectionBox, setSelectionBox,
    dragState, setDragState,
    dragIntent, setDragIntent,
    resizeState, setResizeState,
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
    get dragState() { return store.dragState(); },
    get dragIntent() { return store.dragIntent(); },
    get resizeState() { return store.resizeState(); },
    setMode: store.setMode,

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

    setDragIntent(nodeId: string, startPos: Point): void {
      store.setDragIntent({ nodeId, startPos });
    },

    clearDragIntent(): void {
      store.setDragIntent(null);
    },

    startDrag(state: DragState): void {
      store.setMode('dragging');
      store.setDragState(state);
      store.setDragIntent(null);
    },

    updateDrag(current: Point, dropTargetId: string | null): void {
      const s = store.dragState();
      if (s) store.setDragState({ ...s, origin: current, current, dropTargetId });
    },

    endDrag(): void {
      store.setMode('idle');
      store.setDragState(null);
      store.setDragIntent(null);
    },

    cancelDrag(): void {
      store.setMode('idle');
      store.setDragState(null);
      store.setDragIntent(null);
    },

    startResize(state: ResizeState): void {
      store.setMode('resizing');
      store.setResizeState(state);
    },

    endResize(): void {
      store.setMode('idle');
      store.setResizeState(null);
    },

    reset(): void {
      store.setMode('idle');
      store.setSelectedNodeId(null);
      store.setSelectedNodeIds(new Set<string>());
      store.setHoveredPortId(null);
      store.setWiringState(null);
      store.setSelectionBox(null);
      store.setDragState(null);
      store.setDragIntent(null);
      store.setResizeState(null);
    },
  };
}

import { Component, For, Show, createSignal, onMount } from 'solid-js';
import { useInteraction, type ResizeEdge } from '../stores/interaction.store';
import { useTopology } from '../stores/topology.store';
import { useCanvas } from '../stores/canvas.store';
import { useHistory } from '../stores/history.store';
import { useValidation } from '../stores/validation.store';
import { screenToCanvas } from '../lib/geometry';
import type { Container as ContainerType } from '../types/topology';
import { containerDefinitions } from '../catalog/containers';
import ImageNode from './ImageNode';
import PortCircle from './PortCircle';

const HEADER_HEIGHT = 32;

const ContainerNode: Component<{
  container: ContainerType;
  offsetX?: number;
  offsetY?: number;
}> = (props) => {
  const interaction = useInteraction();
  const topo = useTopology();
  const canvasStore = useCanvas();
  const history = useHistory();
  const validation = useValidation();

  const offX = () => props.offsetX ?? 0;
  const offY = () => props.offsetY ?? 0;
  const absX = () => offX() + props.container.x;
  const absY = () => offY() + props.container.y;

  const def = () => containerDefinitions.find(d => d.kind === props.container.kind);
  const isSelected = () => interaction.selectedNodeIds.has(props.container.id);
  const hasError = () => validation.hasErrors(props.container.id);
  const hasWarning = () => validation.hasWarnings(props.container.id);
  const errorCount = () => validation.nodeErrorCount(props.container.id);

  const drag = () => interaction.dragState;
  const isDropTarget = () => drag()?.dropTargetId === props.container.id;
  const isDragging = () => interaction.mode === 'dragging';
  const handleCursor = () => isDragging() ? 'grabbing' : 'grab';

  let kindTextRef!: SVGTextElement;
  const [kindWidth, setKindWidth] = createSignal(40);
  onMount(() => {
    if (kindTextRef) setKindWidth(kindTextRef.getComputedTextLength());
  });
  const fitBtnX = () => absX() + props.container.width - 12 - kindWidth() - 8 - 20;

  return (
    <g>
      {/* Container body — pointer-events: none so nested children receive clicks/drops */}
      <rect
        x={absX()}
        y={absY()}
        width={props.container.width}
        height={props.container.height}
        rx={8}
        fill="#1f2335"
        stroke={hasError() ? '#f7768e' : hasWarning() ? '#e0af68' : isSelected() ? '#7aa2f7' : (def()?.color ?? '#3b4261')}
        stroke-width={hasError() || hasWarning() ? 2 : isSelected() ? 2 : 1}
        opacity={0.95}
        style={{ 'pointer-events': 'none' }}
      />

      {/* Drop target highlight */}
      <Show when={isDropTarget()}>
        <rect
          x={absX()}
          y={absY()}
          width={props.container.width}
          height={props.container.height}
          rx={8}
          fill="#7aa2f7"
          opacity={0.15}
          style={{ 'pointer-events': 'none' }}
        />
      </Show>

      {/* Header bar */}
      <rect
        x={absX()}
        y={absY()}
        width={props.container.width}
        height={HEADER_HEIGHT}
        rx={8}
        fill={def()?.color ?? '#3b4261'}
        opacity={0.8}
        style={{ cursor: handleCursor() }}
        onPointerDown={(e) => {
          if (e.button !== 0) return;
          e.stopPropagation();
          interaction.select(props.container.id, e.shiftKey);
          interaction.setDragIntent(props.container.id, { x: e.clientX, y: e.clientY });
        }}
      />
      {/* Bottom half of header - square corners to match body */}
      <rect
        x={absX()}
        y={absY() + HEADER_HEIGHT / 2}
        width={props.container.width}
        height={HEADER_HEIGHT / 2}
        fill={def()?.color ?? '#3b4261'}
        opacity={0.8}
        style={{ cursor: handleCursor() }}
        onPointerDown={(e) => {
          if (e.button !== 0) return;
          e.stopPropagation();
          interaction.select(props.container.id, e.shiftKey);
          interaction.setDragIntent(props.container.id, { x: e.clientX, y: e.clientY });
        }}
      />

      {/* Container name */}
      <text
        x={absX() + 12}
        y={absY() + HEADER_HEIGHT / 2 + 1}
        fill="white"
        font-size="13"
        font-weight="600"
        dominant-baseline="middle"
        style={{ 'pointer-events': 'none', 'user-select': 'none' }}
      >
        {props.container.name}
      </text>

      {/* Kind badge */}
      <text
        ref={kindTextRef}
        x={absX() + props.container.width - 12}
        y={absY() + HEADER_HEIGHT / 2 + 1}
        fill="rgba(255,255,255,0.6)"
        font-size="10"
        text-anchor="end"
        dominant-baseline="middle"
        style={{ 'pointer-events': 'none', 'user-select': 'none' }}
      >
        {props.container.kind}
      </text>

      {/* Fit-to-contents button — positioned to the left of the kind badge */}
      <g
        style={{ cursor: 'pointer' }}
        opacity={0.4}
        onMouseEnter={(e) => { (e.currentTarget as SVGGElement).setAttribute('opacity', '1'); }}
        onMouseLeave={(e) => { (e.currentTarget as SVGGElement).setAttribute('opacity', '0.4'); }}
        onPointerDown={(e) => {
          e.stopPropagation();
          history.push(topo.getSnapshot());
          topo.fitToContents(props.container.id);
        }}
      >
        <rect
          x={fitBtnX()}
          y={absY() + 6}
          width={20}
          height={20}
          rx={3}
          fill="rgba(255,255,255,0.1)"
        />
        <path
          d={`M${fitBtnX() + 5} ${absY() + 11} l4 4 -4 4 M${fitBtnX() + 15} ${absY() + 11} l-4 4 4 4`}
          stroke="white"
          stroke-width={1.5}
          fill="none"
          stroke-linecap="round"
          stroke-linejoin="round"
        />
      </g>

      {/* Clickable content area — rendered before children so children are on top */}
      <rect
        x={absX()}
        y={absY() + HEADER_HEIGHT}
        width={props.container.width}
        height={Math.max(0, props.container.height - HEADER_HEIGHT)}
        fill="transparent"
        onPointerDown={(e) => {
          if (e.button !== 0) return;
          e.stopPropagation();
          interaction.select(props.container.id, e.shiftKey);
        }}
      />

      {/* Nested images */}
      <For each={props.container.images}>
        {(image) => (
          <ImageNode
            image={image}
            containerX={absX()}
            containerY={absY() + HEADER_HEIGHT}
            containerId={props.container.id}
          />
        )}
      </For>

      {/* Nested child containers */}
      <For each={props.container.children}>
        {(child) => (
          <ContainerNode
            container={child}
            offsetX={absX()}
            offsetY={absY() + HEADER_HEIGHT}
          />
        )}
      </For>

      {/* Error/warning badge */}
      <Show when={errorCount() > 0}>
        <rect
          x={absX() + props.container.width - 24}
          y={absY() - 10}
          width={20}
          height={20}
          rx={10}
          fill={hasError() ? '#f7768e' : '#e0af68'}
          style={{ 'pointer-events': 'none' }}
        />
        <text
          x={absX() + props.container.width - 14}
          y={absY()}
          fill="#1a1b26"
          font-size="11"
          font-weight="700"
          text-anchor="middle"
          dominant-baseline="middle"
          style={{ 'pointer-events': 'none', 'user-select': 'none' }}
        >
          {errorCount()}
        </text>
      </Show>

      {/* Container ports */}
      <For each={props.container.ports}>
        {(port) => (
          <PortCircle
            port={port}
            nodeId={props.container.id}
            nodeX={absX()}
            nodeY={absY()}
            nodeWidth={props.container.width}
            nodeHeight={props.container.height}
          />
        )}
      </For>

      {/* Resize handles — shown when selected */}
      <Show when={isSelected()}>
        {(() => {
          const startResize = (edge: ResizeEdge, e: PointerEvent & { currentTarget: Element }) => {
            e.stopPropagation();
            history.push(topo.getSnapshot());
            const rect = (e.target as Element).closest('svg')!.getBoundingClientRect();
            const canvasPos = screenToCanvas(
              { x: e.clientX - rect.left, y: e.clientY - rect.top },
              canvasStore.transform
            );
            interaction.startResize({
              containerId: props.container.id,
              edge,
              startCanvasPos: canvasPos,
              startWidth: props.container.width,
              startHeight: props.container.height,
            });
          };

          const HANDLE = 8;
          const cx = absX() + props.container.width;
          const cy = absY() + props.container.height;

          return (
            <>
              {/* Right edge */}
              <rect
                x={cx - 3}
                y={absY() + HEADER_HEIGHT}
                width={6}
                height={props.container.height - HEADER_HEIGHT - HANDLE}
                fill="transparent"
                style={{ cursor: 'ew-resize' }}
                onPointerDown={[startResize, 'right']}
              />
              {/* Bottom edge */}
              <rect
                x={absX()}
                y={cy - 3}
                width={props.container.width - HANDLE}
                height={6}
                fill="transparent"
                style={{ cursor: 'ns-resize' }}
                onPointerDown={[startResize, 'bottom']}
              />
              {/* Bottom-right corner grip */}
              <g style={{ cursor: 'nwse-resize' }} onPointerDown={[startResize, 'bottom-right']}>
                <rect
                  x={cx - HANDLE - 2}
                  y={cy - HANDLE - 2}
                  width={HANDLE + 4}
                  height={HANDLE + 4}
                  fill="transparent"
                />
                <line x1={cx - 2} y1={cy - 6} x2={cx - 6} y2={cy - 2} stroke="#7aa2f7" stroke-width={1.5} opacity={0.7} />
                <line x1={cx - 2} y1={cy - 3} x2={cx - 3} y2={cy - 2} stroke="#7aa2f7" stroke-width={1.5} opacity={0.7} />
              </g>
            </>
          );
        })()}
      </Show>

    </g>
  );
};

export default ContainerNode;

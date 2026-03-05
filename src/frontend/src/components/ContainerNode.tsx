import { Component, For, Show, createSignal, onMount } from 'solid-js';
import { useInteraction } from '../stores/interaction.store';
import { useTopology } from '../stores/topology.store';
import { useHistory } from '../stores/history.store';
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
  const history = useHistory();

  const offX = () => props.offsetX ?? 0;
  const offY = () => props.offsetY ?? 0;
  const absX = () => offX() + props.container.x;
  const absY = () => offY() + props.container.y;

  const def = () => containerDefinitions.find(d => d.kind === props.container.kind);
  const isSelected = () => interaction.selectedNodeIds.has(props.container.id);

  const drag = () => interaction.dragState;
  const isDropTarget = () => drag()?.dropTargetId === props.container.id;

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
        stroke={isSelected() ? '#7aa2f7' : (def()?.color ?? '#3b4261')}
        stroke-width={isSelected() ? 2 : 1}
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
        style={{ cursor: 'default' }}
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
        style={{ cursor: 'default' }}
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
          interaction.setDragIntent(props.container.id, { x: e.clientX, y: e.clientY });
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

    </g>
  );
};

export default ContainerNode;

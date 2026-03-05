import { Component, For, Show } from 'solid-js';
import { useInteraction } from '../stores/interaction.store';
import type { Image } from '../types/topology';
import { imageDefinitions } from '../catalog/images';
import PortCircle from './PortCircle';

const ImageNode: Component<{
  image: Image;
  containerX: number;
  containerY: number;
  containerId: string;
}> = (props) => {
  const interaction = useInteraction();
  const def = () => imageDefinitions.find(d => d.kind === props.image.kind);

  const absX = () => props.containerX + props.image.x;
  const absY = () => props.containerY + props.image.y;
  const isSelected = () => interaction.selectedNodeIds.has(props.image.id);

  const replicaLabel = () => {
    const r = props.image.config?.replicas;
    if (!r || r === '1') return '';
    return `\u00d7${r.startsWith('$') ? r : r}`;
  };
  const replicaBadgeWidth = () => Math.max(28, replicaLabel().length * 7 + 10);

  return (
    <g>
      {/* Per-tenant stacked card effect */}
      <Show when={props.image.scaling === 'PerTenant'}>
        <rect
          x={absX() + 6}
          y={absY() + 6}
          width={props.image.width}
          height={props.image.height}
          rx={4}
          fill="#24283b"
          stroke={def()?.color ?? '#565f89'}
          stroke-width={0.5}
          opacity={0.3}
          style={{ 'pointer-events': 'none' }}
        />
        <rect
          x={absX() + 3}
          y={absY() + 3}
          width={props.image.width}
          height={props.image.height}
          rx={4}
          fill="#24283b"
          stroke={def()?.color ?? '#565f89'}
          stroke-width={0.5}
          opacity={0.5}
          style={{ 'pointer-events': 'none' }}
        />
      </Show>

      <rect
        x={absX()}
        y={absY()}
        width={props.image.width}
        height={props.image.height}
        rx={4}
        fill="#24283b"
        stroke={isSelected() ? '#7aa2f7' : (def()?.color ?? '#565f89')}
        stroke-width={isSelected() ? 2 : 1}
        style={{ cursor: 'pointer' }}
        onPointerDown={(e) => {
          if (e.button !== 0) return;
          e.stopPropagation();
          interaction.select(props.image.id, e.shiftKey);
          interaction.setDragIntent(props.image.id, { x: e.clientX, y: e.clientY });
        }}
      />

      {/* Image name */}
      <text
        x={absX() + props.image.width / 2}
        y={absY() + props.image.height / 2 - 6}
        fill="#c0caf5"
        font-size="11"
        font-weight="500"
        text-anchor="middle"
        dominant-baseline="middle"
        style={{ 'pointer-events': 'none', 'user-select': 'none' }}
      >
        {props.image.name}
      </text>

      {/* Image kind */}
      <text
        x={absX() + props.image.width / 2}
        y={absY() + props.image.height / 2 + 8}
        fill="#565f89"
        font-size="9"
        text-anchor="middle"
        dominant-baseline="middle"
        style={{ 'pointer-events': 'none', 'user-select': 'none' }}
      >
        {props.image.kind}
      </text>

      {/* Image ports */}
      <For each={props.image.ports}>
        {(port) => (
          <PortCircle
            port={port}
            nodeId={props.image.id}
            nodeX={absX()}
            nodeY={absY()}
            nodeWidth={props.image.width}
            nodeHeight={props.image.height}
          />
        )}
      </For>

      {/* Replica badge */}
      <Show when={replicaLabel()}>
        <rect
          x={absX() + props.image.width - replicaBadgeWidth() + 4}
          y={absY() - 8}
          width={replicaBadgeWidth()}
          height={16}
          rx={8}
          fill="#e0af68"
          style={{ 'pointer-events': 'none' }}
        />
        <text
          x={absX() + props.image.width - replicaBadgeWidth() / 2 + 4}
          y={absY()}
          fill="#1a1b26"
          font-size="10"
          font-weight="600"
          text-anchor="middle"
          dominant-baseline="middle"
          style={{ 'pointer-events': 'none', 'user-select': 'none' }}
        >
          {replicaLabel()}
        </text>
      </Show>
    </g>
  );
};

export default ImageNode;

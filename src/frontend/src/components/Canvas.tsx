import { Component, For, Show } from 'solid-js';
import { useTopology } from '../stores/topology.store';
import { useCanvas } from '../stores/canvas.store';
import { useInteraction } from '../stores/interaction.store';
import { useHistory } from '../stores/history.store';
import { containerDefinitions } from '../catalog/containers';
import { imageDefinitions } from '../catalog/images';
import ContainerNode from './ContainerNode';
import Wire from './Wire';
import WirePreview from './WirePreview';
import SelectionBox from './SelectionBox';
import DotGrid from './DotGrid';
import { useCanvasKeyboard } from './Canvas/useCanvasKeyboard';
import { useCanvasPointer } from './Canvas/useCanvasPointer';

const Canvas: Component = () => {
  let svgRef: SVGSVGElement | undefined;
  const topo = useTopology();
  const canvas = useCanvas();
  const interaction = useInteraction();
  const history = useHistory();

  const { handleWheel, handlePointerDown, handlePointerMove, handlePointerUp } =
    useCanvasPointer(() => svgRef, topo, canvas, interaction, history);

  useCanvasKeyboard(topo, interaction, history);

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
      style={{
        cursor: (() => {
          const mode = interaction.mode;
          if (mode === 'panning') return 'grabbing';
          if (mode === 'resizing') {
            const edge = interaction.resizeState?.edge;
            if (edge === 'right') return 'ew-resize';
            if (edge === 'bottom') return 'ns-resize';
            return 'nwse-resize';
          }
          if (mode === 'dragging') {
            const drag = interaction.dragState;
            if (drag?.source.type === 'palette' && (drag.source as any).itemType === 'image' && !drag.dropTargetId) {
              return 'not-allowed';
            }
            return 'grabbing';
          }
          return 'default';
        })(),
        'touch-action': 'none',
      }}
    >
      <DotGrid />
      <g transform={transformStr()}>
        <For each={topo.topology.containers}>
          {(container) => <ContainerNode container={container} />}
        </For>
        <For each={topo.topology.wires}>
          {(wire) => <Wire wire={wire} />}
        </For>
        <Show when={interaction.wiringState}>
          <WirePreview />
        </Show>
        <Show when={interaction.dragState?.source.type === 'palette'}>
          {(() => {
            const drag = interaction.dragState!;
            const src = drag.source as { type: 'palette'; itemType: string; kind: string };
            const defs = src.itemType === 'container' ? containerDefinitions : imageDefinitions();
            const def = defs.find((d: any) => d.kind === src.kind);
            if (!def) return null;
            const w = def.defaultWidth;
            const h = def.defaultHeight;
            return (
              <rect
                x={drag.current.x - w / 2}
                y={drag.current.y - h / 2}
                width={w}
                height={h}
                rx={src.itemType === 'container' ? 8 : 4}
                fill={def.color}
                opacity={0.3}
                stroke={def.color}
                stroke-width={1}
                stroke-dasharray="4 2"
                style={{ 'pointer-events': 'none' }}
              />
            );
          })()}
        </Show>
      </g>
      <Show when={interaction.selectionBox}>
        <SelectionBox />
      </Show>
    </svg>
  );
};

export default Canvas;

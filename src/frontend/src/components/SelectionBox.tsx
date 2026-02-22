import { Component, createMemo } from 'solid-js';
import { useInteraction } from '../stores/interaction.store';
import { useCanvas } from '../stores/canvas.store';
import { canvasToScreen } from '../lib/geometry';

const SelectionBox: Component = () => {
  const interaction = useInteraction();
  const canvas = useCanvas();

  const rect = createMemo(() => {
    const box = interaction.selectionBox;
    if (!box) return null;

    const start = canvasToScreen(box.start, canvas.transform);
    const end = canvasToScreen(box.end, canvas.transform);

    return {
      x: Math.min(start.x, end.x),
      y: Math.min(start.y, end.y),
      width: Math.abs(end.x - start.x),
      height: Math.abs(end.y - start.y),
    };
  });

  return (
    <>
      {rect() && (
        <rect
          x={rect()!.x}
          y={rect()!.y}
          width={rect()!.width}
          height={rect()!.height}
          fill="rgba(122, 162, 247, 0.1)"
          stroke="#7aa2f7"
          stroke-width={1}
          stroke-dasharray="4 2"
          style={{ 'pointer-events': 'none' }}
        />
      )}
    </>
  );
};

export default SelectionBox;

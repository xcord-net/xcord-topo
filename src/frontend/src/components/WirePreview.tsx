import { Component, createMemo } from 'solid-js';
import { useInteraction } from '../stores/interaction.store';
import { cubicBezierPath } from '../lib/wire-routing';

const WirePreview: Component = () => {
  const interaction = useInteraction();

  const path = createMemo(() => {
    const wiring = interaction.wiringState;
    if (!wiring) return '';

    return cubicBezierPath(
      wiring.fromPos,
      wiring.fromSide,
      wiring.cursorPos,
      'Left'
    );
  });

  return (
    <path
      d={path()}
      fill="none"
      stroke="#7aa2f7"
      stroke-width={2}
      stroke-dasharray="6 3"
      opacity={0.5}
      style={{ 'pointer-events': 'none' }}
    />
  );
};

export default WirePreview;

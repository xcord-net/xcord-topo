import { Component } from 'solid-js';
import { useCanvas } from '../stores/canvas.store';

const DotGrid: Component = () => {
  const canvas = useCanvas();

  const patternSize = () => 20 * canvas.transform.scale;
  const dotSize = () => Math.max(0.5, canvas.transform.scale);

  return (
    <>
      <defs>
        <pattern
          id="dot-grid"
          width={patternSize()}
          height={patternSize()}
          patternUnits="userSpaceOnUse"
          x={canvas.transform.x % patternSize()}
          y={canvas.transform.y % patternSize()}
        >
          <circle
            cx={patternSize() / 2}
            cy={patternSize() / 2}
            r={dotSize()}
            fill="#3b4261"
          />
        </pattern>
      </defs>
      <rect width="100%" height="100%" fill="url(#dot-grid)" />
    </>
  );
};

export default DotGrid;

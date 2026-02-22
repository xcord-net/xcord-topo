import { describe, it, expect } from 'vitest';
import { cubicBezierPath } from '../lib/wire-routing';

describe('wire-routing', () => {
  it('generates a valid SVG path', () => {
    const path = cubicBezierPath(
      { x: 100, y: 100 },
      'Right',
      { x: 300, y: 200 },
      'Left',
    );
    expect(path).toMatch(/^M \d+ \d+ C/);
    expect(path).toContain('100 100');
    expect(path).toContain('300 200');
  });

  it('generates different control points for different sides', () => {
    const pathRight = cubicBezierPath({ x: 0, y: 0 }, 'Right', { x: 100, y: 100 }, 'Left');
    const pathBottom = cubicBezierPath({ x: 0, y: 0 }, 'Bottom', { x: 100, y: 100 }, 'Top');
    expect(pathRight).not.toBe(pathBottom);
  });
});

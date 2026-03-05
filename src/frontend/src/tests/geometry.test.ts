import { describe, it, expect } from 'vitest';
import { screenToCanvas, canvasToScreen, clamp, distance, rectContainsPoint, rectsIntersect, portPosition, absoluteContainerPosition, findDeepestContainerAt } from '../lib/geometry';
import type { Container } from '../types/topology';

describe('geometry', () => {
  describe('screenToCanvas', () => {
    it('converts with identity transform', () => {
      const result = screenToCanvas({ x: 100, y: 200 }, { x: 0, y: 0, scale: 1 });
      expect(result).toEqual({ x: 100, y: 200 });
    });

    it('converts with offset', () => {
      const result = screenToCanvas({ x: 150, y: 250 }, { x: 50, y: 50, scale: 1 });
      expect(result).toEqual({ x: 100, y: 200 });
    });

    it('converts with scale', () => {
      const result = screenToCanvas({ x: 200, y: 400 }, { x: 0, y: 0, scale: 2 });
      expect(result).toEqual({ x: 100, y: 200 });
    });

    it('round-trips with canvasToScreen', () => {
      const transform = { x: 30, y: -20, scale: 1.5 };
      const original = { x: 100, y: 200 };
      const screen = canvasToScreen(original, transform);
      const back = screenToCanvas(screen, transform);
      expect(back.x).toBeCloseTo(original.x);
      expect(back.y).toBeCloseTo(original.y);
    });
  });

  describe('clamp', () => {
    it('clamps below min', () => expect(clamp(-5, 0, 10)).toBe(0));
    it('clamps above max', () => expect(clamp(15, 0, 10)).toBe(10));
    it('passes through in range', () => expect(clamp(5, 0, 10)).toBe(5));
  });

  describe('distance', () => {
    it('calculates distance between two points', () => {
      expect(distance({ x: 0, y: 0 }, { x: 3, y: 4 })).toBe(5);
    });
    it('returns 0 for same point', () => {
      expect(distance({ x: 5, y: 5 }, { x: 5, y: 5 })).toBe(0);
    });
  });

  describe('rectContainsPoint', () => {
    const rect = { x: 10, y: 10, width: 100, height: 50 };
    it('returns true for point inside', () => expect(rectContainsPoint(rect, { x: 50, y: 30 })).toBe(true));
    it('returns false for point outside', () => expect(rectContainsPoint(rect, { x: 200, y: 200 })).toBe(false));
    it('returns true for point on edge', () => expect(rectContainsPoint(rect, { x: 10, y: 10 })).toBe(true));
  });

  describe('rectsIntersect', () => {
    it('returns true for overlapping rects', () => {
      expect(rectsIntersect(
        { x: 0, y: 0, width: 100, height: 100 },
        { x: 50, y: 50, width: 100, height: 100 },
      )).toBe(true);
    });
    it('returns false for non-overlapping rects', () => {
      expect(rectsIntersect(
        { x: 0, y: 0, width: 50, height: 50 },
        { x: 100, y: 100, width: 50, height: 50 },
      )).toBe(false);
    });
  });

  describe('portPosition', () => {
    it('calculates top port position', () => {
      const pos = portPosition(100, 100, 200, 100, 'Top', 0.5);
      expect(pos).toEqual({ x: 200, y: 100 });
    });
    it('calculates right port position', () => {
      const pos = portPosition(100, 100, 200, 100, 'Right', 0.5);
      expect(pos).toEqual({ x: 300, y: 150 });
    });
    it('calculates bottom port position', () => {
      const pos = portPosition(100, 100, 200, 100, 'Bottom', 0.5);
      expect(pos).toEqual({ x: 200, y: 200 });
    });
    it('calculates left port position', () => {
      const pos = portPosition(100, 100, 200, 100, 'Left', 0.5);
      expect(pos).toEqual({ x: 100, y: 150 });
    });
  });
});

describe('absoluteContainerPosition', () => {
  const HEADER = 32;

  it('returns position for top-level container', () => {
    const containers: Container[] = [
      { id: 'c1', name: 'C1', kind: 'Host', x: 100, y: 200, width: 400, height: 300, ports: [], images: [], children: [], config: {} },
    ];
    expect(absoluteContainerPosition(containers, 'c1')).toEqual({ x: 100, y: 200 });
  });

  it('returns position for nested child container', () => {
    const containers: Container[] = [{
      id: 'parent', name: 'P', kind: 'Host', x: 50, y: 50, width: 500, height: 400, ports: [], images: [], config: {},
      children: [{
        id: 'child', name: 'C', kind: 'Host', x: 20, y: 30, width: 200, height: 150, ports: [], images: [], children: [], config: {},
      }],
    }];
    expect(absoluteContainerPosition(containers, 'child')).toEqual({ x: 70, y: 112 });
  });

  it('returns null for unknown id', () => {
    expect(absoluteContainerPosition([], 'nope')).toBeNull();
  });
});

describe('findDeepestContainerAt', () => {
  it('returns null when no container is under point', () => {
    const containers: Container[] = [
      { id: 'c1', name: 'C1', kind: 'Host', x: 100, y: 100, width: 200, height: 200, ports: [], images: [], children: [], config: {} },
    ];
    expect(findDeepestContainerAt(containers, { x: 50, y: 50 })).toBeNull();
  });

  it('returns top-level container under point', () => {
    const containers: Container[] = [
      { id: 'c1', name: 'C1', kind: 'Host', x: 100, y: 100, width: 200, height: 200, ports: [], images: [], children: [], config: {} },
    ];
    expect(findDeepestContainerAt(containers, { x: 150, y: 150 })).toBe('c1');
  });

  it('returns deepest nested container', () => {
    const containers: Container[] = [{
      id: 'outer', name: 'O', kind: 'Host', x: 0, y: 0, width: 500, height: 400, ports: [], images: [], config: {},
      children: [{
        id: 'inner', name: 'I', kind: 'Host', x: 10, y: 10, width: 200, height: 150, ports: [], images: [], children: [], config: {},
      }],
    }];
    expect(findDeepestContainerAt(containers, { x: 50, y: 80 })).toBe('inner');
  });

  it('excludes ids in exclude set', () => {
    const containers: Container[] = [
      { id: 'c1', name: 'C1', kind: 'Host', x: 0, y: 0, width: 500, height: 500, ports: [], images: [], children: [], config: {} },
    ];
    expect(findDeepestContainerAt(containers, { x: 50, y: 50 }, new Set(['c1']))).toBeNull();
  });
});

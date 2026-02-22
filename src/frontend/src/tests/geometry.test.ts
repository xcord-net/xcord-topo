import { describe, it, expect } from 'vitest';
import { screenToCanvas, canvasToScreen, clamp, distance, rectContainsPoint, rectsIntersect, portPosition } from '../lib/geometry';

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

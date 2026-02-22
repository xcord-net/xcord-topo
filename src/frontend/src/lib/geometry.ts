import type { Point, Rect, Transform } from '../types/geometry';
import type { PortSide } from '../types/topology';

export function screenToCanvas(screenPoint: Point, transform: Transform): Point {
  return {
    x: (screenPoint.x - transform.x) / transform.scale,
    y: (screenPoint.y - transform.y) / transform.scale,
  };
}

export function canvasToScreen(canvasPoint: Point, transform: Transform): Point {
  return {
    x: canvasPoint.x * transform.scale + transform.x,
    y: canvasPoint.y * transform.scale + transform.y,
  };
}

export function clamp(value: number, min: number, max: number): number {
  return Math.max(min, Math.min(max, value));
}

export function distance(a: Point, b: Point): number {
  return Math.sqrt((a.x - b.x) ** 2 + (a.y - b.y) ** 2);
}

export function rectContainsPoint(rect: Rect, point: Point): boolean {
  return (
    point.x >= rect.x &&
    point.x <= rect.x + rect.width &&
    point.y >= rect.y &&
    point.y <= rect.y + rect.height
  );
}

export function rectsIntersect(a: Rect, b: Rect): boolean {
  return !(a.x + a.width < b.x || b.x + b.width < a.x || a.y + a.height < b.y || b.y + b.height < a.y);
}

export function portPosition(
  nodeX: number,
  nodeY: number,
  nodeWidth: number,
  nodeHeight: number,
  side: PortSide,
  offset: number,
): Point {
  switch (side) {
    case 'Top':
      return { x: nodeX + nodeWidth * offset, y: nodeY };
    case 'Right':
      return { x: nodeX + nodeWidth, y: nodeY + nodeHeight * offset };
    case 'Bottom':
      return { x: nodeX + nodeWidth * offset, y: nodeY + nodeHeight };
    case 'Left':
      return { x: nodeX, y: nodeY + nodeHeight * offset };
  }
}

export function sideNormal(side: PortSide): Point {
  switch (side) {
    case 'Top':
      return { x: 0, y: -1 };
    case 'Right':
      return { x: 1, y: 0 };
    case 'Bottom':
      return { x: 0, y: 1 };
    case 'Left':
      return { x: -1, y: 0 };
  }
}

import type { Point } from '../types/geometry';
import type { PortSide } from '../types/topology';
import { sideNormal } from './geometry';

const CONTROL_POINT_DISTANCE = 80;

export function cubicBezierPath(from: Point, fromSide: PortSide, to: Point, toSide: PortSide): string {
  const fromNormal = sideNormal(fromSide);
  const toNormal = sideNormal(toSide);

  const cp1: Point = {
    x: from.x + fromNormal.x * CONTROL_POINT_DISTANCE,
    y: from.y + fromNormal.y * CONTROL_POINT_DISTANCE,
  };

  const cp2: Point = {
    x: to.x + toNormal.x * CONTROL_POINT_DISTANCE,
    y: to.y + toNormal.y * CONTROL_POINT_DISTANCE,
  };

  return `M ${from.x} ${from.y} C ${cp1.x} ${cp1.y}, ${cp2.x} ${cp2.y}, ${to.x} ${to.y}`;
}

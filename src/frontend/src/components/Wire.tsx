import { Component, createMemo } from 'solid-js';
import { useTopology } from '../stores/topology.store';
import { cubicBezierPath } from '../lib/wire-routing';
import { portPosition } from '../lib/geometry';
import type { Wire as WireType, Port, PortSide } from '../types/topology';

const PORT_COLORS: Record<string, string> = {
  Network: '#7aa2f7',
  Database: '#bb9af7',
  Storage: '#e0af68',
  Control: '#9ece6a',
  Generic: '#a9b1d6',
};

interface PortInfo {
  port: Port;
  nodeX: number;
  nodeY: number;
  nodeWidth: number;
  nodeHeight: number;
}

const Wire: Component<{ wire: WireType }> = (props) => {
  const topo = useTopology();

  const findPort = (nodeId: string, portId: string): PortInfo | null => {
    const HEADER = 32;
    const search = (containers: typeof topo.topology.containers, offX: number, offY: number): PortInfo | null => {
      for (const c of containers) {
        const ax = offX + c.x;
        const ay = offY + c.y;
        if (c.id === nodeId) {
          const port = c.ports.find(p => p.id === portId);
          if (port) return { port, nodeX: ax, nodeY: ay, nodeWidth: c.width, nodeHeight: c.height };
        }
        for (const img of c.images) {
          if (img.id === nodeId) {
            const port = img.ports.find(p => p.id === portId);
            if (port) return { port, nodeX: ax + img.x, nodeY: ay + HEADER + img.y, nodeWidth: img.width, nodeHeight: img.height };
          }
        }
        const found = search(c.children, ax, ay + HEADER);
        if (found) return found;
      }
      return null;
    };
    return search(topo.topology.containers, 0, 0);
  };

  const path = createMemo(() => {
    const from = findPort(props.wire.fromNodeId, props.wire.fromPortId);
    const to = findPort(props.wire.toNodeId, props.wire.toPortId);
    if (!from || !to) return '';

    const fromPos = portPosition(from.nodeX, from.nodeY, from.nodeWidth, from.nodeHeight, from.port.side, from.port.offset);
    const toPos = portPosition(to.nodeX, to.nodeY, to.nodeWidth, to.nodeHeight, to.port.side, to.port.offset);

    return cubicBezierPath(fromPos, from.port.side, toPos, to.port.side);
  });

  const color = createMemo(() => {
    const from = findPort(props.wire.fromNodeId, props.wire.fromPortId);
    return from ? (PORT_COLORS[from.port.type] ?? '#a9b1d6') : '#a9b1d6';
  });

  return (
    <path
      d={path()}
      fill="none"
      stroke={color()}
      stroke-width={2}
      opacity={0.7}
      stroke-linecap="round"
    />
  );
};

export default Wire;

import { Component, createMemo } from 'solid-js';
import { useInteraction } from '../stores/interaction.store';
import { useTopology } from '../stores/topology.store';
import { useHistory } from '../stores/history.store';
import type { Port } from '../types/topology';
import { portPosition } from '../lib/geometry';

const PORT_COLORS: Record<string, string> = {
  Network: '#7aa2f7',
  Database: '#bb9af7',
  Storage: '#e0af68',
  Control: '#9ece6a',
  Generic: '#a9b1d6',
};

const PortCircle: Component<{
  port: Port;
  nodeId: string;
  nodeX: number;
  nodeY: number;
  nodeWidth: number;
  nodeHeight: number;
}> = (props) => {
  const interaction = useInteraction();
  const topo = useTopology();
  const history = useHistory();

  const pos = createMemo(() =>
    portPosition(props.nodeX, props.nodeY, props.nodeWidth, props.nodeHeight, props.port.side, props.port.offset)
  );

  const isHovered = () => interaction.hoveredPortId === props.port.id;
  const radius = () => isHovered() ? 7 : 5;

  /** Find a port's absolute position by recursively searching the topology */
  const findPortPos = (nodeId: string, portId: string) => {
    const HEADER = 32;
    const search = (containers: import('../types/topology').Container[], offX: number, offY: number): { pos: import('../types/geometry').Point; side: import('../types/topology').PortSide } | null => {
      for (const c of containers) {
        const ax = offX + c.x;
        const ay = offY + c.y;
        if (c.id === nodeId) {
          const p = c.ports.find(p => p.id === portId);
          if (p) return { pos: portPosition(ax, ay, c.width, c.height, p.side, p.offset), side: p.side };
        }
        for (const img of c.images) {
          if (img.id === nodeId) {
            const ix = ax + img.x;
            const iy = ay + HEADER + img.y;
            const p = img.ports.find(p => p.id === portId);
            if (p) return { pos: portPosition(ix, iy, img.width, img.height, p.side, p.offset), side: p.side };
          }
        }
        const found = search(c.children, ax, ay + HEADER);
        if (found) return found;
      }
      return null;
    };
    return search(topo.topology.containers, 0, 0);
  };

  const handlePointerDown = (e: PointerEvent) => {
    if (e.button !== 0) return;
    e.stopPropagation();

    // Check if this port already has a wire connected
    const existingWire = topo.topology.wires.find(
      w => w.fromPortId === props.port.id || w.toPortId === props.port.id
    );

    if (existingWire) {
      // Detach: remove the wire and start dragging from the opposite end
      const isFrom = existingWire.fromPortId === props.port.id;
      const otherNodeId = isFrom ? existingWire.toNodeId : existingWire.fromNodeId;
      const otherPortId = isFrom ? existingWire.toPortId : existingWire.fromPortId;
      const other = findPortPos(otherNodeId, otherPortId);

      history.push(topo.getSnapshot());
      topo.removeWire(existingWire.id);

      if (other) {
        interaction.startWiring(otherNodeId, otherPortId, other.side, other.pos);
      }
    } else {
      interaction.startWiring(props.nodeId, props.port.id, props.port.side, pos());
    }
  };

  return (
    <circle
      cx={pos().x}
      cy={pos().y}
      r={radius()}
      fill={PORT_COLORS[props.port.type] ?? '#a9b1d6'}
      stroke="#1a1b26"
      stroke-width={2}
      style={{ cursor: 'crosshair' }}
      onPointerDown={handlePointerDown}
      onPointerEnter={() => interaction.setHoveredPort(props.port.id)}
      onPointerLeave={() => interaction.setHoveredPort(null)}
    >
      <title>{`${props.port.name} (${props.port.type})`}</title>
    </circle>
  );
};

export default PortCircle;

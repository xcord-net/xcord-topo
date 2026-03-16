import type { Topology, Container, Image, Wire } from '../types/topology';
import type { ValidationItem } from '../types/deploy';

// --- Utility ---

function sanitizeName(name: string): string {
  return name
    .toLowerCase()
    .replace(/ /g, '_')
    .replace(/-/g, '_')
    .replace(/[^a-z0-9_]/g, '');
}

function isValidReplicaValue(value: string): boolean {
  if (value.startsWith('$') && value.length > 1 && !value.includes(' ')) return true;
  const n = parseInt(value, 10);
  if (!isNaN(n) && n > 0 && String(n) === value) return true;
  const parts = value.split('-');
  if (parts.length === 2) {
    const min = parseInt(parts[0], 10);
    const max = parseInt(parts[1], 10);
    if (!isNaN(min) && min > 0 && !isNaN(max) && max > 0 && min <= max
        && String(min) === parts[0] && String(max) === parts[1]) return true;
  }
  return false;
}

const DOMAIN_REGEX = /^(?:[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\.)+[a-zA-Z]{2,}$/;
const DANGEROUS_CHARS_REGEX = /[;|&$`"\\<>(){}\[\]*?!#]/;
const VALID_BACKUP_FREQUENCIES = new Set(['hourly', 'daily', 'weekly']);

// --- Tree walking ---

function walkContainers(containers: Container[], fn: (c: Container) => void): void {
  for (const c of containers) {
    fn(c);
    walkContainers(c.children, fn);
  }
}

function walkImages(containers: Container[], fn: (img: Image, parent: Container) => void): void {
  walkContainers(containers, c => {
    for (const img of c.images) fn(img, c);
  });
}

// --- Wire resolution (simplified client-side) ---

function resolveWiredImage(
  topology: Topology, sourceImageId: string, portName: string
): Image | null {
  // Find the source port by name on the source image
  let sourcePort: { id: string } | undefined;
  walkImages(topology.containers, img => {
    if (img.id === sourceImageId) {
      sourcePort = img.ports.find(p => p.name === portName);
    }
  });
  if (!sourcePort) return null;

  // Find wire from/to this port (wires can be drawn in either direction)
  const wire = topology.wires.find(
    w => w.fromPortId === sourcePort!.id || w.toPortId === sourcePort!.id
  );
  if (!wire) return null;

  // The target node is whichever end ISN'T the source image
  const targetNodeId = wire.fromNodeId === sourceImageId ? wire.toNodeId : wire.fromNodeId;

  // Find target image
  let target: Image | null = null;
  walkImages(topology.containers, img => {
    if (img.id === targetNodeId) target = img;
  });
  return target;
}

// --- Checks ---

function checkStructural(topology: Topology, items: ValidationItem[]): void {
  if (!topology.name?.trim()) {
    items.push({ severity: 'Error', message: 'Topology name is required.', field: 'name' });
  }
  if (topology.containers.length === 0) {
    items.push({ severity: 'Error', message: 'Topology must have at least one container.' });
  }

  const allPorts = new Set<string>();
  const allNodeIds = new Set<string>();

  walkContainers(topology.containers, c => {
    allNodeIds.add(c.id);
    for (const p of c.ports) allPorts.add(p.id);

    if (!c.name?.trim()) {
      items.push({ severity: 'Error', message: `Container must have a name.`, nodeId: c.id, field: 'name' });
    }
    if (c.width <= 0 || c.height <= 0) {
      items.push({ severity: 'Error', message: `Container '${c.name}' must have positive dimensions.`, nodeId: c.id });
    }
  });

  walkImages(topology.containers, (img) => {
    allNodeIds.add(img.id);
    for (const p of img.ports) allPorts.add(p.id);

    const replicas = img.config?.replicas;
    if (replicas && !isValidReplicaValue(replicas)) {
      items.push({
        severity: 'Error',
        message: `Image '${img.name}' has invalid replicas value '${replicas}'. Must be a positive integer, a range (e.g. 1-3), or a $VARIABLE reference.`,
        nodeId: img.id, field: 'replicas',
      });
    }
  });

  // Wire validation
  const wireKeys = new Set<string>();
  for (const wire of topology.wires) {
    if (!allNodeIds.has(wire.fromNodeId))
      items.push({ severity: 'Error', message: `Wire references non-existent source node.` });
    if (!allNodeIds.has(wire.toNodeId))
      items.push({ severity: 'Error', message: `Wire references non-existent target node.` });
    if (!allPorts.has(wire.fromPortId))
      items.push({ severity: 'Error', message: `Wire references non-existent source port.` });
    if (!allPorts.has(wire.toPortId))
      items.push({ severity: 'Error', message: `Wire references non-existent target port.` });
    if (wire.fromNodeId === wire.toNodeId)
      items.push({ severity: 'Error', message: `Wire cannot connect a node to itself.` });

    const key = `${wire.fromPortId}-${wire.toPortId}`;
    const reverseKey = `${wire.toPortId}-${wire.fromPortId}`;
    if (wireKeys.has(key) || wireKeys.has(reverseKey)) {
      items.push({ severity: 'Error', message: `Duplicate wire connection.` });
    }
    wireKeys.add(key);
  }
}

function checkNameUniqueness(topology: Topology, items: ValidationItem[]): void {
  // Container names must be globally unique (they become Terraform resource names)
  const seenContainers = new Map<string, string>();

  walkContainers(topology.containers, c => {
    const sanitized = sanitizeName(c.name);
    if (sanitized) {
      const existing = seenContainers.get(sanitized);
      if (existing !== undefined) {
        items.push({
          severity: 'Error',
          message: `Name collision: '${c.name}' and '${existing}' both sanitize to '${sanitized}', causing Terraform resource conflicts.`,
          nodeId: c.id, field: 'name',
        });
      } else {
        seenContainers.set(sanitized, c.name);
      }
    }

    // Image names only need to be unique within their parent container
    const seenImages = new Map<string, string>();
    for (const img of c.images) {
      const imgSanitized = sanitizeName(img.name);
      if (imgSanitized) {
        const existing = seenImages.get(imgSanitized);
        if (existing !== undefined) {
          items.push({
            severity: 'Error',
            message: `Name collision: '${img.name}' and '${existing}' both sanitize to '${imgSanitized}' within '${c.name}'.`,
            nodeId: img.id, field: 'name',
          });
        } else {
          seenImages.set(imgSanitized, img.name);
        }
      }
    }
  });
}

function checkDomainPresence(topology: Topology, items: ValidationItem[]): void {
  walkContainers(topology.containers, c => {
    if (c.kind === 'Caddy' || c.kind === 'Dns') {
      const domain = c.config?.domain ?? '';
      if (!domain.trim()) {
        items.push({
          severity: 'Error',
          message: `${c.kind} container '${c.name}' must have a non-empty domain.`,
          nodeId: c.id, field: 'domain',
        });
      } else if (!DOMAIN_REGEX.test(domain)) {
        items.push({
          severity: 'Error',
          message: `${c.kind} container '${c.name}': '${domain}' is not a valid domain name.`,
          nodeId: c.id, field: 'domain',
        });
      }
    }
  });
}

function checkVolumeSizes(topology: Topology, items: ValidationItem[]): void {
  walkImages(topology.containers, (img, parent) => {
    const sizeStr = img.config?.volumeSize;
    if (sizeStr) {
      const size = parseInt(sizeStr, 10);
      if (isNaN(size) || size <= 0) {
        items.push({
          severity: 'Error',
          message: `Image '${img.name}' in '${parent.name}': volumeSize must be a positive integer, got '${sizeStr}'.`,
          nodeId: img.id, field: 'volumeSize',
        });
      }
    }
  });
}

function checkComputePoolRequiredImages(topology: Topology, items: ValidationItem[]): void {
  walkContainers(topology.containers, c => {
    if (c.kind === 'ComputePool') {
      const hasFed = c.images.some(i => i.kind === 'FederationServer');
      if (!hasFed) {
        items.push({
          severity: 'Error',
          message: `ComputePool '${c.name}' must contain at least one FederationServer image.`,
          nodeId: c.id,
        });
      }
    }
  });
}

function checkWireCompleteness(topology: Topology, items: ValidationItem[]): void {
  walkImages(topology.containers, (img, parent) => {
    if (img.kind === 'FederationServer') {
      if (!resolveWiredImage(topology, img.id, 'pg'))
        items.push({ severity: 'Error', message: `FederationServer '${img.name}' in '${parent.name}' is not connected to a PostgreSQL image.`, nodeId: img.id });
      if (!resolveWiredImage(topology, img.id, 'redis'))
        items.push({ severity: 'Error', message: `FederationServer '${img.name}' in '${parent.name}' is not connected to a Redis image.`, nodeId: img.id });
      if (!resolveWiredImage(topology, img.id, 'minio'))
        items.push({ severity: 'Error', message: `FederationServer '${img.name}' in '${parent.name}' is not connected to a MinIO image.`, nodeId: img.id });
    }
    if (img.kind === 'HubServer') {
      if (!resolveWiredImage(topology, img.id, 'pg'))
        items.push({ severity: 'Error', message: `HubServer '${img.name}' in '${parent.name}' is not connected to a PostgreSQL image.`, nodeId: img.id });
      if (!resolveWiredImage(topology, img.id, 'redis'))
        items.push({ severity: 'Error', message: `HubServer '${img.name}' in '${parent.name}' is not connected to a Redis image.`, nodeId: img.id });
    }
  });
}

function checkCaddyfileSafety(topology: Topology, items: ValidationItem[]): void {
  walkContainers(topology.containers, c => {
    if (c.kind === 'Caddy') {
      const domain = c.config?.domain ?? '';
      if (domain && DANGEROUS_CHARS_REGEX.test(domain)) {
        items.push({
          severity: 'Error',
          message: `Caddy container '${c.name}': domain value contains unsafe characters.`,
          nodeId: c.id, field: 'domain',
        });
      }
    }
  });
}

function checkOrphanedImages(topology: Topology, items: ValidationItem[]): void {
  const connectedNodes = new Set<string>();
  for (const wire of topology.wires) {
    connectedNodes.add(wire.fromNodeId);
    connectedNodes.add(wire.toNodeId);
  }

  walkImages(topology.containers, (img, parent) => {
    if (img.ports.length > 0 && !connectedNodes.has(img.id)) {
      items.push({
        severity: 'Warning',
        message: `Image '${img.name}' in '${parent.name}' has ports but no wires connecting it.`,
        nodeId: img.id,
      });
    }
  });
}

function checkBackupFrequency(topology: Topology, items: ValidationItem[]): void {
  walkContainers(topology.containers, c => {
    const freq = c.config?.backupFrequency;
    if (freq && !VALID_BACKUP_FREQUENCIES.has(freq.toLowerCase())) {
      items.push({
        severity: 'Warning',
        message: `Container '${c.name}': backupFrequency '${freq}' is not recognized (valid: hourly, daily, weekly).`,
        nodeId: c.id, field: 'backupFrequency',
      });
    }
  });

  walkImages(topology.containers, (img, parent) => {
    const freq = img.config?.backupFrequency;
    if (freq && !VALID_BACKUP_FREQUENCIES.has(freq.toLowerCase())) {
      items.push({
        severity: 'Warning',
        message: `Image '${img.name}' in '${parent.name}': backupFrequency '${freq}' is not recognized.`,
        nodeId: img.id, field: 'backupFrequency',
      });
    }
  });
}

function checkBackupTarget(topology: Topology, items: ValidationItem[]): void {
  const bt = topology.backupTarget;
  if (!bt) return;

  if (!bt.bucketName?.trim()) {
    items.push({
      severity: 'Error',
      message: 'Backup target: bucket name is required.',
      field: 'backupTarget.bucketName',
    });
  }
  if (!bt.region?.trim()) {
    items.push({
      severity: 'Error',
      message: 'Backup target: region is required.',
      field: 'backupTarget.region',
    });
  }
  if (bt.kind === 'S3Compatible' && !bt.endpoint?.trim()) {
    items.push({
      severity: 'Error',
      message: 'Backup target: endpoint is required for S3 Compatible storage.',
      field: 'backupTarget.endpoint',
    });
  }
  if (bt.glacierTransitionDays !== undefined) {
    if (bt.kind !== 'AwsS3') {
      items.push({
        severity: 'Warning',
        message: 'Backup target: glacierTransitionDays is only applicable for AWS S3.',
        field: 'backupTarget.glacierTransitionDays',
      });
    } else if (bt.glacierTransitionDays <= 0) {
      items.push({
        severity: 'Error',
        message: 'Backup target: glacierTransitionDays must be greater than 0.',
        field: 'backupTarget.glacierTransitionDays',
      });
    }
  }
}

// --- Public API ---

export function validateTopology(topology: Topology): ValidationItem[] {
  const items: ValidationItem[] = [];

  // Structural
  checkStructural(topology, items);

  // Deploy-blocking (client-side subset)
  checkNameUniqueness(topology, items);
  checkDomainPresence(topology, items);
  checkVolumeSizes(topology, items);
  checkComputePoolRequiredImages(topology, items);
  checkWireCompleteness(topology, items);
  checkCaddyfileSafety(topology, items);

  // Warnings
  checkOrphanedImages(topology, items);
  checkBackupFrequency(topology, items);

  // Topology-level
  checkBackupTarget(topology, items);

  return items;
}

/** Get all node IDs that have errors or warnings. */
export function getNodeErrors(items: ValidationItem[]): Map<string, ValidationItem[]> {
  const map = new Map<string, ValidationItem[]>();
  for (const item of items) {
    if (item.nodeId) {
      const existing = map.get(item.nodeId);
      if (existing) existing.push(item);
      else map.set(item.nodeId, [item]);
    }
  }
  return map;
}

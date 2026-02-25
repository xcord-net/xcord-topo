import type { TierProfile } from '../types/topology';

export const defaultTierProfiles: TierProfile[] = [
  {
    id: 'free',
    name: 'Free Tier',
    imageSpecs: {
      FederationServer: { memoryMb: 256, cpuMillicores: 250, diskMb: 512 },
    },
  },
  {
    id: 'basic',
    name: 'Basic Tier',
    imageSpecs: {
      FederationServer: { memoryMb: 512, cpuMillicores: 500, diskMb: 2048 },
    },
  },
  {
    id: 'pro',
    name: 'Pro Tier',
    imageSpecs: {
      FederationServer: { memoryMb: 1024, cpuMillicores: 1000, diskMb: 5120 },
    },
  },
  {
    id: 'enterprise',
    name: 'Enterprise Tier',
    imageSpecs: {
      FederationServer: { memoryMb: 2048, cpuMillicores: 2000, diskMb: 25600 },
    },
  },
];

/** Shared infrastructure overhead per compute pool host (MB) */
export const sharedOverheadMb = 1024 + 512 + 512 + 128; // PG + Redis + MinIO + Caddy = 2176

export function calculateTenantsPerHost(hostMemoryMb: number, tierProfile: TierProfile): number {
  const available = hostMemoryMb - sharedOverheadMb;
  if (available <= 0) return 0;
  const fedSpec = tierProfile.imageSpecs['FederationServer'];
  if (!fedSpec || fedSpec.memoryMb <= 0) return 0;
  return Math.floor(available / fedSpec.memoryMb);
}

export function calculateHostsRequired(targetTenants: number, tenantsPerHost: number): number {
  if (tenantsPerHost <= 0) return targetTenants;
  return Math.ceil(targetTenants / tenantsPerHost);
}

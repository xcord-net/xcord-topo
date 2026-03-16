import type { TierProfile } from '../types/topology';

export const defaultTierProfiles: TierProfile[] = [
  {
    id: 'free',
    name: 'Free Tier',
    imageSpecs: {
      FederationServer: { memoryMb: 192, cpuMillicores: 100, diskMb: 256 },
    },
  },
  {
    id: 'basic',
    name: 'Basic Tier',
    imageSpecs: {
      FederationServer: { memoryMb: 256, cpuMillicores: 250, diskMb: 512 },
    },
  },
  {
    id: 'pro',
    name: 'Pro Tier',
    imageSpecs: {
      FederationServer: { memoryMb: 512, cpuMillicores: 350, diskMb: 2048 },
    },
  },
  {
    id: 'enterprise',
    name: 'Enterprise Tier',
    imageSpecs: {
      FederationServer: { memoryMb: 1024, cpuMillicores: 750, diskMb: 8192 },
    },
  },
];

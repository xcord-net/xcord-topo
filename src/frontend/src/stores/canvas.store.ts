import { createRoot, createSignal } from 'solid-js';
import type { Transform } from '../types/geometry';

const MIN_SCALE = 0.1;
const MAX_SCALE = 5;
const TRANSFORM_KEY = 'xcord-topo:canvas-transform';

function loadTransform(): Transform {
  try {
    const raw = localStorage.getItem(TRANSFORM_KEY);
    if (raw) return JSON.parse(raw) as Transform;
  } catch { /* ignore */ }
  return { x: 0, y: 0, scale: 1 };
}

let transformTimer: ReturnType<typeof setTimeout> | null = null;
function saveTransform(t: Transform): void {
  if (transformTimer) clearTimeout(transformTimer);
  transformTimer = setTimeout(() => {
    try { localStorage.setItem(TRANSFORM_KEY, JSON.stringify(t)); } catch { /* ignore */ }
  }, 300);
}

const store = createRoot(() => {
  const [transform, rawSetTransform] = createSignal<Transform>(loadTransform());

  const setTransform: typeof rawSetTransform = (v) => {
    const result = rawSetTransform(v);
    saveTransform(transform());
    return result;
  };

  return { transform, setTransform };
});

export function useCanvas() {
  return {
    get transform() { return store.transform(); },

    pan(dx: number, dy: number): void {
      store.setTransform(prev => ({
        ...prev,
        x: prev.x + dx,
        y: prev.y + dy,
      }));
    },

    zoom(delta: number, centerX: number, centerY: number): void {
      store.setTransform(prev => {
        const factor = delta > 0 ? 0.9 : 1.1;
        const newScale = Math.max(MIN_SCALE, Math.min(MAX_SCALE, prev.scale * factor));
        const ratio = newScale / prev.scale;

        return {
          x: centerX - (centerX - prev.x) * ratio,
          y: centerY - (centerY - prev.y) * ratio,
          scale: newScale,
        };
      });
    },

    setScale(scale: number): void {
      store.setTransform(prev => ({
        ...prev,
        scale: Math.max(MIN_SCALE, Math.min(MAX_SCALE, scale)),
      }));
    },

    resetView(): void {
      store.setTransform({ x: 0, y: 0, scale: 1 });
    },

    fitToContent(contentRect: { x: number; y: number; width: number; height: number }, viewWidth: number, viewHeight: number): void {
      const padding = 50;
      const scaleX = (viewWidth - padding * 2) / contentRect.width;
      const scaleY = (viewHeight - padding * 2) / contentRect.height;
      const scale = Math.max(MIN_SCALE, Math.min(MAX_SCALE, Math.min(scaleX, scaleY)));

      store.setTransform({
        x: (viewWidth - contentRect.width * scale) / 2 - contentRect.x * scale,
        y: (viewHeight - contentRect.height * scale) / 2 - contentRect.y * scale,
        scale,
      });
    },
  };
}

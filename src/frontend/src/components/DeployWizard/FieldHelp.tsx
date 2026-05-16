import { Component, createSignal, createEffect, For, Show, onCleanup } from 'solid-js';
import type { CredentialField } from '../../types/deploy';

export const FieldHelp: Component<{ help: NonNullable<CredentialField['help']> }> = (props) => {
  const [open, setOpen] = createSignal(false);
  let containerRef: HTMLDivElement | undefined;

  const handleClickOutside = (e: MouseEvent) => {
    if (containerRef && !containerRef.contains(e.target as Node)) {
      setOpen(false);
    }
  };

  createEffect(() => {
    if (open()) {
      document.addEventListener('mousedown', handleClickOutside);
    } else {
      document.removeEventListener('mousedown', handleClickOutside);
    }
  });

  onCleanup(() => document.removeEventListener('mousedown', handleClickOutside));

  return (
    <div class="relative inline-block" ref={containerRef}>
      <button
        type="button"
        class="ml-1.5 w-4 h-4 rounded-full bg-topo-text-muted/20 text-topo-text-muted hover:bg-topo-brand/20 hover:text-topo-brand text-[10px] font-bold leading-none inline-flex items-center justify-center"
        onClick={(e) => { e.preventDefault(); setOpen(v => !v); }}
      >
        ?
      </button>
      <Show when={open()}>
        <div class="absolute left-6 top-0 z-50 w-80 bg-topo-bg-primary border border-topo-border rounded-lg shadow-xl p-3 text-xs">
          <div class="font-semibold text-topo-text-primary mb-2">{props.help.summary}</div>
          <ol class="list-decimal list-inside space-y-1 text-topo-text-secondary mb-2">
            <For each={props.help.steps}>
              {(step) => <li>{step}</li>}
            </For>
          </ol>
          <Show when={props.help.permissions}>
            <div class="bg-topo-warning/10 border border-topo-warning/20 rounded px-2 py-1.5 mb-2">
              <span class="font-medium text-topo-warning">Required permissions: </span>
              <span class="text-topo-text-secondary">{props.help.permissions}</span>
            </div>
          </Show>
          <Show when={props.help.note}>
            <p class="text-topo-text-muted italic mb-2">{props.help.note}</p>
          </Show>
          <Show when={props.help.url}>
            <a
              href={props.help.url!}
              target="_blank"
              rel="noopener noreferrer"
              class="text-topo-brand hover:underline font-medium"
            >
              View docs &rarr;
            </a>
          </Show>
        </div>
      </Show>
    </div>
  );
};

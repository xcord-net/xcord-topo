import { Component, onMount, onCleanup, createEffect } from 'solid-js';
import { EditorState } from '@codemirror/state';
import { EditorView, keymap, lineNumbers, highlightActiveLine, highlightActiveLineGutter, drawSelection } from '@codemirror/view';
import { defaultKeymap, indentWithTab, history, historyKeymap } from '@codemirror/commands';
import { json } from '@codemirror/lang-json';
import { syntaxHighlighting, indentOnInput, bracketMatching, foldGutter, foldKeymap, HighlightStyle } from '@codemirror/language';
import { tags } from '@lezer/highlight';
import { useTopology } from '../stores/topology.store';
import { useHistory } from '../stores/history.store';

/** Tokyo Night–flavored highlight style matching the topo theme. */
const topoHighlight = HighlightStyle.define([
  { tag: tags.string, color: '#9ece6a' },
  { tag: tags.number, color: '#ff9e64' },
  { tag: tags.bool, color: '#ff9e64' },
  { tag: tags.null, color: '#565f89' },
  { tag: tags.propertyName, color: '#7aa2f7' },
  { tag: tags.punctuation, color: '#a9b1d6' },
  { tag: tags.keyword, color: '#bb9af7' },
]);

const topoTheme = EditorView.theme({
  '&': {
    backgroundColor: '#1a1b26',
    color: '#c0caf5',
    fontSize: '13px',
    height: '100%',
  },
  '.cm-content': {
    fontFamily: 'ui-monospace, "Cascadia Code", "Fira Code", Menlo, Consolas, monospace',
    caretColor: '#c0caf5',
    padding: '8px 0',
  },
  '.cm-cursor, .cm-dropCursor': { borderLeftColor: '#c0caf5' },
  '&.cm-focused .cm-selectionBackground, .cm-selectionBackground': {
    backgroundColor: '#3b4261 !important',
  },
  '.cm-activeLine': { backgroundColor: '#1f2335' },
  '.cm-activeLineGutter': { backgroundColor: '#1f2335' },
  '.cm-gutters': {
    backgroundColor: '#1a1b26',
    color: '#565f89',
    borderRight: '1px solid #3b4261',
  },
  '.cm-lineNumbers .cm-gutterElement': { padding: '0 8px 0 4px' },
  '.cm-foldGutter .cm-gutterElement': { padding: '0 4px' },
  '.cm-matchingBracket': {
    backgroundColor: '#3b4261',
    color: '#c0caf5 !important',
  },
  '.cm-selectionMatch': { backgroundColor: '#3b426180' },
  '.cm-foldPlaceholder': {
    backgroundColor: '#24283b',
    border: '1px solid #3b4261',
    color: '#565f89',
  },
  '&.cm-focused': { outline: 'none' },
  '.cm-scroller': { overflow: 'auto' },
});

const SourceEditor: Component = () => {
  let containerRef: HTMLDivElement | undefined;
  let view: EditorView | undefined;
  let isExternalUpdate = false;

  const topo = useTopology();
  const history_ = useHistory();

  /** Serialize the current topology to pretty JSON. */
  const toJson = () => JSON.stringify(topo.topology, null, 2);

  onMount(() => {
    const state = EditorState.create({
      doc: toJson(),
      extensions: [
        lineNumbers(),
        highlightActiveLine(),
        highlightActiveLineGutter(),
        drawSelection(),
        indentOnInput(),
        bracketMatching(),
        foldGutter(),
        history(),
        keymap.of([
          indentWithTab,
          ...defaultKeymap,
          ...historyKeymap,
          ...foldKeymap,
        ]),
        json(),
        syntaxHighlighting(topoHighlight),
        topoTheme,
        EditorView.updateListener.of((update) => {
          if (isExternalUpdate) return;
          if (!update.docChanged) return;
          const text = update.state.doc.toString();
          try {
            const parsed = JSON.parse(text);
            history_.push(topo.getSnapshot());
            topo.load(parsed);
          } catch {
            // Invalid JSON - don't apply, user is still typing
          }
        }),
      ],
    });

    view = new EditorView({ state, parent: containerRef });
  });

  /** When the topology changes externally (undo/redo, canvas edits, etc.),
   *  push the new JSON into the editor without triggering a feedback loop. */
  createEffect(() => {
    const text = toJson();
    if (!view) return;
    const current = view.state.doc.toString();
    if (text === current) return;
    isExternalUpdate = true;
    view.dispatch({
      changes: { from: 0, to: view.state.doc.length, insert: text },
    });
    isExternalUpdate = false;
  });

  onCleanup(() => view?.destroy());

  return (
    <div ref={containerRef} class="flex-1 overflow-hidden" />
  );
};

export default SourceEditor;

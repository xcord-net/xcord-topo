import { Component, createSignal, Show } from 'solid-js';
import Canvas from './components/Canvas';
import Palette from './components/Palette';
import type { PaletteTab } from './components/Palette';
import Toolbar from './components/Toolbar';
import PropertiesPanel from './components/PropertiesPanel';
import SourceEditor from './components/SourceEditor';
import DeployWizard from './components/DeployWizard';
import { useInteraction } from './stores/interaction.store';

const App: Component = () => {
  const interaction = useInteraction();
  const [showDeploy, setShowDeploy] = createSignal(false);
  const [paletteTab, setPaletteTab] = createSignal<PaletteTab>('containers');

  const toggleDeploy = () => {
    setShowDeploy(prev => !prev);
  };

  return (
    <>
      <div class="h-screen w-screen flex flex-col bg-topo-bg-primary text-topo-text-primary overflow-hidden">
        <Toolbar onToggleDeploy={toggleDeploy} />
        <div class="flex flex-1 overflow-hidden">
          <Palette tab={paletteTab()} onTabChange={setPaletteTab} />
          <Show when={paletteTab() === 'source'} fallback={
            <>
              <div class="flex-1 relative">
                <Canvas />
              </div>
              <Show when={interaction.selectedNodeId !== null}>
                <PropertiesPanel />
              </Show>
            </>
          }>
            <SourceEditor />
          </Show>
        </div>
      </div>
      <Show when={showDeploy()}>
        <DeployWizard onClose={() => setShowDeploy(false)} />
      </Show>
    </>
  );
};

export default App;

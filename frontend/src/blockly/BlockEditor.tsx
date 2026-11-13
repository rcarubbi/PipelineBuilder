import { forwardRef, useEffect, useImperativeHandle, useRef } from 'react';
import * as Blockly from 'blockly/core';
import { seedWorkspace, toolbox } from './workspace';

export interface EditorHandle { workspace: () => Blockly.WorkspaceSvg }
interface Props { onChange: () => void; onSelect: (id: string | null) => void; onReady?: () => void; initialWorkspace?: Record<string, unknown> }

export const BlockEditor = forwardRef<EditorHandle, Props>(function BlockEditor({ onChange, onSelect, onReady, initialWorkspace }, ref) {
  const element = useRef<HTMLDivElement>(null);
  const workspace = useRef<Blockly.WorkspaceSvg | null>(null);
  const callbacks = useRef({ onChange, onSelect, onReady });
  callbacks.current = { onChange, onSelect, onReady };
  useImperativeHandle(ref, () => ({ workspace: () => {
    if (!workspace.current) throw new Error('Editor is not ready.');
    return workspace.current;
  } }), []);

  useEffect(() => {
    const current = Blockly.inject(element.current!, {
      toolbox, renderer: 'zelos', media: '/blockly/', sounds: false, trashcan: true,
      zoom: { controls: true, wheel: true, startScale: 0.85 },
      move: { scrollbars: true, drag: true, wheel: true },
    });
    workspace.current = current;
    Blockly.Events.disable();
    try {
      if (initialWorkspace) Blockly.serialization.workspaces.load(initialWorkspace, current);
      else seedWorkspace(current);
    } finally { Blockly.Events.enable(); }
    const listener = (event: Blockly.Events.Abstract) => {
      if (event.type === Blockly.Events.SELECTED) {
        const id = (event as Blockly.Events.Selected).newElementId;
        // Moving focus to the properties panel deselects Blockly, but must not close the inspector.
        if (id) callbacks.current.onSelect(id);
      }
      if (!event.isUiEvent) callbacks.current.onChange();
    };
    current.addChangeListener(listener);
    (callbacks.current.onReady ?? callbacks.current.onChange)();
    const observer = new ResizeObserver(() => Blockly.svgResize(current));
    observer.observe(element.current!);
    return () => { observer.disconnect(); current.removeChangeListener(listener); current.dispose(); workspace.current = null; };
  }, []);

  return <div ref={element} className="canvas" role="region" aria-label="Pipeline block canvas" />;
});

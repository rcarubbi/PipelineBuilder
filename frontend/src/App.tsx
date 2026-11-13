import { useCallback, useEffect, useRef, useState } from 'react';
import * as Blockly from 'blockly/core';
import { BlockEditor, type EditorHandle } from './blockly/BlockEditor';
import { compileWorkspace, getBlockData, schemaBefore } from './blockly/workspace';
import { BlockProperties } from './components/BlockProperties';
import { EnvironmentEditor, parseEnvironments } from './components/EnvironmentEditor';
import { IconButton, IconLink } from './components/Icon';
import { PageHeader } from './components/PageHeader';
import { api } from './pipeline/api';
import { inferSchemas, resolvePath, type Analysis, type Definition, type Run } from './pipeline/types';

const defaultEnvironments = { development: { INPUT_FILE: 'customers.csv', OUTPUT_FILE: 'customers.parquet' } };

export function App({ pipelineId }: { pipelineId?: string }) {
  const editor = useRef<EditorHandle>(null);
  const [id] = useState(pipelineId ?? crypto.randomUUID());
  const [revision, setRevision] = useState(0);
  const [name, setName] = useState('New pipeline');
  const [initialWorkspace, setInitialWorkspace] = useState<Record<string, unknown>>();
  const [loading, setLoading] = useState(!!pipelineId);
  const [environment, setEnvironment] = useState('development');
  const [environmentJson, setEnvironmentJson] = useState(JSON.stringify(defaultEnvironments, null, 2));
  const [environmentError, setEnvironmentError] = useState<string | null>(null);
  const [definition, setDefinition] = useState<Definition | null>(null);
  const [designError, setDesignError] = useState('');
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [view, setView] = useState<'canvas' | 'json'>('canvas');
  const [analysis, setAnalysis] = useState<Analysis | null>(null);
  const [run, setRun] = useState<Run | null>(null);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState('');
  const [error, setError] = useState('');
  const [changeNumber, setChangeNumber] = useState(0);
  const [savedAtChange, setSavedAtChange] = useState(-1);

  const refresh = useCallback(() => {
    setChangeNumber(n => n + 1);
    setAnalysis(null);
    try { setDefinition(compileWorkspace(editor.current!.workspace())); setDesignError(''); }
    catch (ex) { setDefinition(null); setDesignError((ex as Error).message); }
  }, []);
  const selected = editor.current?.workspace().getBlockById(selectedId ?? '') ?? null;
  const schemas = definition ? (analysis?.schemas ?? inferSchemas(definition)) : [];
  const selectedSchema = schemas.find(s => s.blockId === selectedId) ?? (selected ? {
    input: schemaBefore(selected),
    output: selected.type === 'pipeline_csv' ? getBlockData(selected).schema ?? []
      : selected.type === 'pipeline_map' ? (getBlockData(selected).mappings ?? []).map(m => m.target)
        : selected.type === 'pipeline_parquet' ? [] : schemaBefore(selected),
  } : null);
  const active = run?.status === 'Queued' || run?.status === 'Running';
  const dirty = savedAtChange !== changeNumber;

  useEffect(() => {
    if (!pipelineId) return;
    let disposed = false;
    api.load(pipelineId).then(result => {
      if (disposed) return;
      setName(result.document.name); setRevision(result.revision);
      setEnvironmentJson(JSON.stringify(result.document.environments, null, 2));
      setEnvironment(Object.keys(result.document.environments)[0]);
      setInitialWorkspace(result.document.workspace);
    }).catch(ex => { if (!disposed) setError(ex instanceof Error ? ex.message : 'Could not load pipeline.'); })
      .finally(() => { if (!disposed) setLoading(false); });
    return () => { disposed = true; };
  }, [pipelineId]);

  function environments(): Record<string, Record<string, string>> {
    if (environmentError) throw new Error(environmentError);
    return parseEnvironments(environmentJson);
  }

  function changeEnvironments(json: string, error: string | null) {
    setEnvironmentJson(json); setEnvironmentError(error); setChangeNumber(n => n + 1);
    if (error) return;
    // Keep the selected environment valid when it is renamed or removed.
    const names = Object.keys(parseEnvironments(json));
    if (!names.includes(environment)) setEnvironment(names[0]);
  }

  async function perform(action: () => Promise<void>) {
    setBusy(true); setError(''); setMessage('');
    try { await action(); }
    catch (ex) { setError(ex instanceof Error ? ex.message : 'Operation failed.'); }
    finally { setBusy(false); }
  }

  async function save() {
    const workspace = editor.current!.workspace();
    const result = await api.save(id, revision, { name, definition: compileWorkspace(workspace), workspace: Blockly.serialization.workspaces.save(workspace), environments: environments() });
    setRevision(result.revision); setSavedAtChange(changeNumber);
    history.replaceState(null, '', `?page=designer&pipeline=${id}`);
    setMessage(`Saved revision ${result.revision}.`);
    return result;
  }

  async function exportSaved() {
    if (dirty) throw new Error('Save changes before exporting.');
    const saved = await api.load(id);
    const { downloadPipeline } = await import('./pipeline/exchange');
    setMessage(`Exported ${downloadPipeline(saved)}.`);
  }

  async function discover() {
    if (!selected || selected.type !== 'pipeline_csv') return;
    const variables = environments()[environment];
    if (!variables) throw new Error('Select an existing environment.');
    const result = await api.discover(resolvePath(selected.getFieldValue('PATH'), variables), selected.getFieldValue('DELIMITER'), selected.getFieldValue('HEADER') === 'TRUE');
    selected.data = JSON.stringify({ schema: result.schema }); refresh();
    setMessage(`Sampled up to ${result.sampleLimit} records. Review and save the proposed schema.`);
  }

  function moveSelected(direction: -1 | 1) {
    if (!selected || !['pipeline_map', 'pipeline_filter'].includes(selected.type)) return;
    const neighbor = direction === -1 ? selected.getPreviousBlock() : selected.getNextBlock();
    if (!neighbor || !['pipeline_map', 'pipeline_filter'].includes(neighbor.type)) return;
    const first = direction === -1 ? neighbor : selected;
    const second = direction === -1 ? selected : neighbor;
    const before = first.previousConnection!.targetConnection!;
    const after = second.nextConnection!.targetConnection;
    first.previousConnection!.disconnect(); second.previousConnection!.disconnect();
    if (after) second.nextConnection!.disconnect();
    before.connect(second.previousConnection!); second.nextConnection!.connect(first.previousConnection!);
    if (after) first.nextConnection!.connect(after);
    refresh();
  }

  function extendFilter(operator: 'and' | 'or' | 'not', nullCheck = false) {
    if (!selected || selected.type !== 'pipeline_filter') return;
    const workspace = editor.current!.workspace();
    const existing = selected.getInputTargetBlock('CRITERIA');
    if (!existing) return;
    existing.outputConnection!.disconnect();
    const parent = workspace.newBlock(operator === 'not' ? 'predicate_not' : 'predicate_logic');
    if (operator !== 'not') parent.setFieldValue(operator, 'OP');
    parent.getInput(operator === 'not' ? 'VALUE' : 'LEFT')!.connection!.connect(existing.outputConnection!);
    if (operator !== 'not') {
      const right = workspace.newBlock(nullCheck ? 'predicate_null' : 'predicate_compare');
      right.initSvg(); right.render();
      parent.getInput('RIGHT')!.connection!.connect(right.outputConnection!);
    }
    parent.initSvg(); parent.render();
    selected.getInput('CRITERIA')!.connection!.connect(parent.outputConnection!);
    refresh();
  }

  useEffect(() => {
    if (!active || !run) return;
    let disposed = false;
    const timer = window.setInterval(() => {
      api.getRun(run.id).then(result => { if (!disposed) setRun(result); }).catch(ex => { if (!disposed) setError((ex as Error).message); });
    }, 1000);
    return () => { disposed = true; window.clearInterval(timer); };
  }, [run?.id, active]);

  useEffect(() => {
    const listener = (event: BeforeUnloadEvent) => { if (dirty && !loading && (!pipelineId || initialWorkspace)) event.preventDefault(); };
    window.addEventListener('beforeunload', listener);
    return () => window.removeEventListener('beforeunload', listener);
  }, [dirty, loading, pipelineId, initialWorkspace]);

  let environmentNames: string[] = [];
  try { environmentNames = Object.keys(environments()); } catch { /* Keep invalid JSON editable. */ }

  if (loading || pipelineId && !initialWorkspace) return <>
    <PageHeader page="Designer" />
    <main><IconLink icon="list" label="Back to pipelines" href="/" />
      {loading ? <p role="status">Loading pipeline…</p> : <p role="alert" className="notice error">{error}</p>}
    </main>
  </>;

  return <>
    <PageHeader page="Designer" />
    <main>
      <section className="toolbar" aria-label="Pipeline actions">
        <IconLink icon="list" label="Back to pipelines" href="/" />
        <label>Pipeline name<input value={name} onChange={e => { setName(e.target.value); setChangeNumber(n => n + 1); }} /></label>
        <label>Environment<select value={environment} onChange={e => setEnvironment(e.target.value)}>{environmentNames.map(env => <option key={env}>{env}</option>)}</select></label>
        <div className="actions">
          <IconButton icon="check" label="Validate" disabled={busy || !definition} onClick={() => void perform(async () => { const result = await api.validate(compileWorkspace(editor.current!.workspace())); setAnalysis(result); setMessage(result.issues.length ? 'Fix the highlighted validation issues.' : 'Pipeline is valid.'); })} />
          <IconButton icon="save" label="Save" disabled={busy || !definition} onClick={() => void perform(async () => { await save(); })} />
          <IconButton icon="export" label="Export pipeline as JSON" disabled={busy || revision === 0} onClick={() => void perform(exportSaved)} />
          <IconButton icon="play" label="Save & run" className="primary" disabled={busy || active || !definition} onClick={() => void perform(async () => {
            const saved = await save();
            const started = await api.run(saved.id, saved.revision, environment);
            setRun(started); setMessage(`Run ${started.id} queued. Results are available in execution logs.`);
          })} />
        </div>
        <span className="muted">Revision {revision} · {dirty ? 'unsaved changes' : 'saved'}</span>
      </section>
      {error && <div role="alert" className="notice error">{error}</div>}
      {designError && <div role="status" className="notice">{designError}</div>}
      {message && <div role="status" className="notice">{message}</div>}
      <div className="designer">
        <section className="workspace">
          <div className="view-switch" role="tablist" aria-label="Designer view">
            <IconButton icon="blocks" label="Canvas" role="tab" aria-selected={view === 'canvas'} aria-controls="designer-canvas" onClick={() => setView('canvas')} />
            <IconButton icon="code" label="Executable JSON (read-only)" role="tab" aria-selected={view === 'json'} aria-controls="designer-json" onClick={() => setView('json')} />
            <span className="muted">{view === 'canvas' ? 'Canvas' : 'Executable JSON · read-only, edit on the canvas'}</span>
          </div>
          {/* The canvas stays mounted while hidden so Blockly keeps its state. */}
          <div id="designer-canvas" className="canvas-panel" role="tabpanel" aria-label="Canvas" hidden={view !== 'canvas'}>
            <BlockEditor ref={editor} initialWorkspace={initialWorkspace} onChange={refresh} onSelect={setSelectedId}
              onReady={() => { refresh(); if (pipelineId) setSavedAtChange(changeNumber + 1); }} />
          </div>
          {view === 'json' && <div id="designer-json" className="json-panel" role="tabpanel" aria-label="Executable JSON">
            <pre className="json-view" tabIndex={0} aria-readonly="true">{definition ? JSON.stringify(definition, null, 2) : `Pipeline is incomplete: ${designError}`}</pre>
          </div>}
        </section>
        <aside>
          {selected ? <>
            <BlockProperties block={selected} inputSchema={selectedSchema?.input ?? []} onChange={refresh} onDiscover={() => void perform(discover)} />
            {selected.type === 'pipeline_filter' && <div className="actions">
              <IconButton icon="and" label="Add AND criterion" onClick={() => extendFilter('and')} /><IconButton icon="or" label="Add OR criterion" onClick={() => extendFilter('or')} />
              <IconButton icon="not" label="Negate criteria" onClick={() => extendFilter('not')} /><IconButton icon="null" label="Add AND null check" onClick={() => extendFilter('and', true)} />
            </div>}
            {['pipeline_map', 'pipeline_filter'].includes(selected.type) && <div className="actions">
              <IconButton icon="up" label="Move step up" onClick={() => moveSelected(-1)} /><IconButton icon="down" label="Move step down" onClick={() => moveSelected(1)} />
              <IconButton icon="trash" label="Remove step" onClick={() => { selected.dispose(true); setSelectedId(null); refresh(); }} />
            </div>}
            {selectedSchema && <section><h2>Inferred schemas</h2>{(['input', 'output'] as const).map(side => <div key={side}>
              <h3>{side}</h3><ul>{selectedSchema[side].map(c => <li key={c.name}><code>{c.name}</code>: {c.type}{c.nullable ? ' (nullable)' : ''}</li>)}</ul>
              {selectedSchema[side].length === 0 && <p className="muted">None</p>}
            </div>)}</section>}
          </> : <p>Select a block to edit its properties and inspect its schema.</p>}
        </aside>
      </div>
      {analysis && analysis.issues.length > 0 && <section aria-label="Validation issues"><h2>Validation issues</h2><ul>{analysis.issues.map((issue, index) => <li key={index}><button onClick={() => { const block = editor.current!.workspace().getBlockById(issue.blockId); block?.select(); setSelectedId(issue.blockId); }}>{issue.message}</button></li>)}</ul></section>}
      <EnvironmentEditor json={environmentJson} onChange={changeEnvironments} />
    </main>
  </>;
}

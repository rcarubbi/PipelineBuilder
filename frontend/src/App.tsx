import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react';
import * as Blockly from 'blockly/core';
import { BlockEditor, type EditorHandle } from './blockly/BlockEditor';
import { compileEnvironments, compileWorkspace, getBlockData, resolveWorkspaceText, restoreEnvironmentBlocks, schemaBefore } from './blockly/workspace';
import { BlockProperties } from './components/BlockProperties';
import { Icon, IconButton, IconLink } from './components/Icon';
import { PageHeader } from './components/PageHeader';
import { ConfirmDialog } from './components/ConfirmDialog';
import { Loading } from './components/Loading';
import { Input } from './components/ui/input';
import { Select } from './components/ui/select';
import { Button } from './components/ui/button';
import { Card } from './components/ui/card';
import { Tabs, TabsContent, TabsList, TabsTrigger } from './components/ui/tabs';
import { Toast, Toaster } from './components/Toast';
import { api } from './pipeline/api';
import { inferSchemas, type Analysis, type Definition, type Run, type SavedPipeline } from './pipeline/types';

export function App({ pipelineId, initialPipeline }: { pipelineId?: string; initialPipeline?: SavedPipeline }) {
  const editor = useRef<EditorHandle>(null);
  const designer = useRef<HTMLDivElement>(null);
  const [id] = useState(pipelineId ?? crypto.randomUUID());
  const [revision, setRevision] = useState(initialPipeline?.revision ?? 0);
  const [name, setName] = useState(initialPipeline?.document.name ?? 'New pipeline');
  const [initialWorkspace, setInitialWorkspace] = useState<Record<string, unknown> | undefined>(() =>
    initialPipeline ? restoreEnvironmentBlocks(initialPipeline.document.workspace, initialPipeline.document.environments) : undefined);
  const [loading, setLoading] = useState(!!pipelineId && !initialPipeline);
  const [environment, setEnvironment] = useState(Object.keys(initialPipeline?.document.environments ?? {})[0] ?? 'development');
  const [workspaceEnvironments, setWorkspaceEnvironments] = useState<Record<string, Record<string, string>>>({});
  const [definition, setDefinition] = useState<Definition | null>(null);
  const [designError, setDesignError] = useState('');
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [view, setView] = useState<'canvas' | 'json'>('canvas');
  const [propertiesOpen, setPropertiesOpen] = useState(true);
  const [analysis, setAnalysis] = useState<Analysis | null>(null);
  const [run, setRun] = useState<Run | null>(null);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState('');
  const [error, setError] = useState('');
  const [, setChangeNumber] = useState(0);
  const savedSignature = useRef<string | null>(null);
  const allowUnload = useRef(false);
  const [leaveUrl, setLeaveUrl] = useState<string | null>(null);
  const [deletingBlock, setDeletingBlock] = useState<string | null>(null);
  const [runNotice, setRunNotice] = useState<string | null>(null);

  const refresh = useCallback(() => {
    setChangeNumber(n => n + 1);
    setAnalysis(null);
    try {
      const workspace = editor.current!.workspace();
      const currentEnvironments = compileEnvironments(workspace);
      setWorkspaceEnvironments(currentEnvironments);
      setDefinition(compileWorkspace(workspace)); setDesignError('');
    }
    catch (ex) { setDefinition(null); setDesignError((ex as Error).message); }
  }, []);
  const environmentNames = Object.keys(workspaceEnvironments);
  const availableEnvironments = environmentNames.length ? environmentNames : ['development'];
  useEffect(() => {
    if (environmentNames.length && !environmentNames.includes(environment)) setEnvironment(environmentNames[0]);
  }, [availableEnvironments.join('\0'), environment]);
  const selected = editor.current?.workspace().getBlockById(selectedId ?? '') ?? null;
  const schemas = definition ? (analysis?.schemas ?? inferSchemas(definition)) : [];
  const selectedSchema = schemas.find(s => s.blockId === selectedId) ?? (selected ? {
    input: schemaBefore(selected),
    output: ['pipeline_csv', 'pipeline_parquetInput', 'pipeline_excelInput', 'pipeline_postgresInput'].includes(selected.type) ? getBlockData(selected).schema ?? []
      : selected.type === 'pipeline_map' ? (getBlockData(selected).mappings ?? []).map(m => m.target)
        : ['pipeline_parquet', 'pipeline_csvOutput', 'pipeline_excelOutput'].includes(selected.type) ? [] : schemaBefore(selected),
  } : null);
  const active = run?.status === 'Queued' || run?.status === 'Running';
  const signature = () => JSON.stringify({ name, workspace: Blockly.serialization.workspaces.save(editor.current!.workspace()) });
  const dirty = revision === 0 || (!!editor.current && signature() !== savedSignature.current);

  useEffect(() => {
    if (!pipelineId || initialPipeline) return;
    let disposed = false;
    api.load(pipelineId).then(result => {
      if (disposed) return;
      setName(result.document.name); setRevision(result.revision);
      setEnvironment(Object.keys(result.document.environments)[0] ?? 'development');
      setInitialWorkspace(restoreEnvironmentBlocks(result.document.workspace, result.document.environments));
    }).catch(ex => { if (!disposed) setError(ex instanceof Error ? ex.message : 'Could not load pipeline.'); })
      .finally(() => { if (!disposed) setLoading(false); });
    return () => { disposed = true; };
  }, [pipelineId, initialPipeline]);

  async function perform(action: () => Promise<void>) {
    setBusy(true); setError(''); setMessage(''); setRunNotice(null);
    try { await action(); }
    catch (ex) { setError(ex instanceof Error ? ex.message : 'Operation failed.'); }
    finally { setBusy(false); }
  }

  async function save() {
    const workspace = editor.current!.workspace();
    const workspaceState = Blockly.serialization.workspaces.save(workspace);
    const environments = compileEnvironments(workspace);
    const currentSignature = JSON.stringify({ name, workspace: workspaceState });
    if (revision > 0 && currentSignature === savedSignature.current) return { id, revision };
    const result = await api.save(id, revision, { name, definition: compileWorkspace(workspace), workspace: workspaceState, environments });
    savedSignature.current = currentSignature;
    setRevision(result.revision); setChangeNumber(n => n + 1);
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
    if (!selected || !['pipeline_csv', 'pipeline_parquetInput', 'pipeline_excelInput', 'pipeline_apiInput', 'pipeline_postgresInput'].includes(selected.type)) return;
    if (selected.type === 'pipeline_postgresInput') {
      const configuration = getBlockData(selected);
      const postgres = configuration.postgres ?? {};
      const workspace = editor.current!.workspace();
      const resolve = (name: string, defaultValue = '') => resolveWorkspaceText(
        String(postgres[`${name}Variable`] || postgres[name] || defaultValue), workspace, workspaceEnvironments, environment);
      const result = await api.discover('', ',', true, 'postgres', undefined, undefined, {
        type: 'postgresInput', id: selected.id, host: resolve('host', 'localhost'), port: resolve('port', '5432'),
        database: resolve('database'), username: resolve('username'), password: resolve('password'),
        sslMode: resolve('sslMode', 'Prefer'), query: resolve('query'), schema: [],
      });
      selected.data = JSON.stringify({ ...configuration, schema: result.schema });
      refresh();
      setMessage('PostgreSQL schema discovered. Review the columns and save the pipeline.');
      return;
    }
    if (selected.type === 'pipeline_apiInput') {
      const configuration = getBlockData(selected);
      const apiConfig = configuration.api ?? {};
      const result = await api.discover('', ',', true, 'api', undefined, {
        type: 'apiInput', id: selected.id, url: String(apiConfig.url ?? ''), schema: [], dataPath: String(apiConfig.dataPath ?? ''),
        method: apiConfig.method ?? 'GET', format: apiConfig.format ?? 'auto', authentication: apiConfig.authentication ?? 'none',
        username: apiConfig.username, password: apiConfig.password, token: apiConfig.token, apiKeyName: apiConfig.apiKeyName,
        apiKey: apiConfig.apiKey, oauthTokenUrl: apiConfig.oauthTokenUrl, clientId: apiConfig.clientId,
        clientSecret: apiConfig.clientSecret, scope: apiConfig.scope, nextPath: apiConfig.nextPath,
        useLinkHeader: Boolean(apiConfig.useLinkHeader), body: apiConfig.body, headers: apiConfig.headers,
      });
      selected.data = JSON.stringify({
        ...configuration,
        schema: result.schema,
        api: { ...apiConfig, dataPaths: result.dataPaths ?? [], dataPath: apiConfig.dataPath || result.dataPaths?.[0] || '' },
      });
      refresh();
      setMessage('Sampled API response. Review and save the proposed schema.');
      return;
    }
    const path = selected.getInputTargetBlock('PATH');
    if (!path || path.type !== 'value_literal') throw new Error('Schema discovery requires a literal file path.');
    const format = selected.type === 'pipeline_csv' ? 'csv' : selected.type === 'pipeline_parquetInput' ? 'parquet' : 'excel';
    const result = await api.discover(path.getFieldValue('VALUE'), selected.type === 'pipeline_csv' ? selected.getFieldValue('DELIMITER') : ',', selected.type !== 'pipeline_excelInput' || selected.getFieldValue('HEADER') === 'TRUE', format, selected.type === 'pipeline_excelInput' ? selected.getFieldValue('SHEET') : undefined);
    selected.data = JSON.stringify({ schema: result.schema }); refresh();
    setMessage(format === 'parquet' ? 'Loaded Parquet metadata. Review and save the proposed schema.' : `Sampled up to ${result.sampleLimit} records. Review and save the proposed schema.`);
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
    const listener = (event: BeforeUnloadEvent) => { if (!allowUnload.current && dirty && !loading && (!pipelineId || initialWorkspace)) event.preventDefault(); };
    window.addEventListener('beforeunload', listener);
    return () => window.removeEventListener('beforeunload', listener);
  }, [dirty, loading, pipelineId, initialWorkspace]);

  useEffect(() => {
    const listener = (event: MouseEvent) => {
      if (!dirty || event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
      const anchor = (event.target as Element).closest('a[href]') as HTMLAnchorElement | null;
      if (!anchor || anchor.target && anchor.target !== '_self' || anchor.hasAttribute('download')) return;
      event.preventDefault();
      setLeaveUrl(anchor.href);
    };
    document.addEventListener('click', listener);
    return () => document.removeEventListener('click', listener);
  }, [dirty]);

  useLayoutEffect(() => {
    const element = designer.current;
    const main = element?.closest('main');
    if (!element || !main) return;
    const resize = () => {
      const bottomPadding = Number.parseFloat(getComputedStyle(main).paddingBottom) || 0;
      element.style.height = `${Math.max(0, window.innerHeight - element.getBoundingClientRect().top - bottomPadding - 16)}px`;
    };
    resize();
    const observer = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(resize);
    observer?.observe(main);
    window.addEventListener('resize', resize);
    return () => { observer?.disconnect(); window.removeEventListener('resize', resize); };
  }, [loading, initialWorkspace]);

  if (loading || pipelineId && !initialWorkspace) return <>
    <PageHeader page="Designer" />
    <main><IconLink icon="list" label="Back to pipelines" href="/" />
      {loading ? <Loading label="Loading pipeline…" /> : <Toaster><Toast key={`load-error:${error}`} variant="destructive">{error}</Toast></Toaster>}
    </main>
  </>;

  return <>
    <PageHeader page="Designer" />
    <main>
      <Card className="toolbar" aria-label="Pipeline actions">
        <IconLink icon="list" label="Back to pipelines" href="/" />
        <label>Pipeline name<Input value={name} onChange={e => { setName(e.target.value); setChangeNumber(n => n + 1); }} /></label>
        <label>Environment<Select value={environment} onChange={e => setEnvironment(e.target.value)}>{availableEnvironments.map(env => <option key={env}>{env}</option>)}</Select></label>
        <div className="actions">
          <IconButton icon="check" label="Validate" disabled={busy || !definition} onClick={() => void perform(async () => { const result = await api.validate(compileWorkspace(editor.current!.workspace())); setAnalysis(result); setMessage(result.issues.length ? 'Fix the highlighted validation issues.' : 'Pipeline is valid.'); })} />
          <IconButton icon="save" label="Save" disabled={busy || !definition} onClick={() => void perform(async () => { await save(); })} />
          <IconButton icon="export" label="Export pipeline as JSON" disabled={busy || revision === 0} onClick={() => void perform(exportSaved)} />
          <IconButton icon="play" label="Save & run" className="primary" disabled={busy || active || !definition} onClick={() => void perform(async () => {
            const saved = await save();
            const started = await api.run(saved.id, saved.revision, environment);
            setRun(started); setMessage(''); setRunNotice(started.id);
          })} />
        </div>
        <span className="muted">Revision {revision} · {dirty ? 'unsaved changes' : 'saved'}</span>
      </Card>
      {(error || designError || message || runNotice) && <Toaster>
        {error && <Toast key={`error:${error}`} variant="destructive">{error}</Toast>}
        {designError && <Toast key={`design:${designError}`}>{designError}</Toast>}
        {message && <Toast key={`message:${message}`}>{message}</Toast>}
        {runNotice && <Toast key={`run:${runNotice}`}>Run {runNotice} queued. Results are available in <a className="toast-link" href={`?page=logs&pipeline=${encodeURIComponent(id)}`}>execution logs</a>.</Toast>}
      </Toaster>}
      <div ref={designer} className={`designer${propertiesOpen ? '' : ' properties-collapsed'}`}>
        <section className="workspace">
          <Tabs className="designer-tabs" value={view} onValueChange={value => setView(value as 'canvas' | 'json')}>
            <div className="view-switch">
              <TabsList aria-label="Designer view">
                <TabsTrigger value="canvas" aria-label="Canvas"><Icon name="blocks" /></TabsTrigger>
                <TabsTrigger value="json" aria-label="Pipeline JSON (read-only)"><Icon name="code" /></TabsTrigger>
              </TabsList>
              <span className="muted">{view === 'canvas' ? 'Canvas' : 'Pipeline JSON · read-only; contains environment values'}</span>
              <IconButton icon={propertiesOpen ? 'right' : 'left'} className="properties-toggle"
                label={propertiesOpen ? 'Collapse properties' : 'Expand properties'} aria-expanded={propertiesOpen}
                aria-controls="designer-properties" onClick={() => setPropertiesOpen(open => !open)} />
            </div>
          {/* The canvas stays mounted while hidden so Blockly keeps its state. */}
          <TabsContent value="canvas" forceMount id="designer-canvas" className="canvas-panel" role="tabpanel" aria-label="Canvas" hidden={view !== 'canvas'}>
            <BlockEditor ref={editor} initialWorkspace={initialWorkspace} onChange={refresh} onSelect={setSelectedId}
              onReady={() => { if (pipelineId) savedSignature.current = signature(); refresh(); }} />
          </TabsContent>
          <TabsContent value="json" forceMount id="designer-json" className="json-panel" role="tabpanel" aria-label="Pipeline JSON" hidden={view !== 'json'}>
            <pre className="json-view" tabIndex={0} aria-readonly="true">{definition ? JSON.stringify({ ...definition, environments: workspaceEnvironments }, null, 2) : `Pipeline is incomplete: ${designError}`}</pre>
          </TabsContent>
          </Tabs>
        </section>
        <aside id="designer-properties" aria-label="Properties" aria-hidden={!propertiesOpen} inert={!propertiesOpen}>
          {selected ? <>
            <div className="properties-content">
            <BlockProperties block={selected} inputSchema={selectedSchema?.input ?? []} onChange={refresh} onDiscover={() => void perform(discover)} />
            {selected.type === 'pipeline_filter' && <div className="actions">
              <IconButton icon="and" label="Add AND criterion" onClick={() => extendFilter('and')} /><IconButton icon="or" label="Add OR criterion" onClick={() => extendFilter('or')} />
              <IconButton icon="not" label="Negate criteria" onClick={() => extendFilter('not')} /><IconButton icon="null" label="Add AND null check" onClick={() => extendFilter('and', true)} />
            </div>}
            {['pipeline_map', 'pipeline_filter'].includes(selected.type) && <div className="actions">
              <IconButton icon="up" label="Move step up" onClick={() => moveSelected(-1)} /><IconButton icon="down" label="Move step down" onClick={() => moveSelected(1)} />
              <IconButton icon="trash" label="Remove step" onClick={() => setDeletingBlock(selected.id)} />
            </div>}
            </div>
            {selectedSchema && <section className="inferred-schemas" aria-label="Inferred schemas"><h2>Inferred schemas</h2>{(['input', 'output'] as const).map(side => <div key={side}>
              <h3>{side}</h3><ul>{selectedSchema[side].map(c => <li key={c.name}><code>{c.name}</code>: {c.type}{c.nullable ? ' (nullable)' : ''}</li>)}</ul>
              {selectedSchema[side].length === 0 && <p className="muted">None</p>}
            </div>)}</section>}
          </> : <p>Select a block to edit its properties and inspect its schema.</p>}
        </aside>
      </div>
      {analysis && analysis.issues.length > 0 && <section aria-label="Validation issues"><h2>Validation issues</h2><ul>{analysis.issues.map((issue, index) => <li key={index}><Button variant="ghost" onClick={() => { const block = editor.current!.workspace().getBlockById(issue.blockId); block?.select(); setSelectedId(issue.blockId); }}>{issue.message}</Button></li>)}</ul></section>}
      <ConfirmDialog open={!!leaveUrl} title="Leave designer?" description="Your unsaved changes will be lost." confirmLabel="Leave without saving" onCancel={() => setLeaveUrl(null)} onConfirm={() => { if (leaveUrl) { allowUnload.current = true; window.location.assign(leaveUrl); } }} />
      <ConfirmDialog open={!!deletingBlock} title="Remove step?" description="This step will be removed from the pipeline." confirmLabel="Remove step" onCancel={() => setDeletingBlock(null)} onConfirm={() => { if (!deletingBlock) return; editor.current?.workspace().getBlockById(deletingBlock)?.dispose(true); setDeletingBlock(null); setSelectedId(null); refresh(); }} />
    </main>
  </>;
}

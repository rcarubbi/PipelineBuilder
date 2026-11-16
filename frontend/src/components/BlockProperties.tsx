import type * as Blockly from 'blockly/core';
import { ColumnEditor } from './ColumnEditor';
import { IconButton } from './Icon';
import { getBlockData } from '../blockly/workspace';
import { columnTypes, type Column, type Mapping } from '../pipeline/types';
import { VariableModeField } from './VariableModeField';

interface Props { block: Blockly.Block; inputSchema: Column[]; onChange: () => void; onDiscover: () => void }

export function BlockProperties({ block, inputSchema, onChange, onDiscover }: Props) {
  function field(name: string, value: string) { block.setFieldValue(value, name); onChange(); }
  function data(changes: { schema?: Column[]; mappings?: Mapping[]; api?: Record<string, unknown>; hasHeaderVariable?: string; predicate?: Record<string, unknown> }) { block.data = JSON.stringify({ ...getBlockData(block), ...changes }); onChange(); }
  const configuration = getBlockData(block);
  const mappings = configuration.mappings ?? [];
  const headerOption = () => <VariableModeField label="Header row" variable={!!configuration.hasHeaderVariable}
    variableValue={configuration.hasHeaderVariable ?? ''} placeholder="${env.HAS_HEADER}"
    onModeChange={mode => data({ hasHeaderVariable: mode ? configuration.hasHeaderVariable || '${env.HAS_HEADER}' : '' })}
    onVariableChange={hasHeaderVariable => data({ hasHeaderVariable })}>
    <label className="inline field-control"><input type="checkbox" checked={block.getFieldValue('HEADER') === 'TRUE'}
      onChange={e => field('HEADER', e.target.checked ? 'TRUE' : 'FALSE')} />Header row</label>
  </VariableModeField>;
  function mapping(index: number, changes: Partial<Mapping>) { data({ mappings: mappings.map((m, i) => i === index ? { ...m, ...changes } : m) }); }

  return <section aria-label="Block properties">
    <h2>{block.type.replace('pipeline_', '').replace('predicate_', '')} properties</h2>
    {['pipeline_csv', 'pipeline_parquet', 'pipeline_csvOutput', 'pipeline_parquetInput', 'pipeline_excelInput', 'pipeline_excelOutput'].includes(block.type) &&
      <p>Connect a text expression to set the server file path.</p>}
    {['pipeline_excelInput', 'pipeline_excelOutput'].includes(block.type) && <>
      <label>Sheet name<input value={block.getFieldValue('SHEET')} onChange={e => field('SHEET', e.target.value)} /></label>
      {headerOption()}
    </>}
    {['pipeline_parquetInput', 'pipeline_excelInput'].includes(block.type) && <>
      <IconButton icon="search" label="Discover schema" onClick={onDiscover} />
      <ColumnEditor columns={configuration.schema ?? []} onChange={schema => data({ schema })} />
    </>}
    {block.type === 'pipeline_apiInput' && (() => {
      const api = configuration.api ?? {};
      const dataPaths = Array.isArray(api.dataPaths) ? api.dataPaths as string[] : [];
      const setApi = (name: string, value: unknown) => data({ api: { ...(getBlockData(block).api ?? {}), [name]: value } });
      const text = (name: string, label: string, placeholder = '') => <label>{label}<input value={String(api[name] ?? '')} placeholder={placeholder} onChange={e => setApi(name, e.target.value)} /></label>;
      return <>
        {text('url', 'URL', 'https://example.com/api/items')}
        <VariableModeField label="Method" variable={!!api.methodVariable} variableValue={String(api.methodVariable ?? '')} placeholder="${var.METHOD}"
          onModeChange={mode => { if (mode && !api.methodVariable) setApi('methodVariable', '${var.METHOD}'); else if (!mode) setApi('methodVariable', ''); }}
          onVariableChange={value => setApi('methodVariable', value)}>
          <label className="field-control"><span className="visually-hidden">Method</span><select value={String(api.method ?? 'GET')} onChange={e => setApi('method', e.target.value)}><option>GET</option><option>POST</option></select></label>
        </VariableModeField>
        <VariableModeField label="Response format" variable={!!api.formatVariable} variableValue={String(api.formatVariable ?? '')} placeholder="${var.FORMAT}"
          onModeChange={mode => { if (mode && !api.formatVariable) setApi('formatVariable', '${var.FORMAT}'); else if (!mode) setApi('formatVariable', ''); }}
          onVariableChange={value => setApi('formatVariable', value)}>
          <label className="field-control"><span className="visually-hidden">Response format</span><select value={String(api.format ?? 'auto')} onChange={e => setApi('format', e.target.value)}><option value="auto">Auto (Content-Type)</option><option value="json">JSON</option><option value="xml">XML</option></select></label>
        </VariableModeField>
        <label>Rows path<select aria-label="Discovered response paths" value={String(api.dataPath ?? '')} onChange={e => {
          setApi('dataPath', e.target.value);
          onDiscover();
        }}>
          <option value="">Response root</option>
          {dataPaths.map(path => <option key={path} value={path}>{path.replace(/[./]/g, ' › ')}</option>)}
          {Boolean(api.dataPath) && !dataPaths.includes(String(api.dataPath)) &&
            <option value={String(api.dataPath)}>{String(api.dataPath).replace(/[./]/g, ' › ')}</option>}
        </select></label>
        {text('dataPath', 'Custom JSON/XML path')}
        {text('body', 'POST body (JSON)')}
        <label>Custom headers (JSON object)<textarea value={JSON.stringify(api.headers ?? {})} onChange={e => {
          try { setApi('headers', JSON.parse(e.target.value)); } catch { /* Keep the last valid serialized header map. */ }
        }} /></label>
        <VariableModeField label="Authentication" variable={!!api.authenticationVariable} variableValue={String(api.authenticationVariable ?? '')} placeholder="${var.AUTH_MODE}"
          onModeChange={mode => { if (mode && !api.authenticationVariable) setApi('authenticationVariable', '${var.AUTH_MODE}'); else if (!mode) setApi('authenticationVariable', ''); }}
          onVariableChange={value => setApi('authenticationVariable', value)}>
          <label className="field-control"><span className="visually-hidden">Authentication</span><select value={String(api.authentication ?? 'none')} onChange={e => setApi('authentication', e.target.value)}><option value="none">None</option><option value="basic">Basic</option><option value="bearer">Bearer</option><option value="apiKey">API key header</option><option value="oauth2">OAuth 2.0 Client Credentials</option></select></label>
        </VariableModeField>
        {(api.authentication === 'basic' || api.authenticationVariable) && <>{text('username', 'Username')}{text('password', 'Password', '${secret.NAME}')}</>}
        {(api.authentication === 'bearer' || api.authenticationVariable) && text('token', 'Bearer token', '${secret.NAME}')}
        {(api.authentication === 'apiKey' || api.authenticationVariable) && <>{text('apiKeyName', 'API key header')}{text('apiKey', 'API key', '${secret.NAME}')}</>}
        {(api.authentication === 'oauth2' || api.authenticationVariable) && <>{text('oauthTokenUrl', 'Token URL')}{text('clientId', 'Client ID')}{text('clientSecret', 'Client secret', '${secret.NAME}')}{text('scope', 'Scope (optional)')}</>}
        {text('nextPath', 'HATEOAS next path (optional)')}
        <VariableModeField label="Link rel=next header" variable={!!api.useLinkHeaderVariable} variableValue={String(api.useLinkHeaderVariable ?? '')} placeholder="${env.USE_LINK_HEADER}"
          onModeChange={mode => { if (mode && !api.useLinkHeaderVariable) setApi('useLinkHeaderVariable', '${env.USE_LINK_HEADER}'); else if (!mode) setApi('useLinkHeaderVariable', ''); }}
          onVariableChange={value => setApi('useLinkHeaderVariable', value)}>
          <label className="inline field-control"><input type="checkbox" checked={Boolean(api.useLinkHeader)} onChange={e => setApi('useLinkHeader', e.target.checked)} />Use Link rel="next" header</label>
        </VariableModeField>
        <IconButton icon="search" label="Fetch sample and discover schema" onClick={onDiscover} />
        <ColumnEditor columns={configuration.schema ?? []} onChange={schema => data({ schema })} />
      </>;
    })()}
    {block.type === 'pipeline_csvOutput' && <>
      <label>Delimiter<input maxLength={1} value={block.getFieldValue('DELIMITER')} onChange={e => field('DELIMITER', e.target.value)} /></label>
      {headerOption()}
    </>}
    {block.type === 'pipeline_csv' && <>
      <label>Delimiter<input maxLength={1} value={block.getFieldValue('DELIMITER')} onChange={e => field('DELIMITER', e.target.value)} /></label>
      {headerOption()}
      <IconButton icon="search" label="Discover schema (100 records)" onClick={onDiscover} />
      <p className="muted">Review inferred types before saving. Later rows must match this schema.</p>
      <ColumnEditor columns={configuration.schema ?? []} onChange={schema => data({ schema })} />
    </>}
    {block.type === 'pipeline_map' && <>
      {mappings.map((m, index) => <fieldset key={index}>
        <legend>Mapping {index + 1}</legend>
        <label>Source column<select value={m.source} onChange={e => mapping(index, { source: e.target.value })}>
          <option value="">Select column</option>
          {inputSchema.map(c => <option key={c.name}>{c.name}</option>)}
          {m.source && !inputSchema.some(c => c.name === m.source) && <option value={m.source}>{m.source} (missing)</option>}
        </select></label>
        <label>Output name<input value={m.target.name} onChange={e => mapping(index, { target: { ...m.target, name: e.target.value } })} /></label>
        <VariableModeField label="Output type" variable={!!m.target.typeVariable} variableValue={m.target.typeVariable ?? ''} placeholder="${var.COLUMN_TYPE}"
          onModeChange={mode => mapping(index, { target: { ...m.target, typeVariable: mode ? m.target.typeVariable || '${var.COLUMN_TYPE}' : '' } })}
          onVariableChange={typeVariable => mapping(index, { target: { ...m.target, typeVariable } })}>
          <label className="field-control"><span className="visually-hidden">Output type</span><select value={m.target.type} onChange={e => mapping(index, { target: { ...m.target, type: e.target.value as Column['type'] } })}>{columnTypes.map(t => <option key={t}>{t}</option>)}</select></label>
        </VariableModeField>
        <VariableModeField label="Nullable" variable={!!m.target.nullableVariable} variableValue={m.target.nullableVariable ?? ''} placeholder="${env.NULLABLE}"
          onModeChange={mode => mapping(index, { target: { ...m.target, nullableVariable: mode ? m.target.nullableVariable || '${env.NULLABLE}' : '' } })}
          onVariableChange={nullableVariable => mapping(index, { target: { ...m.target, nullableVariable } })}>
          <label className="inline field-control"><input type="checkbox" checked={m.target.nullable} onChange={e => mapping(index, { target: { ...m.target, nullable: e.target.checked } })} />Nullable</label>
        </VariableModeField>
        {(m.target.type === 'decimal' || m.target.typeVariable) && <div className="pair">
          <VariableModeField label="Precision" variable={!!m.target.precisionVariable} variableValue={m.target.precisionVariable ?? ''} placeholder="${var.PRECISION}"
            onModeChange={mode => mapping(index, { target: { ...m.target, precisionVariable: mode ? m.target.precisionVariable || '${var.PRECISION}' : '' } })}
            onVariableChange={precisionVariable => mapping(index, { target: { ...m.target, precisionVariable } })}>
            <label className="field-control"><span className="visually-hidden">Precision</span><input type="number" min="1" max="28" value={m.target.precision ?? 18} onChange={e => mapping(index, { target: { ...m.target, precision: Number(e.target.value) } })} /></label>
          </VariableModeField>
          <VariableModeField label="Scale" variable={!!m.target.scaleVariable} variableValue={m.target.scaleVariable ?? ''} placeholder="${var.SCALE}"
            onModeChange={mode => mapping(index, { target: { ...m.target, scaleVariable: mode ? m.target.scaleVariable || '${var.SCALE}' : '' } })}
            onVariableChange={scaleVariable => mapping(index, { target: { ...m.target, scaleVariable } })}>
            <label className="field-control"><span className="visually-hidden">Scale</span><input type="number" min="0" max="28" value={m.target.scale ?? 2} onChange={e => mapping(index, { target: { ...m.target, scale: Number(e.target.value) } })} /></label>
          </VariableModeField>
        </div>}
        {(m.target.type === 'date' || m.target.type === 'timestamp') && <label>Parse format (optional)<input placeholder={m.target.type === 'date' ? 'yyyy-MM-dd' : 'O'} value={m.format ?? ''} onChange={e => mapping(index, { format: e.target.value || null })} /></label>}
        <div className="actions">
          <IconButton icon="up" label="Move up" disabled={index === 0} onClick={() => { const next = [...mappings]; [next[index - 1], next[index]] = [next[index], next[index - 1]]; data({ mappings: next }); }} />
          <IconButton icon="trash" label={`Remove mapping ${index + 1}`} onClick={() => data({ mappings: mappings.filter((_, i) => i !== index) })} />
        </div>
      </fieldset>)}
      <IconButton icon="add" label="Add mapping" onClick={() => data({ mappings: [...mappings, { source: inputSchema[0]?.name ?? '', target: { name: `column${mappings.length + 1}`, type: 'string', nullable: false } }] })} />
      <IconButton icon="columns" label="Map all input columns" onClick={() => data({ mappings: inputSchema.map(c => ({ source: c.name, target: { ...c } })) })} />
    </>}
    {(block.type === 'predicate_compare' || block.type === 'predicate_null') && <label>Column<input value={block.getFieldValue('COLUMN')} onChange={e => field('COLUMN', e.target.value)} /></label>}
    {block.type === 'predicate_compare' && <>
      <VariableModeField label="Operator" variable={!!configuration.predicate?.operatorVariable}
        variableValue={String(configuration.predicate?.operatorVariable ?? '')} placeholder="${var.OPERATOR}"
        onModeChange={mode => data({ predicate: { ...(configuration.predicate ?? {}), operatorVariable: mode ? String(configuration.predicate?.operatorVariable || '${var.OPERATOR}') : '' } })}
        onVariableChange={operatorVariable => data({ predicate: { ...(getBlockData(block).predicate ?? {}), operatorVariable } })}>
        <label className="field-control"><span className="visually-hidden">Operator</span><select value={block.getFieldValue('OP')} onChange={e => field('OP', e.target.value)}>
          {['equal', 'notEqual', 'greater', 'greaterOrEqual', 'less', 'lessOrEqual'].map(op => <option key={op}>{op}</option>)}
        </select></label>
      </VariableModeField>
      <label>Constant<input value={block.getFieldValue('VALUE')} onChange={e => field('VALUE', e.target.value)} /></label>
    </>}
    {block.type === 'predicate_null' && <>
      <VariableModeField label="Null check" variable={!!configuration.predicate?.isNullVariable}
        variableValue={String(configuration.predicate?.isNullVariable ?? '')} placeholder="${env.IS_NULL}"
        onModeChange={mode => data({ predicate: { ...(configuration.predicate ?? {}), isNullVariable: mode ? String(configuration.predicate?.isNullVariable || '${env.IS_NULL}') : '' } })}
        onVariableChange={isNullVariable => data({ predicate: { ...(getBlockData(block).predicate ?? {}), isNullVariable } })}>
        <label className="field-control"><span className="visually-hidden">Null check</span><select value={block.getFieldValue('IS_NULL')} onChange={e => field('IS_NULL', e.target.value)}><option value="TRUE">is null</option><option value="FALSE">is not null</option></select></label>
      </VariableModeField>
    </>}
    {block.type === 'predicate_logic' && <label>Operator<select value={block.getFieldValue('OP')} onChange={e => field('OP', e.target.value)}><option value="and">AND</option><option value="or">OR</option></select></label>}
    {block.type === 'pipeline_filter' && <p>Attach criteria blocks on the canvas. Select the expression to edit its column and constant.</p>}
  </section>;
}

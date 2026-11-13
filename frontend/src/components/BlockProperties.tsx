import type * as Blockly from 'blockly/core';
import { ColumnEditor } from './ColumnEditor';
import { IconButton } from './Icon';
import { getBlockData } from '../blockly/workspace';
import { columnTypes, type Column, type Mapping } from '../pipeline/types';

interface Props { block: Blockly.Block; inputSchema: Column[]; onChange: () => void; onDiscover: () => void }

export function BlockProperties({ block, inputSchema, onChange, onDiscover }: Props) {
  function field(name: string, value: string) { block.setFieldValue(value, name); onChange(); }
  function data(changes: { schema?: Column[]; mappings?: Mapping[] }) { block.data = JSON.stringify({ ...getBlockData(block), ...changes }); onChange(); }
  const configuration = getBlockData(block);
  const mappings = configuration.mappings ?? [];
  function mapping(index: number, changes: Partial<Mapping>) { data({ mappings: mappings.map((m, i) => i === index ? { ...m, ...changes } : m) }); }

  return <section aria-label="Block properties">
    <h2>{block.type.replace('pipeline_', '').replace('predicate_', '')} properties</h2>
    {(block.type === 'pipeline_csv' || block.type === 'pipeline_parquet') &&
      <label>Server file path<input value={block.getFieldValue('PATH')} onChange={e => field('PATH', e.target.value)} /></label>}
    {block.type === 'pipeline_csv' && <>
      <label>Delimiter<input maxLength={1} value={block.getFieldValue('DELIMITER')} onChange={e => field('DELIMITER', e.target.value)} /></label>
      <label className="inline"><input type="checkbox" checked={block.getFieldValue('HEADER') === 'TRUE'} onChange={e => field('HEADER', e.target.checked ? 'TRUE' : 'FALSE')} />Header row</label>
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
        <label>Output type<select value={m.target.type} onChange={e => mapping(index, { target: { ...m.target, type: e.target.value as Column['type'] } })}>{columnTypes.map(t => <option key={t}>{t}</option>)}</select></label>
        <label className="inline"><input type="checkbox" checked={m.target.nullable} onChange={e => mapping(index, { target: { ...m.target, nullable: e.target.checked } })} />Nullable</label>
        {m.target.type === 'decimal' && <div className="pair">
          <label>Precision<input type="number" min="1" max="28" value={m.target.precision ?? 18} onChange={e => mapping(index, { target: { ...m.target, precision: Number(e.target.value) } })} /></label>
          <label>Scale<input type="number" min="0" max="28" value={m.target.scale ?? 2} onChange={e => mapping(index, { target: { ...m.target, scale: Number(e.target.value) } })} /></label>
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
      <label>Operator<select value={block.getFieldValue('OP')} onChange={e => field('OP', e.target.value)}>
        {['equal', 'notEqual', 'greater', 'greaterOrEqual', 'less', 'lessOrEqual'].map(op => <option key={op}>{op}</option>)}
      </select></label>
      <label>Constant<input value={block.getFieldValue('VALUE')} onChange={e => field('VALUE', e.target.value)} /></label>
    </>}
    {block.type === 'predicate_null' && <label>Null check<select value={block.getFieldValue('IS_NULL')} onChange={e => field('IS_NULL', e.target.value)}><option value="TRUE">is null</option><option value="FALSE">is not null</option></select></label>}
    {block.type === 'predicate_logic' && <label>Operator<select value={block.getFieldValue('OP')} onChange={e => field('OP', e.target.value)}><option value="and">AND</option><option value="or">OR</option></select></label>}
    {block.type === 'pipeline_filter' && <p>Attach criteria blocks on the canvas. Select the expression to edit its column and constant.</p>}
  </section>;
}

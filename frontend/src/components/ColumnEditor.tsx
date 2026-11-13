import { columnTypes, type Column } from '../pipeline/types';
import { IconButton } from './Icon';

export function ColumnEditor({ columns, onChange }: { columns: Column[]; onChange: (columns: Column[]) => void }) {
  function update(index: number, changes: Partial<Column>) { onChange(columns.map((c, i) => i === index ? { ...c, ...changes } : c)); }
  return <div className="column-editor">
    {columns.map((column, index) => <fieldset key={index}>
      <legend>Column {index + 1}</legend>
      <label>Name<input value={column.name} onChange={e => update(index, { name: e.target.value })} /></label>
      <label>Type<select value={column.type} onChange={e => update(index, { type: e.target.value as Column['type'] })}>
        {columnTypes.map(type => <option key={type}>{type}</option>)}
      </select></label>
      <label className="inline"><input type="checkbox" checked={column.nullable} onChange={e => update(index, { nullable: e.target.checked })} />Nullable</label>
      {column.type === 'decimal' && <div className="pair">
        <label>Precision<input type="number" min="1" max="28" value={column.precision ?? 18} onChange={e => update(index, { precision: Number(e.target.value) })} /></label>
        <label>Scale<input type="number" min="0" max={column.precision ?? 18} value={column.scale ?? 2} onChange={e => update(index, { scale: Number(e.target.value) })} /></label>
      </div>}
      <IconButton icon="trash" label={`Remove column ${index + 1}`} onClick={() => onChange(columns.filter((_, i) => i !== index))} />
    </fieldset>)}
    <IconButton icon="add" label="Add column" onClick={() => onChange([...columns, { name: `column${columns.length + 1}`, type: 'string', nullable: false }])} />
  </div>;
}

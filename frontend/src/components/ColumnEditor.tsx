import { columnTypes, type Column } from '../pipeline/types';
import { IconButton } from './Icon';
import { VariableModeField } from './VariableModeField';
import { Input } from './ui/input';
import { Select } from './ui/select';
import { SwitchField } from './ui/switch-field';

export function ColumnEditor({ columns, onChange }: { columns: Column[]; onChange: (columns: Column[]) => void }) {
  function update(index: number, changes: Partial<Column>) { onChange(columns.map((c, i) => i === index ? { ...c, ...changes } : c)); }
  return <div className="column-editor">
    {columns.map((column, index) => <fieldset key={index}>
      <legend>Column {index + 1}</legend>
      <label>Name<Input value={column.name} onChange={e => update(index, { name: e.target.value })} /></label>
      <VariableModeField label="Type" variable={!!column.typeVariable} variableValue={column.typeVariable ?? ''} placeholder="${var.COLUMN_TYPE}"
        onModeChange={mode => update(index, { typeVariable: mode ? column.typeVariable || '${var.COLUMN_TYPE}' : '' })}
        onVariableChange={typeVariable => update(index, { typeVariable })}>
        <label className="field-control"><span className="visually-hidden">Type</span><Select value={column.type} onChange={e => update(index, { type: e.target.value as Column['type'] })}>
          {columnTypes.map(type => <option key={type}>{type}</option>)}
        </Select></label>
      </VariableModeField>
      <VariableModeField label="Nullable" variable={!!column.nullableVariable} variableValue={column.nullableVariable ?? ''} placeholder="${env.NULLABLE}"
        onModeChange={mode => update(index, { nullableVariable: mode ? column.nullableVariable || '${env.NULLABLE}' : '' })}
        onVariableChange={nullableVariable => update(index, { nullableVariable })}>
        <SwitchField label="Nullable" checked={column.nullable} onCheckedChange={nullable => update(index, { nullable })} />
      </VariableModeField>
      {(column.type === 'decimal' || column.typeVariable) && <div className="pair">
        <VariableModeField label="Precision" variable={!!column.precisionVariable} variableValue={column.precisionVariable ?? ''} placeholder="${var.PRECISION}"
          onModeChange={mode => update(index, { precisionVariable: mode ? column.precisionVariable || '${var.PRECISION}' : '' })}
          onVariableChange={precisionVariable => update(index, { precisionVariable })}>
          <label className="field-control"><span className="visually-hidden">Precision</span><Input type="number" min="1" max="28" value={column.precision ?? 18} onChange={e => update(index, { precision: Number(e.target.value) })} /></label>
        </VariableModeField>
        <VariableModeField label="Scale" variable={!!column.scaleVariable} variableValue={column.scaleVariable ?? ''} placeholder="${var.SCALE}"
          onModeChange={mode => update(index, { scaleVariable: mode ? column.scaleVariable || '${var.SCALE}' : '' })}
          onVariableChange={scaleVariable => update(index, { scaleVariable })}>
          <label className="field-control"><span className="visually-hidden">Scale</span><Input type="number" min="0" max={column.precision ?? 18} value={column.scale ?? 2} onChange={e => update(index, { scale: Number(e.target.value) })} /></label>
        </VariableModeField>
      </div>}
      <IconButton icon="trash" label={`Remove column ${index + 1}`} onClick={() => onChange(columns.filter((_, i) => i !== index))} />
    </fieldset>)}
    <IconButton icon="add" label="Add column" onClick={() => onChange([...columns, { name: `column${columns.length + 1}`, type: 'string', nullable: false }])} />
  </div>;
}

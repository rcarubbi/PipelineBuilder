import { useState } from 'react';
import { IconButton } from './Icon';

export type Environments = Record<string, Record<string, string>>;
interface VariableRow { id: number; name: string; value: string }
interface EnvironmentRow { id: number; name: string; variables: VariableRow[] }

// Must match the ${NAME} placeholder syntax resolved by the backend.
const variableNamePattern = /^[A-Za-z_][A-Za-z0-9_]*$/;
let lastRowId = 0;
const rowId = () => ++lastRowId;

/** Parses environment JSON, throwing a user-facing error when it is not a non-empty map of string-to-string maps. */
export function parseEnvironments(json: string): Environments {
  let value: unknown;
  try { value = JSON.parse(json); } catch { throw new Error('Environments JSON is not valid JSON.'); }
  const isMap = (candidate: unknown): candidate is Record<string, unknown> => !!candidate && typeof candidate === 'object' && !Array.isArray(candidate);
  if (!isMap(value) || Object.keys(value).length === 0 ||
    !Object.values(value).every(vars => isMap(vars) && Object.values(vars).every(v => typeof v === 'string')))
    throw new Error('Environments must be a JSON object of named string-to-string dictionaries.');
  return value as Environments;
}

const toRows = (environments: Environments): EnvironmentRow[] => Object.entries(environments).map(([name, variables]) => ({
  id: rowId(), name, variables: Object.entries(variables).map(([key, value]) => ({ id: rowId(), name: key, value })),
}));

const toJson = (rows: EnvironmentRow[]) => JSON.stringify(Object.fromEntries(rows.map(environment =>
  [environment.name.trim(), Object.fromEntries(environment.variables.map(variable => [variable.name.trim(), variable.value]))])), null, 2);

export function validateRows(rows: EnvironmentRow[]): string | null {
  if (rows.length === 0) return 'Add at least one environment.';
  const names = rows.map(environment => environment.name.trim());
  if (names.some(name => !name)) return 'Every environment needs a name.';
  if (new Set(names).size !== names.length) return 'Environment names must be unique.';
  for (const environment of rows) {
    const label = environment.name.trim();
    const variables = environment.variables.map(variable => variable.name.trim());
    const invalid = variables.find(name => !variableNamePattern.test(name));
    if (invalid !== undefined) return `In "${label}", variable "${invalid}" must start with a letter or underscore and contain only letters, digits and underscores.`;
    if (new Set(variables).size !== variables.length) return `Variable names in "${label}" must be unique.`;
  }
  return null;
}

interface Props {
  json: string;
  /** Receives the latest valid JSON and the current validation error, if any. */
  onChange: (json: string, error: string | null) => void;
}

export function EnvironmentEditor({ json, onChange }: Props) {
  const initial = (() => { try { return toRows(parseEnvironments(json)); } catch { return null; } })();
  const [advanced, setAdvanced] = useState(initial === null);
  const [rows, setRows] = useState<EnvironmentRow[]>(initial ?? []);
  const [notice, setNotice] = useState('');
  const [open, setOpen] = useState(false);
  const formError = advanced ? null : validateRows(rows);
  const jsonError = advanced ? (() => { try { parseEnvironments(json); return null; } catch (ex) { return (ex as Error).message; } })() : null;

  function update(next: EnvironmentRow[]) {
    setRows(next);
    const error = validateRows(next);
    // While the form is invalid the last valid JSON is kept, and the error blocks saving.
    onChange(error ? json : toJson(next), error);
  }

  const updateEnvironment = (id: number, change: (environment: EnvironmentRow) => EnvironmentRow) =>
    update(rows.map(environment => environment.id === id ? change(environment) : environment));

  function switchMode(toAdvanced: boolean) {
    setNotice('');
    if (toAdvanced) { setAdvanced(true); return; }
    try { setRows(toRows(parseEnvironments(json))); setAdvanced(false); onChange(json, null); }
    catch (ex) { setNotice(`${(ex as Error).message} Fix the JSON before switching to the form.`); }
  }

  function addEnvironment() {
    const taken = new Set(rows.map(environment => environment.name.trim()));
    let name = 'environment';
    for (let suffix = 2; taken.has(name); suffix++) name = `environment${suffix}`;
    update([...rows, { id: rowId(), name, variables: rows[0]?.variables.map(variable => ({ id: rowId(), name: variable.name, value: '' })) ?? [] }]);
  }

  const invalid = !!(formError || jsonError);
  const count = advanced ? null : rows.length;
  // Invalid environments block saving, so keep the panel open while there is something to fix.
  return <details className="environment-panel" open={open || invalid} onToggle={e => setOpen(e.currentTarget.open)}>
    <summary onClick={e => { if (invalid) e.preventDefault(); }}>Environments{count !== null && <span className="muted"> ({count})</span>}{invalid && <span className="field-error"> — needs attention</span>}</summary>
    <div className="environment-header">
      <label className="inline"><input type="checkbox" checked={advanced} disabled={!advanced && !!formError} onChange={e => switchMode(e.target.checked)} />Edit as JSON (advanced)</label>
    </div>
    <p className="muted">Use variables in file paths as <code>{'${NAME}'}</code>. Relative paths start inside the configured server input/output directories.</p>
    {notice && <p role="alert" className="field-error">{notice}</p>}
    {advanced ? <>
      <label>Variables by environment (JSON)<textarea rows={9} spellCheck={false} value={json} onChange={e => {
        let error: string | null = null;
        try { parseEnvironments(e.target.value); } catch (ex) { error = (ex as Error).message; }
        onChange(e.target.value, error);
      }} /></label>
      {jsonError && <p role="alert" className="field-error">{jsonError}</p>}
    </> : <>
      {rows.map(environment => {
        const label = environment.name.trim() || 'unnamed environment';
        return <fieldset key={environment.id} className="environment-card">
          <legend>{label}</legend>
          <div className="actions">
            <label>Environment name<input value={environment.name} onChange={e => updateEnvironment(environment.id, current => ({ ...current, name: e.target.value }))} /></label>
            <IconButton icon="trash" label={`Remove environment ${label}`} className="danger" disabled={rows.length === 1}
              onClick={() => { if (window.confirm(`Remove environment "${label}" and its variables?`)) update(rows.filter(e => e.id !== environment.id)); }} />
          </div>
          {environment.variables.length === 0 ? <p className="muted">No variables.</p> : <table className="variable-table">
            <thead><tr><th scope="col">Variable</th><th scope="col">Value</th><th scope="col"><span className="visually-hidden">Actions</span></th></tr></thead>
            <tbody>{environment.variables.map((variable, index) => <tr key={variable.id}>
              <td><input aria-label={`Name of variable ${index + 1} in ${label}`} value={variable.name} spellCheck={false} placeholder="INPUT_FILE"
                onChange={e => updateEnvironment(environment.id, current => ({ ...current, variables: current.variables.map(v => v.id === variable.id ? { ...v, name: e.target.value } : v) }))} /></td>
              <td><input aria-label={`Value of ${variable.name.trim() || `variable ${index + 1}`} in ${label}`} value={variable.value} spellCheck={false}
                onChange={e => updateEnvironment(environment.id, current => ({ ...current, variables: current.variables.map(v => v.id === variable.id ? { ...v, value: e.target.value } : v) }))} /></td>
              <td><IconButton icon="trash" label={`Remove ${variable.name.trim() || `variable ${index + 1}`} from ${label}`}
                onClick={() => updateEnvironment(environment.id, current => ({ ...current, variables: current.variables.filter(v => v.id !== variable.id) }))} /></td>
            </tr>)}</tbody>
          </table>}
          <IconButton icon="add" label={`Add variable to ${label}`}
            onClick={() => updateEnvironment(environment.id, current => ({ ...current, variables: [...current.variables, { id: rowId(), name: '', value: '' }] }))} />
        </fieldset>;
      })}
      {formError && <p role="alert" className="field-error">{formError}</p>}
      <IconButton icon="add" label="Add environment" onClick={addEnvironment} />
    </>}
  </details>;
}

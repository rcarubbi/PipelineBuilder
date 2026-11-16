import type { ReactNode } from 'react';

export function VariableModeField({
  label, variable, onModeChange, children, variableValue, onVariableChange, placeholder = '${var.NAME}',
}: {
  label: string;
  variable: boolean;
  onModeChange: (variable: boolean) => void;
  children: ReactNode;
  variableValue: string;
  onVariableChange: (value: string) => void;
  placeholder?: string;
}) {
  return <div className="variable-field">
    <div className="variable-field-heading">
      <span>{label}</span>
      <div className="mode-switch" role="group" aria-label={`${label} mode`}>
        <button type="button" aria-pressed={!variable} onClick={() => onModeChange(false)}>Value</button>
        <button type="button" aria-pressed={variable} onClick={() => onModeChange(true)}>Variable</button>
      </div>
    </div>
    {variable
      ? <input aria-label={`${label} variable`} value={variableValue} placeholder={placeholder} onChange={e => onVariableChange(e.target.value)} />
      : children}
  </div>;
}

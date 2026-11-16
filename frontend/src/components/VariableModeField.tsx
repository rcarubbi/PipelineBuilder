import type { ReactNode } from 'react';
import { Button } from './ui/button';
import { Input } from './ui/input';

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
  return <div className="variable-field" data-mode={variable ? 'variable' : 'value'}>
    <div className="variable-field-heading">
      <span>{label}</span>
      <div className="mode-switch" role="group" aria-label={`${label} mode`}>
        <Button type="button" size="sm" variant={!variable ? 'secondary' : 'ghost'} aria-pressed={!variable} onClick={() => onModeChange(false)}>Value</Button>
        <Button type="button" size="sm" variant={variable ? 'secondary' : 'ghost'} aria-pressed={variable} onClick={() => onModeChange(true)}>Variable</Button>
      </div>
    </div>
    <div className={`variable-mode-panel${variable ? '' : ' is-active'}`} aria-hidden={variable} inert={variable}>
      <div className="variable-mode-content">{children}</div>
    </div>
    <div className={`variable-mode-panel${variable ? ' is-active' : ''}`} aria-hidden={!variable} inert={!variable}>
      <div className="variable-mode-content">
        <Input aria-label={`${label} variable`} value={variableValue} placeholder={placeholder} onChange={e => onVariableChange(e.target.value)} />
      </div>
    </div>
  </div>;
}

import { Switch } from './switch';

export function SwitchField({ label, checked, onCheckedChange, disabled = false }: {
  label: string;
  checked: boolean;
  onCheckedChange: (checked: boolean) => void;
  disabled?: boolean;
}) {
  return <div className="switch-field">
    <span role="presentation" onClick={() => { if (!disabled) onCheckedChange(!checked); }}>{label}</span>
    <Switch aria-label={label} checked={checked} disabled={disabled} onCheckedChange={onCheckedChange} />
  </div>;
}

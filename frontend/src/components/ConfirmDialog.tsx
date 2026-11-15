import { useEffect, useRef } from 'react';

interface Props {
  title: string;
  description: string;
  confirmLabel: string;
  onConfirm: () => void;
  onCancel: () => void;
}

export function ConfirmDialog({ title, description, confirmLabel, onConfirm, onCancel }: Props) {
  const ref = useRef<HTMLDialogElement>(null);
  useEffect(() => { ref.current?.showModal(); }, []);
  return <dialog ref={ref} className="confirm-dialog" aria-labelledby="confirm-title" aria-describedby="confirm-description"
    onCancel={event => { event.preventDefault(); onCancel(); }}>
    <div className="dialog-marker" aria-hidden="true">!</div>
    <h2 id="confirm-title">{title}</h2>
    <p id="confirm-description">{description}</p>
    <div className="dialog-actions">
      <button type="button" onClick={onCancel} autoFocus>Cancel</button>
      <button type="button" className="dialog-danger" onClick={onConfirm}>{confirmLabel}</button>
    </div>
  </dialog>;
}

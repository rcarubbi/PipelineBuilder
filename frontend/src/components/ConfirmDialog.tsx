import { Button } from './ui/button';
import { useRef } from 'react';
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from './ui/dialog';

interface Props {
  open: boolean;
  title: string;
  description: string;
  confirmLabel: string;
  onConfirm: () => void;
  onCancel: () => void;
}

export function ConfirmDialog(props: Props) {
  // Keep the text intact while Radix finishes the exit animation.
  const lastOpen = useRef(props);
  if (props.open) lastOpen.current = props;
  const { title, description, confirmLabel, onConfirm, onCancel } = lastOpen.current;
  return <Dialog open={props.open} onOpenChange={open => { if (!open) onCancel(); }}>
    <DialogContent className="confirm-dialog">
      <DialogHeader>
        <div className="dialog-marker" aria-hidden="true">!</div>
        <DialogTitle>{title}</DialogTitle>
        <DialogDescription>{description}</DialogDescription>
      </DialogHeader>
    <div className="dialog-actions">
      <Button type="button" variant="outline" onClick={onCancel} autoFocus>Cancel</Button>
      <Button type="button" variant="destructive" onClick={onConfirm}>{confirmLabel}</Button>
    </div>
    </DialogContent>
  </Dialog>;
}

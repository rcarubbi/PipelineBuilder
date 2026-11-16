import { Button } from './ui/button';
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from './ui/dialog';

interface Props {
  title: string;
  description: string;
  confirmLabel: string;
  onConfirm: () => void;
  onCancel: () => void;
}

export function ConfirmDialog({ title, description, confirmLabel, onConfirm, onCancel }: Props) {
  return <Dialog open onOpenChange={open => { if (!open) onCancel(); }}>
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

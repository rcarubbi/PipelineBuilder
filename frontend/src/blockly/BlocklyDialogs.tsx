import { useEffect, useRef, useState } from 'react';
import * as Blockly from 'blockly/core';
import { Button } from '../components/ui/button';
import { Dialog, DialogContent, DialogHeader, DialogTitle } from '../components/ui/dialog';
import { Input } from '../components/ui/input';

type Request =
  | { kind: 'prompt'; message: string; callback: (value: string | null) => void }
  | { kind: 'confirm'; message: string; callback: (value: boolean) => void }
  | { kind: 'alert'; message: string; callback?: () => void };

export function BlocklyDialogs() {
  const [request, setRequest] = useState<Request | null>(null);
  const [open, setOpen] = useState(false);
  const [value, setValue] = useState('');
  const pending = useRef(false);
  const returnFocus = useRef<(() => void) | undefined>(undefined);

  useEffect(() => {
    function show(next: Request, defaultValue = '') {
      pending.current = true;
      setValue(defaultValue);
      setRequest(next);
      setOpen(true);
    }
    Blockly.dialog.setPrompt((message, defaultValue, callback) => show({ kind: 'prompt', message, callback }, defaultValue));
    Blockly.dialog.setConfirm((message, callback) => show({ kind: 'confirm', message, callback }));
    Blockly.dialog.setAlert((message, callback) => show({ kind: 'alert', message, callback }));
    return () => {
      Blockly.dialog.setPrompt();
      Blockly.dialog.setConfirm();
      Blockly.dialog.setAlert();
      returnFocus.current?.();
      returnFocus.current = undefined;
    };
  }, []);

  function finish(accepted: boolean) {
    if (!request || !pending.current) return;
    pending.current = false;
    setOpen(false);
    if (request.kind === 'prompt') request.callback(accepted ? value : null);
    else if (request.kind === 'confirm') request.callback(accepted);
    else request.callback?.();
  }

  return <Dialog open={open} onOpenChange={next => { if (!next) finish(false); }}>
    <DialogContent className="blockly-dialog" aria-describedby={undefined}
      onOpenAutoFocus={event => {
        const manager = Blockly.getFocusManager();
        if (!manager.ephemeralFocusTaken()) returnFocus.current = manager.takeEphemeralFocus(event.currentTarget as HTMLElement);
      }}
      onCloseAutoFocus={event => {
        event.preventDefault();
        returnFocus.current?.();
        returnFocus.current = undefined;
      }}>
      {request && <form onSubmit={event => { event.preventDefault(); finish(true); }}>
        <DialogHeader><DialogTitle>{request.message}</DialogTitle></DialogHeader>
        {request.kind === 'prompt' && <label>Variable name<Input value={value} onChange={event => setValue(event.target.value)}
          onFocus={event => event.target.select()} /></label>}
        <div className="dialog-actions">
          {request.kind !== 'alert' && <Button type="button" variant="outline" onClick={() => finish(false)}>{Blockly.Msg.DIALOG_CANCEL}</Button>}
          <Button type="submit">{Blockly.Msg.DIALOG_OK}</Button>
        </div>
      </form>}
    </DialogContent>
  </Dialog>;
}

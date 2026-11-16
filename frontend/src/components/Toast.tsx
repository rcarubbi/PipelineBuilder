import { useEffect, useState, type ReactNode } from 'react';
import { Alert } from './ui/alert';

const durationMs = 5000;
const transitionMs = 180;

export function Toast({ children, variant = 'default' }: { children: ReactNode; variant?: 'default' | 'destructive' }) {
  const [phase, setPhase] = useState<'entering' | 'visible' | 'exiting' | 'dismissed'>('entering');

  useEffect(() => {
    const show = window.setTimeout(() => setPhase('visible'), 0);
    const dismiss = window.setTimeout(() => setPhase('exiting'), durationMs);
    return () => { window.clearTimeout(show); window.clearTimeout(dismiss); };
  }, []);

  useEffect(() => {
    if (phase !== 'exiting') return;
    const dismiss = window.setTimeout(() => setPhase('dismissed'), transitionMs);
    return () => window.clearTimeout(dismiss);
  }, [phase]);

  if (phase === 'dismissed') return null;
  return <div className={`designer-toast${phase === 'visible' ? ' is-visible' : ''}${phase === 'exiting' ? ' is-exiting' : ''}`}>
    <Alert role={variant === 'destructive' ? 'alert' : 'status'} variant={variant}>{children}</Alert>
  </div>;
}

export function Toaster({ children }: { children: ReactNode }) {
  return <div className="toast-container">{children}</div>;
}

import type { HTMLAttributes } from 'react';
import { cn } from '../../lib/utils';

export function Alert({ className, variant = 'default', ...props }: HTMLAttributes<HTMLDivElement> & { variant?: 'default' | 'destructive' }) {
  return <div role={props.role ?? 'status'} className={cn('relative w-full rounded-lg border px-4 py-3 text-sm', variant === 'destructive' ? 'border-destructive/40 bg-destructive/10 text-destructive' : 'border-border bg-card text-card-foreground', className)} {...props} />;
}

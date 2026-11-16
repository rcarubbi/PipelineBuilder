import * as DialogPrimitive from '@radix-ui/react-dialog';
import type { ComponentProps } from 'react';
import { cn } from '../../lib/utils';

export const Dialog = DialogPrimitive.Root;
export const DialogPortal = DialogPrimitive.Portal;
export const DialogOverlay = ({ className, ...props }: ComponentProps<typeof DialogPrimitive.Overlay>) =>
  <DialogPrimitive.Overlay className={cn('ui-dialog-overlay fixed inset-0 z-50 bg-black/50 backdrop-blur-sm', className)} {...props} />;
export const DialogContent = ({ className, ...props }: ComponentProps<typeof DialogPrimitive.Content>) =>
  <DialogPortal><DialogOverlay /><DialogPrimitive.Content className={cn('ui-dialog-content fixed left-1/2 top-1/2 z-50 grid w-[calc(100%-2rem)] max-w-lg -translate-x-1/2 -translate-y-1/2 gap-4 rounded-xl border bg-card p-6 text-card-foreground shadow-lg focus:outline-none', className)} {...props} /></DialogPortal>;
export const DialogHeader = ({ className, ...props }: ComponentProps<'div'>) =>
  <div className={cn('flex flex-col gap-2 text-left', className)} {...props} />;
export const DialogTitle = ({ className, ...props }: ComponentProps<typeof DialogPrimitive.Title>) =>
  <DialogPrimitive.Title className={cn('text-lg font-semibold leading-none tracking-tight', className)} {...props} />;
export const DialogDescription = ({ className, ...props }: ComponentProps<typeof DialogPrimitive.Description>) =>
  <DialogPrimitive.Description className={cn('text-sm text-muted-foreground', className)} {...props} />;

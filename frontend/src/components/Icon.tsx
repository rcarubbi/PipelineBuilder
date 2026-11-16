import type { AnchorHTMLAttributes } from 'react';
import { Button, type ButtonProps } from './ui/button';

const paths = {
  add: 'M12 5v14M5 12h14',
  play: 'm8 5 11 7-11 7Z',
  logs: 'M8 6h12M8 12h12M8 18h12M3 6h1M3 12h1M3 18h1',
  trash: 'M3 6h18M9 6V3h6v3M5 6l1 15h12l1-15M10 10v7M14 10v7',
  edit: 'm15 4 5 5M4 20l5-1L21 7l-5-5L4 14Z',
  list: 'M4 5h16v14H4ZM4 10h16M9 5v14',
  refresh: 'M20 7a8 8 0 1 0 1 7M20 3v5h-5',
  left: 'm14 5-7 7 7 7',
  right: 'm10 5 7 7-7 7',
  save: 'M4 3h13l4 4v14H3V3ZM7 3v6h10V3M7 21v-8h10v8',
  check: 'm4 12 5 5L20 6',
  filter: 'M3 4h18l-7 8v7l-4 2v-9Z',
  map: 'M3 5h6v5H3ZM15 14h6v5h-6ZM9 7h8v7m-3-3 3 3 3-3',
  search: 'M15 15l6 6M18 10a8 8 0 1 1-16 0 8 8 0 0 1 16 0',
  up: 'm5 14 7-7 7 7',
  down: 'm5 10 7 7 7-7',
  cancel: 'M6 6l12 12M6 18 18 6',
  columns: 'M3 4h18v16H3ZM9 4v16M15 4v16',
  clock: 'M12 7v5l3 2M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0',
  blocks: 'M4 4h7v7H4ZM13 4h7v7h-7ZM4 13h7v7H4ZM13 13h7v7h-7Z',
  code: 'm8 8-4 4 4 4M16 8l4 4-4 4M14 5l-4 14',
  unlink: 'M9 15 15 9M8 12l-2 2a3 3 0 0 0 4 4l2-2M16 12l2-2a3 3 0 0 0-4-4l-2 2M4 4l16 16',
  import: 'M12 3v12m-5-5 5 5 5-5M4 17v4h16v-4',
  export: 'M12 15V3m-5 5 5-5 5 5M4 17v4h16v-4',
};
type IconName = keyof typeof paths | 'and' | 'or' | 'not' | 'null';

export function Icon({ name }: { name: IconName }) {
  return <svg viewBox="0 0 24 24" width="20" height="20" aria-hidden="true" focusable="false">
    {name in paths ? <path d={paths[name as keyof typeof paths]} fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" />
      : <text x="12" y="16" textAnchor="middle" fill="currentColor" fontSize="8" fontWeight="700">{name === 'null' ? '∅' : name.toUpperCase()}</text>}
  </svg>;
}

export function IconButton({ icon, label, className = '', loading = false, variant, ...props }: Omit<ButtonProps, 'asChild' | 'children'> & { icon: IconName; label: string; loading?: boolean }) {
  const intent = variant ?? (className.split(/\s+/).includes('danger') ? 'destructive' : className.split(/\s+/).includes('primary') ? 'default' : 'outline');
  return <Button type="button" size="icon" variant={intent} className={`icon-button ${className}`} title={label} aria-label={label} aria-busy={loading || undefined} {...props}>{loading ? <span className="button-spinner" aria-hidden="true" /> : <Icon name={icon} />}</Button>;
}

export function IconLink({ icon, label, className = '', ...props }: AnchorHTMLAttributes<HTMLAnchorElement> & { icon: IconName; label: string }) {
  const variant = className.split(/\s+/).includes('primary') ? 'default' : 'outline';
  return <Button asChild size="icon" variant={variant} className={`icon-button ${className}`}><a title={label} aria-label={label} {...props}><Icon name={icon} /></a></Button>;
}

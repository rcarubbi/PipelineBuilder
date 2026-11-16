import { Card } from './ui/card';

export function Loading({ label }: { label: string }) {
  return <div className="loading-overlay" role="status" aria-live="polite">
    <Card className="loading-state">
      <span className="loading-spinner" aria-hidden="true" />
      <div><strong>{label}</strong><p>Please wait a moment.</p></div>
    </Card>
  </div>;
}

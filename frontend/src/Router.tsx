import { lazy, Suspense } from 'react';
import { ExecutionLogsPage } from './pages/ExecutionLogsPage';
import { PipelinesPage } from './pages/PipelinesPage';
import { SchedulesPage } from './pages/SchedulesPage';

const Designer = lazy(() => import('./App').then(module => ({ default: module.App })));

export function Router() {
  const parameters = new URLSearchParams(location.search);
  if (parameters.get('page') === 'logs') return <ExecutionLogsPage />;
  if (parameters.get('page') === 'schedules') return <SchedulesPage />;
  if (parameters.get('page') === 'designer' || parameters.has('pipeline'))
    return <Suspense fallback={<p role="status">Loading designer…</p>}><Designer pipelineId={parameters.get('pipeline') ?? undefined} /></Suspense>;
  return <PipelinesPage />;
}

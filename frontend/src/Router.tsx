import { lazy, Suspense, useEffect, useRef, useState, type ComponentType } from 'react';
import { Loading } from './components/Loading';
import { api } from './pipeline/api';
import type { SavedPipeline } from './pipeline/types';
import { ExecutionLogsPage } from './pages/ExecutionLogsPage';
import { PipelinesPage } from './pages/PipelinesPage';
import { SchedulesPage } from './pages/SchedulesPage';

const Designer = lazy(() => import('./App').then(module => ({ default: module.App })));

export function Router() {
  const [opened, setOpened] = useState<{ component: ComponentType<{ pipelineId?: string; initialPipeline?: SavedPipeline }>; pipeline?: SavedPipeline; pipelineId?: string } | null>(null);
  const [opening, setOpening] = useState(false);
  const [openError, setOpenError] = useState('');
  const openingRef = useRef(false);
  useEffect(() => {
    const onPopState = () => window.location.reload();
    window.addEventListener('popstate', onPopState);
    return () => window.removeEventListener('popstate', onPopState);
  }, []);
  async function openDesigner(pipelineId?: string) {
    if (openingRef.current) return;
    openingRef.current = true;
    setOpening(true); setOpenError('');
    try {
      const [module, pipeline] = await Promise.all([import('./App'), pipelineId ? api.load(pipelineId) : Promise.resolve(undefined)]);
      const url = pipelineId ? `?page=designer&pipeline=${encodeURIComponent(pipelineId)}` : '?page=designer';
      history.pushState(null, '', url);
      setOpened({ component: module.App, pipeline, pipelineId });
    } catch (error) {
      setOpenError(error instanceof Error ? error.message : 'Could not open designer.');
    } finally { openingRef.current = false; setOpening(false); }
  }
  if (opened) {
    const OpenedDesigner = opened.component;
    return <OpenedDesigner pipelineId={opened.pipelineId} initialPipeline={opened.pipeline} />;
  }
  const parameters = new URLSearchParams(location.search);
  if (parameters.get('page') === 'logs') return <ExecutionLogsPage />;
  if (parameters.get('page') === 'schedules') return <SchedulesPage />;
  if (parameters.get('page') === 'designer' || parameters.has('pipeline'))
    return <Suspense fallback={<Loading label="Loading designer…" />}><Designer pipelineId={parameters.get('pipeline') ?? undefined} /></Suspense>;
  return <><PipelinesPage onOpenDesigner={openDesigner} />{openError && <p role="alert" className="notice error">{openError}</p>}{opening && <Loading label="Loading designer…" />}</>;
}

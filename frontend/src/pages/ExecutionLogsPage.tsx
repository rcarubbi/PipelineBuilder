import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { api } from '../pipeline/api';
import { IconButton, IconLink } from '../components/Icon';
import { PageHeader } from '../components/PageHeader';
import { Loading } from '../components/Loading';
import { Alert } from '../components/ui/alert';
import { Badge } from '../components/ui/badge';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '../components/ui/table';
import { Card } from '../components/ui/card';
import { Toast, Toaster } from '../components/Toast';
import { Pagination } from '../components/Pagination';
import type { Run, RunHistory } from '../pipeline/types';

const maxPageSize = 100;

function RunResult({ run }: { run: Run }) {
  const succeeded = run.status === 'Succeeded';
  const failed = run.status === 'Failed' || run.status === 'Interrupted';
  const label = succeeded ? 'OK' : run.status === 'Failed' ? 'Failure' : run.status;
  return <Alert variant={failed ? 'destructive' : 'default'} className={`run-result ${succeeded ? 'run-ok' : failed ? 'run-failure' : 'run-pending'}`}>
    <Badge variant={succeeded ? 'secondary' : failed ? 'destructive' : 'outline'}>{label}</Badge>
    {succeeded ? <p>Read {run.rowsRead} records · written {run.rowsWritten} · filtered {run.rowsRead - run.rowsWritten}.</p>
      : failed ? <p>{run.error || 'No failure reason was recorded.'}</p>
        : <p>{run.status === 'Queued' ? 'Waiting for execution.' : run.status === 'Running' ? 'Execution in progress.' : 'Execution was cancelled.'}</p>}
    {failed && run.blockId && <p>Block: <code>{run.blockId}</code></p>}
  </Alert>;
}

export function ExecutionLogsPage() {
  const pipelineId = new URLSearchParams(location.search).get('pipeline') ?? undefined;
  const [page, setPage] = useState(1);
  const [refresh, setRefresh] = useState(0);
  const [history, setHistory] = useState<RunHistory | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [pageSize, setPageSize] = useState(maxPageSize);
  const [viewport, setViewport] = useState({ width: 0, height: 0 });
  const table = useRef<HTMLDivElement>(null);
  const pagination = useRef<HTMLDivElement>(null);
  const viewportRef = useRef(viewport);

  useEffect(() => {
    let disposed = false;
    setLoading(true); setError('');
    api.history(page, pipelineId, pageSize).then(result => { if (!disposed) setHistory(result); })
      .catch(ex => { if (!disposed) setError(ex instanceof Error ? ex.message : 'Could not load execution history.'); })
      .finally(() => { if (!disposed) setLoading(false); });
    return () => { disposed = true; };
  }, [page, pageSize, refresh, pipelineId]);

  useEffect(() => {
    const updateViewport = () => {
      const next = { width: window.innerWidth, height: window.innerHeight };
      if (viewportRef.current.width === next.width && viewportRef.current.height === next.height) return;
      viewportRef.current = next;
      setPage(1);
      setPageSize(maxPageSize);
      setViewport(next);
    };
    updateViewport();
    window.addEventListener('resize', updateViewport);
    return () => window.removeEventListener('resize', updateViewport);
  }, []);

  useLayoutEffect(() => {
    if (page !== 1 || !history || (history.runs.length < pageSize && history.hasMore) || !table.current || !viewport.height) return;
    if (!table.current.getBoundingClientRect().height) return;
    const bottom = viewport.height - (pagination.current?.getBoundingClientRect().height ?? 40) - 64;
    const rows = [...table.current.querySelectorAll('tbody tr')];
    let visibleRows = 0;
    for (const row of rows) {
      if (row.getBoundingClientRect().bottom > bottom) break;
      visibleRows++;
    }
    const fittedPageSize = Math.max(1, Math.min(maxPageSize, visibleRows));
    if (fittedPageSize !== pageSize) setPageSize(fittedPageSize);
  }, [history, page, pageSize, viewport]);

  return <>
    <PageHeader page="Execution logs" pipelineId={pipelineId} />
    <main>
      <Card className="toolbar" aria-label="History actions">
        <IconLink icon="list" label="Back to pipelines" href="/" />
        <IconButton icon="refresh" label="Refresh history" loading={loading} disabled={loading} onClick={() => setRefresh(n => n + 1)} />
        <p className="muted">Results are saved on the server. Refresh to see updates to queued or running executions.</p>
      </Card>
      {loading && <Loading label="Loading execution history…" />}
      {error && <Toaster><Toast key={`error:${error}`} variant="destructive">{error}</Toast></Toaster>}
      {history && <>
        {history.runs.length === 0 ? <p role="status">No executions on this page.</p> : <div ref={table} className="history-table">
          <Table aria-label="Execution logs">
            <TableHeader><TableRow><TableHead scope="col">Run</TableHead><TableHead scope="col">Time</TableHead><TableHead scope="col">Result</TableHead></TableRow></TableHeader>
            <TableBody>{history.runs.map(run => <TableRow key={run.id}>
              <TableCell><p>Revision {run.revision} · {run.environment}</p><p>Trigger: {run.trigger ?? 'Manual'}</p><p className="muted">Run: <code>{run.id}</code></p></TableCell>
              <TableCell><p>Created: <time dateTime={run.createdAt}>{new Date(run.createdAt).toLocaleString()}</time></p>
                {run.startedAt && <p>Started: <time dateTime={run.startedAt}>{new Date(run.startedAt).toLocaleString()}</time></p>}
                {run.finishedAt && <p>Finished: <time dateTime={run.finishedAt}>{new Date(run.finishedAt).toLocaleString()}</time></p>}
              </TableCell>
              <TableCell><RunResult run={run} /></TableCell>
            </TableRow>)}</TableBody>
          </Table>
        </div>}
      </>}
      <div ref={pagination}>
        <Pagination page={page} pageSize={pageSize} totalCount={history?.totalCount ?? 0}
          disabled={loading || !!error} onPageChange={setPage} />
      </div>
    </main>
  </>;
}

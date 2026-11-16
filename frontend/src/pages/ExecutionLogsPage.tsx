import { useEffect, useState } from 'react';
import { api } from '../pipeline/api';
import { IconButton, IconLink } from '../components/Icon';
import { PageHeader } from '../components/PageHeader';
import { Loading } from '../components/Loading';
import { Alert } from '../components/ui/alert';
import { Badge } from '../components/ui/badge';
import { Table, TableBody, TableCaption, TableCell, TableHead, TableHeader, TableRow } from '../components/ui/table';
import { Card } from '../components/ui/card';
import type { Run, RunHistory } from '../pipeline/types';

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

  useEffect(() => {
    let disposed = false;
    setLoading(true); setError('');
    api.history(page, pipelineId).then(result => { if (!disposed) setHistory(result); })
      .catch(ex => { if (!disposed) setError(ex instanceof Error ? ex.message : 'Could not load execution history.'); })
      .finally(() => { if (!disposed) setLoading(false); });
    return () => { disposed = true; };
  }, [page, refresh, pipelineId]);

  return <>
    <PageHeader page="Execution logs" />
    <main>
      <Card className="toolbar" aria-label="History actions">
        <IconLink icon="list" label="Back to pipelines" href="/" />
        <IconButton icon="refresh" label="Refresh history" loading={loading} disabled={loading} onClick={() => setRefresh(n => n + 1)} />
        <p className="muted">Results are saved on the server. Refresh to see updates to queued or running executions.</p>
      </Card>
      {pipelineId && <p>Pipeline: <code>{pipelineId}</code></p>}
      {loading && <Loading label="Loading execution history…" />}
      {error && <Alert role="alert" variant="destructive" className="notice">{error}</Alert>}
      {history && <>
        {history.runs.length === 0 ? <p role="status">No executions on this page.</p> : <div className="history-table">
          <Table>
            <TableCaption>Pipeline executions — page {history.page}</TableCaption>
            <TableHeader><TableRow><TableHead scope="col">Pipeline / run</TableHead><TableHead scope="col">Time</TableHead><TableHead scope="col">Result</TableHead></TableRow></TableHeader>
            <TableBody>{history.runs.map(run => <TableRow key={run.id}>
              <TableCell><p>Pipeline: <code>{run.pipelineId}</code></p><p>Revision {run.revision} · {run.environment}</p><p>Trigger: {run.trigger ?? 'Manual'}</p><p className="muted">Run: <code>{run.id}</code></p></TableCell>
              <TableCell><p>Created: <time dateTime={run.createdAt}>{new Date(run.createdAt).toLocaleString()}</time></p>
                {run.startedAt && <p>Started: <time dateTime={run.startedAt}>{new Date(run.startedAt).toLocaleString()}</time></p>}
                {run.finishedAt && <p>Finished: <time dateTime={run.finishedAt}>{new Date(run.finishedAt).toLocaleString()}</time></p>}
              </TableCell>
              <TableCell><RunResult run={run} /></TableCell>
            </TableRow>)}</TableBody>
          </Table>
        </div>}
      </>}
      <nav className="actions history-pagination" aria-label="History pagination">
        <IconButton icon="left" label="Newer executions" disabled={loading || page === 1} onClick={() => setPage(n => n - 1)} />
        <span>Page {page}</span>
        <IconButton icon="right" label="Older executions" disabled={loading || !!error || !history?.hasMore} onClick={() => setPage(n => n + 1)} />
      </nav>
    </main>
  </>;
}

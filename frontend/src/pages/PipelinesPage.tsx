import { Fragment, useEffect, useRef, useState, type ChangeEvent } from 'react';
import { IconButton, IconLink } from '../components/Icon';
import { PageHeader } from '../components/PageHeader';
import { ConfirmDialog } from '../components/ConfirmDialog';
import { Loading } from '../components/Loading';
import { api } from '../pipeline/api';
import { describe } from '../schedule/cron';
import type { PipelineList, PipelineSummary, Schedule } from '../pipeline/types';

const sameEnvironments = (a: string[], b: string[]) => a.length === b.length && a.every(x => b.includes(x));

export function PipelinesPage({ onOpenDesigner }: { onOpenDesigner?: (pipelineId?: string) => void } = {}) {
  const [page, setPage] = useState(1);
  const [refresh, setRefresh] = useState(0);
  const [list, setList] = useState<PipelineList | null>(null);
  const [schedules, setSchedules] = useState<Schedule[]>([]);
  const [scheduling, setScheduling] = useState<{ pipelineId: string; alias: string; environments: string[] } | null>(null);
  const [expandedScheduleId, setExpandedScheduleId] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [message, setMessage] = useState('');
  const [deleting, setDeleting] = useState<PipelineSummary | null>(null);
  const [runNotice, setRunNotice] = useState<{ id: string; pipelineId: string } | null>(null);
  const [environments, setEnvironments] = useState<Record<string, string>>({});
  const importInput = useRef<HTMLInputElement>(null);

  useEffect(() => {
    let disposed = false;
    setLoading(true); setError('');
    api.pipelines(page).then(result => { if (!disposed) setList(result); })
      .catch(ex => { if (!disposed) setError(ex instanceof Error ? ex.message : 'Could not load pipelines.'); })
      .finally(() => { if (!disposed) setLoading(false); });
    // Schedules are supplementary: the pipeline list stays usable if they cannot be loaded.
    api.schedules().then(result => { if (!disposed) setSchedules(result.schedules); })
      .catch(ex => { if (!disposed) setError(ex instanceof Error ? `Schedules unavailable: ${ex.message}` : 'Schedules unavailable.'); });
    return () => { disposed = true; };
  }, [page, refresh]);

  const schedulesFor = (pipelineId: string) => schedules.map(schedule => ({
    schedule, environments: schedule.pipelines.filter(p => p.pipelineId === pipelineId).map(p => p.environment),
  })).filter(entry => entry.environments.length > 0);

  const attachedEnvironments = (alias: string, pipelineId: string) =>
    schedules.find(s => s.alias === alias)?.pipelines.filter(p => p.pipelineId === pipelineId).map(p => p.environment) ?? [];

  function selectSchedule(pipeline: PipelineSummary, alias: string) {
    const attached = attachedEnvironments(alias, pipeline.id);
    const preferred = environments[pipeline.id] ?? pipeline.environments[0];
    setScheduling({ pipelineId: pipeline.id, alias, environments: attached.length ? attached : preferred ? [preferred] : [] });
  }

  function startScheduling(pipeline: PipelineSummary) {
    if (expandedScheduleId === pipeline.id) { setExpandedScheduleId(null); return; }
    if (scheduling?.pipelineId !== pipeline.id) selectSchedule(pipeline, schedules[0]?.alias ?? '');
    setExpandedScheduleId(pipeline.id);
  }

  /** Applies the checked environments for the chosen schedule: adds new ones and removes unchecked ones. */
  async function saveScheduling(pipeline: PipelineSummary) {
    if (!scheduling) return;
    const attached = attachedEnvironments(scheduling.alias, pipeline.id);
    const added = scheduling.environments.filter(e => !attached.includes(e));
    const removed = attached.filter(e => !scheduling.environments.includes(e));
    setBusy(true); setError(''); setMessage('');
    try {
      for (const environment of added) await api.attachSchedule(scheduling.alias, pipeline.id, environment);
      for (const environment of removed) await api.detachSchedule(scheduling.alias, pipeline.id, environment);
      setMessage(scheduling.environments.length
        ? `${pipeline.name} runs on schedule ${scheduling.alias} in: ${scheduling.environments.join(', ')}.`
        : `${pipeline.name} removed from schedule ${scheduling.alias}.`);
      setScheduling(null); setExpandedScheduleId(null);
    } catch (ex) { setError(ex instanceof Error ? ex.message : 'Could not update the schedule.'); }
    finally { setBusy(false); setRefresh(n => n + 1); }
  }

  async function run(pipeline: PipelineSummary) {
    setBusy(true); setError(''); setMessage(''); setRunNotice(null);
    try {
      const result = await api.run(pipeline.id, pipeline.revision, environments[pipeline.id] ?? pipeline.environments[0]);
      setRunNotice({ id: result.id, pipelineId: pipeline.id });
    } catch (ex) { setError(ex instanceof Error ? ex.message : 'Could not start pipeline.'); }
    finally { setBusy(false); }
  }

  async function remove(pipeline: PipelineSummary) {
    setBusy(true); setError(''); setMessage('');
    try {
      await api.deletePipeline(pipeline.id, pipeline.revision);
      setMessage(`Deleted ${pipeline.name}.`);
      if (list?.pipelines.length === 1 && page > 1) setPage(n => n - 1);
      else setRefresh(n => n + 1);
    } catch (ex) { setError(ex instanceof Error ? ex.message : 'Could not delete pipeline.'); }
    finally { setBusy(false); }
  }

  async function importFile(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0];
    event.target.value = '';
    if (!file) return;
    setBusy(true); setError(''); setMessage('');
    try {
      if (file.size > 1024 * 1024) throw new Error('JSON files must be 1 MiB or smaller.');
      const { parseImport } = await import('../pipeline/exchange');
      const imported = await api.importPipeline(parseImport(await file.text(), file.name));
      if (onOpenDesigner) onOpenDesigner(imported.id);
      else window.location.assign(`?page=designer&pipeline=${encodeURIComponent(imported.id)}`);
    } catch (ex) { setError(ex instanceof Error ? ex.message : 'Could not import pipeline.'); }
    finally { setBusy(false); }
  }

  async function exportPipeline(id: string) {
    setBusy(true); setError(''); setMessage('');
    try {
      const saved = await api.load(id);
      const { downloadPipeline } = await import('../pipeline/exchange');
      setMessage(`Exported ${downloadPipeline(saved)}.`);
    } catch (ex) { setError(ex instanceof Error ? ex.message : 'Could not export pipeline.'); }
    finally { setBusy(false); }
  }

  return <>
    <PageHeader page="Pipelines" />
    <main>
      <section className="toolbar" aria-label="Pipeline list actions">
        <IconLink icon="add" label="Create new pipeline" href="?page=designer" className="primary" onClick={event => {
          if (onOpenDesigner && event.button === 0 && !event.ctrlKey && !event.metaKey && !event.shiftKey && !event.altKey) { event.preventDefault(); onOpenDesigner(); }
        }} />
        <IconButton icon="import" label="Import pipeline from JSON" disabled={busy} onClick={() => importInput.current?.click()} />
        <input ref={importInput} type="file" accept=".json,application/json" hidden onChange={event => void importFile(event)} />
        <IconButton icon="refresh" label="Refresh pipelines" loading={loading} disabled={loading || busy} onClick={() => setRefresh(n => n + 1)} />
        <IconLink icon="clock" label="Manage schedules" href="?page=schedules" />
      </section>
      {message && <p role="status" className="notice">{message}</p>}
      {runNotice && <p role="status" className="notice">Run {runNotice.id} queued. Results are available in <a href={`?page=logs&pipeline=${encodeURIComponent(runNotice.pipelineId)}`}>execution logs</a>.</p>}
      {error && <p role="alert" className="notice error">{error}</p>}
      {loading && <Loading label="Loading pipelines…" />}
      {list && <>
        {list.pipelines.length === 0 ? <p>No pipelines yet. Use + to create one or import a JSON file.</p> : <div className="history-table">
          <table><caption>Saved pipelines — page {list.page}</caption>
            <thead><tr><th scope="col">Pipeline</th><th scope="col">Revision</th><th scope="col">Environment</th><th scope="col">Schedules</th><th scope="col">Actions</th></tr></thead>
            <tbody>{list.pipelines.map(pipeline => <Fragment key={pipeline.id}><tr>
              <td><strong>{pipeline.name}</strong><p className="muted"><code>{pipeline.id}</code></p></td>
              <td>{pipeline.revision}</td>
              <td><select aria-label={`Environment for ${pipeline.name}`} disabled={busy} value={environments[pipeline.id] ?? pipeline.environments[0] ?? ''}
                onChange={event => setEnvironments(previous => ({ ...previous, [pipeline.id]: event.target.value }))}>
                {pipeline.environments.map(environment => <option key={environment}>{environment}</option>)}
              </select></td>
              <td>{schedulesFor(pipeline.id).length === 0 ? <span className="muted">Manual only</span> : <ul className="compact">
                {schedulesFor(pipeline.id).map(({ schedule, environments: scheduled }) => <li key={schedule.id}>
                  <strong>{schedule.alias}</strong> <span className="muted">({scheduled.join(', ')})</span>
                  <br /><span className="muted">{schedule.nextRuns[0] ? <>Next: <time dateTime={schedule.nextRuns[0]}>{new Date(schedule.nextRuns[0]).toLocaleString()}</time></> : 'Paused'}</span>
                </li>)}</ul>}</td>
              <td><div className="actions">
                <IconButton icon="play" label={`Run ${pipeline.name}`} disabled={busy || pipeline.environments.length === 0} onClick={() => void run(pipeline)} />
                 <IconButton icon="clock" label={`Schedule ${pipeline.name}`} aria-expanded={expandedScheduleId === pipeline.id} disabled={busy}
                   onClick={() => startScheduling(pipeline)} />
                <IconButton icon="export" label={`Export ${pipeline.name} as JSON`} disabled={busy} onClick={() => void exportPipeline(pipeline.id)} />
                <IconLink icon="logs" label={`View logs for ${pipeline.name}`} href={`?page=logs&pipeline=${pipeline.id}`} />
                <IconLink icon="edit" label={`Open ${pipeline.name} in designer`} href={`?page=designer&pipeline=${pipeline.id}`} onClick={event => {
                  if (onOpenDesigner && event.button === 0 && !event.ctrlKey && !event.metaKey && !event.shiftKey && !event.altKey) { event.preventDefault(); onOpenDesigner(pipeline.id); }
                }} />
                <IconButton icon="trash" label={`Delete ${pipeline.name}`} className="danger" disabled={busy} onClick={() => setDeleting(pipeline)} />
              </div></td>
            </tr>
            <tr className={`schedule-row${expandedScheduleId === pipeline.id ? ' is-open' : ''}`} aria-hidden={expandedScheduleId !== pipeline.id}>
              <td colSpan={5}><div className="schedule-disclosure" inert={expandedScheduleId !== pipeline.id}>
                <div className="schedule-disclosure-content">
              {scheduling?.pipelineId === pipeline.id && (schedules.length === 0 ? <p>No schedules exist yet. <a href="?page=schedules">Create a schedule</a> first.</p> :
                <div className="actions" role="group" aria-label={`Add ${pipeline.name} to a schedule`}>
                  <label>Schedule<select value={scheduling.alias} onChange={e => selectSchedule(pipeline, e.target.value)}>
                    {schedules.map(schedule => <option key={schedule.id} value={schedule.alias}>{schedule.alias} — {describe(schedule.cronExpression)}</option>)}
                  </select></label>
                  <fieldset className="environment-choices"><legend>Run in environments</legend>
                    {pipeline.environments.map(environment => <label key={environment} className="inline">
                      <input type="checkbox" checked={scheduling.environments.includes(environment)} onChange={e => setScheduling({
                        ...scheduling, environments: e.target.checked ? [...scheduling.environments, environment] : scheduling.environments.filter(x => x !== environment),
                      })} />{environment}</label>)}
                  </fieldset>
                  <IconButton icon="save" label={`Save ${pipeline.name} schedule environments`} className="primary"
                    disabled={busy || !scheduling.alias || sameEnvironments(scheduling.environments, attachedEnvironments(scheduling.alias, pipeline.id))}
                    onClick={() => void saveScheduling(pipeline)} />
                  <IconButton icon="cancel" label="Cancel scheduling" disabled={busy} onClick={() => { setExpandedScheduleId(null); setScheduling(null); }} />
                </div>)}
                </div>
              </div></td>
            </tr>
            </Fragment>)}</tbody>
          </table>
        </div>}
      </>}
      <nav className="actions history-pagination" aria-label="Pipeline pagination">
        <IconButton icon="left" label="Previous pipelines" disabled={loading || busy || page === 1} onClick={() => setPage(n => n - 1)} />
        <span>Page {page}</span>
        <IconButton icon="right" label="More pipelines" disabled={loading || busy || !!error || !list?.hasMore} onClick={() => setPage(n => n + 1)} />
      </nav>
      {deleting && <ConfirmDialog title={`Delete ${deleting.name}?`} description="Execution history and generated files will be kept." confirmLabel="Delete pipeline" onCancel={() => setDeleting(null)} onConfirm={() => { const pipeline = deleting; setDeleting(null); void remove(pipeline); }} />}
    </main>
  </>;
}

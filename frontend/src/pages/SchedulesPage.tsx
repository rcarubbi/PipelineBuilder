import { useEffect, useState } from 'react';
import { IconButton, IconLink } from '../components/Icon';
import { PageHeader } from '../components/PageHeader';
import { api } from '../pipeline/api';
import type { Schedule, ScheduledPipeline } from '../pipeline/types';
import {
  defaultSettings, describe, formatTime, fromCron, maxDayOfMonth, minuteIntervals, toCron, validateSettings, weekdays,
  type Frequency, type ScheduleSettings,
} from '../schedule/cron';

interface Draft { id: string; revision: number; alias: string; isEnabled: boolean; advanced: boolean; settings: ScheduleSettings; cron: string }
const newDraft = (): Draft => ({ id: crypto.randomUUID(), revision: 0, alias: '', isEnabled: true, advanced: false, settings: defaultSettings, cron: toCron(defaultSettings) });
const frequencies: [Frequency, string][] = [['minutes', 'Every few minutes'], ['hourly', 'Hourly'], ['daily', 'Daily'], ['weekly', 'Weekly'], ['monthly', 'Monthly']];
const previewDelayMs = 300;
const formatRun = (run: string) => new Date(run).toLocaleString();

export function SchedulesPage() {
  const [schedules, setSchedules] = useState<Schedule[] | null>(null);
  const [refresh, setRefresh] = useState(0);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [message, setMessage] = useState('');
  const [draft, setDraft] = useState<Draft>(newDraft);
  const [preview, setPreview] = useState<{ runs: string[]; error: string }>({ runs: [], error: '' });

  const cronExpression = draft.advanced ? draft.cron.trim() : toCron(draft.settings);
  const settingsError = draft.advanced ? null : validateSettings(draft.settings);

  useEffect(() => {
    let disposed = false;
    setLoading(true);
    api.schedules().then(result => { if (!disposed) { setSchedules(result.schedules); setError(''); } })
      .catch(ex => { if (!disposed) setError(ex instanceof Error ? ex.message : 'Could not load schedules.'); })
      .finally(() => { if (!disposed) setLoading(false); });
    return () => { disposed = true; };
  }, [refresh]);

  useEffect(() => {
    if (settingsError || !cronExpression) { setPreview({ runs: [], error: settingsError ?? 'Enter a cron expression.' }); return; }
    let disposed = false;
    const timer = window.setTimeout(() => {
      api.previewSchedule(cronExpression).then(result => { if (!disposed) setPreview({ runs: result.nextRuns, error: '' }); })
        .catch(ex => { if (!disposed) setPreview({ runs: [], error: ex instanceof Error ? ex.message : 'Invalid schedule.' }); });
    }, previewDelayMs);
    return () => { disposed = true; window.clearTimeout(timer); };
  }, [cronExpression, settingsError]);

  function update(changes: Partial<ScheduleSettings>) { setDraft(previous => ({ ...previous, settings: { ...previous.settings, ...changes } })); }

  function setAdvanced(advanced: boolean) {
    setDraft(previous => {
      if (advanced) return { ...previous, advanced, cron: toCron(previous.settings) };
      const settings = fromCron(previous.cron);
      if (!settings) { setError('This cron expression cannot be shown in the simple editor. Keep editing it directly.'); return previous; }
      return { ...previous, advanced, settings };
    });
  }

  async function perform(action: () => Promise<string>) {
    setBusy(true); setError(''); setMessage('');
    try { setMessage(await action()); setRefresh(n => n + 1); }
    catch (ex) { setError(ex instanceof Error ? ex.message : 'Operation failed.'); }
    finally { setBusy(false); }
  }

  const save = () => perform(async () => {
    const saved = await api.saveSchedule(draft.id, draft.revision, { alias: draft.alias.trim(), cronExpression, isEnabled: draft.isEnabled });
    setDraft(newDraft());
    return `Saved schedule ${saved.alias}.`;
  });

  function edit(schedule: Schedule) {
    const settings = fromCron(schedule.cronExpression);
    setDraft({ id: schedule.id, revision: schedule.revision, alias: schedule.alias, isEnabled: schedule.isEnabled,
      advanced: !settings, settings: settings ?? defaultSettings, cron: schedule.cronExpression });
    setError(''); setMessage('');
  }

  const remove = (schedule: Schedule) => {
    if (!window.confirm(`Delete schedule "${schedule.alias}"?`)) return;
    void perform(async () => { await api.deleteSchedule(schedule.id, schedule.revision); if (draft.id === schedule.id) setDraft(newDraft()); return `Deleted schedule ${schedule.alias}.`; });
  };

  const detach = (schedule: Schedule, link: ScheduledPipeline) =>
    void perform(async () => {
      await api.detachSchedule(schedule.alias, link.pipelineId, link.environment);
      return `Removed ${link.pipelineName} (${link.environment}) from ${schedule.alias}.`;
    });

  const { settings } = draft;
  const editing = draft.revision > 0;
  return <>
    <PageHeader page="Schedules" />
    <main>
      <section className="toolbar" aria-label="Schedule actions">
        <IconLink icon="list" label="Back to pipelines" href="/" />
        <IconButton icon="refresh" label="Refresh schedules" disabled={loading || busy} onClick={() => setRefresh(n => n + 1)} />
        <p className="muted">Schedules run in UTC. Attach pipelines from the pipeline list. Runs missed while the server is offline are skipped.</p>
      </section>
      {message && <p role="status" className="notice">{message}</p>}
      {error && <p role="alert" className="notice error">{error}</p>}
      <div className="schedule-layout">
        <section className="panel" aria-label="Schedule editor">
          <h2>{editing ? `Edit ${draft.alias}` : 'New schedule'}</h2>
          <label>Alias<input value={draft.alias} maxLength={64} placeholder="e.g. weekday-morning" onChange={e => setDraft({ ...draft, alias: e.target.value })} /></label>
          <label className="inline"><input type="checkbox" checked={draft.advanced} onChange={e => setAdvanced(e.target.checked)} />Edit cron expression directly</label>
          {draft.advanced ? <label>Cron expression (UTC)<input value={draft.cron} spellCheck={false} placeholder="minute hour day month weekday" onChange={e => setDraft({ ...draft, cron: e.target.value })} /></label> : <>
            <label>Repeat<select value={settings.frequency} onChange={e => update({ frequency: e.target.value as Frequency })}>
              {frequencies.map(([value, label]) => <option key={value} value={value}>{label}</option>)}
            </select></label>
            {settings.frequency === 'minutes' && <label>Interval<select value={settings.interval} onChange={e => update({ interval: Number(e.target.value) })}>
              {minuteIntervals.map(n => <option key={n} value={n}>{n === 1 ? 'Every minute' : `Every ${n} minutes`}</option>)}
            </select></label>}
            {settings.frequency === 'hourly' && <label>At minute<input type="number" min={0} max={59} value={settings.minute}
              onChange={e => update({ minute: Math.min(59, Math.max(0, Number(e.target.value) || 0)) })} /></label>}
            {['daily', 'weekly', 'monthly'].includes(settings.frequency) && <label>Time (UTC)<input type="time" required value={formatTime(settings.hour, settings.minute)}
              onChange={e => { const [hour, minute] = e.target.value.split(':').map(Number); if (!Number.isNaN(hour) && !Number.isNaN(minute)) update({ hour, minute }); }} /></label>}
            {settings.frequency === 'weekly' && <fieldset><legend>Days</legend><div className="weekday-picker">
              {weekdays.map((day, index) => <label key={day} className="inline"><input type="checkbox" checked={settings.days.includes(index)}
                onChange={e => update({ days: e.target.checked ? [...settings.days, index] : settings.days.filter(d => d !== index) })} />{day.slice(0, 3)}</label>)}
            </div></fieldset>}
            {settings.frequency === 'monthly' && <label>Day of month<select value={settings.dayOfMonth} onChange={e => update({ dayOfMonth: Number(e.target.value) })}>
              {Array.from({ length: maxDayOfMonth }, (_, i) => i + 1).map(day => <option key={day} value={day}>{day}</option>)}
            </select></label>}
          </>}
          <label className="inline"><input type="checkbox" checked={draft.isEnabled} onChange={e => setDraft({ ...draft, isEnabled: e.target.checked })} />Enabled</label>
          <div className="schedule-preview" aria-live="polite">
            <p><strong>{describe(cronExpression)}</strong></p>
            <p>Cron: <code>{cronExpression || '—'}</code></p>
            {preview.error ? <p className="field-error">{preview.error}</p> : <>
              <p className="muted">Next runs (your local time):</p>
              <ol>{preview.runs.map(run => <li key={run}><time dateTime={run}>{formatRun(run)}</time></li>)}</ol>
            </>}
          </div>
          <div className="actions">
            <IconButton icon="save" label={editing ? 'Save schedule' : 'Create schedule'} className="primary"
              disabled={busy || !draft.alias.trim() || !!preview.error || !cronExpression} onClick={() => void save()} />
            {editing && <IconButton icon="cancel" label="Cancel editing" disabled={busy} onClick={() => setDraft(newDraft())} />}
          </div>
        </section>
        <section aria-label="Schedules">
          {loading && !schedules && <p role="status">Loading schedules…</p>}
          {schedules?.length === 0 && <p>No schedules yet. Create one with the editor.</p>}
          {!!schedules?.length && <div className="history-table"><table>
            <caption>Schedules</caption>
            <thead><tr><th scope="col">Alias</th><th scope="col">When</th><th scope="col">Next runs</th><th scope="col">Pipelines</th><th scope="col">Actions</th></tr></thead>
            <tbody>{schedules.map(schedule => <tr key={schedule.id}>
              <td><strong>{schedule.alias}</strong><p className="muted">{schedule.isEnabled ? 'Enabled' : 'Disabled'}</p></td>
              <td>{describe(schedule.cronExpression)}<p><code>{schedule.cronExpression}</code></p></td>
              <td>{schedule.nextRuns.length ? <ol className="compact">{schedule.nextRuns.map(run => <li key={run}><time dateTime={run}>{formatRun(run)}</time></li>)}</ol>
                : <span className="muted">{schedule.isEnabled ? 'Not scheduled: no pipelines' : 'Paused'}</span>}</td>
              <td>{schedule.pipelines.length === 0 ? <span className="muted">None</span> : <ul className="compact">{schedule.pipelines.map(p => <li key={`${p.pipelineId}/${p.environment}`} className="chip">
                {p.pipelineName} <span className="muted">({p.environment})</span>
                <IconButton icon="unlink" label={`Remove ${p.pipelineName} (${p.environment}) from ${schedule.alias}`} disabled={busy} onClick={() => detach(schedule, p)} />
              </li>)}</ul>}</td>
              <td><div className="actions">
                <IconButton icon="edit" label={`Edit ${schedule.alias}`} disabled={busy} onClick={() => edit(schedule)} />
                <IconButton icon="trash" label={`Delete ${schedule.alias}`} className="danger" disabled={busy || schedule.pipelines.length > 0}
                  title={schedule.pipelines.length > 0 ? 'Remove attached pipelines before deleting' : undefined} onClick={() => remove(schedule)} />
              </div></td>
            </tr>)}</tbody>
          </table></div>}
        </section>
      </div>
    </main>
  </>;
}

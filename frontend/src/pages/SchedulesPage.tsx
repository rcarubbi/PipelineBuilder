import { useEffect, useState } from 'react';
import { IconButton, IconLink } from '../components/Icon';
import { PageHeader } from '../components/PageHeader';
import { ConfirmDialog } from '../components/ConfirmDialog';
import { Loading } from '../components/Loading';
import { api } from '../pipeline/api';
import { Input } from '../components/ui/input';
import { Select } from '../components/ui/select';
import { Card } from '../components/ui/card';
import { Badge } from '../components/ui/badge';
import { Toast, Toaster } from '../components/Toast';
import { Table, TableBody, TableCaption, TableCell, TableHead, TableHeader, TableRow } from '../components/ui/table';
import { SwitchField } from '../components/ui/switch-field';
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
  const [deleting, setDeleting] = useState<Schedule | null>(null);
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
      <Card className="toolbar" aria-label="Schedule actions">
        <IconLink icon="list" label="Back to pipelines" href="/" />
        <IconButton icon="refresh" label="Refresh schedules" loading={loading} disabled={loading || busy} onClick={() => setRefresh(n => n + 1)} />
        <p className="muted">Schedules run in UTC. Attach pipelines from the pipeline list. Runs missed while the server is offline are skipped.</p>
      </Card>
      {(message || error) && <Toaster>
        {message && <Toast key={`message:${message}`}>{message}</Toast>}
        {error && <Toast key={`error:${error}`} variant="destructive">{error}</Toast>}
      </Toaster>}
      <div className="schedule-layout">
        <Card className="panel" aria-label="Schedule editor">
          <h2>{editing ? `Edit ${draft.alias}` : 'New schedule'}</h2>
          <label>Alias<Input value={draft.alias} maxLength={64} placeholder="e.g. weekday-morning" onChange={e => setDraft({ ...draft, alias: e.target.value })} /></label>
          <SwitchField label="Edit cron expression directly" checked={draft.advanced} onCheckedChange={setAdvanced} />
          <div className={`schedule-mode-panel${draft.advanced ? ' is-active' : ''}`} aria-hidden={!draft.advanced} inert={!draft.advanced}>
            <div className="schedule-mode-content"><label>Cron expression (UTC)<Input value={draft.cron} spellCheck={false} placeholder="minute hour day month weekday" onChange={e => setDraft({ ...draft, cron: e.target.value })} /></label></div>
          </div>
          <div className={`schedule-mode-panel${draft.advanced ? '' : ' is-active'}`} aria-hidden={draft.advanced} inert={draft.advanced}>
            <div className="schedule-mode-content">
            <label>Repeat<Select value={settings.frequency} onChange={e => update({ frequency: e.target.value as Frequency })}>
              {frequencies.map(([value, label]) => <option key={value} value={value}>{label}</option>)}
            </Select></label>
            <div className={`schedule-mode-panel${settings.frequency === 'minutes' ? ' is-active' : ''}`} aria-hidden={settings.frequency !== 'minutes'} inert={settings.frequency !== 'minutes'}>
              <div className="schedule-mode-content"><label>Interval<Select value={settings.interval} onChange={e => update({ interval: Number(e.target.value) })}>
                {minuteIntervals.map(n => <option key={n} value={n}>{n === 1 ? 'Every minute' : `Every ${n} minutes`}</option>)}
              </Select></label></div>
            </div>
            <div className={`schedule-mode-panel${settings.frequency === 'hourly' ? ' is-active' : ''}`} aria-hidden={settings.frequency !== 'hourly'} inert={settings.frequency !== 'hourly'}>
              <div className="schedule-mode-content"><label>At minute<Input type="number" min={0} max={59} value={settings.minute}
                onChange={e => update({ minute: Math.min(59, Math.max(0, Number(e.target.value) || 0)) })} /></label></div>
            </div>
            <div className={`schedule-mode-panel${['daily', 'weekly', 'monthly'].includes(settings.frequency) ? ' is-active' : ''}`} aria-hidden={!['daily', 'weekly', 'monthly'].includes(settings.frequency)} inert={!['daily', 'weekly', 'monthly'].includes(settings.frequency)}>
              <div className="schedule-mode-content"><label>Time (UTC)<Input type="time" required value={formatTime(settings.hour, settings.minute)}
                onChange={e => { const [hour, minute] = e.target.value.split(':').map(Number); if (!Number.isNaN(hour) && !Number.isNaN(minute)) update({ hour, minute }); }} /></label></div>
            </div>
            <div className={`schedule-mode-panel${settings.frequency === 'weekly' ? ' is-active' : ''}`} aria-hidden={settings.frequency !== 'weekly'} inert={settings.frequency !== 'weekly'}>
              <div className="schedule-mode-content"><fieldset><legend>Days</legend><div className="weekday-picker">
              {weekdays.map((day, index) => <SwitchField key={day} label={day.slice(0, 3)} checked={settings.days.includes(index)}
                onCheckedChange={checked => update({ days: checked ? [...settings.days, index] : settings.days.filter(d => d !== index) })} />)}
              </div></fieldset></div>
            </div>
            <div className={`schedule-mode-panel${settings.frequency === 'monthly' ? ' is-active' : ''}`} aria-hidden={settings.frequency !== 'monthly'} inert={settings.frequency !== 'monthly'}>
              <div className="schedule-mode-content"><label>Day of month<Select value={settings.dayOfMonth} onChange={e => update({ dayOfMonth: Number(e.target.value) })}>
                {Array.from({ length: maxDayOfMonth }, (_, i) => i + 1).map(day => <option key={day} value={day}>{day}</option>)}
              </Select></label></div>
            </div>
            </div>
          </div>
          <SwitchField label="Enabled" checked={draft.isEnabled} onCheckedChange={isEnabled => setDraft({ ...draft, isEnabled })} />
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
        </Card>
        <section aria-label="Schedules">
          {loading && <Loading label="Loading schedules…" />}
          {schedules?.length === 0 && <p>No schedules yet. Create one with the editor.</p>}
          {!!schedules?.length && <div className="history-table"><Table>
            <TableCaption>Schedules</TableCaption>
            <TableHeader><TableRow><TableHead scope="col">Alias</TableHead><TableHead scope="col">When</TableHead><TableHead scope="col">Next runs</TableHead><TableHead scope="col">Pipelines</TableHead><TableHead scope="col">Actions</TableHead></TableRow></TableHeader>
            <TableBody>{schedules.map(schedule => <TableRow key={schedule.id}>
              <TableCell><strong>{schedule.alias}</strong><p className="muted"><Badge variant={schedule.isEnabled ? 'secondary' : 'outline'}>{schedule.isEnabled ? 'Enabled' : 'Disabled'}</Badge></p></TableCell>
              <TableCell>{describe(schedule.cronExpression)}<p><code>{schedule.cronExpression}</code></p></TableCell>
              <TableCell>{schedule.nextRuns.length ? <ol className="compact">{schedule.nextRuns.map(run => <li key={run}><time dateTime={run}>{formatRun(run)}</time></li>)}</ol>
                : <span className="muted">{schedule.isEnabled ? 'Not scheduled: no pipelines' : 'Paused'}</span>}</TableCell>
              <TableCell>{schedule.pipelines.length === 0 ? <span className="muted">None</span> : <ul className="compact">{schedule.pipelines.map(p => <li key={`${p.pipelineId}/${p.environment}`} className="chip">
                {p.pipelineName} <span className="muted">({p.environment})</span>
                <IconButton icon="unlink" label={`Remove ${p.pipelineName} (${p.environment}) from ${schedule.alias}`} disabled={busy} onClick={() => detach(schedule, p)} />
              </li>)}</ul>}</TableCell>
              <TableCell><div className="actions">
                <IconButton icon="edit" label={`Edit ${schedule.alias}`} disabled={busy} onClick={() => edit(schedule)} />
                <IconButton icon="trash" label={`Delete ${schedule.alias}`} className="danger" disabled={busy || schedule.pipelines.length > 0}
                  title={schedule.pipelines.length > 0 ? 'Remove attached pipelines before deleting' : undefined} onClick={() => setDeleting(schedule)} />
              </div></TableCell>
            </TableRow>)}</TableBody>
          </Table></div>}
        </section>
      </div>
      <ConfirmDialog open={!!deleting} title={`Delete schedule ${deleting?.alias}?`} description="This schedule will be permanently removed." confirmLabel="Delete schedule" onCancel={() => setDeleting(null)} onConfirm={() => { if (!deleting) return; const schedule = deleting; setDeleting(null); remove(schedule); }} />
    </main>
  </>;
}

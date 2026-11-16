import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { api } from '../pipeline/api';
import type { Schedule } from '../pipeline/types';
import { SchedulesPage } from './SchedulesPage';

vi.mock('../pipeline/api', () => ({ api: { schedules: vi.fn(), saveSchedule: vi.fn(), deleteSchedule: vi.fn(), previewSchedule: vi.fn(), detachSchedule: vi.fn() } }));

const nextRun = '2026-11-16T08:30:00Z';
const schedule: Schedule = {
  id: 'schedule-1', alias: 'weekday-morning', cronExpression: '30 8 * * 1,2,3,4,5', isEnabled: true, revision: 2,
  pipelines: [{ pipelineId: 'pipeline-1', pipelineName: 'Customers', environment: 'test' }], nextRuns: [nextRun],
};

beforeEach(() => {
  vi.mocked(api.schedules).mockReset().mockResolvedValue({ schedules: [schedule] });
  vi.mocked(api.previewSchedule).mockReset().mockResolvedValue({ nextRuns: [nextRun] });
  vi.mocked(api.saveSchedule).mockReset().mockResolvedValue(schedule);
  vi.mocked(api.deleteSchedule).mockReset();
  vi.mocked(api.detachSchedule).mockReset().mockResolvedValue({ ...schedule, pipelines: [] });
});
afterEach(() => vi.restoreAllMocks());

it('keeps schedules visible beneath the loading overlay during refresh', async () => {
  render(<SchedulesPage />);
  await screen.findByRole('table', { name: 'Schedules' });
  vi.mocked(api.schedules).mockReturnValueOnce(new Promise(() => {}));
  await userEvent.setup().click(screen.getByRole('button', { name: 'Refresh schedules' }));
  expect(screen.getByRole('table', { name: 'Schedules' })).toBeInTheDocument();
  expect(screen.getByText('Loading schedules…').closest('.loading-overlay')).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Refresh schedules' })).toHaveAttribute('aria-busy', 'true');
});

it('lists schedules with plain-language timing, cron, next runs and attached pipelines', async () => {
  render(<SchedulesPage />);
  const header = within(screen.getByRole('banner'));
  expect(header.getByRole('heading', { name: 'Pipeline Builder', level: 1 })).toBeInTheDocument();
  expect(header.getByText('Schedules')).toBeInTheDocument();

  const table = within(await screen.findByRole('table', { name: 'Schedules' }));
  expect(table.getByText('weekday-morning')).toBeInTheDocument();
  expect(table.getByText('Every weekday at 08:30 UTC')).toBeInTheDocument();
  expect(table.getByText('30 8 * * 1,2,3,4,5')).toBeInTheDocument();
  expect(table.getByText(new Date(nextRun).toLocaleString())).toBeInTheDocument();
  expect(table.getByText('Customers')).toBeInTheDocument();
  expect(table.getByRole('button', { name: 'Delete weekday-morning' })).toBeDisabled();
});

it('builds a weekly cron expression from friendly controls, previews it and saves it', async () => {
  const user = userEvent.setup();
  render(<SchedulesPage />);
  await screen.findByRole('table', { name: 'Schedules' });

  await user.type(screen.getByLabelText('Alias'), 'mon-wed');
  await user.selectOptions(screen.getByLabelText('Repeat'), 'weekly');
  for (const day of ['Tue', 'Thu', 'Fri']) await user.click(screen.getByLabelText(day));
  const time = screen.getByLabelText('Time (UTC)');
  await user.clear(time);
  await user.type(time, '09:15');

  expect(await screen.findByText('Every Monday, Wednesday at 09:15 UTC')).toBeInTheDocument();
  expect(screen.getByText('15 9 * * 1,3')).toBeInTheDocument();
  await vi.waitFor(() => expect(api.previewSchedule).toHaveBeenLastCalledWith('15 9 * * 1,3'));
  await user.click(screen.getByRole('button', { name: 'Create schedule' }));

  expect(api.saveSchedule).toHaveBeenCalledWith(expect.any(String), 0, { alias: 'mon-wed', cronExpression: '15 9 * * 1,3', isEnabled: true });
  expect(await screen.findByText('Saved schedule weekday-morning.')).toBeInTheDocument();
});

it('blocks saving when no weekday is selected or the cron expression is rejected', async () => {
  const user = userEvent.setup();
  render(<SchedulesPage />);
  await screen.findByRole('table', { name: 'Schedules' });
  await user.type(screen.getByLabelText('Alias'), 'empty');
  await user.selectOptions(screen.getByLabelText('Repeat'), 'weekly');
  for (const day of ['Mon', 'Tue', 'Wed', 'Thu', 'Fri']) await user.click(screen.getByLabelText(day));

  expect(await screen.findByText('Select at least one day.')).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Create schedule' })).toBeDisabled();

  vi.mocked(api.previewSchedule).mockRejectedValue(new Error('Use a valid five-field cron expression.'));
  await user.click(screen.getByLabelText('Edit cron expression directly'));
  const cron = screen.getByLabelText('Cron expression (UTC)');
  await user.clear(cron);
  await user.type(cron, '0 0 8 * * *');

  expect(await screen.findByText('Use a valid five-field cron expression.')).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Create schedule' })).toBeDisabled();
  expect(api.saveSchedule).not.toHaveBeenCalled();
});

it('transitions between the simple and direct cron editors', async () => {
  const user = userEvent.setup();
  render(<SchedulesPage />);
  await screen.findByRole('table', { name: 'Schedules' });
  const repeat = screen.getByLabelText('Repeat');
  const cron = screen.getByLabelText('Cron expression (UTC)');
  const simplePanel = repeat.closest('.schedule-mode-panel')!;
  const cronPanel = cron.closest('.schedule-mode-panel')!;

  expect(simplePanel).toHaveClass('is-active');
  expect(cronPanel).not.toHaveClass('is-active');
  await user.click(screen.getByLabelText('Edit cron expression directly'));
  expect(simplePanel).not.toHaveClass('is-active');
  expect(cronPanel).toHaveClass('is-active');
  await user.click(screen.getByLabelText('Edit cron expression directly'));
  expect(simplePanel).toHaveClass('is-active');
  expect(cronPanel).not.toHaveClass('is-active');
});

it('edits an existing schedule with its revision and keeps custom cron expressions in advanced mode', async () => {
  vi.mocked(api.schedules).mockResolvedValue({ schedules: [schedule, { ...schedule, id: 'schedule-2', alias: 'custom', cronExpression: '0 8-17 * * 1-5', pipelines: [] }] });
  const user = userEvent.setup();
  render(<SchedulesPage />);
  await screen.findByRole('table', { name: 'Schedules' });

  await user.click(screen.getByRole('button', { name: 'Edit custom' }));
  expect(screen.getByLabelText('Cron expression (UTC)')).toHaveValue('0 8-17 * * 1-5');
  await user.click(screen.getByLabelText('Edit cron expression directly'));
  expect(screen.getByRole('alert')).toHaveTextContent('cannot be shown in the simple editor');
  expect(screen.getByLabelText('Cron expression (UTC)')).toBeInTheDocument();

  await user.click(screen.getByRole('button', { name: 'Edit weekday-morning' }));
  expect(screen.getByLabelText('Repeat')).toHaveValue('weekly');
  await user.click(screen.getByLabelText('Enabled'));
  await vi.waitFor(() => expect(screen.getByRole('button', { name: 'Save schedule' })).toBeEnabled());
  await user.click(screen.getByRole('button', { name: 'Save schedule' }));

  expect(api.saveSchedule).toHaveBeenCalledWith('schedule-1', 2, { alias: 'weekday-morning', cronExpression: '30 8 * * 1,2,3,4,5', isEnabled: false });
});

it('lists the same pipeline once per environment and removes only the chosen environment', async () => {
  vi.mocked(api.schedules).mockResolvedValue({ schedules: [{ ...schedule, pipelines: ['dev', 'test'].map(environment => ({ pipelineId: 'pipeline-1', pipelineName: 'Customers', environment })) }] });
  const user = userEvent.setup();
  render(<SchedulesPage />);
  await screen.findByRole('table', { name: 'Schedules' });
  expect(screen.getByRole('button', { name: 'Remove Customers (dev) from weekday-morning' })).toBeInTheDocument();

  await user.click(screen.getByRole('button', { name: 'Remove Customers (test) from weekday-morning' }));

  expect(api.detachSchedule).toHaveBeenCalledExactlyOnceWith('weekday-morning', 'pipeline-1', 'test');
  expect(await screen.findByText('Removed Customers (test) from weekday-morning.')).toBeInTheDocument();
});

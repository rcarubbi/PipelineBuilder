import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { api } from '../pipeline/api';
import { PipelinesPage } from './PipelinesPage';

vi.mock('../pipeline/api', () => ({ api: { pipelines: vi.fn(), run: vi.fn(), deletePipeline: vi.fn(), load: vi.fn(), importPipeline: vi.fn(), schedules: vi.fn(), attachSchedule: vi.fn(), detachSchedule: vi.fn() } }));

const pipeline = { id: 'pipeline-1', name: 'Customers', revision: 3, environments: ['development', 'test'] };
const schedule = { id: 'schedule-1', alias: 'weekday-morning', cronExpression: '30 8 * * 1,2,3,4,5', isEnabled: true, revision: 1, pipelines: [], nextRuns: [] };
beforeEach(() => {
  vi.mocked(api.pipelines).mockReset().mockResolvedValue({ page: 1, hasMore: false, totalCount: 1, pipelines: [pipeline] });
  vi.mocked(api.schedules).mockReset().mockResolvedValue({ schedules: [schedule] });
  vi.mocked(api.attachSchedule).mockReset();
  vi.mocked(api.detachSchedule).mockReset();
  vi.mocked(api.run).mockReset();
  vi.mocked(api.deletePipeline).mockReset();
  vi.mocked(api.load).mockReset();
  vi.mocked(api.importPipeline).mockReset();
});
afterEach(() => { vi.restoreAllMocks(); vi.unstubAllGlobals(); });

it('keeps pipelines visible beneath the loading overlay during refresh', async () => {
  render(<PipelinesPage />);
  await screen.findByText('Customers');
  vi.mocked(api.pipelines).mockReturnValueOnce(new Promise(() => {}));
  await userEvent.setup().click(screen.getByRole('button', { name: 'Refresh pipelines' }));
  expect(screen.getByText('Customers')).toBeInTheDocument();
  expect(screen.getByText('Loading pipelines…').closest('.loading-overlay')).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Refresh pipelines' })).toHaveAttribute('aria-busy', 'true');
});

it('lists pipelines with accessible icon actions and navigation hints', async () => {
  render(<PipelinesPage />);
  expect(await screen.findByText('Customers')).toBeInTheDocument();
  const header = within(screen.getByRole('banner'));
  expect(header.getByRole('heading', { name: 'Pipeline Builder', level: 1 })).toBeInTheDocument();
  expect(header.getByText('Pipelines')).toBeInTheDocument();
  expect(screen.getByRole('link', { name: 'Create new pipeline' })).toHaveAttribute('href', '?page=designer');
  expect(screen.getByRole('link', { name: 'Open Customers in designer' })).toHaveAttribute('href', '?page=designer&pipeline=pipeline-1');
  expect(screen.getByRole('link', { name: 'View logs for Customers' })).toHaveAttribute('href', '?page=logs&pipeline=pipeline-1');
  expect(screen.getByRole('table', { name: 'Saved pipelines' })).toBeInTheDocument();
  expect(screen.queryByText(/Saved pipelines — page/)).not.toBeInTheDocument();
  const run = screen.getByRole('button', { name: 'Run Customers' });
  expect(run).toHaveAttribute('title', 'Run Customers');
  expect(run.textContent).toBe('');
  expect(run.querySelector('svg')).toHaveAttribute('aria-hidden', 'true');
  expect(within(screen.getByRole('banner')).queryByRole('link')).not.toBeInTheDocument();
});

it('runs the saved revision in the environment selected by the user', async () => {
  vi.mocked(api.run).mockResolvedValue({ id: 'run-1' } as Awaited<ReturnType<typeof api.run>>);
  const user = userEvent.setup();
  render(<PipelinesPage />);
  await screen.findByText('Customers');

  await user.selectOptions(screen.getByRole('combobox', { name: 'Environment for Customers' }), 'test');
  await user.click(screen.getByRole('button', { name: 'Run Customers' }));

  expect(api.run).toHaveBeenCalledWith('pipeline-1', 3, 'test');
  expect(await screen.findByRole('link', { name: 'execution logs' })).toHaveAttribute('href', '?page=logs&pipeline=pipeline-1');
});

it('requires confirmation before deleting and refreshes the list afterward', async () => {
  Object.defineProperty(HTMLDialogElement.prototype, 'showModal', { configurable: true, value(this: HTMLDialogElement) { this.setAttribute('open', ''); } });
  vi.mocked(api.deletePipeline).mockResolvedValue({ deleted: true });
  const user = userEvent.setup();
  render(<PipelinesPage />);
  await screen.findByText('Customers');
  await user.click(screen.getByRole('button', { name: 'Delete Customers' }));
  expect(screen.getByText('Execution history and generated files will be kept.')).toBeInTheDocument();
  await user.click(screen.getByRole('button', { name: 'Cancel' }));
  expect(api.deletePipeline).not.toHaveBeenCalled();

  vi.mocked(api.pipelines).mockResolvedValue({ page: 1, hasMore: false, totalCount: 0, pipelines: [] });
  await user.click(screen.getByRole('button', { name: 'Delete Customers' }));
  await user.click(screen.getByRole('button', { name: 'Delete pipeline' }));
  expect(api.deletePipeline).toHaveBeenCalledWith('pipeline-1', 3);
  expect(await screen.findByText(/No pipelines yet/)).toBeInTheDocument();
  Reflect.deleteProperty(HTMLDialogElement.prototype, 'showModal');
});

it('keeps a pipeline visible when deletion is rejected', async () => {
  Object.defineProperty(HTMLDialogElement.prototype, 'showModal', { configurable: true, value(this: HTMLDialogElement) { this.setAttribute('open', ''); } });
  vi.mocked(api.deletePipeline).mockRejectedValue(new Error('Pipeline has active runs.'));
  render(<PipelinesPage />);
  await screen.findByText('Customers');

  await userEvent.setup().click(screen.getByRole('button', { name: 'Delete Customers' }));
  await userEvent.setup().click(screen.getByRole('button', { name: 'Delete pipeline' }));

  expect(await screen.findByRole('alert')).toHaveTextContent('Pipeline has active runs.');
  expect(screen.getByText('Customers')).toBeInTheDocument();
  Reflect.deleteProperty(HTMLDialogElement.prototype, 'showModal');
});

it('handles an empty list without hiding the create action', async () => {
  vi.mocked(api.pipelines).mockResolvedValue({ page: 1, hasMore: false, totalCount: 0, pipelines: [] });
  render(<PipelinesPage />);

  expect(await screen.findByText(/No pipelines yet/)).toBeInTheDocument();
  expect(screen.getByRole('link', { name: 'Create new pipeline' })).toBeVisible();
  expect(screen.getByRole('button', { name: 'Next page' })).toBeDisabled();
});

it('imports a JSON definition from the pipeline list', async () => {
  vi.mocked(api.importPipeline).mockResolvedValue({ id: 'imported-id' } as Awaited<ReturnType<typeof api.importPipeline>>);
  const user = userEvent.setup();
  render(<PipelinesPage />);
  await screen.findByText('Customers');

  const definition = { formatVersion: 1, steps: [
    { type: 'csv', id: 'csv', path: 'input.csv', delimiter: ',', hasHeader: true, schema: [{ name: 'id', type: 'int64', nullable: false }] },
    { type: 'parquet', id: 'parquet', path: 'output.parquet' },
  ] };
  const file = new File([JSON.stringify(definition)], 'Imported.json', { type: 'application/json' });
  const input = document.querySelector<HTMLInputElement>('input[type="file"]')!;
  await user.upload(input, file);

  await waitFor(() => expect(api.importPipeline).toHaveBeenCalledWith(expect.objectContaining({ name: 'Imported', environments: { default: {} } })));
});

it('exports the latest saved pipeline from its row', async () => {
  const createObjectURL = vi.fn().mockReturnValue('blob:export');
  vi.stubGlobal('URL', Object.assign(class extends URL {}, { createObjectURL, revokeObjectURL: vi.fn() }));
  vi.mocked(api.load).mockResolvedValue({ id: pipeline.id, revision: 3, document: { name: pipeline.name, definition: { formatVersion: 1, steps: [] }, workspace: {}, environments: { test: {} } } });
  const user = userEvent.setup();
  render(<PipelinesPage />);
  await screen.findByText('Customers');

  await user.click(screen.getByRole('button', { name: 'Export Customers as JSON' }));

  expect(await screen.findByText('Exported Customers.json.')).toBeInTheDocument();
  expect(api.load).toHaveBeenCalledWith(pipeline.id);
  expect(createObjectURL).toHaveBeenCalledOnce();
});

it('loads subsequent pages', async () => {
  vi.mocked(api.pipelines).mockResolvedValueOnce({ page: 1, hasMore: true, totalCount: 26, pipelines: [pipeline] })
    .mockResolvedValueOnce({ page: 2, hasMore: false, totalCount: 26, pipelines: [{ ...pipeline, id: 'pipeline-2', name: 'Orders' }] });
  render(<PipelinesPage />);
  await screen.findByText('Customers');

  await userEvent.setup().click(screen.getByRole('button', { name: 'Next page' }));

  expect(await screen.findByText('Orders')).toBeInTheDocument();
  expect(api.pipelines).toHaveBeenLastCalledWith(2);
  expect(screen.getByText('Page 2 of 2 · 26 total')).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Next page' })).toBeDisabled();
});

it('shows load errors and supports retry', async () => {
  vi.mocked(api.pipelines).mockRejectedValueOnce(new Error('Server unavailable.'));
  render(<PipelinesPage />);
  expect(await screen.findByRole('alert')).toHaveTextContent('Server unavailable.');

  await userEvent.setup().click(screen.getByRole('button', { name: 'Refresh pipelines' }));

  expect(await screen.findByText('Customers')).toBeInTheDocument();
});

it('adds the same pipeline to one schedule in several environments and shows the next run', async () => {
  const nextRun = '2026-11-16T08:30:00Z';
  const links = ['development', 'test'].map(environment => ({ pipelineId: 'pipeline-1', pipelineName: 'Customers', environment }));
  vi.mocked(api.attachSchedule).mockResolvedValue({ ...schedule, pipelines: links });
  const user = userEvent.setup();
  render(<PipelinesPage />);
  await screen.findByText('Customers');
  expect(screen.getByText('Manual only')).toBeInTheDocument();
  expect(screen.getByRole('link', { name: 'Manage schedules' })).toHaveAttribute('href', '?page=schedules');

  await user.click(screen.getByRole('button', { name: 'Schedule Customers' }));
  const form = within(screen.getByRole('group', { name: 'Add Customers to a schedule' }));
  expect(form.getByRole('option', { name: 'weekday-morning — Every weekday at 08:30 UTC' })).toBeInTheDocument();
  expect(form.getByRole('switch', { name: 'development' })).not.toBeChecked();
  expect(form.getByRole('switch', { name: 'development' })).toBeDisabled();
  await user.selectOptions(form.getByRole('combobox', { name: 'Schedule' }), 'weekday-morning');
  await user.click(form.getByRole('switch', { name: 'development' }));
  expect(form.getByRole('switch', { name: 'test' })).not.toBeChecked();
  await user.click(form.getByRole('switch', { name: 'test' }));
  vi.mocked(api.schedules).mockResolvedValue({ schedules: [{ ...schedule, nextRuns: [nextRun], pipelines: links }] });
  await user.click(form.getByRole('button', { name: 'Save Customers schedule environments' }));

  expect(api.attachSchedule).toHaveBeenCalledTimes(2);
  expect(api.attachSchedule).toHaveBeenCalledWith('weekday-morning', 'pipeline-1', 'development');
  expect(api.attachSchedule).toHaveBeenCalledWith('weekday-morning', 'pipeline-1', 'test');
  expect(await screen.findByText('Customers runs on schedule weekday-morning in: development, test.')).toBeInTheDocument();
  expect(await screen.findByText('(development, test)')).toBeInTheDocument();
  expect(screen.getByText(new Date(nextRun).toLocaleString())).toBeInTheDocument();
  expect(screen.queryByRole('group', { name: 'Add Customers to a schedule' })).not.toBeInTheDocument();
});

it('collapses and reopens the schedule editor without losing the current draft', async () => {
  const user = userEvent.setup();
  render(<PipelinesPage />);
  await screen.findByText('Customers');
  const toggle = screen.getByRole('button', { name: 'Schedule Customers' });
  await user.click(toggle);
  const form = within(screen.getByRole('group', { name: 'Add Customers to a schedule' }));
  await user.selectOptions(form.getByRole('combobox', { name: 'Schedule' }), 'weekday-morning');
  await user.click(form.getByRole('switch', { name: 'test' }));

  await user.click(toggle);
  expect(toggle).toHaveAttribute('aria-expanded', 'false');
  expect(screen.queryByRole('group', { name: 'Add Customers to a schedule' })).not.toBeInTheDocument();

  await user.click(toggle);
  expect(toggle).toHaveAttribute('aria-expanded', 'true');
  expect(within(screen.getByRole('group', { name: 'Add Customers to a schedule' })).getByRole('switch', { name: 'test' })).toBeChecked();
});

it('shows saved environments only after selecting their schedule', async () => {
  const links = [{ pipelineId: 'pipeline-1', pipelineName: 'Customers', environment: 'development' }];
  vi.mocked(api.schedules).mockResolvedValue({ schedules: [
    { ...schedule, id: 'every-minute', alias: 'every-minute', cronExpression: '* * * * *', pipelines: [] },
    { ...schedule, pipelines: links },
  ] });
  vi.mocked(api.detachSchedule).mockResolvedValue({ ...schedule, pipelines: [] });
  const user = userEvent.setup();
  render(<PipelinesPage />);
  await screen.findByText('(development)');

  await user.click(screen.getByRole('button', { name: 'Schedule Customers' }));
  const form = within(screen.getByRole('group', { name: 'Add Customers to a schedule' }));
  const save = form.getByRole('button', { name: 'Save Customers schedule environments' });
  expect(form.getByRole('combobox', { name: 'Schedule' })).toHaveValue('');
  expect(form.getByRole('switch', { name: 'development' })).not.toBeChecked();
  expect(form.getByRole('switch', { name: 'test' })).not.toBeChecked();
  expect(form.getByRole('switch', { name: 'development' })).toBeDisabled();
  expect(form.getByRole('switch', { name: 'test' })).toBeDisabled();
  await user.selectOptions(form.getByRole('combobox', { name: 'Schedule' }), 'weekday-morning');
  expect(form.getByRole('switch', { name: 'development' })).toBeChecked();
  expect(form.getByRole('switch', { name: 'test' })).not.toBeChecked();
  expect(form.getByRole('switch', { name: 'development' })).toBeEnabled();
  expect(save).toBeDisabled();
  await user.click(form.getByRole('switch', { name: 'development' }));
  await user.click(save);

  expect(api.detachSchedule).toHaveBeenCalledExactlyOnceWith('weekday-morning', 'pipeline-1', 'development');
  expect(api.attachSchedule).not.toHaveBeenCalled();
});

it('guides the user to create a schedule when none exist', async () => {
  vi.mocked(api.schedules).mockResolvedValue({ schedules: [] });
  const user = userEvent.setup();
  render(<PipelinesPage />);
  await screen.findByText('Customers');

  await user.click(screen.getByRole('button', { name: 'Schedule Customers' }));

  expect(screen.getByRole('link', { name: 'Create a schedule' })).toHaveAttribute('href', '?page=schedules');
  expect(api.attachSchedule).not.toHaveBeenCalled();
});

it('keeps pipelines usable when schedules cannot be loaded', async () => {
  vi.mocked(api.schedules).mockRejectedValue(new Error('Server unavailable.'));
  render(<PipelinesPage />);

  expect(await screen.findByText('Customers')).toBeInTheDocument();
  expect(await screen.findByRole('alert')).toHaveTextContent('Schedules unavailable: Server unavailable.');
  expect(screen.getByRole('button', { name: 'Run Customers' })).toBeEnabled();
});

import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { api } from '../pipeline/api';
import { PipelinesPage } from './PipelinesPage';

vi.mock('../pipeline/api', () => ({ api: { pipelines: vi.fn(), run: vi.fn(), deletePipeline: vi.fn(), load: vi.fn(), importPipeline: vi.fn(), schedules: vi.fn(), attachSchedule: vi.fn(), detachSchedule: vi.fn() } }));

const pipeline = { id: 'pipeline-1', name: 'Customers', revision: 3, environments: ['development', 'test'] };
const schedule = { id: 'schedule-1', alias: 'weekday-morning', cronExpression: '30 8 * * 1,2,3,4,5', isEnabled: true, revision: 1, pipelines: [], nextRuns: [] };
beforeEach(() => {
  vi.mocked(api.pipelines).mockReset().mockResolvedValue({ page: 1, hasMore: false, pipelines: [pipeline] });
  vi.mocked(api.schedules).mockReset().mockResolvedValue({ schedules: [schedule] });
  vi.mocked(api.attachSchedule).mockReset();
  vi.mocked(api.detachSchedule).mockReset();
  vi.mocked(api.run).mockReset();
  vi.mocked(api.deletePipeline).mockReset();
  vi.mocked(api.load).mockReset();
  vi.mocked(api.importPipeline).mockReset();
});
afterEach(() => { vi.restoreAllMocks(); vi.unstubAllGlobals(); });

it('lists pipelines with accessible icon actions and navigation hints', async () => {
  render(<PipelinesPage />);
  expect(await screen.findByText('Customers')).toBeInTheDocument();
  const header = within(screen.getByRole('banner'));
  expect(header.getByRole('heading', { name: 'Pipeline Builder', level: 1 })).toBeInTheDocument();
  expect(header.getByText('Pipelines')).toBeInTheDocument();
  expect(screen.getByRole('link', { name: 'Create new pipeline' })).toHaveAttribute('href', '?page=designer');
  expect(screen.getByRole('link', { name: 'Open Customers in designer' })).toHaveAttribute('href', '?page=designer&pipeline=pipeline-1');
  expect(screen.getByRole('link', { name: 'View logs for Customers' })).toHaveAttribute('href', '?page=logs&pipeline=pipeline-1');
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
  expect(await screen.findByText(/Run run-1 queued for Customers/)).toBeInTheDocument();
});

it('requires confirmation before deleting and refreshes the list afterward', async () => {
  const confirm = vi.spyOn(window, 'confirm').mockReturnValueOnce(false).mockReturnValueOnce(true);
  vi.mocked(api.deletePipeline).mockResolvedValue({ deleted: true });
  const user = userEvent.setup();
  render(<PipelinesPage />);
  await screen.findByText('Customers');
  await user.click(screen.getByRole('button', { name: 'Delete Customers' }));
  expect(api.deletePipeline).not.toHaveBeenCalled();

  vi.mocked(api.pipelines).mockResolvedValue({ page: 1, hasMore: false, pipelines: [] });
  await user.click(screen.getByRole('button', { name: 'Delete Customers' }));

  expect(confirm).toHaveBeenCalledWith(expect.stringContaining('Execution history and generated files will be kept'));
  expect(api.deletePipeline).toHaveBeenCalledWith('pipeline-1', 3);
  expect(await screen.findByText(/No pipelines yet/)).toBeInTheDocument();
});

it('keeps a pipeline visible when deletion is rejected', async () => {
  vi.spyOn(window, 'confirm').mockReturnValue(true);
  vi.mocked(api.deletePipeline).mockRejectedValue(new Error('Pipeline has active runs.'));
  render(<PipelinesPage />);
  await screen.findByText('Customers');

  await userEvent.setup().click(screen.getByRole('button', { name: 'Delete Customers' }));

  expect(await screen.findByRole('alert')).toHaveTextContent('Pipeline has active runs.');
  expect(screen.getByText('Customers')).toBeInTheDocument();
});

it('handles an empty list without hiding the create action', async () => {
  vi.mocked(api.pipelines).mockResolvedValue({ page: 1, hasMore: false, pipelines: [] });
  render(<PipelinesPage />);

  expect(await screen.findByText(/No pipelines yet/)).toBeInTheDocument();
  expect(screen.getByRole('link', { name: 'Create new pipeline' })).toBeVisible();
  expect(screen.getByRole('button', { name: 'More pipelines' })).toBeDisabled();
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
  vi.mocked(api.pipelines).mockResolvedValueOnce({ page: 1, hasMore: true, pipelines: [pipeline] })
    .mockResolvedValueOnce({ page: 2, hasMore: false, pipelines: [{ ...pipeline, id: 'pipeline-2', name: 'Orders' }] });
  render(<PipelinesPage />);
  await screen.findByText('Customers');

  await userEvent.setup().click(screen.getByRole('button', { name: 'More pipelines' }));

  expect(await screen.findByText('Orders')).toBeInTheDocument();
  expect(api.pipelines).toHaveBeenLastCalledWith(2);
  expect(screen.getByRole('button', { name: 'More pipelines' })).toBeDisabled();
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
  expect(form.getByRole('checkbox', { name: 'development' })).toBeChecked();
  expect(form.getByRole('checkbox', { name: 'test' })).not.toBeChecked();
  await user.click(form.getByRole('checkbox', { name: 'test' }));
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

it('shows existing environments for a schedule and removes only the unchecked one', async () => {
  const links = ['development', 'test'].map(environment => ({ pipelineId: 'pipeline-1', pipelineName: 'Customers', environment }));
  vi.mocked(api.schedules).mockResolvedValue({ schedules: [{ ...schedule, pipelines: links }] });
  vi.mocked(api.detachSchedule).mockResolvedValue({ ...schedule, pipelines: [links[1]] });
  const user = userEvent.setup();
  render(<PipelinesPage />);
  await screen.findByText('(development, test)');

  await user.click(screen.getByRole('button', { name: 'Schedule Customers' }));
  const form = within(screen.getByRole('group', { name: 'Add Customers to a schedule' }));
  const save = form.getByRole('button', { name: 'Save Customers schedule environments' });
  expect(form.getByRole('checkbox', { name: 'development' })).toBeChecked();
  expect(form.getByRole('checkbox', { name: 'test' })).toBeChecked();
  expect(save).toBeDisabled();
  await user.click(form.getByRole('checkbox', { name: 'development' }));
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

import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { api } from '../pipeline/api';
import type { Run } from '../pipeline/types';
import { ExecutionLogsPage } from './ExecutionLogsPage';

vi.mock('../pipeline/api', () => ({ api: { history: vi.fn() } }));

const run: Run = {
  id: 'run-1', pipelineId: 'pipeline-1', revision: 3, environment: 'test', status: 'Succeeded',
  rowsRead: 10, rowsWritten: 7, error: null, blockId: null,
  createdAt: '2026-11-12T12:00:00Z', startedAt: '2026-11-12T12:00:01Z', finishedAt: '2026-11-12T12:00:02Z', trigger: 'Scheduled: nightly',
};

beforeEach(() => { vi.mocked(api.history).mockReset(); history.replaceState(null, '', '/'); });

it('keeps execution history visible beneath the loading overlay during refresh', async () => {
  vi.mocked(api.history).mockResolvedValueOnce({ page: 1, hasMore: false, runs: [run] });
  render(<ExecutionLogsPage />);
  await screen.findByText('OK');
  vi.mocked(api.history).mockReturnValueOnce(new Promise(() => {}));
  await userEvent.setup().click(screen.getByRole('button', { name: 'Refresh history' }));
  expect(screen.getByText('OK')).toBeInTheDocument();
  expect(screen.getByText('Loading execution history…').closest('.loading-overlay')).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Refresh history' })).toHaveAttribute('aria-busy', 'true');
});

it('shows OK and its summary in green, and Failure and its reason in red', async () => {
  vi.mocked(api.history).mockResolvedValue({ page: 1, hasMore: false, runs: [run,
    { ...run, id: 'run-2', status: 'Failed', error: 'Record 4 has an invalid age.', blockId: 'csv' },
    { ...run, id: 'run-3', status: 'Interrupted', error: 'The process stopped during execution.' },
  ] });
  render(<ExecutionLogsPage />);

  expect(await screen.findByText('OK')).toBeInTheDocument();
  const header = within(screen.getByRole('banner'));
  expect(header.getByRole('heading', { name: 'Pipeline Builder', level: 1 })).toBeInTheDocument();
  expect(header.getByText('Execution logs')).toBeInTheDocument();
  expect(screen.getByText('OK').closest('.run-result')).toHaveClass('run-ok');
  expect(screen.getByText('Read 10 records · written 7 · filtered 3.')).toBeInTheDocument();
  expect(screen.getByText('Failure').closest('.run-result')).toHaveClass('run-failure');
  expect(screen.getByText('Record 4 has an invalid age.')).toBeInTheDocument();
  expect(screen.getByText('csv')).toBeInTheDocument();
  expect(screen.getByText('Interrupted').closest('.run-result')).toHaveClass('run-failure');
  expect(screen.getByText('The process stopped during execution.')).toBeInTheDocument();
  expect(screen.getAllByText('Trigger: Scheduled: nightly')).toHaveLength(3);
});

it('does not present queued, running or cancelled runs as successful', async () => {
  vi.mocked(api.history).mockResolvedValue({ page: 1, hasMore: false, runs: ['Queued', 'Running', 'Cancelled'].map(status => ({ ...run, id: `run-${status}`, status: status as Run['status'] })) });
  render(<ExecutionLogsPage />);

  for (const status of ['Queued', 'Running', 'Cancelled']) expect((await screen.findByText(status)).closest('.run-result')).toHaveClass('run-pending');
  expect(screen.queryByText('OK')).not.toBeInTheDocument();
});

it('handles empty history and refreshes it', async () => {
  vi.mocked(api.history).mockResolvedValueOnce({ page: 1, hasMore: false, runs: [] }).mockResolvedValueOnce({ page: 1, hasMore: false, runs: [run] });
  render(<ExecutionLogsPage />);
  expect(await screen.findByText('No executions on this page.')).toBeInTheDocument();

  await userEvent.setup().click(screen.getByRole('button', { name: 'Refresh history' }));

  expect(await screen.findByText('OK')).toBeInTheDocument();
  expect(api.history).toHaveBeenCalledTimes(2);
});

it('loads older pages without losing pagination bounds', async () => {
  vi.mocked(api.history).mockResolvedValueOnce({ page: 1, hasMore: true, runs: [run] })
    .mockResolvedValueOnce({ page: 2, hasMore: false, runs: [{ ...run, id: 'older', status: 'Failed', error: 'Invalid record.' }] });
  render(<ExecutionLogsPage />);
  await screen.findByText('OK');
  expect(screen.getByRole('button', { name: 'Newer executions' })).toBeDisabled();

  await userEvent.setup().click(screen.getByRole('button', { name: 'Older executions' }));

  expect(await screen.findByText('Failure')).toBeInTheDocument();
  expect(api.history).toHaveBeenLastCalledWith(2, undefined);
  expect(screen.getByRole('button', { name: 'Older executions' })).toBeDisabled();
  expect(screen.getByRole('button', { name: 'Newer executions' })).toBeEnabled();
});

it('shows request errors and lets the user retry', async () => {
  vi.mocked(api.history).mockRejectedValueOnce(new Error('Server unavailable.')).mockResolvedValueOnce({ page: 1, hasMore: false, runs: [run] });
  render(<ExecutionLogsPage />);
  expect(await screen.findByRole('alert')).toHaveTextContent('Server unavailable.');

  await userEvent.setup().click(screen.getByRole('button', { name: 'Refresh history' }));

  expect(await screen.findByText('OK')).toBeInTheDocument();
  expect(screen.queryByRole('alert')).not.toBeInTheDocument();
});

it('requests only the selected pipeline history', async () => {
  history.replaceState(null, '', '?page=logs&pipeline=pipeline-1');
  vi.mocked(api.history).mockResolvedValue({ page: 1, hasMore: false, runs: [run] });
  render(<ExecutionLogsPage />);

  await screen.findByText('OK');

  expect(api.history).toHaveBeenCalledWith(1, 'pipeline-1');
  expect(screen.getByRole('link', { name: 'Back to pipelines' })).toHaveAttribute('href', '/');
});

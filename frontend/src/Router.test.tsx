import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { api } from './pipeline/api';
import { Router } from './Router';

vi.mock('./App', () => ({ App: ({ pipelineId }: { pipelineId?: string }) => <p>Designer {pipelineId ?? 'new'}</p> }));
vi.mock('./pages/PipelinesPage', () => ({ PipelinesPage: ({ onOpenDesigner }: { onOpenDesigner: (id: string) => void }) => <div><p>Pipeline list</p><button onClick={() => onOpenDesigner('pipeline-1')}>Open designer</button></div> }));
vi.mock('./pages/ExecutionLogsPage', () => ({ ExecutionLogsPage: () => <p>Run logs</p> }));
vi.mock('./pipeline/api', () => ({ api: { load: vi.fn() } }));

afterEach(() => history.replaceState(null, '', '/'));

it('opens the pipeline list by default', () => {
  history.replaceState(null, '', '/');
  render(<Router />);
  expect(screen.getByText('Pipeline list')).toBeInTheDocument();
});

it('keeps the list visible while loading a saved designer', async () => {
  let resolve!: (value: Awaited<ReturnType<typeof api.load>>) => void;
  vi.mocked(api.load).mockReturnValueOnce(new Promise(done => { resolve = done; }));
  render(<Router />);
  await userEvent.setup().click(screen.getByRole('button', { name: 'Open designer' }));
  expect(screen.getByText('Pipeline list')).toBeInTheDocument();
  expect(screen.getByText('Loading designer…').closest('.loading-overlay')).toBeInTheDocument();
  expect(location.search).toBe('');
  resolve({ id: 'pipeline-1', revision: 1, document: { name: 'Example', definition: { formatVersion: 1, steps: [] }, workspace: {}, environments: {} } });
  expect(await screen.findByText('Designer pipeline-1')).toBeInTheDocument();
  expect(location.search).toBe('?page=designer&pipeline=pipeline-1');
});

it('keeps the list visible when loading the designer fails', async () => {
  vi.mocked(api.load).mockRejectedValueOnce(new Error('Could not load pipeline.'));
  render(<Router />);
  await userEvent.setup().click(screen.getByRole('button', { name: 'Open designer' }));
  expect(await screen.findByRole('alert')).toHaveTextContent('Could not load pipeline.');
  expect(screen.getByText('Pipeline list')).toBeInTheDocument();
  expect(location.search).toBe('');
});

it('opens a new designer explicitly', async () => {
  history.replaceState(null, '', '?page=designer');
  render(<Router />);
  expect(await screen.findByText('Designer new')).toBeInTheDocument();
});

it('keeps existing pipeline bookmarks working', async () => {
  history.replaceState(null, '', '?pipeline=pipeline-1');
  render(<Router />);
  expect(await screen.findByText('Designer pipeline-1')).toBeInTheDocument();
});

it('opens logs instead of the designer when a pipeline filter is present', () => {
  history.replaceState(null, '', '?page=logs&pipeline=pipeline-1');
  render(<Router />);
  expect(screen.getByText('Run logs')).toBeInTheDocument();
});

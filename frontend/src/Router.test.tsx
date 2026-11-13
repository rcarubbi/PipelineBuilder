import { render, screen } from '@testing-library/react';
import { Router } from './Router';

vi.mock('./App', () => ({ App: ({ pipelineId }: { pipelineId?: string }) => <p>Designer {pipelineId ?? 'new'}</p> }));
vi.mock('./pages/PipelinesPage', () => ({ PipelinesPage: () => <p>Pipeline list</p> }));
vi.mock('./pages/ExecutionLogsPage', () => ({ ExecutionLogsPage: () => <p>Run logs</p> }));

afterEach(() => history.replaceState(null, '', '/'));

it('opens the pipeline list by default', () => {
  history.replaceState(null, '', '/');
  render(<Router />);
  expect(screen.getByText('Pipeline list')).toBeInTheDocument();
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

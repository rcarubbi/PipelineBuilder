import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import * as Blockly from 'blockly/core';
import { App } from './App';
import { api } from './pipeline/api';
import { compileWorkspace, seedWorkspace } from './blockly/workspace';
import type { Run, SavedPipeline } from './pipeline/types';

vi.mock('./pipeline/api', () => ({ api: { load: vi.fn(), save: vi.fn(), run: vi.fn(), getRun: vi.fn() } }));
vi.mock('./blockly/BlockEditor', async () => {
  const { forwardRef, useEffect, useImperativeHandle, useState } = await import('react');
  const Blockly = await import('blockly/core');
  const { seedWorkspace } = await import('./blockly/workspace');
  return { BlockEditor: forwardRef(function TestEditor({ initialWorkspace, onReady }: { initialWorkspace?: Record<string, unknown>; onReady: () => void }, ref) {
    const [workspace] = useState(() => new Blockly.Workspace());
    useImperativeHandle(ref, () => ({ workspace: () => workspace }));
    useEffect(() => {
      if (initialWorkspace) Blockly.serialization.workspaces.load(initialWorkspace, workspace);
      else seedWorkspace(workspace);
      onReady();
      return () => workspace.dispose();
    }, []);
    return <div aria-label="Test block editor" data-environments={JSON.stringify(Object.fromEntries(
      workspace.getTopBlocks(false).filter(block => block.type === 'define_environment').map(block => [
        `${block.getFieldValue('ENVIRONMENT')}.${block.getFieldValue('NAME')}`, block.getFieldValue('VALUE'),
      ])))}/>;
  }) };
});

let saved: SavedPipeline;
beforeEach(() => {
  history.replaceState(null, '', '/');
  const workspace = new Blockly.Workspace();
  seedWorkspace(workspace);
  workspace.getBlockById('csv')!.getInputTargetBlock('PATH')!.setFieldValue('saved-customers.csv', 'VALUE');
  saved = { id: 'pipeline-1', revision: 4, document: { name: 'Saved export', workspace: Blockly.serialization.workspaces.save(workspace),
    definition: compileWorkspace(workspace), environments: { test: { INPUT_FILE: 'saved-customers.csv', OUTPUT_FILE: 'saved.parquet' } } } };
  workspace.dispose();
  vi.mocked(api.load).mockReset().mockResolvedValue(saved);
  vi.mocked(api.save).mockReset().mockResolvedValue({ ...saved, revision: 5 });
  vi.mocked(api.run).mockReset();
  vi.mocked(api.getRun).mockReset();
});
afterEach(() => { vi.restoreAllMocks(); vi.unstubAllGlobals(); });

it('collapses and expands properties without replacing the canvas', async () => {
  const user = userEvent.setup();
  render(<App pipelineId="pipeline-1" />);
  await screen.findByText('Revision 4 · saved');
  const canvas = screen.getByLabelText('Test block editor');
  const collapse = screen.getByRole('button', { name: 'Collapse properties' });
  expect(collapse).toHaveAttribute('aria-expanded', 'true');
  await user.click(collapse);
  expect(screen.getByRole('button', { name: 'Expand properties' })).toHaveAttribute('aria-expanded', 'false');
  expect(document.getElementById('designer-properties')).toHaveAttribute('aria-hidden', 'true');
  expect(canvas.closest('.designer')).toHaveClass('properties-collapsed');
  await user.click(screen.getByRole('button', { name: 'Expand properties' }));
  expect(document.getElementById('designer-properties')).toHaveAttribute('aria-hidden', 'false');
  expect(screen.getByLabelText('Test block editor')).toBe(canvas);
});

it('opens a preloaded pipeline without requesting it again', async () => {
  render(<App pipelineId="pipeline-1" initialPipeline={saved} />);
  expect(await screen.findByText('Revision 4 · saved')).toBeInTheDocument();
  expect(screen.getByLabelText('Pipeline name')).toHaveValue('Saved export');
  expect(api.load).not.toHaveBeenCalled();
});

it('does not save or increment revision when the saved pipeline is unchanged', async () => {
  render(<App pipelineId="pipeline-1" />);
  expect(await screen.findByText('Revision 4 · saved')).toBeInTheDocument();
  await userEvent.setup().click(screen.getByRole('button', { name: 'Save' }));
  expect(api.save).not.toHaveBeenCalled();
  expect(screen.getByText('Revision 4 · saved')).toBeInTheDocument();
});

it('recognizes reverted edits and saves only actual changes', async () => {
  const user = userEvent.setup();
  render(<App pipelineId="pipeline-1" />);
  const name = await screen.findByLabelText('Pipeline name');
  await user.type(name, 'x');
  await user.keyboard('{Backspace}');
  expect(screen.getByText('Revision 4 · saved')).toBeInTheDocument();
  await user.click(screen.getByRole('button', { name: 'Save' }));
  expect(api.save).not.toHaveBeenCalled();
  await user.type(name, ' changed');
  await user.click(screen.getByRole('button', { name: 'Save' }));
  expect(api.save).toHaveBeenCalledWith('pipeline-1', 4, expect.objectContaining({ name: 'Saved export changed' }));
});

it('runs the existing revision without saving and links to execution logs', async () => {
  vi.mocked(api.run).mockResolvedValue({ id: 'run-1', status: 'Queued' } as Run);
  render(<App pipelineId="pipeline-1" />);
  await screen.findByText('Revision 4 · saved');
  await userEvent.setup().click(screen.getByRole('button', { name: 'Save & run' }));
  expect(api.save).not.toHaveBeenCalled();
  expect(api.run).toHaveBeenCalledWith('pipeline-1', 4, 'test');
  expect(await screen.findByRole('link', { name: 'execution logs' })).toHaveAttribute('href', '?page=logs&pipeline=pipeline-1');
});

it('confirms leaving the designer when the name has unsaved changes', async () => {
  Object.defineProperty(HTMLDialogElement.prototype, 'showModal', { configurable: true, value(this: HTMLDialogElement) { this.setAttribute('open', ''); } });
  const user = userEvent.setup();
  render(<App pipelineId="pipeline-1" />);
  const name = await screen.findByLabelText('Pipeline name');
  await user.type(name, ' changed');
  await user.click(screen.getByRole('link', { name: 'Back to pipelines' }));
  expect(screen.getByRole('dialog', { name: 'Leave designer?' })).toBeInTheDocument();
  await user.click(screen.getByRole('button', { name: 'Cancel' }));
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  Reflect.deleteProperty(HTMLDialogElement.prototype, 'showModal');
});

it('automatically opens the saved workspace and uses its revision when saving', async () => {
  render(<App pipelineId="pipeline-1" />);
  expect(await screen.findByLabelText('Pipeline name')).toHaveValue('Saved export');
  expect(await screen.findByText('Revision 4 · saved')).toBeInTheDocument();

  await userEvent.setup().click(screen.getByRole('button', { name: 'Save' }));

  expect(api.save).not.toHaveBeenCalled();
  expect(screen.getByText('Revision 4 · saved')).toBeInTheDocument();
});

it('exports the latest saved revision from the designer', async () => {
  const createObjectURL = vi.fn().mockReturnValue('blob:export');
  vi.stubGlobal('URL', Object.assign(class extends URL {}, { createObjectURL, revokeObjectURL: vi.fn() }));
  render(<App pipelineId="pipeline-1" />);
  await screen.findByLabelText('Pipeline name');

  await userEvent.setup().click(screen.getByRole('button', { name: 'Export pipeline as JSON' }));

  expect(await screen.findByText('Exported Saved export.json.')).toBeInTheDocument();
  expect(api.load).toHaveBeenCalledTimes(2);
  expect(api.load).toHaveBeenLastCalledWith('pipeline-1');
  expect(createObjectURL).toHaveBeenCalledOnce();
});

it('requires designer changes to be saved before export', async () => {
  const user = userEvent.setup();
  render(<App pipelineId="pipeline-1" />);
  const name = await screen.findByLabelText('Pipeline name');
  await user.clear(name);
  await user.type(name, 'Unsaved name');
  await user.click(screen.getByRole('button', { name: 'Export pipeline as JSON' }));

  expect(await screen.findByRole('alert')).toHaveTextContent('Save changes before exporting.');
  expect(api.load).toHaveBeenCalledOnce();
});

it('switches between the canvas and a read-only executable JSON view without losing the canvas', async () => {
  const user = userEvent.setup();
  render(<App pipelineId="pipeline-1" />);
  await screen.findByLabelText('Pipeline name');
  const canvasTab = screen.getByRole('tab', { name: 'Canvas' });
  const jsonTab = screen.getByRole('tab', { name: 'Pipeline JSON (read-only)' });
  expect(canvasTab).toHaveAttribute('aria-selected', 'true');
  expect(screen.queryByRole('tabpanel', { name: 'Pipeline JSON' })).not.toBeInTheDocument();
  expect(screen.queryByText('Pipeline JSON', { selector: 'summary' })).not.toBeInTheDocument();

  await user.click(jsonTab);

  expect(jsonTab).toHaveAttribute('aria-selected', 'true');
  expect(document.getElementById('designer-canvas')).toHaveAttribute('hidden');
  const json = within(screen.getByRole('tabpanel', { name: 'Pipeline JSON (read-only)' }));
  const view = json.getByText(/"formatVersion": 1/);
  expect(JSON.parse(view.textContent!)).toEqual({ ...saved.document.definition, environments: saved.document.environments });
  expect(view.tagName).toBe('PRE');
  expect(json.queryByRole('textbox')).not.toBeInTheDocument();

  await user.click(canvasTab);

  expect(screen.getByRole('tabpanel', { name: 'Canvas' })).toBeVisible();
  expect(screen.getByLabelText('Test block editor')).toBeInTheDocument();
});

it('restores saved environment values as Blockly blocks without an external editor', async () => {
  render(<App pipelineId="pipeline-1" />);
  const canvas = await screen.findByLabelText('Test block editor');
  await waitFor(() => expect(canvas).toHaveAttribute('data-environments', JSON.stringify({
    'test.INPUT_FILE': 'saved-customers.csv', 'test.OUTPUT_FILE': 'saved.parquet',
  })));
  expect(screen.queryByText('Environments')).not.toBeInTheDocument();
  expect(screen.getByLabelText('Environment')).toHaveValue('test');
});

it('does not replace a failed load with an editable new pipeline', async () => {
  vi.mocked(api.load).mockRejectedValue(new Error('Pipeline not found.'));
  render(<App pipelineId="missing" />);

  expect(await screen.findByRole('alert')).toHaveTextContent('Pipeline not found.');
  expect(screen.queryByRole('button', { name: 'Save' })).not.toBeInTheDocument();
  expect(screen.getByRole('link', { name: 'Back to pipelines' })).toHaveAttribute('href', '/');
  expect(within(screen.getByRole('banner')).getByText('Designer')).toBeInTheDocument();
});

it('creates a new draft without loading a saved pipeline or showing old header content', async () => {
  render(<App />);
  expect(await screen.findByLabelText('Pipeline name')).toHaveValue('New pipeline');
  expect(api.load).not.toHaveBeenCalled();
  const header = within(screen.getByRole('banner'));
  expect(header.getByRole('heading', { name: 'Pipeline Builder', level: 1 })).toBeInTheDocument();
  expect(header.getByText('Designer')).toBeInTheDocument();
  expect(header.queryByRole('link')).not.toBeInTheDocument();
  expect(screen.queryByText('CSV in. Typed transformations. Parquet out.')).not.toBeInTheDocument();
  expect(screen.queryByText('POC · server files · fail-fast')).not.toBeInTheDocument();
});

it('keeps variable management in Blockly without a separate environment panel', async () => {
  const { container } = render(<App />);
  await screen.findByLabelText('Pipeline name');

  expect(screen.getByLabelText('Test block editor')).toBeInTheDocument();
  expect(screen.queryByText('Environments')).not.toBeInTheDocument();
  expect(screen.queryByRole('heading', { name: 'Run' })).not.toBeInTheDocument();
  expect(screen.queryByRole('heading', { name: 'Open saved pipeline' })).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Add map / convert' })).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Add filter' })).not.toBeInTheDocument();
  expect(screen.queryByRole('navigation', { name: 'Pipeline steps' })).not.toBeInTheDocument();
  expect(container.querySelector('.canvas-actions, .step-list')).not.toBeInTheDocument();
});

it('still saves and starts a run with feedback outside the removed panel', async () => {
  const queued: Run = { id: 'run-1', pipelineId: saved.id, revision: 5, environment: 'test', status: 'Queued',
    rowsRead: 0, rowsWritten: 0, error: null, blockId: null, createdAt: '2026-11-12T12:00:00Z', startedAt: null, finishedAt: null, trigger: 'Manual' };
  vi.mocked(api.run).mockResolvedValue(queued);
  vi.mocked(api.getRun).mockResolvedValue({ ...queued, status: 'Succeeded' });
  render(<App pipelineId={saved.id} />);
  await screen.findByLabelText('Pipeline name');

  await userEvent.setup().click(screen.getByRole('button', { name: 'Save & run' }));

  expect(api.run).toHaveBeenCalledWith(saved.id, 4, 'test');
  const toast = await screen.findByRole('status');
  expect(toast).toHaveTextContent('Run run-1 queued. Results are available in execution logs.');
  expect(toast.closest('.toast-container')).toBeInTheDocument();
  expect(screen.queryByRole('heading', { name: 'Run' })).not.toBeInTheDocument();
});

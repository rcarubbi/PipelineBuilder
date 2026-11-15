import * as Blockly from 'blockly/core';
import { compileEnvironments, compileWorkspace, seedWorkspace } from '../blockly/workspace';
import type { SavedPipeline } from './types';
import { downloadPipeline, parseImport } from './exchange';

afterEach(() => { vi.unstubAllGlobals(); vi.restoreAllMocks(); });

let saved: SavedPipeline;
beforeEach(() => {
  const workspace = new Blockly.Workspace();
  seedWorkspace(workspace);
  const definition = compileWorkspace(workspace);
  const csv = definition.steps[0];
  if (csv.type === 'csv') csv.schema = csv.schema.map(column => ({ ...column, precision: 18, scale: 2 }));
  saved = { id: 'id-is-not-exported', revision: 3, document: {
    name: 'Customers', definition,
    workspace: Blockly.serialization.workspaces.save(workspace),
    environments: { test: { INPUT_FILE: 'customers.csv', OUTPUT_FILE: 'customers.parquet' } },
  } };
  workspace.dispose();
});

it('exports and restores the saved document without installation-specific fields', async () => {
  const createObjectURL = vi.fn().mockReturnValue('blob:export');
  vi.stubGlobal('URL', Object.assign(class extends URL {}, { createObjectURL, revokeObjectURL: vi.fn() }));
  const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {});

  expect(downloadPipeline(saved)).toBe('Customers.json');

  const blob = createObjectURL.mock.calls[0][0] as Blob;
  const exported = JSON.parse(await blob.text());
  expect(exported).toMatchObject({ format: 'pipeline-builder/pipeline', formatVersion: 1, pipeline: saved.document });
  expect(exported.exportedAt).toBeTruthy();
  expect(exported).not.toHaveProperty('id');
  expect(exported).not.toHaveProperty('revision');
  expect(click).toHaveBeenCalledOnce();
  expect(parseImport(await blob.text(), 'Customers.json')).toEqual(saved.document);
});

it('exports Blockly variable assignments and their values', async () => {
  const workspace = new Blockly.Workspace();
  seedWorkspace(workspace);
  const variable = workspace.getVariableMap().createVariable('filename');
  const assignment = workspace.newBlock('variables_set');
  assignment.setFieldValue(variable.getId(), 'VAR');
  const value = workspace.newBlock('value_literal');
  value.setFieldValue('customers.csv', 'VALUE');
  assignment.getInput('VALUE')!.connection!.connect(value.outputConnection!);
  const pipeline: SavedPipeline = { ...saved, document: { ...saved.document,
    definition: compileWorkspace(workspace), workspace: Blockly.serialization.workspaces.save(workspace) } };
  workspace.dispose();
  const createObjectURL = vi.fn().mockReturnValue('blob:export');
  vi.stubGlobal('URL', Object.assign(class extends URL {}, { createObjectURL, revokeObjectURL: vi.fn() }));
  vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {});

  downloadPipeline(pipeline);

  const text = await (createObjectURL.mock.calls[0][0] as Blob).text();
  const exported = JSON.parse(text);
  expect(exported.pipeline.definition.variables).toEqual([
    { name: 'filename', value: { kind: 'literal', id: value.id, value: 'customers.csv' } },
  ]);
  expect(parseImport(text, 'pipeline.json').definition.variables).toEqual(exported.pipeline.definition.variables);
});

it('exports environment definitions created with Blockly environment blocks', async () => {
  const workspace = new Blockly.Workspace();
  seedWorkspace(workspace);
  const block = workspace.newBlock('define_environment');
  block.setFieldValue('test', 'ENVIRONMENT');
  block.setFieldValue('INPUT_FILE', 'NAME');
  block.setFieldValue('test.csv', 'VALUE');
  const pipeline: SavedPipeline = { ...saved, document: { ...saved.document,
    definition: compileWorkspace(workspace), workspace: Blockly.serialization.workspaces.save(workspace),
    environments: compileEnvironments(workspace) } };
  workspace.dispose();
  const createObjectURL = vi.fn().mockReturnValue('blob:export');
  vi.stubGlobal('URL', Object.assign(class extends URL {}, { createObjectURL, revokeObjectURL: vi.fn() }));
  vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {});

  downloadPipeline(pipeline);

  const text = await (createObjectURL.mock.calls[0][0] as Blob).text();
  expect(JSON.parse(text).pipeline.environments).toEqual({ test: { INPUT_FILE: 'test.csv' } });
  expect(parseImport(text, 'pipeline.json').environments).toEqual({ test: { INPUT_FILE: 'test.csv' } });
});

it('rebuilds Blockly and default variables from a definition-only file', () => {
  const definition = structuredClone(saved.document.definition);
  const csv = definition.steps[0];
  if (csv.type === 'csv') {
    csv.path = '${INPUT_FILE}';
    csv.schema = csv.schema.map(({ name, type }) => ({ name, type } as typeof csv.schema[number]));
  }

  const imported = parseImport(JSON.stringify(definition), 'customers-template.json');
  const workspace = new Blockly.Workspace();
  try {
    Blockly.serialization.workspaces.load(imported.workspace, workspace);
    expect(compileWorkspace(workspace)).toEqual(imported.definition);
    expect(imported.name).toBe('customers-template');
    expect(imported.environments).toEqual({ default: { INPUT_FILE: '', OUTPUT_FILE: '' } });
  } finally { workspace.dispose(); }
});

it('rejects an exported workspace that disagrees with its executable definition', () => {
  const workspace = new Blockly.Workspace();
  Blockly.serialization.workspaces.load(saved.document.workspace, workspace);
  workspace.getBlockById('csv')!.setFieldValue('different.csv', 'PATH');
  const mismatched = { format: 'pipeline-builder/pipeline', formatVersion: 1, pipeline: {
    ...saved.document, workspace: Blockly.serialization.workspaces.save(workspace),
  } };
  workspace.dispose();

  expect(() => parseImport(JSON.stringify(mismatched), 'Customers.json')).toThrow('does not match');
});

it('rejects unsupported formats and files larger than one mebibyte', () => {
  expect(() => parseImport('{"format":"other"}', 'pipeline.json')).toThrow('Unsupported');
  expect(() => parseImport(' '.repeat(1024 * 1024 + 1), 'large.json')).toThrow('1 MiB');
});

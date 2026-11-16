import * as Blockly from 'blockly/core';
import { compileEnvironments, compileWorkspace, definitionToWorkspace, seedWorkspace } from '../blockly/workspace';
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

it('imports an exported pipeline with an API input', () => {
  const workspace = new Blockly.Workspace();
  const input = workspace.newBlock('pipeline_apiInput');
  input.data = JSON.stringify({
    schema: [{ name: 'id', type: 'string', nullable: false, precision: 18, scale: 2 }],
    api: { url: 'https://example.com/items', dataPath: 'items', method: 'GET', format: 'json', authentication: 'basic', username: 'user', password: 'pass', token: 'bearer-token' },
  });
  const output = workspace.newBlock('pipeline_parquet');
  const path = workspace.newBlock('value_literal');
  path.setFieldValue('items.parquet', 'VALUE');
  output.getInput('PATH')!.connection!.connect(path.outputConnection!);
  input.nextConnection!.connect(output.previousConnection!);
  const document = {
    name: 'API pipeline', definition: compileWorkspace(workspace),
    workspace: Blockly.serialization.workspaces.save(workspace), environments: compileEnvironments(workspace),
  };
  workspace.dispose();

  expect(parseImport(JSON.stringify({ format: 'pipeline-builder/pipeline', formatVersion: 1, pipeline: document }), 'api.json')).toEqual(document);
});

it('round-trips an exported PostgreSQL pipeline with secret and variable references', () => {
  const definition = { formatVersion: 1 as const, variables: [], steps: [
    { type: 'postgresInput' as const, id: 'postgres', host: '${env.PG_HOST}', port: '5432', database: 'customers', username: 'reader',
      password: '${secret.PG_PASSWORD}', sslMode: 'Require', query: 'SELECT id FROM customers', schema: [
        { name: 'id', type: 'int64' as const, nullable: false, precision: 18, scale: 2 },
      ] },
    { type: 'parquet' as const, id: 'output', path: 'expression', pathExpression: { kind: 'literal' as const, id: 'output-path', value: 'customers.parquet' } },
  ] };
  const document = { name: 'PostgreSQL import', definition, workspace: definitionToWorkspace(definition), environments: { dev: { PG_HOST: 'db.example' } } };
  const envelope = { format: 'pipeline-builder/pipeline', formatVersion: 1, pipeline: document };

  expect(parseImport(JSON.stringify(envelope), 'postgres.json')).toEqual(document);
});

it('rebuilds Blockly and default variables from a definition-only file', () => {
  const definition = structuredClone(saved.document.definition);
  const csv = definition.steps[0];
  if (csv.type === 'csv') {
    delete csv.pathExpression;
    csv.path = '${INPUT_FILE}';
    csv.schema = csv.schema.map(({ name, type }) => ({ name, type } as typeof csv.schema[number]));
  }

  const imported = parseImport(JSON.stringify(definition), 'customers-template.json');
  const workspace = new Blockly.Workspace();
  try {
    Blockly.serialization.workspaces.load(imported.workspace, workspace);
    expect(compileWorkspace(workspace)).toEqual(imported.definition);
    expect(imported.name).toBe('customers-template');
    expect(imported.environments).toEqual({ default: { INPUT_FILE: '' } });
  } finally { workspace.dispose(); }
});

it('rejects an exported workspace that disagrees with its executable definition', () => {
  const workspace = new Blockly.Workspace();
  Blockly.serialization.workspaces.load(saved.document.workspace, workspace);
  workspace.getBlockById('csv')!.getInputTargetBlock('PATH')!.setFieldValue('different.csv', 'VALUE');
  const mismatched = { format: 'pipeline-builder/pipeline', formatVersion: 1, pipeline: {
    ...saved.document, workspace: Blockly.serialization.workspaces.save(workspace),
  } };
  workspace.dispose();

  expect(() => parseImport(JSON.stringify(mismatched), 'Customers.json')).toThrow('does not match');
});

it('imports backend exports with null optional fields omitted from Blockly data', () => {
  const document = structuredClone(saved.document);
  const definition = document.definition as unknown as Record<string, any>;
  const csv = definition.steps[0];
  csv.hasHeaderVariable = null;
  csv.schema[0].typeVariable = null;
  csv.schema[0].nullableVariable = null;
  const filter = definition.steps[1];
  filter.criteria.columnExpression = null;
  filter.criteria.valueExpression = null;
  filter.criteria.operatorVariable = null;

  expect(parseImport(JSON.stringify({ format: 'pipeline-builder/pipeline', formatVersion: 1, pipeline: { ...document, definition } }), 'Customers.json').name).toBe('Customers');
});

it('rejects unsupported formats and files larger than one mebibyte', () => {
  expect(() => parseImport('{"format":"other"}', 'pipeline.json')).toThrow('Unsupported');
  expect(() => parseImport(' '.repeat(1024 * 1024 + 1), 'large.json')).toThrow('1 MiB');
});

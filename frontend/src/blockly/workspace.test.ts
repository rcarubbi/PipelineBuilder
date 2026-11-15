import * as Blockly from 'blockly/core';
import { compileWorkspace, definitionToWorkspace, schemaBefore, seedWorkspace } from './workspace';
import type { Definition } from '../pipeline/types';

describe('pipeline workspace', () => {
  let workspace: Blockly.Workspace;
  beforeEach(() => { workspace = new Blockly.Workspace(); seedWorkspace(workspace); });
  afterEach(() => workspace.dispose());

  it('compiles a typed pipeline without generating executable code', () => {
    const definition = compileWorkspace(workspace);
    expect(definition.steps.map(s => s.type)).toEqual(['csv', 'filter', 'parquet']);
    expect(definition.steps[1]).toMatchObject({ criteria: { kind: 'compare', column: 'age', operator: 'greaterOrEqual', value: '18' } });
    expect(definition.steps[0]).toMatchObject({ schema: [{ name: 'id', type: 'int64' }, { name: 'name', type: 'string' }, { name: 'age', type: 'int64' }, { name: 'country', type: 'string' }] });
  });

  it('preserves stable IDs and custom configuration through serialization', () => {
    const state = Blockly.serialization.workspaces.save(workspace);
    const expected = compileWorkspace(workspace);
    workspace.clear();
    Blockly.serialization.workspaces.load(state, workspace);
    expect(compileWorkspace(workspace)).toEqual(expected);
  });

  it('compiles and restores a CSV output block', () => {
    const previous = workspace.getBlockById('parquet')!.previousConnection!.targetConnection!;
    workspace.getBlockById('parquet')!.dispose();
    const output = workspace.newBlock('pipeline_csvOutput');
    const path = workspace.newBlock('value_literal');
    path.setFieldValue('result.csv', 'VALUE');
    output.getInput('PATH')!.connection!.connect(path.outputConnection!);
    previous.connect(output.previousConnection!);
    const definition = compileWorkspace(workspace);
    expect(definition.steps.at(-1)).toMatchObject({ type: 'csvOutput', delimiter: ',', hasHeader: true, pathExpression: { kind: 'literal', value: 'result.csv' } });
    const restored = new Blockly.Workspace();
    try {
      Blockly.serialization.workspaces.load(definitionToWorkspace(definition), restored);
      expect(compileWorkspace(restored)).toEqual(definition);
    } finally { restored.dispose(); }
  });

  it.each(['parquetInput', 'excelInput'] as const)('compiles and restores %s input with Excel output', inputType => {
    workspace.clear();
    const input = workspace.newBlock(`pipeline_${inputType}`);
    input.data = JSON.stringify({ schema: [{ name: 'name', type: 'string', nullable: false }] });
    const inputPath = workspace.newBlock('value_literal');
    inputPath.setFieldValue(inputType === 'excelInput' ? 'source.xlsx' : 'source.parquet', 'VALUE');
    input.getInput('PATH')!.connection!.connect(inputPath.outputConnection!);
    if (inputType === 'excelInput') input.setFieldValue('Source', 'SHEET');
    const output = workspace.newBlock('pipeline_excelOutput');
    output.setFieldValue('Results', 'SHEET');
    const outputPath = workspace.newBlock('value_literal');
    outputPath.setFieldValue('output.xls', 'VALUE');
    output.getInput('PATH')!.connection!.connect(outputPath.outputConnection!);
    input.nextConnection!.connect(output.previousConnection!);

    const definition = compileWorkspace(workspace);
    expect(definition.steps[0]).toMatchObject({ type: inputType, schema: [{ name: 'name', type: 'string' }] });
    expect(definition.steps[1]).toMatchObject({ type: 'excelOutput', sheet: 'Results', hasHeader: true });
    if (inputType === 'excelInput') expect(definition.steps[0]).toMatchObject({ sheet: 'Source' });
    const restored = new Blockly.Workspace();
    try {
      Blockly.serialization.workspaces.load(definitionToWorkspace(definition), restored);
      expect(compileWorkspace(restored)).toEqual(definition);
    } finally { restored.dispose(); }
  });

  it('compiles native variable assignments and references, including renaming and restored definitions', () => {
    const variable = workspace.getVariableMap().createVariable('filename');
    const assignment = workspace.newBlock('variables_set');
    assignment.setFieldValue(variable.getId(), 'VAR');
    const value = workspace.newBlock('value_literal');
    value.setFieldValue('renamed.csv', 'VALUE');
    assignment.getInput('VALUE')!.connection!.connect(value.outputConnection!);
    const csv = workspace.getBlockById('csv')!;
    csv.getInputTargetBlock('PATH')!.dispose();
    const reference = workspace.newBlock('variables_get');
    reference.setFieldValue(variable.getId(), 'VAR');
    csv.getInput('PATH')!.connection!.connect(reference.outputConnection!);
    workspace.getVariableMap().renameVariable(variable, 'input_file');

    const definition = compileWorkspace(workspace);
    expect(definition.variables).toEqual([{ name: 'input_file', value: { kind: 'literal', id: value.id, value: 'renamed.csv' } }]);
    expect(definition.steps[0]).toMatchObject({ pathExpression: { kind: 'variable', name: 'input_file' } });
    const restored = new Blockly.Workspace();
    try {
      Blockly.serialization.workspaces.load(definitionToWorkspace(definition), restored);
      expect(compileWorkspace(restored)).toEqual(definition);
      expect(restored.getBlocksByType('variables_set', false)).toHaveLength(1);
      expect(restored.getBlocksByType('variables_get', false)).toHaveLength(1);
    } finally { restored.dispose(); }
  });

  it('rejects multiple assignments to the same variable', () => {
    const variable = workspace.getVariableMap().createVariable('name');
    for (const text of ['a', 'b']) {
      const assignment = workspace.newBlock('variables_set');
      assignment.setFieldValue(variable.getId(), 'VAR');
      const value = workspace.newBlock('value_literal');
      value.setFieldValue(text, 'VALUE');
      assignment.getInput('VALUE')!.connection!.connect(value.outputConnection!);
    }
    expect(() => compileWorkspace(workspace)).toThrow('Variable names must be nonempty and unique');
  });

  it('compiles native variable assignments connected within the pipeline chain', () => {
    const variable = workspace.getVariableMap().createVariable('filename');
    const assignment = workspace.newBlock('variables_set');
    assignment.setFieldValue(variable.getId(), 'VAR');
    const value = workspace.newBlock('value_literal');
    value.setFieldValue('input.csv', 'VALUE');
    assignment.getInput('VALUE')!.connection!.connect(value.outputConnection!);
    const csv = workspace.getBlockById('csv')!;
    const next = csv.nextConnection!.targetConnection!;
    csv.nextConnection!.disconnect();
    csv.nextConnection!.connect(assignment.previousConnection!);
    assignment.nextConnection!.connect(next);

    const definition = compileWorkspace(workspace);
    expect(definition.variables).toEqual([{ name: 'filename', value: { kind: 'literal', id: value.id, value: 'input.csv' } }]);
    expect(definition.steps.map(step => step.type)).toEqual(['csv', 'filter', 'parquet']);
  });

  it('connects text expressions to comparison fields and UTC format', () => {
    const predicate = workspace.getBlockById('adults')!.getInputTargetBlock('CRITERIA')!;
    const environment = workspace.newBlock('value_current_environment');
    predicate.getInput('COLUMN_EXPRESSION')!.connection!.connect(environment.outputConnection!);
    const utc = workspace.newBlock('value_utc');
    const format = workspace.newBlock('value_literal');
    format.setFieldValue('yyyy', 'VALUE');
    utc.getInput('FORMAT_EXPRESSION')!.connection!.connect(format.outputConnection!);
    predicate.getInput('VALUE_EXPRESSION')!.connection!.connect(utc.outputConnection!);

    const definition = compileWorkspace(workspace);
    expect(definition.steps[1]).toMatchObject({ criteria: {
      columnExpression: { kind: 'environment', name: 'CURRENT_ENVIRONMENT' },
      valueExpression: { kind: 'utc', formatExpression: { kind: 'literal', value: 'yyyy' } },
    } });
    const restored = new Blockly.Workspace();
    try {
      Blockly.serialization.workspaces.load(definitionToWorkspace(definition), restored);
      expect(compileWorkspace(restored)).toEqual(definition);
      expect(restored.getBlocksByType('value_current_environment', false)).toHaveLength(1);
    } finally { restored.dispose(); }
  });

  it('rebuilds Blockly state from the executable definition with stable step IDs', () => {
    const definition = compileWorkspace(workspace);
    const rebuilt = new Blockly.Workspace();
    try {
      Blockly.serialization.workspaces.load(definitionToWorkspace(definition), rebuilt);
      expect(compileWorkspace(rebuilt)).toEqual(definition);
      expect(rebuilt.getBlockById('csv')).not.toBeNull();
      expect(rebuilt.getBlockById('adults')).not.toBeNull();
      expect(rebuilt.getBlockById('parquet')).not.toBeNull();
    } finally { rebuilt.dispose(); }
  });

  it('rebuilds map steps and nested filter expressions', () => {
    const definition: Definition = { formatVersion: 1, steps: [
      { type: 'csv', id: 'predicate-1', path: 'input.csv', delimiter: ',', hasHeader: true,
        schema: [{ name: 'age', type: 'int64', nullable: false, precision: 18, scale: 2 }] },
      { type: 'map', id: 'map', mappings: [{ source: 'age', target: { name: 'years', type: 'int64', nullable: false, precision: 18, scale: 2 }, format: null }] },
      { type: 'filter', id: 'filter', criteria: { kind: 'logical', operator: 'and', children: [
        { kind: 'null', column: 'years', isNull: false },
        { kind: 'logical', operator: 'not', children: [{ kind: 'compare', column: 'years', operator: 'less', value: '18' }] },
      ] } },
      { type: 'parquet', id: 'parquet', path: 'output.parquet' },
    ] };
    const rebuilt = new Blockly.Workspace();
    try {
      Blockly.serialization.workspaces.load(definitionToWorkspace(definition), rebuilt);
      expect(compileWorkspace(rebuilt)).toEqual(definition);
    } finally { rebuilt.dispose(); }
  });

  it('rejects disconnected blocks rather than silently ignoring them', () => {
    workspace.newBlock('pipeline_map');
    expect(() => compileWorkspace(workspace)).toThrow('Connect all blocks');
  });

  it('rejects a filter without its expression', () => {
    workspace.getBlockById('adults')!.getInputTargetBlock('CRITERIA')!.dispose();
    expect(() => compileWorkspace(workspace)).toThrow('Connect a boolean criterion');
  });

  it('rejects malformed schemas before calling the backend', () => {
    workspace.getBlockById('csv')!.data = JSON.stringify({ schema: [{ name: 'age', type: 'executable-code', nullable: false }] });
    expect(() => compileWorkspace(workspace)).toThrow('Define or discover');
  });

  it('keeps upstream columns available while a new map is still unconfigured', () => {
    const output = workspace.getBlockById('parquet')!;
    const previous = output.previousConnection!.targetConnection!;
    output.previousConnection!.disconnect();
    const map = workspace.newBlock('pipeline_map');
    previous.connect(map.previousConnection!);
    map.nextConnection!.connect(output.previousConnection!);
    expect(() => compileWorkspace(workspace)).toThrow('Configure at least one valid mapping');
    expect(schemaBefore(map).map(c => c.name)).toEqual(['id', 'name', 'age', 'country']);
  });
});

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

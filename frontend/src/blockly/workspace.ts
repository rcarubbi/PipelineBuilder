import * as Blockly from 'blockly/core';
import * as En from 'blockly/msg/en';
import { columnTypes, type Column, type Definition, type Mapping, type Predicate, type Step, type ComparisonOperator } from '../pipeline/types';

// Blockly's locale types include a synthetic default export; the ESM locale contains named strings.
Blockly.setLocale(Object.fromEntries(Object.entries(En).filter((entry): entry is [string, string] => typeof entry[1] === 'string')));
Blockly.common.defineBlocksWithJsonArray([
  { type: 'pipeline_csv', message0: 'CSV %1 delimiter %2 header %3', args0: [
    { type: 'field_input', name: 'PATH', text: '${INPUT_FILE}' },
    { type: 'field_input', name: 'DELIMITER', text: ',' },
    { type: 'field_checkbox', name: 'HEADER', checked: true },
  ], nextStatement: 'Pipeline', colour: 165, tooltip: 'Select this block to edit or discover its schema.' },
  { type: 'pipeline_map', message0: 'Map / Convert columns', previousStatement: 'Pipeline', nextStatement: 'Pipeline', colour: 230, tooltip: 'Select this block to configure its output columns.' },
  { type: 'pipeline_filter', message0: 'Keep rows where %1', args0: [{ type: 'input_value', name: 'CRITERIA', check: 'Boolean' }], previousStatement: 'Pipeline', nextStatement: 'Pipeline', colour: 230 },
  { type: 'pipeline_parquet', message0: 'Write Parquet %1', args0: [{ type: 'field_input', name: 'PATH', text: '${OUTPUT_FILE}' }], previousStatement: 'Pipeline', colour: 20 },
  { type: 'predicate_compare', message0: 'column %1 %2 %3', args0: [
    { type: 'field_input', name: 'COLUMN', text: 'age' },
    { type: 'field_dropdown', name: 'OP', options: [['=', 'equal'], ['≠', 'notEqual'], ['>', 'greater'], ['≥', 'greaterOrEqual'], ['<', 'less'], ['≤', 'lessOrEqual']] },
    { type: 'field_input', name: 'VALUE', text: '18' },
  ], output: 'Boolean', colour: 285 },
  { type: 'predicate_logic', message0: '%1 %2 %3', args0: [
    { type: 'input_value', name: 'LEFT', check: 'Boolean' },
    { type: 'field_dropdown', name: 'OP', options: [['AND', 'and'], ['OR', 'or']] },
    { type: 'input_value', name: 'RIGHT', check: 'Boolean' },
  ], output: 'Boolean', colour: 285 },
  { type: 'predicate_not', message0: 'NOT %1', args0: [{ type: 'input_value', name: 'VALUE', check: 'Boolean' }], output: 'Boolean', colour: 285 },
  { type: 'predicate_null', message0: 'column %1 %2', args0: [
    { type: 'field_input', name: 'COLUMN', text: 'age' },
    { type: 'field_dropdown', name: 'IS_NULL', options: [['is null', 'TRUE'], ['is not null', 'FALSE']] },
  ], output: 'Boolean', colour: 285 },
]);

export const toolbox = {
  kind: 'categoryToolbox', contents: [
    { kind: 'category', name: 'Input', colour: '165', contents: [{ kind: 'block', type: 'pipeline_csv' }] },
    { kind: 'category', name: 'Processing', colour: '230', contents: ['pipeline_map', 'pipeline_filter'].map(type => ({ kind: 'block', type })) },
    { kind: 'category', name: 'Criteria', colour: '285', contents: ['predicate_compare', 'predicate_logic', 'predicate_not', 'predicate_null'].map(type => ({ kind: 'block', type })) },
    { kind: 'category', name: 'Output', colour: '20', contents: [{ kind: 'block', type: 'pipeline_parquet' }] },
  ],
};

export function getBlockData(block: Blockly.Block): { schema?: Column[]; mappings?: Mapping[] } {
  const data = block.data ? JSON.parse(block.data) : {};
  if (!data || typeof data !== 'object') throw new Error('Invalid block configuration.');
  return data;
}

export function schemaBefore(block: Blockly.Block): Column[] {
  let step = block;
  while (!step.type.startsWith('pipeline_') && step.getParent()) step = step.getParent()!;
  const previous: Blockly.Block[] = [];
  for (let cursor = step.getPreviousBlock(); cursor; cursor = cursor.getPreviousBlock()) previous.unshift(cursor);
  let schema: Column[] = [];
  for (const cursor of previous) {
    if (cursor.type === 'pipeline_csv') schema = getBlockData(cursor).schema ?? [];
    if (cursor.type === 'pipeline_map') schema = (getBlockData(cursor).mappings ?? []).map(m => m.target);
  }
  return schema;
}

function validColumn(column: Column): boolean {
  return column != null && typeof column.name === 'string' && columnTypes.includes(column.type) && typeof column.nullable === 'boolean';
}

export function compileWorkspace(workspace: Blockly.Workspace): Definition {
  const roots = workspace.getTopBlocks(false);
  if (roots.length !== 1 || roots[0].type !== 'pipeline_csv') throw new Error('Connect all blocks into one chain starting with CSV.');
  const steps: Step[] = [];
  let block: Blockly.Block | null = roots[0];
  while (block) {
    switch (block.type) {
      case 'pipeline_csv': {
        const schema = getBlockData(block).schema;
        if (!schema?.length || !schema.every(validColumn)) throw new Error('Define or discover the CSV schema first.');
        steps.push({ type: 'csv', id: block.id, path: block.getFieldValue('PATH'), delimiter: block.getFieldValue('DELIMITER'), hasHeader: block.getFieldValue('HEADER') === 'TRUE', schema });
        break;
      }
      case 'pipeline_map': {
        const mappings = getBlockData(block).mappings;
        if (!mappings?.length || !mappings.every(m => m && typeof m.source === 'string' && validColumn(m.target))) throw new Error('Configure at least one valid mapping.');
        steps.push({ type: 'map', id: block.id, mappings });
        break;
      }
      case 'pipeline_filter': steps.push({ type: 'filter', id: block.id, criteria: compilePredicate(block.getInputTargetBlock('CRITERIA'), 0) }); break;
      case 'pipeline_parquet': steps.push({ type: 'parquet', id: block.id, path: block.getFieldValue('PATH') }); break;
      default: throw new Error(`Unsupported block '${block.type}'.`);
    }
    block = block.getNextBlock();
  }
  if (steps.length > 100 || steps.at(-1)?.type !== 'parquet') throw new Error('Finish the chain with Parquet output (maximum 100 steps).');
  return { formatVersion: 1, steps };
}

function compilePredicate(block: Blockly.Block | null, depth: number): Predicate {
  if (!block) throw new Error('Connect a boolean criterion to every filter and expression socket.');
  if (depth >= 12) throw new Error('Filter expressions cannot exceed 12 levels.');
  switch (block.type) {
    case 'predicate_compare': return { kind: 'compare', column: block.getFieldValue('COLUMN'), operator: block.getFieldValue('OP') as ComparisonOperator, value: block.getFieldValue('VALUE') };
    case 'predicate_null': return { kind: 'null', column: block.getFieldValue('COLUMN'), isNull: block.getFieldValue('IS_NULL') === 'TRUE' };
    case 'predicate_logic': return { kind: 'logical', operator: block.getFieldValue('OP') as 'and' | 'or', children: [compilePredicate(block.getInputTargetBlock('LEFT'), depth + 1), compilePredicate(block.getInputTargetBlock('RIGHT'), depth + 1)] };
    case 'predicate_not': return { kind: 'logical', operator: 'not', children: [compilePredicate(block.getInputTargetBlock('VALUE'), depth + 1)] };
    default: throw new Error('Unsupported boolean expression.');
  }
}

export function seedWorkspace(workspace: Blockly.Workspace): void {
  Blockly.serialization.workspaces.load({ blocks: { languageVersion: 0, blocks: [{
    type: 'pipeline_csv', id: 'csv', x: 30, y: 30,
    data: JSON.stringify({ schema: [
      { name: 'id', type: 'int64', nullable: false }, { name: 'name', type: 'string', nullable: false },
      { name: 'age', type: 'int64', nullable: false }, { name: 'country', type: 'string', nullable: false },
    ] }),
    next: { block: { type: 'pipeline_filter', id: 'adults', inputs: { CRITERIA: { block: {
      type: 'predicate_compare', fields: { COLUMN: 'age', OP: 'greaterOrEqual', VALUE: '18' },
    } } }, next: { block: { type: 'pipeline_parquet', id: 'parquet' } } } },
  }] } }, workspace);
}

export function definitionToWorkspace(definition: Definition): Record<string, unknown> {
  const ids = new Set(definition.steps.map(step => step.id));
  let nextId = 0;
  const predicateBlock = (predicate: Predicate): Record<string, unknown> => {
    const id = () => {
      let candidate: string;
      do { candidate = `predicate-${++nextId}`; } while (ids.has(candidate));
      ids.add(candidate);
      return candidate;
    };
    switch (predicate.kind) {
      case 'compare': return { type: 'predicate_compare', id: id(), fields: { COLUMN: predicate.column, OP: predicate.operator, VALUE: predicate.value } };
      case 'null': return { type: 'predicate_null', id: id(), fields: { COLUMN: predicate.column, IS_NULL: predicate.isNull ? 'TRUE' : 'FALSE' } };
      case 'logical':
        if (predicate.operator === 'not' && predicate.children.length === 1)
          return { type: 'predicate_not', id: id(), inputs: { VALUE: { block: predicateBlock(predicate.children[0]) } } };
        if ((predicate.operator === 'and' || predicate.operator === 'or') && predicate.children.length === 2)
          return { type: 'predicate_logic', id: id(), fields: { OP: predicate.operator }, inputs: {
            LEFT: { block: predicateBlock(predicate.children[0]) }, RIGHT: { block: predicateBlock(predicate.children[1]) },
          } };
        throw new Error('Invalid logical expression in definition.');
      default: throw new Error('Unsupported predicate in definition.');
    }
  };
  const blocks = definition.steps.map((step, index): Record<string, unknown> => {
    const block: Record<string, unknown> = { type: `pipeline_${step.type}`, id: step.id };
    if (index === 0) { block.x = 30; block.y = 30; }
    if (step.type === 'csv') {
      block.fields = { PATH: step.path, DELIMITER: step.delimiter, HEADER: step.hasHeader ? 'TRUE' : 'FALSE' };
      block.data = JSON.stringify({ schema: step.schema });
    } else if (step.type === 'map') block.data = JSON.stringify({ mappings: step.mappings });
    else if (step.type === 'filter') block.inputs = { CRITERIA: { block: predicateBlock(step.criteria) } };
    else if (step.type === 'parquet') block.fields = { PATH: step.path };
    return block;
  });
  for (let index = blocks.length - 2; index >= 0; index--) blocks[index].next = { block: blocks[index + 1] };
  return { blocks: { languageVersion: 0, blocks: blocks.slice(0, 1) } };
}

import * as Blockly from 'blockly/core';
import 'blockly/blocks';
import * as En from 'blockly/msg/en';
import { columnTypes, type Column, type Definition, type Mapping, type Predicate, type Step, type ComparisonOperator, type TextExpression } from '../pipeline/types';

// Blockly's locale types include a synthetic default export; the ESM locale contains named strings.
Blockly.setLocale(Object.fromEntries(Object.entries(En).filter((entry): entry is [string, string] => typeof entry[1] === 'string')));
Blockly.common.defineBlocksWithJsonArray([
  { type: 'pipeline_csv', message0: 'CSV %1 delimiter %2 header %3', args0: [
    { type: 'input_value', name: 'PATH', check: 'String' },
    { type: 'field_input', name: 'DELIMITER', text: ',' },
    { type: 'field_checkbox', name: 'HEADER', checked: true },
  ], nextStatement: 'Pipeline', colour: 165, tooltip: 'Select this block to edit or discover its schema.' },
  { type: 'pipeline_map', message0: 'Map / Convert columns', previousStatement: 'Pipeline', nextStatement: 'Pipeline', colour: 230, tooltip: 'Select this block to configure its output columns.' },
  { type: 'pipeline_filter', message0: 'Keep rows where %1', args0: [{ type: 'input_value', name: 'CRITERIA', check: 'Boolean' }], previousStatement: 'Pipeline', nextStatement: 'Pipeline', colour: 230 },
  { type: 'pipeline_parquet', message0: 'Write Parquet %1', args0: [{ type: 'input_value', name: 'PATH', check: 'String' }], previousStatement: 'Pipeline', colour: 20 },
  { type: 'pipeline_csvOutput', message0: 'Write CSV %1 delimiter %2 header %3', args0: [
    { type: 'input_value', name: 'PATH', check: 'String' },
    { type: 'field_input', name: 'DELIMITER', text: ',' },
    { type: 'field_checkbox', name: 'HEADER', checked: true },
  ], previousStatement: 'Pipeline', colour: 20 },
  { type: 'pipeline_parquetInput', message0: 'Read Parquet %1', args0: [{ type: 'input_value', name: 'PATH', check: 'String' }], nextStatement: 'Pipeline', colour: 165 },
  { type: 'pipeline_apiInput', message0: 'Fetch API', nextStatement: 'Pipeline', colour: 165, tooltip: 'Fetch JSON or XML data into rows.' },
  { type: 'pipeline_postgresInput', message0: 'Read PostgreSQL', nextStatement: 'Pipeline', colour: 165, tooltip: 'Read query results from an external PostgreSQL database.' },
  { type: 'pipeline_excelInput', message0: 'Read Excel %1 sheet %2 header %3', args0: [
    { type: 'input_value', name: 'PATH', check: 'String' }, { type: 'field_input', name: 'SHEET', text: 'Sheet1' },
    { type: 'field_checkbox', name: 'HEADER', checked: true },
  ], nextStatement: 'Pipeline', colour: 165 },
  { type: 'pipeline_excelOutput', message0: 'Write Excel %1 sheet %2 header %3', args0: [
    { type: 'input_value', name: 'PATH', check: 'String' }, { type: 'field_input', name: 'SHEET', text: 'Sheet1' },
    { type: 'field_checkbox', name: 'HEADER', checked: true },
  ], previousStatement: 'Pipeline', colour: 20 },
  { type: 'value_literal', message0: 'text %1', args0: [{ type: 'field_input', name: 'VALUE', text: '' }], output: 'String', colour: 160 },
  { type: 'value_concat', message0: 'join %1 and %2', args0: [{ type: 'input_value', name: 'LEFT', check: 'String' }, { type: 'input_value', name: 'RIGHT', check: 'String' }], output: 'String', colour: 160 },
  { type: 'value_utc', message0: 'UTC now format %1 override format %2', args0: [{ type: 'field_input', name: 'FORMAT', text: 'yyyyMMdd_HHmmss' }, { type: 'input_value', name: 'FORMAT_EXPRESSION', check: 'String' }], output: 'String', colour: 160 },
  { type: 'value_current_environment', message0: 'current environment', output: 'String', colour: 300 },
  { type: 'value_variable', message0: 'variable %1', args0: [{ type: 'field_input', name: 'NAME', text: 'name' }], output: 'String', colour: 300 },
  { type: 'value_environment', message0: 'environment variable %1', args0: [{ type: 'field_input', name: 'NAME', text: 'name' }], output: 'String', colour: 300 },
  { type: 'define_variable', message0: 'define variable %1 as %2', args0: [{ type: 'field_input', name: 'NAME', text: 'name' }, { type: 'input_value', name: 'VALUE', check: 'String' }], colour: 300 },
  { type: 'define_environment', message0: 'environment %1 variable %2 value %3', args0: [{ type: 'field_input', name: 'ENVIRONMENT', text: 'development' }, { type: 'field_input', name: 'NAME', text: 'name' }, { type: 'field_input', name: 'VALUE', text: '' }], colour: 300 },
  { type: 'predicate_compare', message0: 'column %1 override %2 %3 value %4 override %5', args0: [
    { type: 'field_input', name: 'COLUMN', text: 'age' },
    { type: 'input_value', name: 'COLUMN_EXPRESSION', check: 'String' },
    { type: 'field_dropdown', name: 'OP', options: [['=', 'equal'], ['≠', 'notEqual'], ['>', 'greater'], ['≥', 'greaterOrEqual'], ['<', 'less'], ['≤', 'lessOrEqual']] },
    { type: 'field_input', name: 'VALUE', text: '18' },
    { type: 'input_value', name: 'VALUE_EXPRESSION', check: 'String' },
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
    { kind: 'category', name: 'Input', colour: '165', contents: ['pipeline_csv', 'pipeline_parquetInput', 'pipeline_excelInput', 'pipeline_apiInput', 'pipeline_postgresInput'].map(type => ({ kind: 'block', type })) },
    { kind: 'category', name: 'Processing', colour: '230', contents: ['pipeline_map', 'pipeline_filter'].map(type => ({ kind: 'block', type })) },
    { kind: 'category', name: 'Criteria', colour: '285', contents: ['predicate_compare', 'predicate_logic', 'predicate_not', 'predicate_null'].map(type => ({ kind: 'block', type })) },
    { kind: 'category', name: 'Values', colour: '160', contents: ['value_literal', 'value_concat', 'value_utc', 'value_current_environment', 'value_environment', 'define_environment'].map(type => ({ kind: 'block', type })) },
    { kind: 'category', name: 'Variables', colour: '330', custom: 'VARIABLE' },
    { kind: 'category', name: 'Output', colour: '20', contents: ['pipeline_parquet', 'pipeline_csvOutput', 'pipeline_excelOutput'].map(type => ({ kind: 'block', type })) },
  ],
};

export function getBlockData(block: Blockly.Block): { schema?: Column[]; mappings?: Mapping[]; api?: Record<string, unknown>; postgres?: Record<string, unknown>; hasHeaderVariable?: string; predicate?: Record<string, unknown> } {
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
    if (['pipeline_csv', 'pipeline_parquetInput', 'pipeline_excelInput', 'pipeline_apiInput', 'pipeline_postgresInput'].includes(cursor.type)) schema = getBlockData(cursor).schema ?? [];
    if (cursor.type === 'pipeline_map') schema = (getBlockData(cursor).mappings ?? []).map(m => m.target);
  }
  return schema;
}

function validColumn(column: Column): boolean {
  return column != null && typeof column.name === 'string' && columnTypes.includes(column.type) && typeof column.nullable === 'boolean';
}

export function compileWorkspace(workspace: Blockly.Workspace): Definition {
  const roots = workspace.getTopBlocks(false);
  const inputTypes = ['pipeline_csv', 'pipeline_parquetInput', 'pipeline_excelInput', 'pipeline_apiInput', 'pipeline_postgresInput'];
  const pipelines = roots.filter(block => inputTypes.includes(block.type));
  if (pipelines.length !== 1 || roots.some(block => ![...inputTypes, 'define_variable', 'define_environment', 'variables_set'].includes(block.type))) throw new Error('Connect all blocks into one pipeline chain starting with an input, or use variable assignment blocks.');
  for (const root of roots.filter(block => block.type === 'variables_set'))
    for (let block: Blockly.Block | null = root; block; block = block.getNextBlock())
      if (block.type !== 'variables_set') throw new Error('Only variable assignments can be connected together.');
  compileEnvironments(workspace);
  const steps: Step[] = [];
  let block: Blockly.Block | null = pipelines[0];
  while (block) {
    switch (block.type) {
      case 'pipeline_csv': {
        const schema = getBlockData(block).schema;
        if (!schema?.length || !schema.every(validColumn)) throw new Error('Define or discover the CSV schema first.');
        steps.push({ type: 'csv', id: block.id, path: 'expression', pathExpression: compileText(block.getInputTargetBlock('PATH')), delimiter: block.getFieldValue('DELIMITER'), hasHeader: block.getFieldValue('HEADER') === 'TRUE', ...(getBlockData(block).hasHeaderVariable ? { hasHeaderVariable: getBlockData(block).hasHeaderVariable } : {}), schema });
        break;
      }
      case 'pipeline_parquetInput':
      case 'pipeline_excelInput': {
        const schema = getBlockData(block).schema;
        if (!schema?.length || !schema.every(validColumn)) throw new Error('Define the input schema first.');
        const common = { id: block.id, path: 'expression', pathExpression: compileText(block.getInputTargetBlock('PATH')), schema };
        if (block.type === 'pipeline_parquetInput') steps.push({ type: 'parquetInput', ...common });
        else steps.push({ type: 'excelInput', ...common, sheet: block.getFieldValue('SHEET'), hasHeader: block.getFieldValue('HEADER') === 'TRUE', ...(getBlockData(block).hasHeaderVariable ? { hasHeaderVariable: getBlockData(block).hasHeaderVariable } : {}) });
        break;
      }
      case 'pipeline_apiInput': {
        const config = getBlockData(block);
        const api = config.api ?? {};
        const schema = config.schema;
        if (!schema?.length || !schema.every(validColumn)) throw new Error('Define or discover the API schema first.');
        steps.push({
          type: 'apiInput', id: block.id, schema,
          url: String(api.url ?? ''), dataPath: String(api.dataPath ?? ''),
          method: String(api.methodVariable || api.method || 'GET'),
          format: String(api.formatVariable || api.format || 'auto'),
          authentication: String(api.authenticationVariable || api.authentication || 'none'),
          username: String(api.username ?? ''), password: String(api.password ?? ''), token: String(api.token ?? ''),
          apiKeyName: String(api.apiKeyName ?? ''), apiKey: String(api.apiKey ?? ''),
          oauthTokenUrl: String(api.oauthTokenUrl ?? ''), clientId: String(api.clientId ?? ''),
          clientSecret: String(api.clientSecret ?? ''), scope: String(api.scope ?? ''),
          nextPath: String(api.nextPath ?? ''), useLinkHeader: Boolean(api.useLinkHeader),
          ...(api.useLinkHeaderVariable ? { useLinkHeaderVariable: String(api.useLinkHeaderVariable) } : {}),
          body: String(api.body ?? ''), headers: api.headers as Record<string, string> ?? {},
        });
        break;
      }
      case 'pipeline_postgresInput': {
        const config = getBlockData(block);
        const postgres = config.postgres ?? {};
        const schema = config.schema;
        if (!schema?.length || !schema.every(validColumn)) throw new Error('Discover or define the PostgreSQL schema first.');
        steps.push({
          type: 'postgresInput', id: block.id,
          host: String(postgres.hostVariable || postgres.host || ''),
          port: String(postgres.portVariable || postgres.port || '5432'),
          database: String(postgres.databaseVariable || postgres.database || ''),
          username: String(postgres.usernameVariable || postgres.username || ''),
          password: String(postgres.passwordVariable || postgres.password || ''),
          sslMode: String(postgres.sslModeVariable || postgres.sslMode || 'Prefer'),
          query: String(postgres.queryVariable || postgres.query || ''), schema,
        });
        break;
      }
      case 'pipeline_map': {
        const mappings = getBlockData(block).mappings;
        if (!mappings?.length || !mappings.every(m => m && typeof m.source === 'string' && validColumn(m.target))) throw new Error('Configure at least one valid mapping.');
        steps.push({ type: 'map', id: block.id, mappings });
        break;
      }
      case 'pipeline_filter': steps.push({ type: 'filter', id: block.id, criteria: compilePredicate(block.getInputTargetBlock('CRITERIA'), 0) }); break;
      case 'pipeline_parquet': steps.push({ type: 'parquet', id: block.id, path: 'expression', pathExpression: compileText(block.getInputTargetBlock('PATH')) }); break;
      case 'pipeline_csvOutput': steps.push({ type: 'csvOutput', id: block.id, path: 'expression', pathExpression: compileText(block.getInputTargetBlock('PATH')), delimiter: block.getFieldValue('DELIMITER'), hasHeader: block.getFieldValue('HEADER') === 'TRUE', ...(getBlockData(block).hasHeaderVariable ? { hasHeaderVariable: getBlockData(block).hasHeaderVariable } : {}) }); break;
      case 'pipeline_excelOutput': steps.push({ type: 'excelOutput', id: block.id, path: 'expression', pathExpression: compileText(block.getInputTargetBlock('PATH')), sheet: block.getFieldValue('SHEET'), hasHeader: block.getFieldValue('HEADER') === 'TRUE', ...(getBlockData(block).hasHeaderVariable ? { hasHeaderVariable: getBlockData(block).hasHeaderVariable } : {}) }); break;
       case 'variables_set': break;
      default: throw new Error(`Unsupported block '${block.type}'.`);
    }
    block = block.getNextBlock();
  }
  const variables = compileVariables(workspace);
  if (steps.length > 100 || !['parquet', 'csvOutput', 'excelOutput'].includes(steps.at(-1)?.type ?? '')) throw new Error('Finish the chain with an output (maximum 100 steps).');
  return { formatVersion: 1, steps, variables };
}

export function compileVariables(workspace: Blockly.Workspace): NonNullable<Definition['variables']> {
  const roots = workspace.getTopBlocks(false);
  const assignments = workspace.getAllBlocks(false).filter(block => block.type === 'variables_set');
  const variables = [
    ...roots.filter(block => block.type === 'define_variable').map(block => ({ name: block.getFieldValue('NAME') as string, value: compileText(block.getInputTargetBlock('VALUE')) })),
    ...assignments.map(block => ({ name: variableName(block), value: compileText(block.getInputTargetBlock('VALUE')) })),
  ];
  if (variables.some(variable => !variable.name.trim()) || new Set(variables.map(variable => variable.name)).size !== variables.length)
    throw new Error('Variable names must be nonempty and unique.');
  return variables;
}

export function resolveWorkspaceText(value: string, workspace: Blockly.Workspace,
  environments: Record<string, Record<string, string>>, environment: string): string {
  const variables = new Map(compileVariables(workspace).map(variable => [variable.name, variable.value]));
  const active = new Set<string>();
  const evaluate = (expression: TextExpression, depth = 0): string => {
    if (!expression || depth >= 32) throw new Error('Variable expression is missing or too deeply nested.');
    switch (expression.kind) {
      case 'literal': return expression.value;
      case 'concat': return evaluate(expression.left, depth + 1) + evaluate(expression.right, depth + 1);
      case 'environment':
        if (expression.name === 'CURRENT_ENVIRONMENT') return environment;
        return environments[environment]?.[expression.name] ?? throwMissing(expression.name);
      case 'variable': {
        const definition = variables.get(expression.name);
        if (!definition || active.has(expression.name)) throw new Error(`Missing or circular pipeline variable '${expression.name}'.`);
        active.add(expression.name);
        try { return evaluate(definition, depth + 1); } finally { active.delete(expression.name); }
      }
      case 'utc': {
        const format = expression.formatExpression ? evaluate(expression.formatExpression, depth + 1) : expression.format;
        if (format === 'O') return new Date().toISOString();
        const now = new Date();
        // shortcut: discovery supports common UTC format tokens; expand when connection/query templates need other .NET formats.
        const parts: Record<string, string> = {
          yyyy: String(now.getUTCFullYear()).padStart(4, '0'), MM: String(now.getUTCMonth() + 1).padStart(2, '0'),
          dd: String(now.getUTCDate()).padStart(2, '0'), HH: String(now.getUTCHours()).padStart(2, '0'),
          mm: String(now.getUTCMinutes()).padStart(2, '0'), ss: String(now.getUTCSeconds()).padStart(2, '0'),
          fff: String(now.getUTCMilliseconds()).padStart(3, '0'),
        };
        return format.replace(/yyyy|fff|MM|dd|HH|mm|ss/g, token => parts[token]);
      }
    }
  };
  const throwMissing = (name: string): never => { throw new Error(`Missing environment variable '${name}'.`); };
  return value.replace(/\$\{(?:(var|env|secret)\.)?([A-Za-z_][A-Za-z0-9_]*)\}/g, (match, scope: string | undefined, name: string) => {
    if (scope === 'secret') return match;
    if (scope === 'var') {
      const definition = variables.get(name);
      if (!definition || active.has(name)) throw new Error(`Missing or circular pipeline variable '${name}'.`);
      active.add(name);
      try { return evaluate(definition); } finally { active.delete(name); }
    }
    if (name === 'CURRENT_ENVIRONMENT') return environment;
    return environments[environment]?.[name] ?? throwMissing(name);
  });
}

export function compileText(block: Blockly.Block | null, depth = 0): TextExpression {
  if (!block || depth >= 32) throw new Error('Connect a text value (maximum depth 32).');
  const id = block.id;
  switch (block.type) {
    case 'value_literal': return { kind: 'literal', id, value: block.getFieldValue('VALUE') };
    case 'value_concat': return { kind: 'concat', id, left: compileText(block.getInputTargetBlock('LEFT'), depth + 1), right: compileText(block.getInputTargetBlock('RIGHT'), depth + 1) };
    case 'value_utc': return { kind: 'utc', id, format: block.getFieldValue('FORMAT'), ...(block.getInputTargetBlock('FORMAT_EXPRESSION') ? { formatExpression: compileText(block.getInputTargetBlock('FORMAT_EXPRESSION'), depth + 1) } : {}) };
    case 'value_current_environment': return { kind: 'environment', id, name: 'CURRENT_ENVIRONMENT' };
    case 'value_variable': return { kind: 'variable', id, name: block.getFieldValue('NAME') };
    case 'variables_get': return { kind: 'variable', id, name: variableName(block) };
    case 'value_environment': return { kind: 'environment', id, name: block.getFieldValue('NAME') };
    default: throw new Error('Unsupported text value.');
  }
}

function variableName(block: Blockly.Block): string {
  const variable = block.workspace.getVariableMap().getVariableById(block.getFieldValue('VAR'));
  if (!variable) throw new Error('Select an existing variable.');
  return variable.getName();
}

function isVariableReference(value: string): boolean {
  return /\$\{(?:(?:var|env|secret)\.)?[A-Za-z_][A-Za-z0-9_]*\}/.test(value);
}

export function compileEnvironments(workspace: Blockly.Workspace): Record<string, Record<string, string>> {
  const result: Record<string, Record<string, string>> = Object.create(null);
  for (const block of workspace.getTopBlocks(false).filter(b => b.type === 'define_environment')) {
    const environment = (block.getFieldValue('ENVIRONMENT') as string).trim();
    const name = (block.getFieldValue('NAME') as string).trim();
    if (!environment || !name) throw new Error('Environment and variable names are required.');
    const values = result[environment] ??= Object.create(null);
    if (Object.hasOwn(values, name)) throw new Error(`Duplicate environment variable '${name}' in '${environment}'.`);
    values[name] = block.getFieldValue('VALUE');
  }
  return result;
}

/** Migrates environment values from older pipeline documents into editable Blockly declaration blocks. */
export function restoreEnvironmentBlocks(
  workspaceState: Record<string, unknown>,
  environments: Record<string, Record<string, string>>,
): Record<string, unknown> {
  const restored = structuredClone(workspaceState);
  const serializedBlocks = (restored.blocks ??= { languageVersion: 0, blocks: [] }) as { languageVersion?: number; blocks?: Record<string, unknown>[] };
  serializedBlocks.languageVersion ??= 0;
  serializedBlocks.blocks ??= [];
  const blocks = serializedBlocks.blocks;
  const declared = new Set(blocks.filter(block => block.type === 'define_environment').map(block => {
    const fields = block.fields as Record<string, string> | undefined;
    return `${fields?.ENVIRONMENT}\0${fields?.NAME}`;
  }));
  const usedIds = new Set(blocks.map(block => block.id).filter((id): id is string => typeof id === 'string'));
  let index = 0;
  for (const [environment, values] of Object.entries(environments)) for (const [name, value] of Object.entries(values)) {
    const key = `${environment}\0${name}`;
    if (declared.has(key)) continue;
    let id: string;
    do { id = `restored-environment-${++index}`; } while (usedIds.has(id));
    usedIds.add(id);
    declared.add(key);
    blocks.push({ type: 'define_environment', id, x: 40 + index * 20, y: 40 + index * 20,
      fields: { ENVIRONMENT: environment, NAME: name, VALUE: value } });
  }
  return restored;
}

function compilePredicate(block: Blockly.Block | null, depth: number): Predicate {
  if (!block) throw new Error('Connect a boolean criterion to every filter and expression socket.');
  if (depth >= 12) throw new Error('Filter expressions cannot exceed 12 levels.');
  switch (block.type) {
    case 'predicate_compare': return { kind: 'compare', column: block.getFieldValue('COLUMN'), operator: block.getFieldValue('OP') as ComparisonOperator, value: block.getFieldValue('VALUE'), ...(block.getInputTargetBlock('COLUMN_EXPRESSION') ? { columnExpression: compileText(block.getInputTargetBlock('COLUMN_EXPRESSION')) } : {}), ...(block.getInputTargetBlock('VALUE_EXPRESSION') ? { valueExpression: compileText(block.getInputTargetBlock('VALUE_EXPRESSION')) } : {}), ...(getBlockData(block).predicate?.operatorVariable ? { operatorVariable: String(getBlockData(block).predicate!.operatorVariable) } : {}) };
    case 'predicate_null': return { kind: 'null', column: block.getFieldValue('COLUMN'), isNull: block.getFieldValue('IS_NULL') === 'TRUE', ...(getBlockData(block).predicate?.isNullVariable ? { isNullVariable: String(getBlockData(block).predicate!.isNullVariable) } : {}) };
    case 'predicate_logic': return { kind: 'logical', operator: block.getFieldValue('OP') as 'and' | 'or', children: [compilePredicate(block.getInputTargetBlock('LEFT'), depth + 1), compilePredicate(block.getInputTargetBlock('RIGHT'), depth + 1)] };
    case 'predicate_not': return { kind: 'logical', operator: 'not', children: [compilePredicate(block.getInputTargetBlock('VALUE'), depth + 1)] };
    default: throw new Error('Unsupported boolean expression.');
  }
}

export function seedWorkspace(workspace: Blockly.Workspace): void {
  Blockly.serialization.workspaces.load({ blocks: { languageVersion: 0, blocks: [{
    type: 'pipeline_csv', id: 'csv', x: 30, y: 30,
    inputs: { PATH: { block: { type: 'value_literal', fields: { VALUE: 'customers.csv' } } } },
    data: JSON.stringify({ schema: [
      { name: 'id', type: 'int64', nullable: false }, { name: 'name', type: 'string', nullable: false },
      { name: 'age', type: 'int64', nullable: false }, { name: 'country', type: 'string', nullable: false },
    ] }),
    next: { block: { type: 'pipeline_filter', id: 'adults', inputs: { CRITERIA: { block: {
      type: 'predicate_compare', fields: { COLUMN: 'age', OP: 'greaterOrEqual', VALUE: '18' },
    } } }, next: { block: { type: 'pipeline_parquet', id: 'parquet', inputs: { PATH: { block: { type: 'value_literal', fields: { VALUE: 'customers.parquet' } } } } } } } },
  }] } }, workspace);
}

export function definitionToWorkspace(definition: Definition): Record<string, unknown> {
  const ids = new Set(definition.steps.map(step => step.id));
  let nextId = 0;
  const variableIds = new Map<string, string>();
  const variableId = (name: string) => {
    if (!variableIds.has(name)) variableIds.set(name, `variable-${variableIds.size + 1}`);
    return variableIds.get(name)!;
  };
  const textBlock = (expression: TextExpression): Record<string, unknown> => {
    switch (expression.kind) {
      case 'literal': return { type: 'value_literal', id: expression.id, fields: { VALUE: expression.value } };
      case 'utc': return { type: 'value_utc', id: expression.id, fields: { FORMAT: expression.format }, ...(expression.formatExpression ? { inputs: { FORMAT_EXPRESSION: { block: textBlock(expression.formatExpression) } } } : {}) };
      case 'variable': return { type: 'variables_get', id: expression.id, fields: { VAR: { id: variableId(expression.name) } } };
      case 'environment': return expression.name === 'CURRENT_ENVIRONMENT' ? { type: 'value_current_environment', id: expression.id } : { type: 'value_environment', id: expression.id, fields: { NAME: expression.name } };
      case 'concat': return { type: 'value_concat', id: expression.id, inputs: { LEFT: { block: textBlock(expression.left) }, RIGHT: { block: textBlock(expression.right) } } };
    }
  };
  const predicateBlock = (predicate: Predicate): Record<string, unknown> => {
    const id = () => {
      let candidate: string;
      do { candidate = `predicate-${++nextId}`; } while (ids.has(candidate));
      ids.add(candidate);
      return candidate;
    };
    switch (predicate.kind) {
      case 'compare': return { type: 'predicate_compare', id: id(), data: JSON.stringify({ predicate: { operatorVariable: predicate.operatorVariable ?? '' } }), fields: { COLUMN: predicate.column, OP: predicate.operator, VALUE: predicate.value }, inputs: { ...(predicate.columnExpression ? { COLUMN_EXPRESSION: { block: textBlock(predicate.columnExpression) } } : {}), ...(predicate.valueExpression ? { VALUE_EXPRESSION: { block: textBlock(predicate.valueExpression) } } : {}) } };
      case 'null': return { type: 'predicate_null', id: id(), data: JSON.stringify({ predicate: { isNullVariable: predicate.isNullVariable ?? '' } }), fields: { COLUMN: predicate.column, IS_NULL: predicate.isNull ? 'TRUE' : 'FALSE' } };
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
    if (step.type === 'apiInput') {
      block.data = JSON.stringify({ schema: step.schema, api: {
        url: step.url, dataPath: step.dataPath,
        method: isVariableReference(step.method) ? 'GET' : step.method,
        methodVariable: isVariableReference(step.method) ? step.method : '',
        format: isVariableReference(step.format) ? 'auto' : step.format,
        formatVariable: isVariableReference(step.format) ? step.format : '',
        authentication: isVariableReference(step.authentication) ? 'none' : step.authentication,
        authenticationVariable: isVariableReference(step.authentication) ? step.authentication : '',
        username: step.username, password: step.password, token: step.token,
        apiKeyName: step.apiKeyName, apiKey: step.apiKey, oauthTokenUrl: step.oauthTokenUrl, clientId: step.clientId,
        clientSecret: step.clientSecret, scope: step.scope, nextPath: step.nextPath, useLinkHeader: step.useLinkHeader,
        useLinkHeaderVariable: step.useLinkHeaderVariable,
        body: step.body, headers: step.headers,
      } });
    } else if (step.type === 'postgresInput') {
      const property = (value: string) => isVariableReference(value) ? { value: '', variable: value } : { value, variable: '' };
      const host = property(step.host), port = property(step.port), database = property(step.database);
      const username = property(step.username), password = property(step.password), sslMode = property(step.sslMode), query = property(step.query);
      block.data = JSON.stringify({ schema: step.schema, postgres: {
        host: host.value, hostVariable: host.variable, port: port.value, portVariable: port.variable,
        database: database.value, databaseVariable: database.variable, username: username.value, usernameVariable: username.variable,
        password: password.value, passwordVariable: password.variable, sslMode: sslMode.value, sslModeVariable: sslMode.variable,
        query: query.value, queryVariable: query.variable,
      } });
    } else if (step.type === 'csv') {
      block.fields = { DELIMITER: step.delimiter, HEADER: step.hasHeader ? 'TRUE' : 'FALSE' };
      block.inputs = { PATH: { block: textBlock(step.pathExpression ?? { kind: 'literal', id: `${step.id}-path`, value: step.path }) } };
      block.data = JSON.stringify({ schema: step.schema, hasHeaderVariable: 'hasHeaderVariable' in step ? step.hasHeaderVariable ?? '' : '' });
    } else if (step.type === 'parquetInput' || step.type === 'excelInput') {
      block.inputs = { PATH: { block: textBlock(step.pathExpression ?? { kind: 'literal', id: `${step.id}-path`, value: step.path }) } };
      block.data = JSON.stringify({ schema: step.schema, hasHeaderVariable: 'hasHeaderVariable' in step ? step.hasHeaderVariable ?? '' : '' });
      if (step.type === 'excelInput') block.fields = { SHEET: step.sheet, HEADER: step.hasHeader ? 'TRUE' : 'FALSE' };
    } else if (step.type === 'map') block.data = JSON.stringify({ mappings: step.mappings });
    else if (step.type === 'filter') block.inputs = { CRITERIA: { block: predicateBlock(step.criteria) } };
    else if (step.type === 'parquet' || step.type === 'csvOutput' || step.type === 'excelOutput') {
      block.inputs = { PATH: { block: textBlock(step.pathExpression ?? { kind: 'literal', id: `${step.id}-path`, value: step.path }) } };
      if (step.type === 'csvOutput') { block.fields = { DELIMITER: step.delimiter, HEADER: step.hasHeader ? 'TRUE' : 'FALSE' }; block.data = JSON.stringify({ hasHeaderVariable: step.hasHeaderVariable ?? '' }); }
      if (step.type === 'excelOutput') { block.fields = { SHEET: step.sheet, HEADER: step.hasHeader ? 'TRUE' : 'FALSE' }; block.data = JSON.stringify({ hasHeaderVariable: step.hasHeaderVariable ?? '' }); }
    }
    return block;
  });
  for (let index = blocks.length - 2; index >= 0; index--) blocks[index].next = { block: blocks[index + 1] };
  const variables = (definition.variables ?? []).map((variable, index) => ({
    type: 'variables_set', x: 400, y: 30 + index * 80, fields: { VAR: { id: variableId(variable.name) } }, inputs: { VALUE: { block: textBlock(variable.value) } },
  }));
  return { variables: [...variableIds].map(([name, id]) => ({ name, id })), blocks: { languageVersion: 0, blocks: [...blocks.slice(0, 1), ...variables] } };
}

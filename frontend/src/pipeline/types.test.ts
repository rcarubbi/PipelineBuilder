import { inferSchemas, resolvePath, type Definition } from './types';

it('propagates the mapped output schema to filters and output', () => {
  const definition: Definition = { formatVersion: 1, steps: [
    { type: 'csv', id: 'in', path: 'input.csv', delimiter: ',', hasHeader: true, schema: [{ name: 'Age', type: 'string', nullable: false }] },
    { type: 'map', id: 'map', mappings: [{ source: 'Age', target: { name: 'age', type: 'int64', nullable: false } }] },
    { type: 'filter', id: 'filter', criteria: { kind: 'compare', column: 'age', operator: 'greater', value: '18' } },
    { type: 'parquet', id: 'out', path: 'output.parquet' },
  ] };
  const schemas = inferSchemas(definition);
  expect(schemas[2].input).toEqual([{ name: 'age', type: 'int64', nullable: false }]);
  expect(schemas[3].input).toEqual(schemas[2].input);
  expect(schemas[3].output).toEqual([]);
});

it('resolves variables once and rejects missing variables', () => {
  expect(resolvePath('${ROOT}/${FILE}', { ROOT: 'data', FILE: '${OTHER}.csv' })).toBe('data/${OTHER}.csv');
  expect(() => resolvePath('${MISSING}', {})).toThrow('Missing environment variable');
  expect(() => resolvePath('${constructor}', {})).toThrow('Missing environment variable');
});

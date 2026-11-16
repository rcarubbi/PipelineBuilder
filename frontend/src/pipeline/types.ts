export const columnTypes = ['string', 'boolean', 'int64', 'decimal', 'double', 'date', 'timestamp'] as const;
export type ColumnType = typeof columnTypes[number];
export interface Column {
  name: string; type: ColumnType; nullable: boolean; precision?: number; scale?: number;
  typeVariable?: string; nullableVariable?: string; precisionVariable?: string; scaleVariable?: string;
}
export interface Mapping { source: string; target: Column; format?: string | null }
export type ComparisonOperator = 'equal' | 'notEqual' | 'greater' | 'greaterOrEqual' | 'less' | 'lessOrEqual';
export type Predicate =
  | { kind: 'compare'; column: string; operator: ComparisonOperator; value: string; columnExpression?: TextExpression; valueExpression?: TextExpression; operatorVariable?: string }
  | { kind: 'null'; column: string; isNull: boolean; isNullVariable?: string }
  | { kind: 'logical'; operator: 'and' | 'or' | 'not'; children: Predicate[] };
export type TextExpression =
  | { kind: 'literal'; id: string; value: string }
  | { kind: 'concat'; id: string; left: TextExpression; right: TextExpression }
  | { kind: 'variable' | 'environment'; id: string; name: string }
  | { kind: 'utc'; id: string; format: string; formatExpression?: TextExpression };
export type Step =
  | { type: 'csv'; id: string; path: string; pathExpression?: TextExpression; delimiter: string; hasHeader: boolean; hasHeaderVariable?: string; schema: Column[] }
  | { type: 'parquetInput'; id: string; path: string; pathExpression?: TextExpression; schema: Column[] }
  | { type: 'excelInput'; id: string; path: string; pathExpression?: TextExpression; schema: Column[]; sheet: string; hasHeader: boolean; hasHeaderVariable?: string }
  | { type: 'apiInput'; id: string; url: string; schema: Column[]; dataPath: string; method: string; format: string; authentication: string; username?: string; password?: string; token?: string; apiKeyName?: string; apiKey?: string; oauthTokenUrl?: string; clientId?: string; clientSecret?: string; scope?: string; nextPath?: string; useLinkHeader: boolean; useLinkHeaderVariable?: string; body?: string; headers?: Record<string, string> }
  | { type: 'map'; id: string; mappings: Mapping[] }
  | { type: 'filter'; id: string; criteria: Predicate }
  | { type: 'parquet'; id: string; path: string; pathExpression?: TextExpression }
  | { type: 'csvOutput'; id: string; path: string; pathExpression?: TextExpression; delimiter: string; hasHeader: boolean; hasHeaderVariable?: string }
  | { type: 'excelOutput'; id: string; path: string; pathExpression?: TextExpression; sheet: string; hasHeader: boolean; hasHeaderVariable?: string };
export interface Definition { formatVersion: 1; steps: Step[]; variables?: { name: string; value: TextExpression }[] }
export interface PipelineDocument {
  name: string;
  definition: Definition;
  workspace: Record<string, unknown>;
  environments: Record<string, Record<string, string>>;
}
export interface SavedPipeline { id: string; revision: number; document: PipelineDocument }
export interface PipelineSummary { id: string; name: string; revision: number; environments: string[] }
export interface PipelineList { pipelines: PipelineSummary[]; page: number; hasMore: boolean }
export interface Analysis {
  issues: { blockId: string; message: string }[];
  schemas: { blockId: string; input: Column[]; output: Column[] }[];
}
export interface Run {
  id: string; pipelineId: string; revision: number; environment: string;
  status: 'Queued' | 'Running' | 'Succeeded' | 'Failed' | 'Cancelled' | 'Interrupted';
  rowsRead: number; rowsWritten: number; error: string | null; blockId: string | null;
  createdAt: string; startedAt: string | null; finishedAt: string | null; trigger: string;
}
export interface RunHistory { runs: Run[]; page: number; hasMore: boolean }
export interface ScheduledPipeline { pipelineId: string; pipelineName: string; environment: string }
export interface Schedule {
  id: string; alias: string; cronExpression: string; isEnabled: boolean; revision: number;
  pipelines: ScheduledPipeline[]; nextRuns: string[];
}
export function inferSchemas(definition: Definition): Analysis['schemas'] {
  let current: Column[] = [];
  return definition.steps.map(step => {
    const input = current;
    if (step.type === 'csv' || step.type === 'parquetInput' || step.type === 'excelInput' || step.type === 'apiInput') current = step.schema;
    if (step.type === 'map') current = step.mappings.map(m => m.target);
    return { blockId: step.id, input, output: step.type === 'parquet' || step.type === 'csvOutput' || step.type === 'excelOutput' ? [] : current };
  });
}

export function resolvePath(path: string, variables: Record<string, string>): string {
  return path.replace(/\$\{([A-Za-z_][A-Za-z0-9_]*)\}/g, (_, key: string) => {
    if (!Object.hasOwn(variables, key)) throw new Error(`Missing environment variable '${key}'.`);
    return variables[key];
  });
}

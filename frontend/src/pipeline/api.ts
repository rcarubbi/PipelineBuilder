import type { Analysis, Column, Definition, PipelineDocument, PipelineList, Run, RunHistory, SavedPipeline, Schedule } from './types';

async function request<T>(path: string, method = 'GET', body?: unknown): Promise<T> {
  const response = await fetch(`/api/v1/${path}`, {
    method,
    headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const result = await response.json();
  if (!response.ok) {
    const messages = Array.isArray(result.errors)
      ? result.errors.map((e: { message: string }) => e.message).join('\n')
      : Object.values(result.errors ?? {}).flat().join('\n');
    throw new Error(messages || result.title || `Request failed (${response.status}).`);
  }
  return result as T;
}

const scheduleLink = (alias: string, pipelineId: string, environment: string) =>
  `schedules/by-alias/${encodeURIComponent(alias)}/pipelines/${encodeURIComponent(pipelineId)}/environments/${encodeURIComponent(environment)}`;

export const api = {
  pipelines: (page: number) => request<PipelineList>(`pipelines?page=${page}`),
  deletePipeline: (id: string, revision: number) => request<{ deleted: boolean }>(`pipelines/${encodeURIComponent(id)}?revision=${revision}`, 'DELETE'),
  save: (id: string, expectedRevision: number, document: PipelineDocument) => request<SavedPipeline>(`pipelines/${id}`, 'PUT', { expectedRevision, document }),
  load: (id: string) => request<SavedPipeline>(`pipelines/${encodeURIComponent(id)}`),
  importPipeline: (document: PipelineDocument) => request<SavedPipeline>('pipelines/import', 'POST', document),
  validate: (definition: Definition) => request<Analysis>('pipelines/validate', 'POST', definition),
  discover: (path: string, delimiter: string, hasHeader: boolean, format = 'csv', sheet?: string, api?: Record<string, unknown>, postgres?: Record<string, unknown>) =>
    request<{ schema: Column[]; sampleLimit: number; dataPaths?: string[] }>('schemas/discover', 'POST', { path, delimiter, hasHeader, format, sheet, api, postgres }),
  run: (pipelineId: string, revision: number, environment: string) => request<Run>('runs', 'POST', { pipelineId, revision, environment }),
  getRun: (id: string) => request<Run>(`runs/${id}`),
  history: (page: number, pipelineId?: string, pageSize = 25) => request<RunHistory>(`runs?page=${page}&pageSize=${pageSize}${pipelineId ? `&pipelineId=${encodeURIComponent(pipelineId)}` : ''}`),
  cancel: (id: string) => request<{ cancellationRequested: boolean }>(`runs/${id}/cancel`, 'POST'),
  schedules: () => request<{ schedules: Schedule[] }>('schedules'),
  saveSchedule: (id: string, expectedRevision: number, schedule: { alias: string; cronExpression: string; isEnabled: boolean }) =>
    request<Schedule>(`schedules/${encodeURIComponent(id)}`, 'PUT', { expectedRevision, ...schedule }),
  deleteSchedule: (id: string, revision: number) => request<{ deleted: boolean }>(`schedules/${encodeURIComponent(id)}?revision=${revision}`, 'DELETE'),
  previewSchedule: (cronExpression: string) => request<{ nextRuns: string[] }>('schedules/preview', 'POST', { cronExpression }),
  attachSchedule: (alias: string, pipelineId: string, environment: string) =>
    request<Schedule>(scheduleLink(alias, pipelineId, environment), 'PUT'),
  detachSchedule: (alias: string, pipelineId: string, environment: string) =>
    request<Schedule>(scheduleLink(alias, pipelineId, environment), 'DELETE'),
};

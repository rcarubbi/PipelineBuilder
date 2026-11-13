import { api } from './api';
import type { PipelineDocument } from './types';

afterEach(() => vi.unstubAllGlobals());

it('turns backend ProblemDetails into readable errors', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ title: 'Validation failed', errors: [{ code: 'csv', message: 'Unknown column age.' }] }), { status: 400 })));
  await expect(api.load('pipeline')).rejects.toThrow('Unknown column age.');
});

it('starts a run from an explicit saved revision and environment', async () => {
  const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ id: 'run', status: 'Queued' }), { status: 202 }));
  vi.stubGlobal('fetch', fetchMock);
  await api.run('pipeline', 3, 'test');
  expect(fetchMock).toHaveBeenCalledWith('/api/v1/runs', expect.objectContaining({ method: 'POST', body: JSON.stringify({ pipelineId: 'pipeline', revision: 3, environment: 'test' }) }));
});

it('imports a new pipeline by posting its document directly', async () => {
  const document: PipelineDocument = { name: 'Imported', definition: { formatVersion: 1, steps: [] }, workspace: {}, environments: { default: {} } };
  const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ id: 'new-id', revision: 1, document }), { status: 201 }));
  vi.stubGlobal('fetch', fetchMock);

  await expect(api.importPipeline(document)).resolves.toMatchObject({ id: 'new-id' });

  expect(fetchMock).toHaveBeenCalledWith('/api/v1/pipelines/import', expect.objectContaining({ method: 'POST', body: JSON.stringify(document) }));
});

it('requests a bounded execution-history page', async () => {
  const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ runs: [], page: 2, hasMore: false })));
  vi.stubGlobal('fetch', fetchMock);

  await expect(api.history(2)).resolves.toEqual({ runs: [], page: 2, hasMore: false });
  expect(fetchMock).toHaveBeenCalledWith('/api/v1/runs?page=2', expect.objectContaining({ method: 'GET' }));
});

it('addresses schedule associations by alias, pipeline and URL-encoded environment', async () => {
  const fetchMock = vi.fn().mockImplementation(() => Promise.resolve(new Response('{}')));
  vi.stubGlobal('fetch', fetchMock);

  await api.attachSchedule('daily', 'pipeline-1', 'qa/eu');
  await api.detachSchedule('daily', 'pipeline-1', 'qa/eu');

  expect(fetchMock).toHaveBeenNthCalledWith(1, '/api/v1/schedules/by-alias/daily/pipelines/pipeline-1/environments/qa%2Feu', expect.objectContaining({ method: 'PUT' }));
  expect(fetchMock).toHaveBeenNthCalledWith(2, '/api/v1/schedules/by-alias/daily/pipelines/pipeline-1/environments/qa%2Feu', expect.objectContaining({ method: 'DELETE' }));
});

it('filters history by pipeline and deletes using the displayed revision', async () => {
  const fetchMock = vi.fn().mockImplementation(() => Promise.resolve(new Response('{}')));
  vi.stubGlobal('fetch', fetchMock);

  await api.history(1, 'pipeline-1');
  await api.deletePipeline('pipeline-1', 3);

  expect(fetchMock).toHaveBeenCalledWith('/api/v1/runs?page=1&pipelineId=pipeline-1', expect.objectContaining({ method: 'GET' }));
  expect(fetchMock).toHaveBeenCalledWith('/api/v1/pipelines/pipeline-1?revision=3', expect.objectContaining({ method: 'DELETE' }));
});

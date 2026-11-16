export function PageHeader({ page, pipelineId }: { page: string; pipelineId?: string }) {
  return <header><div><h1>Pipeline Builder</h1><p className="page-subtitle">{page}</p>
    {pipelineId && <p className="page-context">Pipeline ID: <code>{pipelineId}</code></p>}
  </div></header>;
}

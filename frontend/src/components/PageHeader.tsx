export function PageHeader({ page }: { page: string }) {
  return <header><div><h1>Pipeline Builder</h1><p className="page-subtitle">{page}</p></div></header>;
}

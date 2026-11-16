import { Button } from './ui/button';

export function Pagination({ page, pageSize, totalCount, disabled = false, onPageChange }: {
  page: number; pageSize: number; totalCount: number; disabled?: boolean; onPageChange: (page: number) => void;
}) {
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize));
  const pages = [...new Set([1, totalPages, page - 1, page, page + 1].filter(value => value >= 1 && value <= totalPages))].sort((a, b) => a - b);
  const goTo = (target: number) => onPageChange(Math.max(1, Math.min(totalPages, target)));

  return <nav className="pagination" aria-label="Pagination">
    <div className="pagination-controls">
      <Button variant="outline" size="sm" aria-label="First page" disabled={disabled || page <= 1} onClick={() => goTo(1)}>First</Button>
      <Button variant="outline" size="sm" aria-label="Previous page" disabled={disabled || page <= 1} onClick={() => goTo(page - 1)}>Previous</Button>
      <div className="pagination-pages">
        {pages.map((number, index) => <span className="pagination-item" key={number}>
          {index > 0 && number - pages[index - 1] > 1 && <span aria-hidden="true">…</span>}
          <Button variant={number === page ? 'default' : 'outline'} size="sm" aria-label={`Page ${number}`}
            aria-current={number === page ? 'page' : undefined} disabled={disabled} onClick={() => goTo(number)}>{number}</Button>
        </span>)}
      </div>
      <Button variant="outline" size="sm" aria-label="Next page" disabled={disabled || page >= totalPages} onClick={() => goTo(page + 1)}>Next</Button>
      <Button variant="outline" size="sm" aria-label="Last page" disabled={disabled || page >= totalPages} onClick={() => goTo(totalPages)}>Last</Button>
    </div>
    <span className="muted">Page {page} of {totalPages} · {totalCount} total</span>
  </nav>;
}

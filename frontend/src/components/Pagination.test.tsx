import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Pagination } from './Pagination';

it('shows totals, boundary controls, current page and adjacent pages', async () => {
  const onPageChange = vi.fn();
  render(<Pagination page={5} pageSize={10} totalCount={200} onPageChange={onPageChange} />);

  expect(screen.getByText('Page 5 of 20 · 200 total')).toBeInTheDocument();
  for (const number of [1, 4, 5, 6, 20]) expect(screen.getByRole('button', { name: `Page ${number}` })).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Page 5' })).toHaveAttribute('aria-current', 'page');
  expect(screen.getAllByText('…')).toHaveLength(2);

  const user = userEvent.setup();
  await user.click(screen.getByRole('button', { name: 'First page' }));
  expect(onPageChange).toHaveBeenLastCalledWith(1);
  await user.click(screen.getByRole('button', { name: 'Page 6' }));
  expect(onPageChange).toHaveBeenLastCalledWith(6);
  await user.click(screen.getByRole('button', { name: 'Last page' }));
  expect(onPageChange).toHaveBeenLastCalledWith(20);
});

it('renders an empty collection as one disabled page', () => {
  render(<Pagination page={1} pageSize={25} totalCount={0} onPageChange={vi.fn()} />);

  expect(screen.getByText('Page 1 of 1 · 0 total')).toBeInTheDocument();
  for (const name of ['First page', 'Previous page', 'Next page', 'Last page'])
    expect(screen.getByRole('button', { name })).toBeDisabled();
});

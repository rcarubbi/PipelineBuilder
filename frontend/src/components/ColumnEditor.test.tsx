import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ColumnEditor } from './ColumnEditor';

it('allows a schema to be defined with labeled keyboard-accessible controls', async () => {
  const change = vi.fn();
  const user = userEvent.setup();
  render(<ColumnEditor columns={[]} onChange={change} />);
  await user.click(screen.getByRole('button', { name: 'Add column' }));
  expect(change).toHaveBeenCalledWith([{ name: 'column1', type: 'string', nullable: false }]);
});

it('exposes decimal precision and scale instead of silently rounding values', () => {
  render(<ColumnEditor columns={[{ name: 'amount', type: 'decimal', nullable: true, precision: 12, scale: 4 }]} onChange={vi.fn()} />);
  expect(screen.getByLabelText('Precision')).toHaveValue(12);
  expect(screen.getByLabelText('Scale')).toHaveValue(4);
  expect(screen.getByLabelText('Nullable')).toBeChecked();
});

import { fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ColumnEditor } from './ColumnEditor';
import type { Column } from '../pipeline/types';

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

it('stores variable references for typed schema properties', () => {
  const change = vi.fn();
  let columns: Column[] = [{ name: 'amount', type: 'decimal', nullable: false }];
  const { rerender } = render(<ColumnEditor columns={columns} onChange={change} />);
  const edit = (property: string, label: string, value: string) => {
    fireEvent.click(within(screen.getByRole('group', { name: `${property} mode` })).getByRole('button', { name: 'Variable' }));
    columns = change.mock.calls.at(-1)![0] as Column[];
    rerender(<ColumnEditor columns={columns} onChange={change} />);
    fireEvent.change(screen.getByLabelText(label), { target: { value } });
    columns = change.mock.calls.at(-1)![0] as Column[];
    rerender(<ColumnEditor columns={columns} onChange={change} />);
  };

  edit('Type', 'Type variable', '${env.COLUMN_TYPE}');
  edit('Nullable', 'Nullable variable', '${env.COLUMN_NULLABLE}');
  edit('Precision', 'Precision variable', '${var.PRECISION}');
  edit('Scale', 'Scale variable', '${var.SCALE}');

  expect(change).toHaveBeenLastCalledWith([{
    name: 'amount', type: 'decimal', nullable: false,
    typeVariable: '${env.COLUMN_TYPE}', nullableVariable: '${env.COLUMN_NULLABLE}',
    precisionVariable: '${var.PRECISION}', scaleVariable: '${var.SCALE}',
  }]);
});

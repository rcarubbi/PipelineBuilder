import { fireEvent, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { EnvironmentEditor, parseEnvironments } from './EnvironmentEditor';

const initial = JSON.stringify({ development: { INPUT_FILE: 'customers.csv', OUTPUT_FILE: 'out.parquet' } }, null, 2);

function Harness({ json = initial, onChange = vi.fn() }: { json?: string; onChange?: (json: string, error: string | null) => void }) {
  const [state, setState] = useState(json);
  return <EnvironmentEditor json={state} onChange={(next, error) => { setState(next); onChange(next, error); }} />;
}
const lastJson = (onChange: ReturnType<typeof vi.fn>) => JSON.parse(onChange.mock.lastCall![0]);
afterEach(() => vi.restoreAllMocks());

it('shows environments and variables as a labelled form by default', () => {
  render(<Harness />);

  expect(screen.getByLabelText('Environment name')).toHaveValue('development');
  expect(screen.getByLabelText('Name of variable 1 in development')).toHaveValue('INPUT_FILE');
  expect(screen.getByLabelText('Value of INPUT_FILE in development')).toHaveValue('customers.csv');
  expect(screen.queryByLabelText('Variables by environment (JSON)')).not.toBeInTheDocument();
});

it('adds and edits variables, producing the same JSON the backend expects', async () => {
  const onChange = vi.fn();
  const user = userEvent.setup();
  render(<Harness onChange={onChange} />);

  await user.click(screen.getByRole('button', { name: 'Add variable to development' }));
  await user.type(screen.getByLabelText('Name of variable 3 in development'), 'ROOT');
  await user.type(screen.getByLabelText('Value of ROOT in development'), 'data/in');
  await user.clear(screen.getByLabelText('Value of INPUT_FILE in development'));
  await user.type(screen.getByLabelText('Value of INPUT_FILE in development'), 'orders.csv');

  expect(onChange).toHaveBeenLastCalledWith(expect.any(String), null);
  expect(lastJson(onChange)).toEqual({ development: { INPUT_FILE: 'orders.csv', OUTPUT_FILE: 'out.parquet', ROOT: 'data/in' } });
});

it('adds an environment pre-filled with the same variable names and removes variables', async () => {
  const onChange = vi.fn();
  const user = userEvent.setup();
  render(<Harness onChange={onChange} />);

  await user.click(screen.getByRole('button', { name: 'Add environment' }));
  await user.click(screen.getByRole('button', { name: 'Remove OUTPUT_FILE from development' }));

  expect(lastJson(onChange)).toEqual({ development: { INPUT_FILE: 'customers.csv' }, environment: { INPUT_FILE: '', OUTPUT_FILE: '' } });
});

it.each([
  ['an invalid variable name', 'Name of variable 1 in development', '1BAD', 'must start with a letter or underscore'],
  ['a duplicate variable name', 'Name of variable 2 in development', 'INPUT_FILE', 'must be unique'],
  ['an empty environment name', 'Environment name', '', 'Every environment needs a name.'],
])('reports %s and keeps the last valid JSON', async (_, field, value, message) => {
  const onChange = vi.fn();
  const user = userEvent.setup();
  render(<Harness onChange={onChange} />);

  await user.clear(screen.getByLabelText(field));
  if (value) await user.type(screen.getByLabelText(field), value);

  expect(screen.getByRole('alert')).toHaveTextContent(message);
  const [json, error] = onChange.mock.lastCall!;
  expect(error).toContain(message);
  expect(() => parseEnvironments(json)).not.toThrow();
  expect(screen.getByLabelText('Edit as JSON (advanced)')).toBeDisabled();
});

it('requires confirmation and keeps at least one environment', async () => {
  Object.defineProperty(HTMLDialogElement.prototype, 'showModal', { configurable: true, value(this: HTMLDialogElement) { this.setAttribute('open', ''); } });
  const onChange = vi.fn();
  const user = userEvent.setup();
  render(<Harness onChange={onChange} />);
  expect(screen.getByRole('button', { name: 'Remove environment development' })).toBeDisabled();

  await user.click(screen.getByRole('button', { name: 'Add environment' }));
  await user.click(screen.getByRole('button', { name: 'Remove environment development' }));
  expect(screen.getByRole('dialog', { name: 'Remove environment development?' })).toBeInTheDocument();
  await user.click(screen.getByRole('button', { name: 'Remove environment' }));
  expect(Object.keys(lastJson(onChange))).toEqual(['environment']);
  Reflect.deleteProperty(HTMLDialogElement.prototype, 'showModal');
});

it('switches to the JSON editor and back, keeping changes made in either mode', async () => {
  const onChange = vi.fn();
  const user = userEvent.setup();
  render(<Harness onChange={onChange} />);

  await user.click(screen.getByLabelText('Edit as JSON (advanced)'));
  const textarea = screen.getByLabelText('Variables by environment (JSON)');
  expect(JSON.parse((textarea as HTMLTextAreaElement).value)).toEqual(JSON.parse(initial));
  fireEvent.change(textarea, { target: { value: '{"test":{"INPUT_FILE":"t.csv"}}' } });
  await user.click(screen.getByLabelText('Edit as JSON (advanced)'));

  expect(screen.getByLabelText('Environment name')).toHaveValue('test');
  expect(screen.getByLabelText('Value of INPUT_FILE in test')).toHaveValue('t.csv');
});

it('stays in JSON mode with an explanation when the JSON is invalid', async () => {
  const onChange = vi.fn();
  const user = userEvent.setup();
  render(<Harness onChange={onChange} />);
  await user.click(screen.getByLabelText('Edit as JSON (advanced)'));

  fireEvent.change(screen.getByLabelText('Variables by environment (JSON)'), { target: { value: '{"test": ' } });
  expect(onChange).toHaveBeenLastCalledWith('{"test": ', 'Environments JSON is not valid JSON.');
  await user.click(screen.getByLabelText('Edit as JSON (advanced)'));

  expect(screen.getByLabelText('Variables by environment (JSON)')).toBeInTheDocument();
  expect(screen.getAllByRole('alert')[0]).toHaveTextContent('Fix the JSON before switching to the form.');
});

it('opens directly in JSON mode when saved environments cannot be shown as a form', () => {
  render(<Harness json="[]" />);

  expect(screen.getByLabelText('Variables by environment (JSON)')).toHaveValue('[]');
  expect(screen.getByRole('alert')).toHaveTextContent('named string-to-string dictionaries');
});

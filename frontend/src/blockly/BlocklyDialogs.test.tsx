import { act, fireEvent, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import * as Blockly from 'blockly/core';
import { BlocklyDialogs } from './BlocklyDialogs';

beforeEach(() => Blockly.setLocale({ DIALOG_CANCEL: 'Cancel', DIALOG_OK: 'OK' }));

it('submits a variable prompt in the shared dialog and restores Blockly focus', async () => {
  const callback = vi.fn();
  render(<BlocklyDialogs />);
  act(() => Blockly.dialog.prompt('New variable name:', 'item', callback));

  const dialog = screen.getByRole('dialog', { name: 'New variable name:' });
  expect(dialog).toHaveClass('ui-dialog-content');
  const input = screen.getByRole('textbox', { name: 'Variable name' });
  expect(input).toHaveFocus();
  expect(input).toHaveValue('item');
  expect(Blockly.getFocusManager().ephemeralFocusTaken()).toBe(true);
  await userEvent.keyboard('filename');
  fireEvent.submit(input.closest('form')!);

  expect(callback).toHaveBeenCalledExactlyOnceWith('filename');
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  await vi.waitFor(() => expect(Blockly.getFocusManager().ephemeralFocusTaken()).toBe(false));
});

it('cancels prompts with Escape and confirmations with Cancel', async () => {
  const callback = vi.fn();
  render(<BlocklyDialogs />);
  act(() => Blockly.dialog.prompt('Rename variable:', 'item', callback));
  await userEvent.keyboard('{Escape}');
  expect(callback).toHaveBeenCalledExactlyOnceWith(null);

  const confirm = vi.fn();
  act(() => Blockly.dialog.confirm('Delete variable?', confirm));
  await userEvent.click(screen.getByRole('button', { name: Blockly.Msg.DIALOG_CANCEL }));
  expect(confirm).toHaveBeenCalledExactlyOnceWith(false);
});

it('supports a follow-up alert and prompt from Blockly validation callbacks', async () => {
  render(<BlocklyDialogs />);
  const callback = vi.fn();
  act(() => Blockly.dialog.prompt('New variable name:', '', () => {
    Blockly.dialog.alert('Name already exists.', () => Blockly.dialog.prompt('New variable name:', 'other', callback));
  }));
  fireEvent.submit(screen.getByRole('textbox').closest('form')!);
  expect(screen.getByRole('dialog', { name: 'Name already exists.' })).toBeInTheDocument();
  await userEvent.click(screen.getByRole('button', { name: Blockly.Msg.DIALOG_OK }));
  expect(screen.getByRole('textbox')).toHaveValue('other');
  await userEvent.keyboard('{Escape}');
  expect(callback).toHaveBeenCalledExactlyOnceWith(null);
});

it('releases ephemeral focus when the editor unmounts', () => {
  const { unmount } = render(<BlocklyDialogs />);
  act(() => Blockly.dialog.prompt('New variable name:', '', vi.fn()));
  unmount();
  expect(Blockly.getFocusManager().ephemeralFocusTaken()).toBe(false);
});

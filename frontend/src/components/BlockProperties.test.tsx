import * as Blockly from 'blockly/core';
import { fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { BlockProperties } from './BlockProperties';

it('selects a discovered nested API data path and refreshes its schema sample', async () => {
  const workspace = new Blockly.Workspace();
  try {
    const block = workspace.newBlock('pipeline_apiInput');
    block.data = JSON.stringify({
      schema: [{ name: 'id', type: 'int64', nullable: false }],
      api: { dataPath: '', dataPaths: ['payload.items', 'result.rows'] },
    });
    const discover = vi.fn();
    const onChange = vi.fn();
    const { rerender } = render(<BlockProperties block={block} inputSchema={[]} onChange={onChange} onDiscover={discover} />);
    const user = userEvent.setup();

    await user.selectOptions(screen.getByLabelText('Discovered response paths'), 'payload.items');
    const methodMode = screen.getByRole('group', { name: 'Method mode' });
    expect(methodMode.closest('.variable-field')).toHaveAttribute('data-mode', 'value');
    await user.click(within(methodMode).getByRole('button', { name: 'Variable' }));
    rerender(<BlockProperties block={block} inputSchema={[]} onChange={onChange} onDiscover={discover} />);
    expect(methodMode.closest('.variable-field')).toHaveAttribute('data-mode', 'variable');
    fireEvent.change(screen.getByLabelText('Method variable'), { target: { value: '${var.API_METHOD}' } });
    await user.click(within(screen.getByRole('group', { name: 'Link rel=next header mode' })).getByRole('button', { name: 'Variable' }));
    rerender(<BlockProperties block={block} inputSchema={[]} onChange={onChange} onDiscover={discover} />);
    fireEvent.change(screen.getByLabelText('Link rel=next header variable'), { target: { value: '${env.LINK_HEADER}' } });

    expect(JSON.parse(block.data!).api).toMatchObject({
      dataPath: 'payload.items',
      methodVariable: '${var.API_METHOD}',
      useLinkHeaderVariable: '${env.LINK_HEADER}',
    });
    expect(discover).toHaveBeenCalledOnce();
  } finally {
    workspace.dispose();
  }
});

it('configures PostgreSQL connection fields and SQL through variable modes', async () => {
  const workspace = new Blockly.Workspace();
  try {
    const block = workspace.newBlock('pipeline_postgresInput');
    block.data = JSON.stringify({ schema: [], postgres: { host: 'localhost', port: '5432', sslMode: 'Prefer' } });
    const discover = vi.fn();
    const onChange = vi.fn();
    const { rerender } = render(<BlockProperties block={block} inputSchema={[]} onChange={onChange} onDiscover={discover} />);
    const user = userEvent.setup();

    await user.click(within(screen.getByRole('group', { name: 'Host mode' })).getByRole('button', { name: 'Variable' }));
    rerender(<BlockProperties block={block} inputSchema={[]} onChange={onChange} onDiscover={discover} />);
    fireEvent.change(screen.getByLabelText('Host variable'), { target: { value: '${env.PG_HOST}' } });
    fireEvent.change(screen.getByLabelText('Database'), { target: { value: 'customers' } });
    fireEvent.change(screen.getByLabelText('Username'), { target: { value: 'reader' } });
    fireEvent.change(screen.getByLabelText('SQL query'), { target: { value: 'SELECT id FROM customers' } });
    await user.click(screen.getByRole('button', { name: 'Run query and discover PostgreSQL schema' }));

    expect(JSON.parse(block.data!)).toMatchObject({ postgres: {
      hostVariable: '${env.PG_HOST}', database: 'customers', username: 'reader', query: 'SELECT id FROM customers',
    } });
    expect(discover).toHaveBeenCalledOnce();
  } finally { workspace.dispose(); }
});

import { act, render } from '@testing-library/react';
import * as Blockly from 'blockly/core';
import { BlockEditor } from './BlockEditor';

vi.mock('blockly/core', async importOriginal => {
  const actual = await importOriginal<typeof import('blockly/core') & { default?: typeof import('blockly/core') }>();
  return { ...actual.default, ...actual, inject: vi.fn(), svgResize: vi.fn() };
});
vi.mock('./workspace', () => ({ seedWorkspace: vi.fn(), toolbox: {} }));

describe('BlockEditor selection', () => {
  const addChangeListener = vi.fn<(listener: (event: Blockly.Events.Abstract) => void) => void>();

  beforeEach(() => {
    addChangeListener.mockClear();
    vi.mocked(Blockly.inject).mockReturnValue({
      addChangeListener,
      removeChangeListener: vi.fn(),
      dispose: vi.fn(),
    } as unknown as Blockly.WorkspaceSvg);
    vi.stubGlobal('ResizeObserver', class { observe() {} disconnect() {} });
  });
  afterEach(() => vi.unstubAllGlobals());

  it('keeps the inspected block when Blockly deselects it on external focus', () => {
    const onSelect = vi.fn();
    render(<BlockEditor onChange={vi.fn()} onSelect={onSelect} />);
    const listener = addChangeListener.mock.calls[0][0];

    act(() => listener(new Blockly.Events.Selected(null, 'csv', 'workspace')));
    act(() => listener(new Blockly.Events.Selected('csv', null, 'workspace')));

    expect(onSelect).toHaveBeenCalledExactlyOnceWith('csv');

    act(() => listener(new Blockly.Events.Selected(null, 'parquet', 'workspace')));
    expect(onSelect).toHaveBeenLastCalledWith('parquet');
  });

  it('still refreshes the app when blocks are deleted', () => {
    const onChange = vi.fn();
    render(<BlockEditor onChange={onChange} onSelect={vi.fn()} />);
    onChange.mockClear();

    act(() => addChangeListener.mock.calls[0][0](new Blockly.Events.BlockDelete()));

    expect(onChange).toHaveBeenCalledOnce();
  });

  it('keeps workspace scrollbars and canvas panning enabled', () => {
    render(<BlockEditor onChange={vi.fn()} onSelect={vi.fn()} />);

    expect(Blockly.inject).toHaveBeenCalledWith(expect.any(HTMLDivElement), expect.objectContaining({
      move: { scrollbars: true, drag: true, wheel: true },
    }));
  });
});

import { act, render, screen } from '@testing-library/react';
import { Toast } from './Toast';

afterEach(() => { vi.runOnlyPendingTimers(); vi.useRealTimers(); });

it('softly appears and dismisses after its timeout', () => {
  vi.useFakeTimers();
  render(<Toast>Saved successfully.</Toast>);
  const toast = screen.getByRole('status').parentElement!;

  expect(toast).not.toHaveClass('is-visible');
  act(() => vi.advanceTimersByTime(0));
  expect(toast).toHaveClass('is-visible');
  act(() => vi.advanceTimersByTime(5000));
  expect(toast).toHaveClass('is-exiting');
  act(() => vi.advanceTimersByTime(180));
  expect(screen.queryByRole('status')).not.toBeInTheDocument();
});

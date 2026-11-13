import { defaultSettings, describe, fromCron, toCron, validateSettings, type ScheduleSettings } from './cron';

const settings = (changes: Partial<ScheduleSettings>): ScheduleSettings => ({ ...defaultSettings, ...changes });

it.each([
  [settings({ frequency: 'minutes', interval: 1 }), '* * * * *', 'Every minute'],
  [settings({ frequency: 'minutes', interval: 15 }), '*/15 * * * *', 'Every 15 minutes'],
  [settings({ frequency: 'hourly', minute: 5 }), '5 * * * *', 'Every hour at minute 5'],
  [settings({ frequency: 'daily', hour: 8, minute: 30 }), '30 8 * * *', 'Every day at 08:30 UTC'],
  [settings({ frequency: 'weekly', hour: 7, minute: 0, days: [5, 1, 3, 2, 4] }), '0 7 * * 1,2,3,4,5', 'Every weekday at 07:00 UTC'],
  [settings({ frequency: 'weekly', hour: 9, minute: 15, days: [1, 3] }), '15 9 * * 1,3', 'Every Monday, Wednesday at 09:15 UTC'],
  [settings({ frequency: 'monthly', hour: 2, minute: 0, dayOfMonth: 28 }), '0 2 28 * *', 'On day 28 of every month at 02:00 UTC'],
])('converts %o to %s and describes it as "%s"', (input, cron, description) => {
  expect(toCron(input)).toBe(cron);
  expect(describe(cron)).toBe(description);
  expect(toCron(fromCron(cron)!)).toBe(cron);
});

it.each(['0 8 * 1 *', '0 8 1 * 1', '*/7 * * * *', '0 8 31 * *', '0-30 8 * * *', '0 8 * * 1,1', '0 0 8 * * *', 'bad'])(
  'treats %s as a custom expression instead of misrepresenting it', expression => {
    expect(fromCron(expression)).toBeNull();
    expect(describe(expression)).toBe('Custom cron schedule (UTC)');
  });

it('requires at least one day for weekly schedules', () => {
  expect(validateSettings(settings({ frequency: 'weekly', days: [] }))).toBe('Select at least one day.');
  expect(validateSettings(settings({ frequency: 'weekly', days: [0] }))).toBeNull();
});

/** Builds and recognises the five-field UTC cron expressions produced by the schedule editor. */
export type Frequency = 'minutes' | 'hourly' | 'daily' | 'weekly' | 'monthly';
export interface ScheduleSettings { frequency: Frequency; interval: number; minute: number; hour: number; days: number[]; dayOfMonth: number }

// Divisors of 60 only: "*/n" restarts every hour, so other steps would not be evenly spaced.
export const minuteIntervals = [1, 2, 3, 4, 5, 6, 10, 12, 15, 20, 30];
// Days 29-31 are excluded so a monthly schedule runs every month.
export const maxDayOfMonth = 28;
export const weekdays = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
export const defaultSettings: ScheduleSettings = { frequency: 'daily', interval: 15, minute: 0, hour: 8, days: [1, 2, 3, 4, 5], dayOfMonth: 1 };

const pad = (value: number) => String(value).padStart(2, '0');
export const formatTime = (hour: number, minute: number) => `${pad(hour)}:${pad(minute)}`;

export function toCron(settings: ScheduleSettings): string {
  const { minute, hour } = settings;
  switch (settings.frequency) {
    case 'minutes': return settings.interval === 1 ? '* * * * *' : `*/${settings.interval} * * * *`;
    case 'hourly': return `${minute} * * * *`;
    case 'daily': return `${minute} ${hour} * * *`;
    case 'weekly': return `${minute} ${hour} * * ${[...new Set(settings.days)].sort((a, b) => a - b).join(',')}`;
    case 'monthly': return `${minute} ${hour} ${settings.dayOfMonth} * *`;
  }
}

export function validateSettings(settings: ScheduleSettings): string | null {
  if (settings.frequency === 'weekly' && settings.days.length === 0) return 'Select at least one day.';
  return null;
}

/** Returns editor settings for expressions the editor can represent, or null for custom expressions. */
export function fromCron(expression: string): ScheduleSettings | null {
  const parts = expression.trim().split(/\s+/);
  if (parts.length !== 5) return null;
  const [minuteField, hourField, dayField, monthField, weekdayField] = parts;
  const number = (value: string, min: number, max: number) => /^\d{1,2}$/.test(value) && +value >= min && +value <= max ? +value : null;
  if (monthField !== '*') return null;
  const anyDay = dayField === '*' && weekdayField === '*';
  if (hourField === '*' && anyDay) {
    if (minuteField === '*') return { ...defaultSettings, frequency: 'minutes', interval: 1 };
    const step = /^\*\/(\d{1,2})$/.exec(minuteField);
    if (step) return minuteIntervals.includes(+step[1]) ? { ...defaultSettings, frequency: 'minutes', interval: +step[1] } : null;
  }
  const minute = number(minuteField, 0, 59);
  if (minute === null) return null;
  if (hourField === '*') return anyDay ? { ...defaultSettings, frequency: 'hourly', minute } : null;
  const hour = number(hourField, 0, 23);
  if (hour === null) return null;
  if (anyDay) return { ...defaultSettings, frequency: 'daily', minute, hour };
  if (dayField === '*') {
    const days = weekdayField.split(',').map(day => number(day, 0, 6));
    if (days.some(day => day === null) || new Set(days).size !== days.length) return null;
    return { ...defaultSettings, frequency: 'weekly', minute, hour, days: (days as number[]).sort((a, b) => a - b) };
  }
  const dayOfMonth = weekdayField === '*' ? number(dayField, 1, maxDayOfMonth) : null;
  return dayOfMonth === null ? null : { ...defaultSettings, frequency: 'monthly', minute, hour, dayOfMonth };
}

export function describe(expression: string): string {
  const settings = fromCron(expression);
  if (!settings) return 'Custom cron schedule (UTC)';
  const at = `at ${formatTime(settings.hour, settings.minute)} UTC`;
  switch (settings.frequency) {
    case 'minutes': return settings.interval === 1 ? 'Every minute' : `Every ${settings.interval} minutes`;
    case 'hourly': return `Every hour at minute ${settings.minute}`;
    case 'daily': return `Every day ${at}`;
    case 'weekly': {
      const key = settings.days.join(',');
      const days = key === '1,2,3,4,5' ? 'weekday' : key === '0,6' ? 'Saturday and Sunday' : settings.days.map(day => weekdays[day]).join(', ');
      return `Every ${days} ${at}`;
    }
    case 'monthly': return `On day ${settings.dayOfMonth} of every month ${at}`;
  }
}

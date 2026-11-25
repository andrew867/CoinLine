export const discountZones = ['local', 'intra', 'inter', 'international'] as const
export type DiscountZone = typeof discountZones[number]
export type DiscountPayment = 'coin' | 'card'
export type DiscountValue = (key: string) => string | null | undefined

const days = ['Weekdays', 'Saturday', 'Sunday', 'Holidays'] as const
type Day = typeof days[number]
type Window = { start: number; end: number }
export type DiscountPeriod = { number: 1 | 2; window: Window | null }

function unsigned(value: string | null | undefined, maximum: number): number | null {
  if (value == null || !/^\d+$/.test(value)) return null
  const parsed = Number(value)
  return Number.isSafeInteger(parsed) && parsed <= maximum ? parsed : null
}

function dateParts(date: string) {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(date) || date.startsWith('0000')) return null
  const parsed = new Date(`${date}T00:00:00Z`)
  if (Number.isNaN(parsed.getTime()) || parsed.toISOString().slice(0, 10) !== date) return null
  return { month: parsed.getUTCMonth() + 1, day: parsed.getUTCDate(), weekday: parsed.getUTCDay() }
}

function timeMinutes(time: string) {
  if (!/^\d{2}:\d{2}$/.test(time)) return null
  const [hour, minute] = time.split(':').map(Number)
  return hour < 24 && minute < 60 ? hour * 60 + minute : null
}

function windowAt(value: DiscountValue, index: number): Window | null {
  const prefix = `period-${index}`
  const sh = unsigned(value(`${prefix}.startHour`), 23), sm = unsigned(value(`${prefix}.startMinute`), 59)
  const eh = unsigned(value(`${prefix}.endHour`), 24), em = unsigned(value(`${prefix}.endMinute`), 59)
  if (sh === null || sm === null || eh === null || em === null || (eh === 24 && em !== 0)) return null
  return { start: sh * 60 + sm, end: eh * 60 + em }
}

function contains(window: Window, minute: number) {
  if (window.start === window.end) return false
  return window.start < window.end ? minute >= window.start && minute < window.end
    : minute >= window.start || minute < window.end
}

function overlaps(first: Window | null, second: Window | null) {
  if (!first || !second) return false
  for (let minute = 0; minute < 1440; minute++) if (contains(first, minute) && contains(second, minute)) return true
  return false
}

export function formatDiscountWindow(window: Window | null) {
  if (window === null) return 'Invalid or unavailable time'
  const format = (minute: number) => `${String(Math.floor(minute / 60)).padStart(2, '0')}:${String(minute % 60).padStart(2, '0')}`
  const text = `${format(window.start)} to ${format(window.end)}`
  return `${text}${window.start === window.end ? ' (inactive)' : window.start > window.end ? ' (overnight)' : ''}`
}

type Preview = { kind: 'unavailable'; message: string } | {
  kind: 'calendar'; day: Day; holidaySlots: number[]; invalidHolidaySlots: number[]; periods: [DiscountPeriod, DiscountPeriod]; overlap: boolean;
  selection: { kind: 'none' | 'unavailable'; message: string } | {
    kind: 'matched'; period: 1 | 2; discountIndex: number | null; paymentMask: number | null; eligible: boolean | null
  }
}

export function previewTimeDiscount(value: DiscountValue, date: string, time: string, zone: DiscountZone, payment: DiscountPayment): Preview {
  const calendar = dateParts(date), minute = timeMinutes(time)
  if (!calendar || minute === null) return { kind: 'unavailable', message: 'Choose a valid date and time on the terminal’s local clock.' }
  const holidaySlots: number[] = [], invalidHolidaySlots: number[] = []
  const monthDays = [31, 29, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31]
  for (let index = 0; index < 10; index++) {
    const month = unsigned(value(`holiday-${index}.month`), 255), day = unsigned(value(`holiday-${index}.day`), 255)
    if (month === null || day === null) return { kind: 'unavailable', message: 'The holiday list is incomplete or an edited value is not a whole byte.' }
    if (month === 0 && day === 0) continue
    if (month < 1 || month > 12 || day < 1 || day > monthDays[month - 1]) { invalidHolidaySlots.push(index + 1); continue }
    if (month === calendar.month && day === calendar.day) holidaySlots.push(index + 1)
  }
  const dayIndex = holidaySlots.length > 0 ? 3 : calendar.weekday === 0 ? 2 : calendar.weekday === 6 ? 1 : 0
  const first = windowAt(value, dayIndex * 2), second = windowAt(value, dayIndex * 2 + 1)
  const periods: [DiscountPeriod, DiscountPeriod] = [{ number: 1, window: first }, { number: 2, window: second }]
  const result = { kind: 'calendar' as const, day: days[dayIndex], holidaySlots, invalidHolidaySlots, periods, overlap: overlaps(first, second) }
  // Selection is by time first. A payment mismatch does not advance to period 2.
  for (const period of periods) {
    if (!period.window) return { ...result, selection: { kind: 'unavailable', message: `Period ${period.number} has an invalid or unavailable time; review it before interpreting this preview.` } }
    if (!contains(period.window, minute)) continue
    const prefix = `period-${dayIndex * 2 + period.number - 1}.${zone}`
    const discountIndex = unsigned(value(`${prefix}Index`), 15)
    const paymentMask = unsigned(value(`${prefix}Payment`), 3)
    const enabled = paymentMask === null ? null : (paymentMask & (payment === 'coin' ? 1 : 2)) !== 0
    const eligible = enabled === false ? false : enabled === true && discountIndex !== null && discountIndex < 10 ? true : null
    return { ...result, selection: { kind: 'matched', period: period.number, discountIndex, paymentMask, eligible } }
  }
  return { ...result, selection: { kind: 'none', message: 'No discount period matches this date and time.' } }
}

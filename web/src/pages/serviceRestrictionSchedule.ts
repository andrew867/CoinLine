const daySeconds = 24 * 60 * 60

function clock(seconds: number) {
  const hours = Math.floor(seconds / 3600)
  const minutes = Math.floor(seconds % 3600 / 60)
  const sec = seconds % 60
  return `${String(hours).padStart(2, '0')}:${String(minutes).padStart(2, '0')}${sec ? `:${String(sec).padStart(2, '0')}` : ''}`
}

export function describeServiceRestriction(values: Record<string, string | null>, later: boolean): string {
  const parse = (key: string) => {
    const value = values[key]
    return value != null && /^\d+$/.test(value) ? Number(value) : NaN
  }
  const level = parse('restriction')
  if (level === 0) return 'No restrictions are scheduled.'
  if (!Number.isInteger(level) || level < 1 || level > (later ? 4 : 3)) return 'Select a restriction level supported by this firmware.'
  const hour = parse('startHour'), minute = parse('startMinute')
  const durationHours = parse('durationHours'), durationMinutes = parse('durationMinutes')
  if (![hour, minute, durationHours, durationMinutes].every(Number.isInteger) || hour < 0 || hour > 23 || minute < 0 || minute > 59 ||
    durationHours < 0 || durationHours > 24 || durationMinutes < 0 || durationMinutes > 59 || (durationHours === 24 && durationMinutes !== 0))
    return 'Enter a valid start time and a duration of at most 24 hours.'
  const start = (hour * 60 + minute) * 60
  const duration = (durationHours * 60 + durationMinutes) * 60
  if (duration === 0) return 'A restriction needs a positive duration.'
  const stop = start + Math.min(duration, daySeconds - 1)
  const nextDay = stop >= daySeconds
  const range = `Every day, ${clock(start)} to ${clock(stop % daySeconds)}${nextDay ? ' the next day' : ''} (phone local time).`
  return duration === daySeconds ? `${range} Firmware caps a 24-hour duration at 23 hours, 59 minutes and 59 seconds.` : range
}

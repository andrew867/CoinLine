type ReadValue = (key: string) => string | null | undefined

function number(value: string | null | undefined) {
  return value != null && /^\d+$/.test(value) ? Number(value) : NaN
}

function time(hour: number, minute: number) {
  return Number.isInteger(hour) && hour >= 0 && hour < 24 && Number.isInteger(minute) && minute >= 0 && minute < 60
    ? `${String(hour).padStart(2, '0')}:${String(minute).padStart(2, '0')}` : null
}

export function summaryCollection(value: ReadValue) {
  const days = number(value('days'))
  if (days === 0) return 'Collection disabled (zero days).'
  const start = time(number(value('startHour')), number(value('startMinute')))
  const stamps = Array.from({ length: 4 }, (_, i) => time(number(value(`timestamp-${i}.hour`)), number(value(`timestamp-${i}.minute`))))
  if (!start || !Number.isInteger(days) || days < 1 || stamps.some(stamp => stamp == null)) return 'Review the collection time and report timestamps.'
  const checkpoints = stamps.filter(stamp => stamp !== '00:00').join(', ')
  if (stamps[0]! > start) {
    return `Daily checkpoints at ${start}, ${checkpoints} (phone local time).${days > 1 ? ' The timestamps override the multi-day interval.' : ''}`
  }
  return `Every ${days === 1 ? 'day' : `${days} days`} starting at ${start} (phone local time).${checkpoints ? ` Additional timestamps ${checkpoints} are evaluated in their stored order; review the sequence.` : ''}`
}

export function optionalCdrCollection(value: ReadValue) {
  const mode = number(value('enabled'))
  if (mode === 0) return 'Normal call records only; the phone uses the MT35 threshold.'
  if (![1, 4, 8, 16].includes(mode)) return 'Imported combination of optional CDR filters. Review the stored mode.'
  const start = time(number(value('detailHour')), number(value('detailMinute')))
  const days = number(value('detailDays')), hours = number(value('detailHours'))
  if (!start || !Number.isInteger(days) || days < 0 || !Number.isInteger(hours) || hours < 0 || hours > 23) return 'Review the optional logging window.'
  const duration = days === 0 && hours === 0 ? '1 day (firmware default)' : [days ? `${days} day${days === 1 ? '' : 's'}` : '', hours ? `${hours} hour${hours === 1 ? '' : 's'}` : ''].filter(Boolean).join(', ')
  return `One logging window from ${start} on the phone’s table-receipt day, for ${duration}. Outside it the phone uses the MT35 threshold.`
}

export function thresholdDescription(value: string | null | undefined) {
  const count = number(value)
  return count === 0 ? 'No count-triggered call-in' : Number.isInteger(count) && count > 0 ? `${count} stored record${count === 1 ? '' : 's'}` : 'Review threshold'
}

export function carrierReportLabel(messageType: number, reference: number) {
  if (messageType === 71 && reference === 253) return 'Combined carrier statistics (reference 253)'
  if (reference === 255) return 'Other carriers (reference 255)'
  return `Carrier reference ${reference}`
}

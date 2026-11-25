import { expect, it } from 'vitest'
import { carrierReportLabel, optionalCdrCollection, summaryCollection, thresholdDescription } from './collectionPolicy'

const fields: Record<string, string> = {
  days: '3', startHour: '2', startMinute: '15', 'timestamp-0.hour': '6', 'timestamp-0.minute': '0',
  'timestamp-1.hour': '12', 'timestamp-1.minute': '0', 'timestamp-2.hour': '0', 'timestamp-2.minute': '0',
  'timestamp-3.hour': '0', 'timestamp-3.minute': '0', enabled: '4', detailHour: '20', detailMinute: '0', detailDays: '0', detailHours: '6',
}

it('shows that sequential checkpoints take precedence over a multi-day interval', () => {
  expect(summaryCollection(key => fields[key])).toBe('Daily checkpoints at 02:15, 06:00, 12:00 (phone local time). The timestamps override the multi-day interval.')
  expect(summaryCollection(key => ({ ...fields, days: '0' } as Record<string, string>)[key])).toBe('Collection disabled (zero days).')
  const noStamps = { ...fields, 'timestamp-0.hour': '0', 'timestamp-1.hour': '0' } as Record<string, string>
  expect(summaryCollection(key => noStamps[key])).toBe('Every 3 days starting at 02:15 (phone local time).')
})

it('explains the optional CDR window, threshold meaning, and combined-carrier report reference', () => {
  expect(optionalCdrCollection(key => fields[key])).toContain('for 6 hours')
  expect(optionalCdrCollection(key => ({ ...fields, enabled: '0' } as Record<string, string>)[key])).toContain('MT35 threshold')
  expect(optionalCdrCollection(key => ({ ...fields, detailHours: '0' } as Record<string, string>)[key])).toContain('1 day (firmware default)')
  expect(optionalCdrCollection(key => ({ ...fields, enabled: '5' } as Record<string, string>)[key])).toContain('Imported combination')
  expect(thresholdDescription('0')).toBe('No count-triggered call-in')
  expect(thresholdDescription('2')).toBe('2 stored records')
  expect(carrierReportLabel(71, 253)).toBe('Combined carrier statistics (reference 253)')
  expect(carrierReportLabel(57, 253)).toBe('Carrier reference 253')
})

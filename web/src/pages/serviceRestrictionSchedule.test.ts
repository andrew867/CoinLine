import { expect, it } from 'vitest'
import { describeServiceRestriction } from './serviceRestrictionSchedule'

const window = { restriction: '4', startHour: '20', startMinute: '30', durationHours: '6', durationMinutes: '15' }

it('previews a daily window across midnight in the phone clock', () => {
  expect(describeServiceRestriction(window, true)).toBe('Every day, 20:30 to 02:45 the next day (phone local time).')
  expect(describeServiceRestriction({ ...window, durationHours: '1', durationMinutes: '0' }, true)).toBe('Every day, 20:30 to 21:30 (phone local time).')
  expect(describeServiceRestriction({ ...window, startHour: '0', startMinute: '0', durationHours: '24', durationMinutes: '0' }, true)).toContain('00:00 to 23:59:59')
  expect(describeServiceRestriction({ ...window, durationHours: '24', durationMinutes: '0' }, true)).toContain('20:30 to 20:29:59 the next day')
})

it('distinguishes disabled, unsupported and invalid schedules from an active window', () => {
  expect(describeServiceRestriction({ ...window, restriction: '0', startHour: '255' }, false)).toBe('No restrictions are scheduled.')
  expect(describeServiceRestriction(window, false)).toContain('supported by this firmware')
  expect(describeServiceRestriction({ ...window, startMinute: '60' }, true)).toContain('valid start time')
  expect(describeServiceRestriction({ ...window, durationHours: '24' }, true)).toContain('at most 24 hours')
  expect(describeServiceRestriction({ ...window, durationHours: '0', durationMinutes: '0' }, true)).toContain('positive duration')
})

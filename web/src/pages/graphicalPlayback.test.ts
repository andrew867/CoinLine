import { expect, it } from 'vitest'
import {
  advanceGraphicalPlayback, describeGraphicalAddress, graphicalFrame, newGraphicalPlayback,
  readGraphicalRecords, resolveGraphicalScreen,
} from './graphicalPlayback'

function record(number: number, values: Record<string, string>, edits?: Record<string, string>) {
  return { number, edits, sections: [{ fields: Object.entries(values).map(([key, value]) => ({ key, value })) }] }
}

it('follows context links across all three records and reads the addressed mixed panel and its keys', () => {
  const model = readGraphicalRecords([
    record(131, {
      onHook: 'true', offHook: 'true', connected: 'true', idleNavigation: 'true', connectedNavigation: 'false',
      offHookScreen: '72', connectedScreen: '16', offHookDuration: '4',
      'screen-0.kind': '84', 'screen-0.text': 'Original', 'screen-0.dwell': '125',
      'screen-0.enter-0': '0', 'screen-0.next': '72',
    }, { 'screen-0.text': 'Edited preview' }),
    record(132, {
      'screen-0.kind': '77', 'screen-0.panel-0.line-0': 'First panel',
      'screen-0.panel-1.line-0': 'Second panel', 'screen-0.panel-1.enter-0': '0',
      'screen-0.panel-1.label-0': 'Go', 'screen-0.panel-1.button-0.next': '16',
    }),
    record(133, { 'screen-0.kind': '66', 'screen-0.enter-0': '0', 'screen-0.dwell': '100', 'screen-0.connected': '72' }),
  ])

  expect(model.header?.offHook).toEqual({ enabled: true, navigation: true, start: 72 })
  expect(model.header?.connected?.start).toBe(16)
  expect(model.offHookHoldMs).toBe(4000)
  const first = newGraphicalPlayback(0)
  expect(graphicalFrame(model, 'onHook', first).screen?.text).toBe('Edited preview')
  expect(advanceGraphicalPlayback(model, 'onHook', first, 1249).address).toBe(0)
  const second = advanceGraphicalPlayback(model, 'onHook', first, 1250)
  expect(describeGraphicalAddress(second.address, resolveGraphicalScreen(model, second.address))).toBe('Screen 9 · Panel 2')
  expect(resolveGraphicalScreen(model, second.address)?.lines[0]).toBe('Second panel')
  expect(resolveGraphicalScreen(model, second.address)?.keys[0]).toEqual({ label: 'Go', digits: null, next: 16 })
  expect(advanceGraphicalPlayback(model, 'connected', newGraphicalPlayback(16), 1000).address).toBe(72)
})

it('previews phases, flashing, and the off-hook hold without writing records', () => {
  const model = readGraphicalRecords([record(131, {
    'screen-0.kind': '84', 'screen-0.text': 'Primary', 'screen-0.dwell': '250',
    'screen-0.enter-0': '2', 'screen-0.exit-0': '0', 'screen-0.enter-1': '3', 'screen-0.flash': '17',
    'screen-0.next': '1', 'screen-1.kind': '84', 'screen-1.text': 'Alternate', 'screen-1.enter-0': '0',
  })])
  const start = newGraphicalPlayback(0)
  expect(graphicalFrame(model, 'onHook', { ...start, elapsed: 1000 }).screen?.text).toBe('Alternate')
  expect(graphicalFrame(model, 'onHook', { ...start, elapsed: 2000 }).screen?.text).toBe('Primary')
  const secondPhase = advanceGraphicalPlayback(model, 'onHook', start, 2500)
  expect(secondPhase.phase).toBe(1)
  expect(graphicalFrame(model, 'onHook', secondPhase).x).toBe(-1)
  const hold = { ...start, hold: { remaining: 500, next: 1 } }
  expect(advanceGraphicalPlayback(model, 'offHook', hold, 499).address).toBe(0)
  expect(advanceGraphicalPlayback(model, 'offHook', hold, 500).address).toBe(1)

  const disabledExit = readGraphicalRecords([record(131, {
    'screen-0.kind': '84', 'screen-0.dwell': '100', 'screen-0.enter-0': '0',
    'screen-0.exit-0': '255', 'screen-0.enter-1': '0', 'screen-0.next': '1',
  })])
  expect(advanceGraphicalPlayback(disabledExit, 'onHook', start, 1000).address).toBe(1)
})

import { describe, expect, it } from 'vitest'
import {
  advertisingFrame, advertisingPage, compileAdvertisingPlayback, firmwareAdvertisingTiming,
  type AdvertisingFeatures, type AdvertisingSlot,
} from './advertisingPlayback'

const features: AdvertisingFeatures = {
  onHookEnabled: true, callEstablishedEnabled: false, quickAccessEnabled: false, scrollTicks: 2,
}

function slot(text: string, options: Partial<AdvertisingSlot> = {}): AdvertisingSlot {
  return { text, mode: 0, durationTicks: 60, blankBefore: false, endSequence: false, ...options }
}

function onHook(slots: AdvertisingSlot[]) {
  return compileAdvertisingPlayback(advertisingPage(slots, features, 'onHook'), firmwareAdvertisingTiming(features.scrollTicks))
}

describe('advertising playback', () => {
  it('splits both off-hook contexts at slot 11 when the respective feature is enabled', () => {
    const slots = Array.from({ length: 20 }, (_, index) => slot(`${index + 1}`))
    expect(advertisingPage(slots, features, 'onHook').active).toHaveLength(20)
    expect(advertisingPage(slots, features, 'callEstablished').active).toHaveLength(0)
    const split = { ...features, callEstablishedEnabled: true, quickAccessEnabled: true }
    expect(advertisingPage(slots, split, 'onHook').active).toHaveLength(10)
    expect(advertisingPage(slots, split, 'callEstablished').active[0].text).toBe('11')
    expect(advertisingPage(slots, split, 'quickAccess').active[0].text).toBe('11')
    expect(advertisingPage(slots, { ...split, onHookEnabled: false }, 'onHook').active).toHaveLength(0)
  })

  it('rotates at a terminator and retains an explicit timed blank', () => {
    const playback = onHook([slot('A'), slot(''), slot('', { endSequence: true }), slot('B')])
    expect(advertisingFrame(playback, 99).slot).toBeNull()
    expect(advertisingFrame(playback, 100).text.trim()).toBe('A')
    expect(advertisingFrame(playback, 160)).toMatchObject({ slot: 1, text: ' '.repeat(20) })
    expect(advertisingFrame(playback, 220)).toMatchObject({ slot: 0 })
  })

  it('alternates with the next text and preserves a zero-duration static slot as an alternate only', () => {
    const playback = onHook([slot('First', { mode: 2, durationTicks: 200 }), slot('Second', { durationTicks: 0 }), slot('Third')])
    expect(advertisingFrame(playback, 100).text.trim()).toBe('First')
    expect(advertisingFrame(playback, 225).text.trim()).toBe('')
    expect(advertisingFrame(playback, 240)).toMatchObject({ slot: 0, text: 'Second'.padEnd(20) })
    expect(advertisingFrame(playback, 366).text.trim()).toBe('Third')
  })

  it('uses MT30 scroll timing and honors blank-before', () => {
    const beginning = slot('A'.repeat(20))
    const scroll = slot('B'.repeat(20), { mode: 5 })
    const playback = onHook([beginning, scroll])
    expect(advertisingFrame(playback, 160).text).toBe('A'.repeat(19) + 'B')
    expect(advertisingFrame(playback, 162).text).toBe('A'.repeat(18) + 'BB')
    expect(advertisingFrame(playback, 200).text).toBe('B'.repeat(20))
    expect(advertisingFrame(playback, 260).text).toBe('B'.repeat(19) + ' ')
    expect(advertisingFrame(onHook([beginning, { ...scroll, blankBefore: true }]), 160).text).toBe(' '.repeat(19) + 'B')
  })
})

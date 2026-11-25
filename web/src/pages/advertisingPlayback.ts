import { normalizedAdvertisingText, advertisingDisplayWidth } from './advertisingGlyphs'

export type AdvertisingSlot = {
  text: string
  durationTicks: number
  mode: number
  blankBefore: boolean
  endSequence: boolean
  spareByte?: number | null
  reservedFlags?: number | null
  containsUnmappedGlyphs?: boolean
}

export type AdvertisingFeatures = {
  onHookEnabled: boolean | null
  quickAccessEnabled: boolean | null
  callEstablishedEnabled: boolean | null
  scrollTicks: number | null
}

export type AdvertisingContext = 'onHook' | 'callEstablished' | 'quickAccess'

export type AdvertisingTiming = {
  start: number
  scroll: number
  flashOn: number
  flashOff: number
  alternateGap: number
}

type Phase = { text: string; ticks: number }
type Stage = { slot: number; phases: Phase[]; ticks: number; period: number }
type Rotation = { stages: Stage[]; ticks: number; lastLine: string }

const width = advertisingDisplayWidth
export const blankAdvertisingLine = ' '.repeat(width)

export function advertisingLine(text: string) {
  return normalizedAdvertisingText(text).slice(0, width).padEnd(width)
}

export function firmwareAdvertisingTiming(scrollTicks: number | null): AdvertisingTiming {
  return { start: 100, scroll: scrollTicks && scrollTicks > 0 ? scrollTicks : 10, flashOn: 125, flashOff: 25, alternateGap: 15 }
}

export const maximizerAdvertisingTiming: AdvertisingTiming = {
  start: 0, scroll: 5, flashOn: 10, flashOff: 10, alternateGap: 3,
}

export function advertisingPage(slots: AdvertisingSlot[], features: AdvertisingFeatures, context: AdvertisingContext) {
  let unavailable: string | null = null
  if (context === 'onHook' && features.onHookEnabled === false)
    unavailable = 'On-hook advertising is disabled in MT26.'
  if (context === 'callEstablished' && !features.callEstablishedEnabled)
    unavailable = 'Call-established advertising requires the MT26 setting.'
  if (context === 'quickAccess' && !features.quickAccessEnabled)
    unavailable = 'Quick-access advertising requires the MT26 setting.'

  const split = features.callEstablishedEnabled || features.quickAccessEnabled
  const start = context === 'onHook' ? 0 : 10
  const end = context === 'onHook' && split ? 10 : 20
  const page = slots.slice(start, end)
  const terminator = page.findIndex((slot) => slot.endSequence)
  const active = terminator < 0 ? page : page.slice(0, terminator)
  return {
    start,
    active: unavailable ? [] : active,
    unavailable: unavailable ?? (active.length === 0 ? `Message ${start + 1} ends this sequence.` : null),
  }
}

function repeatToPhaseEnd(phases: Phase[], duration: number) {
  const period = phases.reduce((total, phase) => total + phase.ticks, 0)
  const cycles = Math.floor((duration - 1) / period)
  let ticks = cycles * period
  for (const phase of phases) {
    ticks += phase.ticks
    if (ticks >= duration) break
  }
  return ticks
}

function rotation(slots: AdvertisingSlot[], start: number, timing: AdvertisingTiming, initialLine: string): Rotation {
  let line = initialLine
  let ticks = 0
  const stages = slots.map((slot, index) => {
    const phases: Phase[] = []
    const message = advertisingLine(slot.text)
    const dwell = Math.max(1, slot.durationTicks)
    if (slot.blankBefore) line = blankAdvertisingLine

    function show(text: string, length: number) {
      line = text
      phases.push({ text, ticks: length })
    }

    function scroll(text: string) {
      for (const character of text) show(line.slice(1) + character, timing.scroll)
    }

    switch (slot.mode) {
      case 0:
        show(slot.durationTicks > 0 ? message : line, dwell)
        break
      case 1:
        show(message, timing.flashOn)
        show(blankAdvertisingLine, timing.flashOff)
        break
      case 2:
        show(message, timing.flashOn)
        show(blankAdvertisingLine, timing.alternateGap)
        show(advertisingLine(slots[(index + 1) % slots.length].text), timing.flashOn)
        show(blankAdvertisingLine, timing.alternateGap)
        break
      case 3:
      case 4:
      case 5:
        if (slot.mode !== 4) scroll(message)
        show(message, dwell)
        if (slot.mode !== 3) scroll(blankAdvertisingLine)
        break
      default:
        show(line, 1)
        break
    }

    const period = phases.reduce((total, phase) => total + phase.ticks, 0)
    const flashing = slot.mode === 1 || slot.mode === 2
    const length = flashing ? repeatToPhaseEnd(phases, dwell) : period
    if (flashing) line = stageLine(phases, (length - 1) % period)
    ticks += length
    return { slot: start + index, phases, ticks: length, period }
  })
  return { stages, ticks, lastLine: line }
}

function stageLine(phases: Phase[], elapsed: number) {
  for (const phase of phases) {
    if (elapsed < phase.ticks) return phase.text
    elapsed -= phase.ticks
  }
  return blankAdvertisingLine
}

export function compileAdvertisingPlayback(page: ReturnType<typeof advertisingPage>, timing: AdvertisingTiming) {
  const first = rotation(page.active, page.start, timing, blankAdvertisingLine)
  const loop = rotation(page.active, page.start, timing, first.lastLine)
  return { start: timing.start, first, loop }
}

export function advertisingFrame(playback: ReturnType<typeof compileAdvertisingPlayback>, elapsedTicks: number) {
  let time = elapsedTicks - playback.start
  if (time < 0 || playback.first.ticks === 0) return { text: blankAdvertisingLine, slot: null }
  const current = time < playback.first.ticks ? playback.first : playback.loop
  if (current === playback.loop) time = (time - playback.first.ticks) % playback.loop.ticks
  for (const stage of current.stages) {
    if (time < stage.ticks) return { text: stageLine(stage.phases, time % stage.period), slot: stage.slot }
    time -= stage.ticks
  }
  return { text: blankAdvertisingLine, slot: null }
}

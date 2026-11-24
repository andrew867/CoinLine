export const graphicalRecords = [131, 132, 133] as const
const screensPerRecord = 8
const screenCount = 24
const noScreen = 255
const tickMs = 10

export type GraphicalContext = 'onHook' | 'offHook' | 'connected'
type GraphicalField = { key: string; value: string | null }
export type GraphicalRecord = {
  number: number
  sections: readonly { fields: readonly GraphicalField[] }[]
  edits?: Readonly<Record<string, string>>
}
type ChoiceKey = { label: string | null; digits: string | null; next: number }
type Phase = { entrance: number; exit: number }
export type GraphicalScreen = {
  kind: 'bitmap' | 'text' | 'mixed'
  bitmap: string | null
  text: string | null
  lines: (string | null)[]
  keys: ChoiceKey[]
  dwell: number
  flash: number
  next: number
  connected: number
  phases: Phase[]
}
type Model = {
  header: Record<GraphicalContext, { enabled: boolean; navigation: boolean; start: number }> | null
  offHookHoldMs: number
  screens: Map<number, GraphicalScreen>
  changedKind: boolean
}
export type Playback = {
  address: number
  phase: number
  elapsed: number
  history: number[]
  hold?: { remaining: number; next: number }
}

const screenKind = { 66: 'bitmap', 84: 'text', 77: 'mixed' } as const

export function readGraphicalRecords(records: GraphicalRecord[]): Model {
  let header: Model['header'] = null
  let offHookHoldMs = 0
  let changedKind = false
  const screens = new Map<number, GraphicalScreen>()
  for (const record of records) {
    const fields = new Map(record.sections.flatMap(section => section.fields.map(field => [field.key, field.value] as const)))
    const value = (key: string) => record.edits?.[key] ?? fields.get(key) ?? null
    const number = (key: string, fallback = 0) => {
      const result = Number(value(key) ?? fallback)
      return Number.isFinite(result) && result >= 0 ? result : fallback
    }
    const enabled = (key: string) => value(key) === 'true'
    if (record.number === 131) {
      header = {
        onHook: { enabled: enabled('onHook'), navigation: enabled('idleNavigation'), start: 0 },
        offHook: { enabled: enabled('offHook'), navigation: enabled('idleNavigation'), start: number('offHookScreen', noScreen) },
        connected: { enabled: enabled('connected'), navigation: enabled('connectedNavigation'), start: number('connectedScreen', noScreen) },
      }
      offHookHoldMs = number('offHookDuration') * 1000
    }
    for (let index = 0; index < screensPerRecord; index++) {
      const key = `screen-${index}`
      const original = fields.get(`${key}.kind`) ?? null
      if (value(`${key}.kind`) !== original) {
        changedKind = true
        continue
      }
      const kind = screenKind[Number(original) as keyof typeof screenKind]
      if (!kind) continue
      const screen = (record.number - 131) * screensPerRecord + index
      for (let panel = 0; panel < (kind === 'mixed' ? 4 : 1); panel++) {
        const prefix = kind === 'mixed' ? `${key}.panel-${panel}` : key
        const phases: Phase[] = []
        for (let phase = 0; phase < 4; phase++) {
          const entrance = number(`${prefix}.enter-${phase}`, noScreen)
          if (entrance > 14) break
          phases.push({ entrance, exit: number(`${prefix}.exit-${phase}`) })
        }
        screens.set(screen | (panel << 6), {
          kind,
          bitmap: value(`${key}.bitmap`),
          text: value(`${key}.text`),
          lines: Array.from({ length: 4 }, (_, line) => value(`${prefix}.line-${line}`)),
          keys: Array.from({ length: 6 }, (_, button) => {
            const buttonKey = kind === 'mixed' ? `${prefix}.button-${button}` : `button-${index * 6 + button}`
            return { label: value(`${prefix}.label-${button}`), digits: value(`${buttonKey}.digits`), next: number(`${buttonKey}.next`, noScreen) }
          }),
          dwell: number(`${prefix}.dwell`) * tickMs || 50,
          flash: number(`${prefix}.flash`),
          next: number(`${prefix}.next`, noScreen),
          connected: number(`${prefix}.connected`, noScreen),
          phases,
        })
      }
    }
  }
  return { header, offHookHoldMs, screens, changedKind }
}

export function resolveGraphicalScreen(model: Model, address: number): GraphicalScreen | undefined {
  if (address < 0 || address > 255 || (address & 63) >= screenCount) return undefined
  const primary = model.screens.get(address & 63)
  return primary?.kind === 'mixed' ? model.screens.get(address) : primary
}

export function describeGraphicalAddress(address: number, screen?: GraphicalScreen): string {
  if (address === noScreen) return 'No screen selected'
  if ((address & 63) === 63) return 'Built-in application screen'
  if ((address & 63) >= screenCount) return `Unknown screen address ${address}`
  return `Screen ${(address & 63) + 1}${screen?.kind === 'mixed' ? ` · Panel ${(address >> 6) + 1}` : ''}`
}

export const nextGraphicalAddress = (screen: GraphicalScreen, mode: GraphicalContext) =>
  mode === 'connected' ? screen.connected : screen.next

export const newGraphicalPlayback = (address: number): Playback => ({ address, phase: 0, elapsed: 0, history: [] })

export function jumpGraphicalPlayback(state: Playback, address: number): Playback {
  return { ...newGraphicalPlayback(address), history: [...state.history.slice(-23), state.address] }
}

const directions = [[-1, 0], [1, 0], [0, -1], [0, 1]] as const
const scrollMs = (vector?: (typeof directions)[number]) => vector ? (vector[0] ? 112 : 72) * 50 : 0

function phaseTiming(phase: Phase) {
  const entrance = directions[Math.floor(phase.entrance / 3) - 1]
  const exit = directions[phase.exit - 2]
  return {
    entrance, enterMs: scrollMs(entrance), exit, exitMs: scrollMs(exit),
    clearMs: phase.exit === 1 ? tickMs : 0,
  }
}

export function advanceGraphicalPlayback(model: Model, mode: GraphicalContext, state: Playback, delta: number): Playback {
  let current = state
  if (current.hold) {
    const { remaining, next } = current.hold
    if (delta < remaining) return { ...current, hold: { remaining: remaining - delta, next } }
    current = jumpGraphicalPlayback(current, next)
    delta -= remaining
  }
  let elapsed = current.elapsed + delta
  for (let step = 0; step < 32; step++) {
    const screen = resolveGraphicalScreen(model, current.address)
    const phase = screen?.phases[current.phase]
    if (!screen || !phase) return current
    const { enterMs, exitMs, clearMs } = phaseTiming(phase)
    const total = enterMs + screen.dwell + exitMs + clearMs
    if (elapsed < total) return { ...current, elapsed }
    elapsed -= total
    current = phase.exit <= 5 && screen.phases[current.phase + 1]
      ? { ...current, phase: current.phase + 1, elapsed: 0 }
      : jumpGraphicalPlayback(current, nextGraphicalAddress(screen, mode))
  }
  return current
}

export function graphicalFrame(model: Model, mode: GraphicalContext, state: Playback) {
  const screen = resolveGraphicalScreen(model, state.address)
  const phase = screen?.phases[state.phase]
  if (!screen || !phase || state.hold) return { screen, x: 0, y: 0 }
  const { entrance, enterMs, exit, exitMs } = phaseTiming(phase)
  const elapsed = state.elapsed
  if (entrance && elapsed < enterMs) {
    const left = 1 - elapsed / enterMs
    return { screen, x: entrance[0] * left, y: entrance[1] * left }
  }
  const dwell = elapsed - enterMs
  if (exit && dwell >= screen.dwell) {
    const moved = Math.min(1, (dwell - screen.dwell) / exitMs)
    return { screen, x: -exit[0] * moved, y: -exit[1] * moved }
  }
  const flashOn = (screen.flash >> 4) * 1000
  const flashOff = (screen.flash & 15) * 1000
  const style = phase.entrance % 3
  const blanked = phase.exit === 1 && dwell >= screen.dwell
  if (blanked || (style !== 0 && flashOn > 0 && flashOff > 0 && dwell % (flashOn + flashOff) >= flashOn)) {
    return { screen: !blanked && style === 2 ? resolveGraphicalScreen(model, nextGraphicalAddress(screen, mode)) : undefined, x: 0, y: 0 }
  }
  return { screen, x: 0, y: 0 }
}

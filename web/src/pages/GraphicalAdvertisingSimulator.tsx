import { useEffect, useMemo, useState } from 'react'
import { apiGet } from '../api/client'
import { NativeBitmapPreview } from './NativeBitmapField'
import { GraphicalPanelPreview } from './NativeGraphicalFields'
import {
  advanceGraphicalPlayback, describeGraphicalAddress, graphicalFrame, graphicalRecords,
  jumpGraphicalPlayback, newGraphicalPlayback, nextGraphicalAddress, readGraphicalRecords,
  resolveGraphicalScreen, type GraphicalContext, type GraphicalRecord, type Playback,
} from './graphicalPlayback'

type Props = GraphicalRecord & { setId: string }
type SavedRecord = { supported: true; sections: GraphicalRecord['sections'] } | { supported: false }

const modes = { onHook: 'On hook', offHook: 'Off hook', connected: 'Call connected' } as const

export function GraphicalAdvertisingSimulator(props: Props) {
  const [open, setOpen] = useState(false)
  return (
    <details className="graphical-simulator" onToggle={event => setOpen(event.currentTarget.open)}>
      <summary>Simulate graphical advertising</summary>
      {open && <PlaybackControls {...props} />}
    </details>
  )
}

function PlaybackControls({ setId, number, sections, edits }: Props) {
  const [otherRecords, setOtherRecords] = useState<GraphicalRecord[] | null>(null)
  const [loadError, setLoadError] = useState(false)
  const [mode, setMode] = useState<GraphicalContext>('onHook')
  const [playback, setPlayback] = useState<Playback | null>(null)
  const [playing, setPlaying] = useState(false)
  const [notice, setNotice] = useState('')

  useEffect(() => {
    let active = true
    void Promise.all(graphicalRecords.filter(record => record !== number).map(async record => {
      const layout = await apiGet<SavedRecord>(`/api/tables/sets/${setId}/records/${record}/fields`)
      return layout.supported ? { number: record, sections: layout.sections } : null
    })).then(records => {
      if (active) setOtherRecords(records.filter(record => record != null))
    }).catch(() => { if (active) setLoadError(true) })
    return () => { active = false }
  }, [setId, number])

  const model = useMemo(() => readGraphicalRecords([{ number, sections, edits }, ...(otherRecords ?? [])]),
    [number, sections, edits, otherRecords])
  const context = model.header?.[mode]
  const current = playback ?? newGraphicalPlayback(context?.start ?? 255)
  const screen = context?.enabled && resolveGraphicalScreen(model, current.address)
  const atEnd = !screen || (!screen.phases[current.phase] && !current.hold)

  useEffect(() => {
    if (!playing || atEnd || !context?.enabled) return
    let last = performance.now()
    const timer = window.setInterval(() => {
      const now = performance.now()
      const delta = Math.min(250, now - last)
      last = now
      setPlayback(state => advanceGraphicalPlayback(model, mode, state ?? newGraphicalPlayback(context.start), delta))
    }, 50)
    return () => window.clearInterval(timer)
  }, [playing, atEnd, context, mode, model])

  function changeMode(nextMode: GraphicalContext) {
    const entry = model.header?.[nextMode]
    const hold = nextMode === 'offHook' && mode === 'onHook' && entry?.enabled && entry.navigation
      && screen && model.offHookHoldMs > 0
    setPlayback(hold ? {
      ...newGraphicalPlayback(current.address), hold: { remaining: model.offHookHoldMs, next: entry.start },
    } : null)
    setMode(nextMode)
    setNotice('')
  }

  if (loadError) return <p role="alert">Could not load the other graphical records. Close and reopen the simulator to retry.</p>
  if (!otherRecords) return <p role="status">Loading graphical screens…</p>
  if (!context) return <p>Load MT131 to preview the display contexts for this set.</p>

  const frame = graphicalFrame(model, mode, current)
  const next = current.hold?.next ?? (screen ? nextGraphicalAddress(screen, mode) : 255)
  const screenName = describeGraphicalAddress(current.address, screen || undefined)
  const shown = context.enabled ? frame.screen : undefined

  function goTo(address: number) {
    setPlayback(jumpGraphicalPlayback(current, address))
    setNotice('')
  }

  return (
    <div className="graphical-simulator-controls">
      <div className="row-actions">
        <label>Display context
          <select value={mode} onChange={event => changeMode(event.target.value as GraphicalContext)}>
            {Object.entries(modes).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
          </select>
        </label>
        <button type="button" disabled={atEnd} onClick={() => setPlaying(!playing || atEnd)}>{playing && !atEnd ? 'Pause' : 'Play loop'}</button>
        <button type="button" onClick={() => { setPlayback(null); setNotice('') }}>Restart loop</button>
        {context.navigation && <>
          <button type="button" disabled={current.history.length === 0} onClick={() => {
            const address = current.history.at(-1)!
            setPlayback({ ...newGraphicalPlayback(address), history: current.history.slice(0, -1) })
            setNotice('')
          }}>Previous screen</button>
          <button type="button" disabled={next === 255} onClick={() => goTo(next)}>Next screen</button>
        </>}
      </div>
      {model.changedKind && <p>Save a new screen type to load its content in the simulator.</p>}
      <p>{!context.enabled ? `${modes[mode]} advertising is disabled.` : !screen
        ? current.address === 255 || (current.address & 63) >= 24 ? screenName : `${screenName} is unavailable in this set.`
        : screenName + (current.hold ? ' · Holding the previous on-hook screen'
          : atEnd ? ' · No enabled loop step' : ` · Loop step ${current.phase + 1}`)}</p>
      {screen && <div className="graphical-simulator-viewport">
        <div className="graphical-simulator-frame" style={{ transform: `translate(${frame.x * 100}%, ${frame.y * 100}%)` }}>
          {shown?.kind === 'bitmap' && shown.bitmap && <NativeBitmapPreview name={screenName} value={shown.bitmap} />}
          {shown?.kind === 'text' && <TextScreen key={shown.text} text={shown.text} />}
          {shown?.kind === 'mixed' && <GraphicalPanelPreview text={shown.lines} keys={shown.keys.map(key => key.label ?? '')} />}
        </div>
      </div>}
      {screen && <div className="row-actions" role="group" aria-label="Graphical choice keys">
        {screen.keys.map((key, index) => (
          <button type="button" key={index} disabled={key.next === 255 && (!key.digits || mode === 'connected')}
            onClick={() => {
              if (key.next !== 255) goTo(key.next)
              if (key.digits && mode !== 'connected') {
                setPlaying(false)
                setNotice(mode === 'onHook' ? `Lift the handset to dial ${key.digits}.` : `The phone would dial ${key.digits}.`)
              }
            }}>
            Key {index + 1}{key.label?.trim() && ` · ${key.label.trim()}`}
          </button>
        ))}
      </div>}
      {notice && <p role="status">{notice}</p>}
    </div>
  )
}

function TextScreen({ text }: { text: string | null }) {
  const [start, setStart] = useState(0)
  const lines = text?.split('\n') ?? ['Unmapped glyphs']
  return <>
    <div className="table-config-vfd" aria-label="Graphical text display">
      {Array.from({ length: 9 }, (_, index) => <div key={index}>{(lines[start + index] ?? '').padEnd(19)}</div>)}
    </div>
    {lines.length > 9 && <div className="native-graphical-page">
      <button type="button" disabled={start === 0} onClick={() => setStart(value => value - 8)}>Previous lines</button>
      <button type="button" disabled={start + 9 >= lines.length} onClick={() => setStart(value => value + 8)}>Next lines</button>
    </div>}
  </>
}

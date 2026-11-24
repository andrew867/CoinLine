import { useEffect, useMemo, useRef, useState } from 'react'
import {
  advertisingFrame, advertisingLine, advertisingPage, blankAdvertisingLine,
  compileAdvertisingPlayback, firmwareAdvertisingTiming, maximizerAdvertisingTiming,
  type AdvertisingContext, type AdvertisingFeatures, type AdvertisingSlot,
} from './advertisingPlayback'

type Props = {
  slots: AdvertisingSlot[]
  features: AdvertisingFeatures
  selectedSlot: number
  onSelectSlot: (index: number) => void
}

const desktopRates = [
  ['flashOn', 'Flash interval'], ['alternateGap', 'Alternating blank interval'], ['scroll', 'Scroll step'],
] as const

export function AdvertisingSimulator({ slots, features, selectedSlot, onSelectSlot }: Props) {
  const [view, setView] = useState<'slot' | AdvertisingContext>('slot')
  const [desktop, setDesktop] = useState(false)
  const [desktopTiming, setDesktopTiming] = useState(maximizerAdvertisingTiming)
  const [playing, setPlaying] = useState(false)
  const [frame, setFrame] = useState<ReturnType<typeof advertisingFrame> | null>(null)
  const elapsed = useRef(0)
  const { onHookEnabled, quickAccessEnabled, callEstablishedEnabled, scrollTicks } = features
  const page = useMemo(() => view === 'slot' ? null : advertisingPage(slots, {
    onHookEnabled, quickAccessEnabled, callEstablishedEnabled, scrollTicks,
  }, view), [view, slots, onHookEnabled, quickAccessEnabled, callEstablishedEnabled, scrollTicks])
  const playback = useMemo(() => page && compileAdvertisingPlayback(page,
    desktop ? desktopTiming : firmwareAdvertisingTiming(scrollTicks)), [page, desktop, desktopTiming, scrollTicks])

  useEffect(() => {
    if (!playing || !playback || page?.unavailable) return
    const compiled = playback
    let previous = performance.now()
    let animation: number
    function draw(now: number) {
      elapsed.current += Math.max(0, now - previous)
      previous = now
      const next = advertisingFrame(compiled, elapsed.current / 10)
      setFrame((current) => current?.text === next.text && current.slot === next.slot ? current : next)
      animation = requestAnimationFrame(draw)
    }
    animation = requestAnimationFrame(draw)
    return () => cancelAnimationFrame(animation)
  }, [playing, playback, page?.unavailable])

  function restart() {
    elapsed.current = 0
    setFrame(null)
  }

  function reset() {
    setPlaying(false)
    restart()
  }

  const slot = slots[selectedSlot]
  const frameSlot = frame?.slot
  const line = view === 'slot'
    ? (slot.endSequence ? blankAdvertisingLine : advertisingLine(slot.text))
    : (page?.unavailable ? blankAdvertisingLine : frame?.text ?? blankAdvertisingLine)
  const status = view === 'slot'
    ? (slot.endSequence ? 'This slot ends its sequence.' : `Message ${selectedSlot + 1}`)
    : page?.unavailable ?? (frameSlot == null ? 'Ready to preview' : `${playing ? 'Playing' : 'Paused'} · Message ${frameSlot + 1}`)

  return (
    <section className="table-config-simulator" aria-labelledby="advertising-preview-heading">
      <div>
        <h3 id="advertising-preview-heading">Terminal display preview</h3>
        <div className="table-config-vfd" role="img" aria-label={`Terminal display, lower line ${line.trim() || 'blank'}`}>
          <div aria-hidden="true">{blankAdvertisingLine}</div>
          <div aria-hidden="true">{line}</div>
        </div>
        <p className="table-config-vfd-status">{status}</p>
      </div>
      <div>
        <div className="table-config-selectors">
          <label>Preview
            <select value={view} onChange={(event) => {
              setView(event.target.value as typeof view)
              reset()
            }}>
              <option value="slot">Single message</option>
              <option value="onHook">On-hook sequence</option>
              <option value="callEstablished">Call-established sequence</option>
              <option value="quickAccess">Quick-access sequence</option>
            </select>
          </label>
          {view === 'slot' ? (
            <label>Preview slot <select value={selectedSlot} onChange={(event) => onSelectSlot(Number(event.target.value))}>
              {slots.map((_, index) => <option key={index} value={index}>Message {index + 1}</option>)}
            </select></label>
          ) : (
            <label>Timing
              <select value={desktop ? 'desktop' : 'firmware'} onChange={(event) => {
                setDesktop(event.target.value === 'desktop')
                reset()
              }}>
                <option value="firmware">MTR 1.9 terminal timing</option>
                <option value="desktop">Maximizer preview timing</option>
              </select>
            </label>
          )}
        </div>
        {view !== 'slot' && desktop && (
          <div className="table-config-selectors table-config-preview-rates">
            {desktopRates.map(([key, name]) => (
              <label key={key}>{name} (10 ms ticks)
                <input type="number" min={1} max={1000} value={desktopTiming[key]} onChange={(event) => {
                  const ticks = Math.round(event.target.valueAsNumber)
                  if (!Number.isFinite(ticks) || ticks < 1 || ticks > 1000) return
                  setDesktopTiming((current) => ({ ...current, [key]: ticks, flashOff: key === 'flashOn' ? ticks : current.flashOff }))
                  reset()
                }} />
              </label>
            ))}
          </div>
        )}
        {view !== 'slot' && (
          <div className="table-config-preview-actions">
            <button type="button" disabled={!!page?.unavailable} onClick={() => setPlaying(!playing)}>
              {playing ? 'Pause' : frame ? 'Resume' : 'Play'}
            </button>
            <button type="button" disabled={!!page?.unavailable} onClick={restart}>Restart</button>
          </div>
        )}
      </div>
    </section>
  )
}

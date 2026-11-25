import { useState } from 'react'

// MT30 prompt and display timers are nonnegative signed 16-bit values in 10 ms ticks.
export const interfaceTimingKeys = ['clearDigits', 'transient', 'transientHint', 'voiceDelay', 'voiceRepeat',
  'idleTimeout', 'languageTimeout', 'scrollSpeed'] as const

export function interfaceTicksToSeconds(ticks: string | null) {
  if (ticks === null || !/^\d+$/.test(ticks)) return null
  const value = Number(ticks)
  if (!Number.isSafeInteger(value) || value > 32767) return null
  return `${Math.floor(value / 100)}.${String(value % 100).padStart(2, '0')}`
}

export function interfaceSecondsToTicks(seconds: string) {
  const match = /^(?:(\d{1,3})(?:\.(\d{1,2}))?|\.(\d{1,2}))$/.exec(seconds.trim())
  if (!match) return null
  const ticks = Number(match[1] ?? '0') * 100 + Number((match[2] ?? match[3] ?? '').padEnd(2, '0'))
  return ticks <= 32767 ? String(ticks) : null
}

export type InterfaceTimer = { key: string; label: string; value: string | null; changed: boolean }

export function InterfaceTimingPanel({ timers, editable, onChange }: {
  timers: InterfaceTimer[]; editable: boolean; onChange: (key: string, value: string) => void
}) {
  const [selected, setSelected] = useState('scrollSpeed')
  const [seconds, setSeconds] = useState('')
  const current = timers.find(timer => timer.key === selected) ?? timers[0]
  if (!current) return null
  const ticks = interfaceSecondsToTicks(seconds)
  return <section className="table-config-panel" aria-label="Interface timing worksheet">
    <h3>Prompt and display timing</h3>
    <p>Each tick is exactly 10 milliseconds. The readouts include staged changes and use the packed value, not the duration of a recorded terminal event.</p>
    <div className="interface-timing-grid">{timers.map(timer => {
      const value = interfaceTicksToSeconds(timer.value)
      return <div className="interface-timing-value" key={timer.key}>
        <span>{timer.label}</span>
        <strong>{value === null ? 'Outside supported range' : `${value} s`}</strong>
        <small>{timer.value === null ? 'Stored value unavailable' : `${timer.value} ticks`}{timer.changed ? ' · Staged' : ''}</small>
      </div>
    })}</div>
    {editable && <div className="table-config-selectors">
      <label>Timing field<select value={current.key} onChange={event => { setSelected(event.target.value); setSeconds('') }}>
        {timers.map(timer => <option key={timer.key} value={timer.key}>{timer.label}</option>)}
      </select></label>
      <label>New duration (seconds)<input type="text" inputMode="decimal" value={seconds} placeholder={interfaceTicksToSeconds(current.value) ?? undefined}
        aria-invalid={seconds.trim() !== '' && ticks === null} onChange={event => setSeconds(event.target.value)} /></label>
      <button type="button" className="quiet" disabled={ticks === null} onClick={() => {
        if (ticks !== null) { onChange(current.key, ticks); setSeconds('') }
      }}>Stage exact ticks</button>
      <p role="status">{seconds.trim() === '' ? 'Enter up to two decimal places, then stage the value.' : ticks === null
        ? 'Enter an exact duration from 0 through 327.67 seconds, with no rounding.' : `${seconds.trim()} s = ${ticks} ticks; stage to include this field in the next save.`}</p>
    </div>}
  </section>
}

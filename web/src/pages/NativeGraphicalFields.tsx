import { Fragment, useState } from 'react'

const cellsPerRow = 19
const pageRows = 9
const pageStep = 8
const screenCount = 24
const noScreen = 255
const applicationScreen = 63
const flashFields = [['Flash on (seconds)', 4], ['Flash off (seconds)', 0]] as const

type InputProps = { label: string; value: string; onChange: (value: string) => void }

export function GraphicalTextField({ label, value, unmapped, onChange }: InputProps & { unmapped: boolean }) {
  const [page, setPage] = useState(0)
  const rows = value ? value.split('\n') : []
  const offset = Math.min(page, Math.max(0, Math.ceil((rows.length - pageRows) / pageStep))) * pageStep
  const displayed = rows.slice(offset, offset + pageRows)
  return (
    <div className="native-graphical-content">
      <label className="native-table-field">
        <span>{label}</span>
        <textarea rows={7} wrap="off" spellCheck={false} value={value} onChange={(event) => {
          const text = event.currentTarget.value
          const lines = text.split('\n')
          event.currentTarget.setCustomValidity(lines.length > 54 || lines.some((line) => line.normalize().length > cellsPerRow)
            ? 'Use up to 54 lines of 19 display characters.' : '')
          onChange(text)
        }} />
        <small>{unmapped ? 'Some stored characters are unavailable in this editor. Edits replace the visible text.' : 'Up to 54 lines of 19 display cells.'}</small>
      </label>
      <figure className="native-graphical-preview">
        <figcaption>Screen preview</figcaption>
        <div className="table-config-vfd" role="img" aria-label={unmapped ? 'Screen with unmapped glyphs' :
          `Screen lines ${offset + 1} through ${offset + pageRows}. ${displayed.filter((line) => line.trim()).join('. ') || 'Blank'}`}>
          {Array.from({ length: pageRows }, (_, index) => (
            <div key={index}>{(unmapped && index === 0 ? 'Unmapped glyphs' : displayed[index] ?? '').padEnd(cellsPerRow)}</div>
          ))}
        </div>
        {rows.length > pageRows && (
          <div className="native-graphical-page">
            <button type="button" disabled={offset === 0} onClick={() => setPage(offset / pageStep - 1)}>Previous lines</button>
            <button type="button" disabled={offset + pageRows >= rows.length} onClick={() => setPage(offset / pageStep + 1)}>Next lines</button>
          </div>
        )}
      </figure>
    </div>
  )
}

export function GraphicalPanelPreview({ text, keys }: { text: (string | null)[]; keys: (string | null)[] }) {
  return (
    <figure className="native-graphical-preview native-graphical-panel">
      <figcaption>Mixed panel preview</figcaption>
      <div className="table-config-vfd" aria-label="Mixed panel display">
        {text.map((line, index) => <div key={index}>{(line ?? 'Unmapped glyphs').padEnd(cellsPerRow)}</div>)}
        {Array.from({ length: 3 }, (_, row) => (
          <Fragment key={row}>
            <div className="native-graphical-keys">
              <span aria-label={`Key ${row * 2 + 1} label`}>{(keys[row * 2] ?? '?').padEnd(9)}</span>
              <span aria-label={`Key ${row * 2 + 2} label`}>{(keys[row * 2 + 1] ?? '?').padEnd(9)}</span>
            </div>
            {row < 2 && <div aria-hidden="true">{' '.repeat(cellsPerRow)}</div>}
          </Fragment>
        ))}
      </div>
    </figure>
  )
}

export function GraphicalLinkField({ label, value, onChange }: InputProps) {
  const packed = Number(value)
  const screen = packed === noScreen ? noScreen : packed & 63
  const hasPanel = screen < screenCount
  const selection = hasPanel || packed === noScreen || packed === applicationScreen ? screen : packed
  return (
    <div className="native-graphical-field-pair">
      <label className="native-table-field">
        <span>{label}</span>
        <select value={selection} onChange={(event) => onChange(event.target.value)}>
          <option value={noScreen}>None</option>
          {Array.from({ length: screenCount }, (_, index) => <option key={index} value={index}>Screen {index + 1}</option>)}
          <option value={applicationScreen}>Built-in application screen</option>
          {selection !== applicationScreen && selection !== noScreen && !hasPanel && <option value={selection}>Stored value {packed}</option>}
        </select>
      </label>
      {hasPanel && (
        <label className="native-table-field">
          <span>{label} panel</span>
          <select value={packed >> 6} onChange={(event) => onChange(String(screen | Number(event.target.value) << 6))}>
            {Array.from({ length: 4 }, (_, index) => <option key={index} value={index}>Panel {index + 1}</option>)}
          </select>
        </label>
      )}
    </div>
  )
}

export function GraphicalFlashField({ value, onChange }: InputProps) {
  const packed = Number(value)
  return (
    <div className="native-graphical-field-pair">
      {flashFields.map(([label, shift]) => (
        <label className="native-table-field" key={label}>
          <span>{label}</span>
          <select value={(packed >> shift) & 15} onChange={(event) => onChange(String((packed & ~(15 << shift)) | Number(event.target.value) << shift))}>
            {Array.from({ length: 16 }, (_, index) => <option key={index} value={index}>{index}</option>)}
          </select>
        </label>
      ))}
    </div>
  )
}

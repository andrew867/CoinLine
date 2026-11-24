import { useEffect, useId, useRef, useState } from 'react'
import type { ClipboardEvent, KeyboardEvent } from 'react'

export type SpareByteEdits = Record<number, string>
const isSpareByte = (value: string) => /^[0-9A-F]{2}$/i.test(value)

const columns = 16
const pageSize = 128
const byteSteps: Readonly<Partial<Record<string, number>>> = {
  ArrowLeft: -1, ArrowRight: 1, ArrowUp: -columns, ArrowDown: columns, PageUp: -pageSize, PageDown: pageSize,
}
const hexOffset = (offset: number) => offset.toString(16).toUpperCase().padStart(4, '0')

export function SpareTableByteEditor({ bytes, edits, editable, onChange }: {
  bytes: string; edits: SpareByteEdits; editable: boolean; onChange: (edits: SpareByteEdits) => void
}) {
  const [page, setPage] = useState(0)
  const [feedback, setFeedback] = useState('')
  const grid = useRef<HTMLTableElement>(null)
  const pendingFocus = useRef<number | null>(null)
  const instructions = useId()
  const size = bytes.length / 2
  const first = page * pageSize + 1
  const last = Math.min(first + pageSize - 1, size)
  const pages = Math.ceil(size / pageSize)
  const storedByte = (offset: number) => bytes.slice((offset - 1) * 2, offset * 2)

  function focusCell(offset: number) {
    grid.current?.querySelector<HTMLInputElement>(`input[data-spare-offset="${offset}"]`)?.focus()
  }

  useEffect(() => {
    if (pendingFocus.current === null) return
    focusCell(pendingFocus.current)
    pendingFocus.current = null
  }, [page])

  function replace(offset: number, replacement: string[]) {
    const updated = { ...edits }
    replacement.forEach((value, index) => {
      const position = offset + index
      if (value === storedByte(position)) delete updated[position]
      else updated[position] = value
    })
    onChange(updated)
    setFeedback('')
  }

  function paste(event: ClipboardEvent<HTMLInputElement>, offset: number) {
    event.preventDefault()
    const compact = event.clipboardData.getData('text').replace(/\s+/g, '').toUpperCase()
    if (!/^(?:[0-9A-F]{2})+$/.test(compact)) {
      setFeedback('Paste complete hexadecimal byte pairs.')
      return
    }
    const replacement = compact.match(/.{2}/g) ?? []
    if (offset + replacement.length - 1 > size) {
      setFeedback('The pasted bytes would extend past the end of the record.')
      return
    }
    if (replacement.some((_, index) => storedByte(offset + index) === '??')) {
      setFeedback('The pasted range includes protected bytes. Use the settings editor to replace those fields.')
      return
    }
    replace(offset, replacement)
  }

  function navigate(event: KeyboardEvent<HTMLInputElement>, offset: number) {
    const step = byteSteps[event.key]
    const edge = event.key === 'Home' || event.key === 'End'
    const wholeRecord = event.ctrlKey || event.metaKey
    if ((!edge && step === undefined) || event.altKey || event.shiftKey || (wholeRecord && !edge)) return
    event.preventDefault()
    let skip = step ?? 0
    let next = offset + skip
    if (edge) {
      const rowStart = Math.floor((offset - 1) / columns) * columns + 1
      const rowEnd = Math.min(size, rowStart + columns - 1)
      if (event.key === 'Home') {
        next = wholeRecord ? 1 : rowStart
        skip = 1
      } else {
        next = wholeRecord ? size : rowEnd
        skip = -1
      }
    } else if (Math.abs(skip) === pageSize) {
      skip = Math.sign(skip)
    }
    while (next >= 1 && next <= size && storedByte(next) === '??') next += skip
    if (next < 1 || next > size) return
    const nextPage = Math.floor((next - 1) / pageSize)
    if (nextPage === page) focusCell(next)
    else {
      pendingFocus.current = next
      setPage(nextPage)
    }
  }

  return <section className="spare-table-editor" aria-label="Spare table byte grid">
    <p id={instructions}>Offsets are hexadecimal. Protected bytes are masked.{editable && ' Replace bytes in place or paste a sequence of hex pairs.'}</p>
    <div className="table-config-selectors">
      <label>Byte page
        <select value={page} onChange={(event) => { setPage(Number(event.target.value)); setFeedback('') }}>
          {Array.from({ length: pages }, (_, index) => <option key={index} value={index}>
            {hexOffset(index * pageSize + 1)} to {hexOffset(Math.min((index + 1) * pageSize, size))}
          </option>)}
        </select>
      </label>
    </div>
    <div className="spare-table-scroll">
      <table ref={grid} aria-label="Record bytes" aria-describedby={instructions}>
        <thead><tr><th scope="col">Offset</th>{Array.from({ length: columns }, (_, index) =>
          <th scope="col" key={index}>+{index.toString(16).toUpperCase()}</th>)}</tr></thead>
        <tbody>{Array.from({ length: Math.ceil((last - first + 1) / columns) }, (_, row) => {
          const start = first + row * columns
          return <tr key={start}><th scope="row">{hexOffset(start)}</th>{Array.from({ length: columns }, (_, col) => {
            const offset = start + col
            if (offset > last) return <td key={offset} />
            const stored = storedByte(offset)
            if (stored === '??') return <td key={offset} className="spare-table-protected" aria-label={`Byte ${hexOffset(offset)} protected`}>··</td>
            const value = edits[offset] ?? stored
            return <td key={offset}>{editable ? <input type="text" maxLength={2} spellCheck={false} autoComplete="off"
              aria-label={`Byte ${hexOffset(offset)}`} aria-invalid={!isSpareByte(value)} data-spare-offset={offset}
              data-dirty={offset in edits} value={value} onFocus={(event) => event.currentTarget.select()}
              onChange={(event) => {
                const changed = event.target.value.toUpperCase()
                if (/^[0-9A-F]{0,2}$/.test(changed)) replace(offset, [changed])
              }} onPaste={(event) => paste(event, offset)} onKeyDown={(event) => navigate(event, offset)} /> : value}</td>
          })}</tr>
        })}</tbody>
      </table>
    </div>
    {feedback && <p role="status" className="native-table-feedback">{feedback}</p>}
  </section>
}

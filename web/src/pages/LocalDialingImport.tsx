import { useEffect, useRef, useState } from 'react'
import { exportLocalDialingCsv, planLocalDialingImport, readExchangeFile,
  type ExchangeFile, type ExchangeSource, type LocalDialingSegment } from './localDialingExchangeData'

function selectInitialArea(source: ExchangeSource | undefined, segment: LocalDialingSegment) {
  if (!source) return ''
  if (segment.area && source.areas.has(segment.area)) return segment.area
  return source.areas.size === 1 ? source.areas.keys().next().value ?? '' : ''
}

export function LocalDialingImport({ segment, editable, exchangeFile, onExchangeFile, stage }: {
  segment: LocalDialingSegment
  editable: boolean
  exchangeFile: ExchangeFile | null
  onExchangeFile: (value: ExchangeFile | null) => void
  stage: (fields: Record<string, string>) => void
}) {
  const source = exchangeFile?.source
  const [area, setArea] = useState(() => selectInitialArea(source, segment))
  const [centers, setCenters] = useState<Set<string>>(new Set())
  const [selectedRate, setSelectedRate] = useState('')
  const [otherRate, setOtherRate] = useState('')
  const [unlistedRate, setUnlistedRate] = useState('')
  const [preview, setPreview] = useState(false)
  const [loading, setLoading] = useState(false)
  const [message, setMessage] = useState('')
  const request = useRef<AbortController | null>(null)
  useEffect(() => () => request.current?.abort(), [])

  async function load(file: File | undefined) {
    request.current?.abort()
    setPreview(false)
    onExchangeFile(null)
    setArea('')
    setCenters(new Set())
    setSelectedRate('')
    setOtherRate('')
    setUnlistedRate('')
    setMessage('')
    setLoading(!!file)
    if (!file) return
    const controller = new AbortController()
    request.current = controller
    try {
      const parsed = await readExchangeFile(file, controller.signal)
      if (controller.signal.aborted) return
      onExchangeFile({ name: file.name, source: parsed })
      setArea(selectInitialArea(parsed, segment))
    } catch (error) {
      if (!controller.signal.aborted) setMessage(error instanceof TypeError ? 'Choose an extracted UTF-8 report or spreadsheet.'
        : error instanceof Error ? error.message : 'Could not read the file.')
    } finally {
      if (!controller.signal.aborted) setLoading(false)
    }
  }

  function download() {
    setMessage('')
    try {
      const csv = exportLocalDialingCsv(segment)
      const url = URL.createObjectURL(new Blob([csv], { type: 'text/csv;charset=utf-8' }))
      const link = document.createElement('a')
      link.href = url
      link.download = `exchange-rates-${segment.area}.csv`
      link.click()
      window.setTimeout(() => URL.revokeObjectURL(url), 0)
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'Could not export this segment.')
    }
  }

  const areas = [...(source?.areas.keys() ?? [])].sort()
  const entries = source?.areas.get(area)
  const choices = source?.kind === 'registry' && entries ? [...new Set([...entries.values()].map(String))].sort() : []
  let proposal: ReturnType<typeof planLocalDialingImport> | undefined
  let problem: string | undefined
  if (editable && preview && source) {
    try { proposal = planLocalDialingImport(source, { area, centers, selectedRate, otherRate, unlistedRate }, segment) }
    catch (error) { problem = error instanceof Error ? error.message : 'Could not prepare this import.' }
  }

  function change(action: () => void) {
    action()
    setPreview(false)
    setMessage('')
  }

  return (
    <details className="local-dialing-import">
      <summary>{editable ? 'Import or export exchange rates' : 'Export exchange rates'}</summary>
      <div className="row-actions">
        <button type="button" className="quiet" disabled={!segment.area} onClick={download}>Download rate-index CSV</button>
      </div>
      {editable && <>
        <p className="page-intro">Choose an extracted NANPA or CNA report, or a CSV with NPA, NXX and RateIndex columns.</p>
        <div className="table-config-selectors">
          <label>Exchange data file
            <input type="file" accept=".csv,.tsv,.txt,text/csv,text/plain" onChange={event => void load(event.target.files?.[0])} />
          </label>
          {source && <label>Area code from file
            <select value={area} onChange={event => change(() => { setArea(event.target.value); setCenters(new Set()) })}>
              <option value="">Choose an area code</option>
              {areas.map(npa => <option key={npa}>{npa}</option>)}
            </select>
          </label>}
        </div>
        {loading && <p role="status">Reading exchange data…</p>}
        {source && <p className="exchange-import-source">{source.label} · {areas.length} area {areas.length === 1 ? 'code' : 'codes'}
          {exchangeFile?.name && <> · <strong>{exchangeFile.name}</strong></>}
        </p>}
        {entries && <>
          {choices.length > 0 && <div className="table-config-selectors">
            <label>Rate centers for the selected index
              <select multiple size={Math.min(6, Math.max(2, choices.length))} value={[...centers]}
                onChange={event => change(() => setCenters(new Set([...event.target.selectedOptions].map(option => option.value))))}>
                {choices.map(center => <option key={center}>{center}</option>)}
              </select>
            </label>
            {centers.size > 0 && <RateInput label="Selected rate center index" value={selectedRate} max={segment.maxRate}
              onChange={value => change(() => setSelectedRate(value))} />}
            {centers.size < choices.length && <RateInput label="Other in-service exchange rate index" value={otherRate} max={segment.maxRate}
              onChange={value => change(() => setOtherRate(value))} />}
          </div>}
          <div className="table-config-selectors">
            {entries.size < segment.fields.length && <RateInput label="Unlisted exchange rate index" value={unlistedRate}
              max={segment.maxRate} onChange={value => change(() => setUnlistedRate(value))} />}
            <button type="button" onClick={() => { setPreview(true); setMessage('') }}>Preview import</button>
          </div>
          {entries.size < segment.fields.length && <p className="page-intro">
            {area === segment.storedArea ? 'Leave the unlisted index blank to keep existing rates.' : 'Choose a rate index for unlisted exchanges in the new area code.'}
          </p>}
        </>}
      </>}
      {problem && <p role="alert">{problem}</p>}
      {proposal && <section aria-label="Exchange import preview">
        <p>{proposal.listed} exchanges from the file. {proposal.missing > 0 &&
          `${proposal.missing} unlisted exchanges ${proposal.filler == null ? 'keep their current rates' : `use rate index ${proposal.filler}`}. `}
          {proposal.changed.length} rate {proposal.changed.length === 1 ? 'change' : 'changes'}.
          {proposal.areaChanged && ` New area code ${area}.`}</p>
        {proposal.changed.length > 0 && <table>
          <caption>{proposal.changed.length > 10 ? 'First ten changes' : 'Rate changes'}</caption>
          <thead><tr><th>Exchange</th><th>Current index</th><th>New index</th></tr></thead>
          <tbody>{proposal.changed.slice(0, 10).map(row => <tr key={row.exchange}>
            <td>{row.exchange}</td><td>{row.from ?? 'Unknown'}</td><td>{row.to}</td>
          </tr>)}</tbody>
        </table>}
        <button type="button" disabled={!proposal.changed.length && !proposal.areaChanged} onClick={() => {
          if (!proposal) return
          stage(proposal.edits)
          setPreview(false)
          setMessage('Import staged. Save the record to keep these changes.')
        }}>Stage import</button>
      </section>}
      {message && <p role="status">{message}</p>}
    </details>
  )
}

function RateInput({ label, value, max, onChange }: { label: string; value: string; max: number; onChange: (value: string) => void }) {
  return <label>{label}<input inputMode="numeric" value={value} placeholder={`0 to ${max}`}
    onChange={event => onChange(event.target.value)} /></label>
}

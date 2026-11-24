import { useEffect, useRef, useState } from 'react'
import { exportLocalDialingCsv, homeExchangeRegion, planLocalDialingImport, readCnaRegionFile, readExchangeFile,
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
  const source = exchangeFile?.cnaRegions?.source ?? exchangeFile?.source
  const cnaSource = exchangeFile?.source.kind === 'registry' && exchangeFile.source.provinces
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

  async function load(file: File | undefined, cnaRegions = false) {
    request.current?.abort()
    setPreview(false)
    if (cnaRegions && exchangeFile) onExchangeFile({ ...exchangeFile, cnaRegions: undefined })
    else {
      onExchangeFile(null)
      setArea('')
    }
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
      if (cnaRegions && exchangeFile) {
        const parsed = await readCnaRegionFile(file, controller.signal, exchangeFile.source)
        if (controller.signal.aborted) return
        onExchangeFile({ ...exchangeFile, cnaRegions: { name: file.name, source: parsed } })
        return
      }
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
  const choices = source?.kind === 'registry' && entries ? [...new Set(entries.values())].map(String).sort() : []
  const home = exchangeFile?.homeExchange ?? ''
  let homeRegion: ReturnType<typeof homeExchangeRegion> | undefined
  let homeProblem: string | undefined
  if (source?.kind === 'region' && home.trim().length >= 6) {
    try { homeRegion = homeExchangeRegion(source, home) }
    catch (error) { homeProblem = error instanceof Error ? error.message : 'Could not resolve the home exchange.' }
  }
  const sameRegionCount = entries && homeRegion ? [...entries.values()].filter(region => region === homeRegion.region).length : 0
  const otherRegionCount = entries && homeRegion ? entries.size - sameRegionCount : 0
  const canPreview = source?.kind !== 'region' || !!homeRegion
  let proposal: ReturnType<typeof planLocalDialingImport> | undefined
  let problem: string | undefined
  if (editable && preview && source && canPreview) {
    try {
      const selected = homeRegion ? new Set([homeRegion.region]) : centers
      proposal = planLocalDialingImport(source, { area, centers: selected, selectedRate, otherRate, unlistedRate }, segment)
    }
    catch (error) { problem = error instanceof Error ? error.message : 'Could not prepare this import.' }
  }

  function change(action: () => void) {
    action()
    setPreview(false)
    setMessage('')
  }

  function changeHome(value: string) {
    if (!exchangeFile || source?.kind !== 'region') return
    onExchangeFile({ ...exchangeFile, homeExchange: value })
    if (area) return
    try { setArea(homeExchangeRegion(source, value).area) }
    catch { /* Keep the destination area open while the prefix is incomplete or absent. */ }
  }

  return (
    <details className="local-dialing-import">
      <summary>{editable ? 'Import or export exchange rates' : 'Export exchange rates'}</summary>
      <div className="row-actions">
        <button type="button" className="quiet" disabled={!segment.area} onClick={download}>Download rate-index CSV</button>
      </div>
      {editable && <>
        <p className="page-intro">Load an exchange report, rate-index CSV, or NPA/NXX crosswalk with a LATA or LIR column.</p>
        <div className="table-config-selectors">
          <label>Exchange data file
            <input type="file" accept=".csv,.tsv,.txt,text/csv,text/plain" disabled={loading}
              onChange={event => {
                const file = event.target.files?.[0]
                event.target.value = ''
                if (file) void load(file)
              }} />
          </label>
          {cnaSource && <label>CNA Exchange Area Listing (optional)
            <input type="file" accept=".csv,text/csv" disabled={loading} onChange={event => {
              const file = event.target.files?.[0]
              event.target.value = ''
              if (file) void load(file, true)
            }} />
          </label>}
          {exchangeFile?.cnaRegions && <button type="button" className="quiet" onClick={() => void load(undefined, true)}>
            Remove LIR listing
          </button>}
          {source?.kind === 'region' && <label>Phone’s home NPA-NXX
            <input value={home} placeholder="NPA-NXX" maxLength={7} inputMode="numeric" disabled={loading}
              onChange={event => change(() => changeHome(event.target.value))} />
          </label>}
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
          {exchangeFile?.cnaRegions && <> · <strong>{exchangeFile.cnaRegions.name}</strong></>}
        </p>}
        {homeProblem && <p role="alert">{homeProblem}</p>}
        {source?.kind === 'region' && homeRegion && <p>
          Home {source.regionKind} <strong>{homeRegion.region}</strong>.
          {entries && ` ${sameRegionCount} mapped to this ${source.regionKind} and ${otherRegionCount} to other regions in ${area}.`}
        </p>}
        {entries && canPreview && <>
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
          {source?.kind === 'region' && <div className="table-config-selectors">
            {sameRegionCount > 0 && <RateInput label={`Same ${source.regionKind} rate index`} value={selectedRate} max={segment.maxRate}
              onChange={value => change(() => setSelectedRate(value))} />}
            {otherRegionCount > 0 && <RateInput label={`Other ${source.regionKind} rate index`} value={otherRate} max={segment.maxRate}
              onChange={value => change(() => setOtherRate(value))} />}
          </div>}
          <div className="table-config-selectors">
            {entries.size < segment.fields.length && <RateInput label={source?.kind === 'region' ? 'Unmapped exchange rate index' : 'Unlisted exchange rate index'} value={unlistedRate}
              max={segment.maxRate} onChange={value => change(() => setUnlistedRate(value))} />}
            <button type="button" onClick={() => { setPreview(true); setMessage('') }}>Preview import</button>
          </div>
          {source?.kind === 'region' && <p className="page-intro">Use the tariff for this phone when choosing rate indices.</p>}
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

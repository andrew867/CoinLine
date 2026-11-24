import { lazy, Suspense, useEffect, useState } from 'react'
import { apiGet, apiPost, apiPostRaw } from '../api/client'
import { AdvertisingSimulator } from './AdvertisingSimulator'
import type { AdvertisingFeatures, AdvertisingSlot } from './advertisingPlayback'
import type { ExchangeFile } from './localDialingExchangeData'

const NativeTableEditor = lazy(async () => ({ default: (await import('./NativeTableEditor')).NativeTableEditor }))

type AdvertisingSnapshot = AdvertisingFeatures & { slots: AdvertisingSlot[]; revision: number; sha256: string | null }
type Definition = { id: string; tableNumber: number; name: string }
type Version = {
  tableDefinitionId: string
  tableNumber: number
  definitionName: string
  tableRevision: number
  sortOrder: number
  dependsOnTableDefinitionId: string | null
}

const modes = ['Static', 'Flash', 'Alternate with next slot', 'Scroll in', 'Scroll out', 'Scroll in and out']

const panels: { name: string; fields: string; records: [number, string][] }[] = [
  { name: 'Text and display', fields: 'Advertising sequence, display timing, languages and graphical screens.', records: [[29, 'Text advertising'], [27, 'Visual prompts L1, older'], [28, 'Visual prompts L2, older'], [85, 'Visual prompts L1'], [86, 'Visual prompts L2'], [98, 'Visual prompts L3'], [99, 'Visual prompts L4'], [131, 'Graphical screens 1'], [132, 'Graphical screens 2'], [133, 'Graphical screens 3']] },
  { name: 'Terminal features and call setup', fields: 'Access numbers, terminal options, service timers, modem and answer supervision.', records: [[21, 'NCC terminal access'], [26, 'Feature and call options'], [30, 'User interface parameters'], [31, 'Installation and service'], [33, 'Modem parameters'], [49, 'Answer supervision'], [68, 'Audio-Gram messaging']] },
  { name: 'Dialing, screening and service', fields: 'Key destinations, call restrictions, digit translation and screening patterns.', records: [[55, 'Quick Access Keys'], [58, 'Service Level Restrictions'], [59, 'Call screening, older'], [62, 'Numbering plan'], [92, 'Call-Screening List']] },
  { name: 'Rates and local dialing', fields: 'Tariff ladders, area and exchange classification, international rates and discounts.', records: [[73, 'Rate table'], ...segments(74, 8, 'Uncompressed'), [90, 'Uncompressed segment 9'], [91, 'Uncompressed segment 10'], ...segments(101, 15, 'Compressed'), ...segments(136, 14, 'Double-compressed'), [154, 'Double-compressed segment 15'], [155, 'Double-compressed segment 16'], [150, 'NPA set-based rating'], [151, 'International set-based rating'], [152, 'Holiday and time-of-day discounts']] },
  { name: 'Cards, coin and carriers', fields: 'Magnetic and calling card policies, smart cards, coin windows, carrier screens and promotions.', records: [[22, 'Card acceptance, older'], [134, 'Card acceptance'], [23, 'Carrier screens, older'], [135, 'Carrier screens'], [50, 'Coin validation'], [72, 'Spare table and e-purse'], [93, 'Smart-card parameters'], [95, 'Instant Win']] },
  { name: 'Reporting and code downloads', fields: 'Collection thresholds, call-in schedules and code-server parameters.', records: [[32, 'Communications statistics settings'], [34, 'Call and carrier statistics settings'], [35, 'Time and call-in settings'], [94, 'Remote code download settings']] },
]

function segments(start: number, count: number, label: string): [number, string][] {
  return Array.from({ length: count }, (_, index) => [start + index, `${label} segment ${index + 1}`])
}

type Props = { setId: string; draft: boolean; versions: Version[]; onSaved: () => void }

export function TableSetConfiguration({ setId, draft, versions, onSaved, initialRecord, recordDirty, onRecordDirty }: Props & {
  initialRecord: number
  recordDirty: boolean
  onRecordDirty: (dirty: boolean) => void
}) {
  const initialPanel = panels.findIndex(({ records }) => records.some(([number]) => number === initialRecord))
  const [panelIndex, setPanelIndex] = useState(Math.max(0, initialPanel))
  const [tableNumber, setTableNumber] = useState(initialPanel < 0 ? 29 : initialRecord)
  const [exchangeFile, setExchangeFile] = useState<ExchangeFile | null>(null)
  const panel = panels[panelIndex]
  const record = panel.records.find(([number]) => number === tableNumber)!
  const version = versions.find((item) => item.tableNumber === tableNumber)
  const advertisingRevision = [26, 29, 30].map((number) => versions.find((item) => item.tableNumber === number)?.tableRevision ?? 0).join(':')

  return (
    <section className="table-config-panel" aria-labelledby="config-heading">
      <h2 id="config-heading">Terminal configuration</h2>
      <div className="table-config-selectors">
        <label>
          Configuration panel
          <select value={panelIndex} onChange={(event) => {
            if (recordDirty && !window.confirm('Discard unsaved changes to this record?')) return
            const index = Number(event.target.value)
            onRecordDirty(false)
            setPanelIndex(index)
            setTableNumber(panels[index].records[0][0])
          }}>
            {panels.map((item, index) => <option key={item.name} value={index}>{item.name}</option>)}
          </select>
        </label>
        <label>
          Record
          <select value={tableNumber} onChange={(event) => {
            if (recordDirty && !window.confirm('Discard unsaved changes to this record?')) return
            onRecordDirty(false)
            setTableNumber(Number(event.target.value))
          }}>
            {panel.records.map(([type, name]) => <option value={type} key={type}>MT{type} · {name}</option>)}
          </select>
        </label>
      </div>
      <p className="page-intro">{panel.fields}</p>
      {tableNumber === 29 ? (
        <AdvertisingEditor key={setId} setId={setId} draft={draft} revision={advertisingRevision} onSaved={onSaved} onDirty={onRecordDirty} />
      ) : (
        <div key={`${setId}:${tableNumber}`}>
          <Suspense fallback={<p role="status">Loading record editor…</p>}>
            <NativeTableEditor setId={setId} number={tableNumber} revision={version?.tableRevision ?? 0}
              exchangeFile={exchangeFile} onExchangeFile={setExchangeFile}
              onSaved={onSaved} onDirty={onRecordDirty} />
          </Suspense>
          {!recordDirty && (version && draft ? (
            <details><summary>Import replacement record</summary>
              <RecordImport setId={setId} draft={draft} version={version} versions={versions} onSaved={onSaved} record={record} />
            </details>
          ) : !version && <RecordImport setId={setId} draft={draft} version={version}
            versions={versions} onSaved={onSaved} record={record} />)}
        </div>
      )}
    </section>
  )
}

function AdvertisingEditor({ setId, draft, revision, onSaved, onDirty }: Pick<Props, 'setId' | 'draft' | 'onSaved'> & {
  revision: string
  onDirty: (dirty: boolean) => void
}) {
  const [snapshot, setSnapshot] = useState<AdvertisingSnapshot | null>(null)
  const slots = snapshot?.slots
  const onHookEnabled = snapshot?.onHookEnabled
  const [previewIndex, setPreviewIndex] = useState(0)
  const [expanded, setExpanded] = useState(false)
  const [open, setOpen] = useState(false)
  const [dirty, setDirty] = useState(false)
  const [block, setBlock] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [feedback, setFeedback] = useState('')
  const endpoint = `/api/tables/sets/${setId}/advertising`
  const blockChanged = block !== null && block !== slots?.map((slot) => slot.text).join('\n')

  useEffect(() => onDirty(dirty || blockChanged), [dirty, blockChanged, onDirty])

  useEffect(() => {
    if (!expanded) return
    const controller = new AbortController()
    void fetch(endpoint, { signal: controller.signal })
      .then(async (response) => {
        if (!response.ok) throw new Error((await response.json().catch(() => null))?.error ?? 'Could not load text advertising.')
        const data = await response.json() as AdvertisingSnapshot
        if (!controller.signal.aborted) setSnapshot(data)
      })
      .catch((error: unknown) => {
        if (!controller.signal.aborted) setFeedback(error instanceof Error ? error.message : 'Could not load advertising.')
      })
    return () => controller.abort()
  }, [expanded, endpoint, revision])

  function update(index: number, patch: Partial<AdvertisingSlot>) {
    setSnapshot((current) => current && {
      ...current, slots: current.slots.map((slot, i) => i === index ? { ...slot, ...patch } : slot),
    })
    setDirty(true)
    setFeedback('')
  }

  function applyBlock() {
    if (block === null) return
    const lines = block.split('\n')
    if (lines.length > 20 || lines.some((line) => !/^[ -~]{0,20}$/.test(line))) {
      setFeedback('Use up to 20 lines of 20 printable ASCII characters each.')
      return
    }
    setSnapshot((current) => current && {
      ...current, slots: current.slots.map((slot, index) => {
        const text = lines[index] ?? ''
        return { ...slot, text, endSequence: text.length === 0 && slot.endSequence }
      }),
    })
    setBlock(null)
    setDirty(true)
    setFeedback('')
  }

  async function save(event: React.FormEvent) {
    event.preventDefault()
    if (!snapshot || blockChanged) return
    setBusy(true)
    setFeedback('')
    try {
      const { revision, sha256 } = snapshot
      const response = await fetch(endpoint, {
        method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ slots, revision, sha256 }),
      })
      if (!response.ok) throw new Error((await response.json().catch(() => null))?.error ?? 'Could not save advertising.')
      setDirty(false)
      onSaved()
      const latest = await fetch(endpoint)
      if (!latest.ok) {
        setSnapshot(null)
        throw new Error('Advertising was saved. Refresh the page to load the updated record.')
      }
      setSnapshot(await latest.json() as AdvertisingSnapshot)
      setFeedback('Advertising saved to this draft.')
    } catch (error) {
      setFeedback(error instanceof Error ? error.message : 'Could not save advertising.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <details onToggle={(event) => {
      setOpen(event.currentTarget.open)
      if (event.currentTarget.open) setExpanded(true)
    }}>
      <summary>{draft ? 'Edit advertising messages' : 'View advertising messages'}</summary>
      <p className="page-intro">Use up to 20 characters per slot. Duration 0 skips a static slot. Mark the first unused slot in each rotation as End sequence. An unmarked slot with empty text and a duration displays a blank line.</p>
      {typeof onHookEnabled === 'boolean' && <p>On-hook advertising is {onHookEnabled ? 'enabled' : 'disabled'} in the feature record.</p>}
      {!slots && !feedback && <p>Loading messages…</p>}
      {slots && (
        <form onSubmit={(event) => void save(event)}>
          {open && <AdvertisingSimulator slots={slots} features={snapshot} selectedSlot={previewIndex} onSelectSlot={setPreviewIndex} />}
          {draft && block === null && <button type="button" disabled={busy}
            onClick={() => setBlock(slots.map((slot) => slot.text).join('\n'))}>Edit as text block</button>}
          {block !== null && <fieldset disabled={busy}>
            <label>Advertising text block
              <textarea rows={20} cols={20} wrap="off" spellCheck={false} value={block}
                onChange={(event) => setBlock(event.target.value)} />
            </label>
            <button type="button" onClick={applyBlock}>Apply block text</button>
            <button type="button" onClick={() => setBlock(null)}>Cancel block edit</button>
          </fieldset>}
          <div className="table-config-ads">
            {slots.map((slot, index) => (
              <fieldset key={index} disabled={!draft || busy || block !== null}>
                <legend>Message {index + 1}</legend>
                <label>Text<input value={slot.text} maxLength={20} onFocus={() => setPreviewIndex(index)} onChange={(event) => {
                  const text = event.target.value
                  update(index, { text, endSequence: text.length === 0 && slot.endSequence })
                }} /></label>
                <label>Seconds<input type="number" min="0" max="655.35" step="0.01" value={slot.durationTicks / 100} onChange={(event) => update(index, { durationTicks: Math.round(Number(event.target.value) * 100) })} /></label>
                <label>Display mode
                  <select value={slot.mode} onChange={(event) => update(index, { mode: Number(event.target.value) })}>
                    {modes.map((name, i) => <option key={name} value={i}>{name}</option>)}
                    {slot.mode >= modes.length && <option value={slot.mode}>Profile mode {slot.mode}</option>}
                  </select>
                </label>
                <label className="table-config-check"><input type="checkbox" checked={slot.blankBefore} onChange={(event) => update(index, { blankBefore: event.target.checked })} /> Blank before display</label>
                <label className="table-config-check"><input type="checkbox" checked={slot.endSequence} onChange={(event) => update(index, { endSequence: event.target.checked })} /> End sequence</label>
              </fieldset>
            ))}
          </div>
          {draft && <button type="submit" disabled={!dirty || busy || block !== null}>{busy ? 'Saving…' : 'Save advertising'}</button>}
        </form>
      )}
      {feedback && <p role="status">{feedback}</p>}
    </details>
  )
}

function RecordImport({ setId, draft, version, versions, onSaved, record: [number, name] }: Omit<Props, 'versions'> & {
  version: Version | undefined; versions: Version[]; record: [number, string]
}) {
  const [file, setFile] = useState<File | null>(null)
  const [feedback, setFeedback] = useState('')
  const [busy, setBusy] = useState(false)
  const legacyPlaceholder = number === 30 && version?.definitionName === 'Instant-win configuration'

  async function save(event: React.FormEvent) {
    event.preventDefault()
    if (!file) return
    setFeedback('')
    setBusy(true)
    try {
      if (file.size > 65535) throw new Error('The record must be at most 65,535 bytes.')
      const bytes = new Uint8Array(await file.arrayBuffer())
      if (bytes.length <= 2 || bytes[0] !== number) throw new Error(`Choose a complete binary record beginning with MT${number}.`)
      const definitions = await apiGet<Definition[]>('/api/tables/definitions')
      const definitionId = version?.tableDefinitionId ?? definitions.find((item) => item.tableNumber === number &&
        (number !== 30 || item.name === name))?.id ?? (await apiPost<{ id: string }>('/api/tables/definitions', {
        name, tableNumber: number,
      })).id
      const response = await apiPostRaw('/api/tables/versions', {
        tableSetId: setId, tableDefinitionId: definitionId, tableRevision: (version?.tableRevision ?? 0) + 1,
        payloadBase64: btoa(Array.from(bytes, (value) => String.fromCharCode(value)).join('')),
        sortOrder: version?.sortOrder ?? Math.max(-1, ...versions.map((item) => item.sortOrder)) + 1,
        dependsOnTableDefinitionId: version?.dependsOnTableDefinitionId ?? null,
      })
      if (!response.ok) throw new Error((await response.json().catch(() => null))?.error ?? 'Could not import this record.')
      setFeedback(`${name} saved to this draft.`)
      onSaved()
    } catch (error) {
      setFeedback(error instanceof Error ? error.message : 'Could not import this record.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <>
      <p>{version ? `Revision ${version.tableRevision} is in this set.` : 'This record is not in the set.'}</p>
      {legacyPlaceholder && <p>This draft contains the old Instant Win placeholder under MT30. Use a set without the placeholder for user interface parameters.</p>}
      {draft && !legacyPlaceholder && (
        <form className="table-config-selectors" onSubmit={(event) => void save(event)}>
          <label>Binary record for this firmware<input type="file" onChange={(event) => setFile(event.target.files?.[0] ?? null)} /></label>
          <button type="submit" disabled={!file || busy}>{busy ? 'Importing…' : 'Import record'}</button>
        </form>
      )}
      {feedback && <p role="status">{feedback}</p>}
    </>
  )
}

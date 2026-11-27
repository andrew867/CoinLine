import { lazy, Suspense, useEffect, useRef, useState } from 'react'
import { apiFetch, apiGet, apiPost, apiPostRaw } from '../api/client'
import { canPerform, permissionOperation as op } from '../api/consoleSession'
import { AdvertisingSimulator } from './AdvertisingSimulator'
import { DialedNumberReview } from './DialedNumberReview'
import type { AdvertisingFeatures, AdvertisingSlot } from './advertisingPlayback'
import { advertisingPage } from './advertisingPlayback'
import { authorableAdvertisingText, glyphPalettes, normalizedAdvertisingText, advertisingDisplayWidth } from './advertisingGlyphs'
import type { ExchangeFile } from './localDialingExchangeData'

const NativeTableEditor = lazy(async () => ({ default: (await import('./NativeTableEditor')).NativeTableEditor }))
const starterFrames = Object.entries(import.meta.glob<string>('../assets/graphical-starter/*.png',
  { eager: true, query: '?url', import: 'default' })).sort(([a], [b]) => a.localeCompare(b)).map(([, url]) => url)

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
  { name: 'Dialing, screening and service', fields: 'Key destinations, call restrictions, digit translation and screening patterns.', records: [[24, 'Universal Call Screening'], [55, 'Quick Access Keys'], [58, 'Service Level Restrictions'], [59, 'Expanded Call Screening'], [62, 'Numbering plan'], [92, 'Call-Screening List']] },
  { name: 'Rates and local dialing', fields: 'Tariff ladders, area and exchange classification, international rates and discounts.', records: [[73, 'Rate table'], ...segments(74, 8, 'Uncompressed'), [90, 'Uncompressed segment 9'], [91, 'Uncompressed segment 10'], ...segments(101, 15, 'Compressed'), ...segments(136, 14, 'Double-compressed'), [154, 'Double-compressed segment 15'], [155, 'Double-compressed segment 16'], [150, 'NPA set-based rating'], [151, 'International set-based rating'], [152, 'Holiday and time-of-day discounts']] },
  { name: 'Cards, coin and carriers', fields: 'Magnetic and calling card policies, smart cards, coin windows, carrier screens, IOPR and promotions.', records: [[22, 'Card acceptance, older'], [134, 'Card acceptance'], [23, 'Carrier screens, older'], [135, 'Carrier screens'], [50, 'Coin validation'], [72, 'Spare table, e-purse and IOPR'], [93, 'Smart-card parameters'], [95, 'Instant Win']] },
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
  const [importGeneration, setImportGeneration] = useState(0)
  const panel = panels[panelIndex]
  const record = panel.records.find(([number]) => number === tableNumber)!
  const version = versions.find((item) => item.tableNumber === tableNumber)
  const canWriteRecord = draft && canPerform('table-records', version ? op.update : op.add)
  const canImport = canWriteRecord && (version != null || canPerform('table-definitions', op.read))
  const advertisingRevision = [26, 29, 30].map((number) => versions.find((item) => item.tableNumber === number)?.tableRevision ?? 0).join(':')

  function onImported() {
    setImportGeneration(generation => generation + 1)
    onSaved()
  }

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
      {(panel.name === 'Dialing, screening and service' || panel.name === 'Rates and local dialing') &&
        canPerform('table-records', op.read) && <DialedNumberReview setId={setId} recordRevision={versions.map(item => `${item.tableNumber}:${item.tableRevision}`).sort().join(',')} />}
      {draft && !recordDirty && tableNumber >= 131 && tableNumber <= 133 && canPerform('table-records', op.add) &&
        <GraphicalStarterPanel setId={setId} onSaved={onImported} />}
      {tableNumber === 29 ? (
        <AdvertisingEditor key={setId} setId={setId} draft={canWriteRecord} revision={advertisingRevision} onSaved={onSaved} onDirty={onRecordDirty} />
      ) : (
        <div key={`${setId}:${tableNumber}`}>
          <Suspense fallback={<p role="status">Loading record editor…</p>}>
            <NativeTableEditor key={importGeneration} setId={setId} number={tableNumber} revision={version?.tableRevision ?? 0}
              exchangeFile={exchangeFile} onExchangeFile={setExchangeFile}
              onSaved={onSaved} onDirty={onRecordDirty} />
          </Suspense>
          {!recordDirty && canImport && (version ? (
            <details><summary>Import replacement record</summary>
              <RecordImport setId={setId} draft={draft} version={version} versions={versions} onSaved={onImported} record={record} />
            </details>
          ) : <RecordImport setId={setId} draft={draft} version={version}
            versions={versions} onSaved={onImported} record={record} />)}
        </div>
      )}
    </section>
  )
}

function GraphicalStarterPanel({ setId, onSaved }: { setId: string; onSaved: () => void }) {
  const [enableSampleMenu, setEnableSampleMenu] = useState(false)
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState('')
  const [playing, setPlaying] = useState(false)
  const [frame, setFrame] = useState(0)
  const dwellMs = frame < 16 ? 500 : 5000
  useEffect(() => {
    if (!playing || starterFrames.length === 0) return
    const timer = window.setTimeout(() => setFrame(current => (current + 1) % starterFrames.length), dwellMs)
    return () => window.clearTimeout(timer)
  }, [playing, frame, dwellMs])

  async function create() {
    setBusy(true)
    setMessage('')
    try {
      const response = await apiPostRaw(`/api/tables/sets/${setId}/graphical-starter`, { enableSampleMenu })
      const result = await response.json()
      if (!response.ok) throw new Error(result.error ?? 'Could not add graphical starter records.')
      setMessage(result.created === 0 ? 'The matching starter records are already in this draft.'
        : `${result.created} graphical records added. Open the simulator to preview the loop.`)
      onSaved()
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'Could not add graphical starter records.')
    } finally {
      setBusy(false)
    }
  }

  return <details onToggle={event => { if (!event.currentTarget.open) setPlaying(false) }}>
    <summary>Start graphical advertising from sample artwork</summary>
    <p>Add MT131, MT132 and MT133 with sixteen 500 ms globe frames, four static advertisements shown for five seconds each, and an example menu.
      Existing different records are never replaced. Usage statistics and connected-call advertising are disabled initially.</p>
    {starterFrames.length > 0 && <div className="row-actions">
      <img src={starterFrames[frame]} alt="Graphical starter artwork preview" width={224} height={144}
        style={{ imageRendering: 'pixelated', maxWidth: '100%', height: 'auto' }} />
      <button type="button" onClick={() => setPlaying(value => !value)}>
        {playing ? 'Pause sample animation' : 'Play sample animation'}
      </button>
      <span>Screen {frame + 1} of {starterFrames.length} · {frame < 16 ? 'Globe frame, 500 ms' : 'Static advertisement, 5 seconds'}</span>
    </div>}
    <label><input type="checkbox" checked={enableSampleMenu}
      onChange={event => setEnableSampleMenu(event.target.checked)} disabled={busy} />
      Enable the sample off-hook navigation menu (no dialed numbers)
    </label>
    <div className="row-actions">
      <button type="button" onClick={() => void create()} disabled={busy}>{busy ? 'Adding records…' : 'Add graphical starter'}</button>
    </div>
    {message && <p role="status">{message}</p>}
  </details>
}

function AdvertisingEditor({ setId, draft, revision, onSaved, onDirty }: Pick<Props, 'setId' | 'draft' | 'onSaved'> & {
  revision: string
  onDirty: (dirty: boolean) => void
}) {
  const [snapshot, setSnapshot] = useState<AdvertisingSnapshot | null>(null)
  const [originalSlots, setOriginalSlots] = useState<AdvertisingSlot[]>([])
  const slots = snapshot?.slots
  const onHookEnabled = snapshot?.onHookEnabled
  const [previewIndex, setPreviewIndex] = useState(0)
  const [expanded, setExpanded] = useState(false)
  const [open, setOpen] = useState(false)
  const [dirty, setDirty] = useState(false)
  const [block, setBlock] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [feedback, setFeedback] = useState('')
  const [palette, setPalette] = useState<keyof typeof glyphPalettes>('all')
  const slotInputs = useRef<(HTMLInputElement | null)[]>([])
  const endpoint = `/api/tables/sets/${setId}/advertising`
  const blockChanged = block !== null && block !== slots?.map((slot) => slot.text).join('\n')
  const keepsImportedText = (index: number, text: string) =>
    originalSlots[index]?.containsUnmappedGlyphs === true && originalSlots[index].text === text
  const invalid = (slot: AdvertisingSlot, index: number) =>
    !slot.endSequence && !keepsImportedText(index, slot.text) && !authorableAdvertisingText(slot.text)
  const invalidSlots = slots?.some(invalid) ?? false
  const visibleIdleText = slots && snapshot ? advertisingPage(slots, snapshot, 'onHook').active.filter(slot =>
    slot.text.trim().length > 0 && (slot.mode === 0 ? slot.durationTicks > 0 : slot.mode >= 1 && slot.mode <= 5)).length : null

  useEffect(() => onDirty(dirty || blockChanged), [dirty, blockChanged, onDirty])

  useEffect(() => {
    if (!expanded) return
    const controller = new AbortController()
    void apiFetch(endpoint, { signal: controller.signal })
      .then(async (response) => {
        if (!response.ok) throw new Error((await response.json().catch(() => null))?.error ?? 'Could not load text advertising.')
        const data = await response.json() as AdvertisingSnapshot
        if (!controller.signal.aborted) { setSnapshot(data); setOriginalSlots(data.slots) }
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

  function updateText(index: number, text: string) {
    update(index, { text, endSequence: text.length === 0 && slots?.[index].endSequence,
      containsUnmappedGlyphs: keepsImportedText(index, text) })
  }

  function insertGlyph(glyph: string) {
    const slot = slots?.[previewIndex]
    if (!slot || block !== null) return
    const input = slotInputs.current[previewIndex]
    const start = input?.selectionStart ?? slot.text.length
    const end = input?.selectionEnd ?? start
    const next = slot.text.slice(0, start) + glyph + slot.text.slice(end)
    if (Array.from(normalizedAdvertisingText(next)).length > advertisingDisplayWidth) {
      setFeedback('Select a cell to replace before adding another glyph to a full message.')
      return
    }
    updateText(previewIndex, next)
    requestAnimationFrame(() => { input?.focus(); input?.setSelectionRange(start + glyph.length, start + glyph.length) })
  }

  function applyBlock() {
    if (block === null) return
    const lines = block.split('\n')
    if (lines.length > 20 || lines.some((line, index) => !keepsImportedText(index, line) && !authorableAdvertisingText(line))) {
      setFeedback('Use up to 20 lines of 20 display cells each, with ASCII or supported Western glyphs. Replace unmapped cells before editing their text.')
      return
    }
    setSnapshot((current) => current && {
      ...current, slots: current.slots.map((slot, index) => {
        const entered = lines[index] ?? ''
        const text = keepsImportedText(index, entered) ? entered : normalizedAdvertisingText(entered)
        return { ...slot, text, endSequence: text.length === 0 && slot.endSequence,
          containsUnmappedGlyphs: keepsImportedText(index, text) }
      }),
    })
    setBlock(null)
    setDirty(true)
    setFeedback('')
  }

  async function save(event: React.FormEvent) {
    event.preventDefault()
    if (!draft || !snapshot || blockChanged || invalidSlots) return
    setBusy(true)
    setFeedback('')
    try {
      const { revision, sha256 } = snapshot
      const response = await apiFetch(endpoint, {
        method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ slots, revision, sha256 }),
      })
      if (!response.ok) throw new Error((await response.json().catch(() => null))?.error ?? 'Could not save advertising.')
      setDirty(false)
      onSaved()
      const latest = await apiFetch(endpoint)
      if (!latest.ok) {
        setSnapshot(null)
        throw new Error('Advertising was saved. Refresh the page to load the updated record.')
      }
      const data = await latest.json() as AdvertisingSnapshot
      setSnapshot(data); setOriginalSlots(data.slots)
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
      <p className="page-intro">Use up to 20 display cells per slot, including supported Western accents. Duration 0 skips a static slot. Mark the first unused slot in each rotation as End sequence. An unmarked slot with empty text and a duration displays a blank line.</p>
      {typeof onHookEnabled === 'boolean' && <p>On-hook advertising is {onHookEnabled ? 'enabled' : 'disabled'} in the feature record.</p>}
      {onHookEnabled === true && visibleIdleText === 0 && <p role="alert">The idle text rotation has no visible messages. Downloading this MT29 will leave the phone without idle text advertising. Check message 1, the end marker and display durations.</p>}
      {!slots && !feedback && <p>Loading messages…</p>}
      {slots && (
        <form onSubmit={(event) => void save(event)}>
          {open && <AdvertisingSimulator slots={slots} features={snapshot} selectedSlot={previewIndex} onSelectSlot={setPreviewIndex} />}
          {draft && block === null && <details className="advertising-glyph-palette">
            <summary>Display character palette</summary>
            <div className="table-config-selectors"><label>Language palette
              <select value={palette} disabled={busy} onChange={event => setPalette(event.target.value as keyof typeof glyphPalettes)}>
                <option value="all">All supported Western glyphs</option>
                <option value="french">French accents</option><option value="spanish">Spanish accents</option>
              </select></label>
              <span>Insert in message {previewIndex + 1}; focus a text field to choose another message.</span>
            </div>
            <div className="advertising-glyph-buttons">
              {Array.from(glyphPalettes[palette]).map(glyph => <button key={glyph} type="button" className="quiet" disabled={busy}
                aria-label={`Insert ${glyph} into message ${previewIndex + 1}`} onClick={() => insertGlyph(glyph)}>{glyph}</button>)}
            </div>
            <p className="page-intro">These palettes are authoring aids. MT29 has no language selector byte. A compatible Western-font display is required for accented text.</p>
          </details>}
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
                <label>Text<input value={slot.text} maxLength={40} ref={node => { slotInputs.current[index] = node }}
                  aria-invalid={invalid(slot, index) || undefined} onFocus={() => setPreviewIndex(index)}
                  onChange={event => updateText(index, event.target.value)} /></label>
                {slot.containsUnmappedGlyphs && !slot.endSequence && <small className="advertising-import-notice">Imported text contains unmapped cells. The original bytes are retained until its text is changed.</small>}
                {invalid(slot, index) && <small role="alert">Enter at most 20 supported display cells. Replace any unmapped characters before saving.</small>}
                <label>Seconds<input type="number" min="0" max="655.35" step="0.01" value={slot.durationTicks / 100} onChange={(event) => update(index, { durationTicks: Math.round(Number(event.target.value) * 100) })} /></label>
                <label>Display mode
                  <select value={slot.mode} onChange={(event) => update(index, { mode: Number(event.target.value) })}>
                    {modes.map((name, i) => <option key={name} value={i}>{name}</option>)}
                    {slot.mode >= modes.length && <option value={slot.mode}>Profile mode {slot.mode}</option>}
                  </select>
                </label>
                <label className="table-config-check"><input type="checkbox" checked={slot.blankBefore} onChange={(event) => update(index, { blankBefore: event.target.checked })} /> Blank before display</label>
                <label className="table-config-check"><input type="checkbox" checked={slot.endSequence} onChange={(event) => update(index, { endSequence: event.target.checked })} /> End sequence</label>
                <details className="advertising-spares"><summary>Spare fields</summary>
                  <p className="page-intro">Reserved for firmware-specific use. Ordinary edits leave these values intact.</p>
                  {[0x10, 0x20, 0x80].map(mask => <label key={mask} className="table-config-check">
                    <input type="checkbox" checked={((slot.reservedFlags ?? 0) & mask) !== 0}
                      onChange={event => update(index, { reservedFlags: event.target.checked ? (slot.reservedFlags ?? 0) | mask : (slot.reservedFlags ?? 0) & ~mask })} />
                    Spare attribute bit {Math.log2(mask)} (0x{mask.toString(16).toUpperCase()})
                  </label>)}
                  <label>Spare byte<input type="number" min="0" max="255" step="1" value={slot.spareByte ?? 0}
                    onChange={event => update(index, { spareByte: Number(event.target.value) })} /></label>
                </details>
              </fieldset>
            ))}
          </div>
          {draft && <button type="submit" disabled={!dirty || busy || block !== null || invalidSlots}>{busy ? 'Saving…' : 'Save advertising'}</button>}
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
    if (!draft || !file || !canPerform('table-records', version ? op.update : op.add)) return
    setFeedback('')
    setBusy(true)
    try {
      if (file.size > 65535) throw new Error('The record must be at most 65,535 bytes.')
      const bytes = new Uint8Array(await file.arrayBuffer())
      if (bytes.length <= 2 || bytes[0] !== number) throw new Error(`Choose a complete binary record beginning with MT${number}.`)
      let definitionId = version?.tableDefinitionId
      if (!definitionId) {
        const definitions = await apiGet<Definition[]>('/api/tables/definitions')
        definitionId = definitions.find((item) => item.tableNumber === number && (number !== 30 || item.name === name))?.id
        if (!definitionId) {
          if (!canPerform('table-definitions', op.add)) throw new Error('This table definition is missing. Ask an operator with definition creation permission to register it.')
          definitionId = (await apiPost<{ id: string }>('/api/tables/definitions', { name, tableNumber: number })).id
        }
      }
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

import { useEffect, useRef, useState } from 'react'
import { apiFetch, apiPostRaw } from '../api/client'
import { canPerform, permissionOperation as op } from '../api/consoleSession'
import { NativeBitmapField } from './NativeBitmapField'
import { CarrierLibraryPanel } from './CarrierLibraryPanel'
import { CarrierBrandingPreview } from './CarrierLibraryPage'
import { GraphicalFlashField, GraphicalLinkField, GraphicalPanelPreview, GraphicalTextField } from './NativeGraphicalFields'
import { GraphicalAdvertisingSimulator } from './GraphicalAdvertisingSimulator'
import { NumberingPlanEditor, type NumberingPlan, type NumberingPlanChanges } from './NumberingPlanEditor'
import { SpareTableByteEditor, type SpareByteEdits } from './SpareTableByteEditor'
import { LocalDialingImport } from './LocalDialingImport'
import { ScreeningSlotActions, type ScreeningAction } from './ScreeningSlotActions'
import { exchangeCount, type ExchangeFile } from './localDialingExchangeData'
import { describeServiceRestriction } from './serviceRestrictionSchedule'
import { MessagingServicePreview } from './MessagingServicePreview'
import { CollectionPolicyPreview } from './CollectionPolicyPreview'

type Field = {
  key: string
  label: string
  editor: 'checkbox' | 'select' | 'text' | 'number' | 'bitmap' | 'password' | 'password-hex' | 'password-pin'
    | 'screen-kind' | 'screen-text' | 'screen-link' | 'flash-rate' | 'screen-icon' | 'card-standard' | 'discount-method' | 'datetime' | 'time' | 'rate-period'
  value: string | null
  min: number
  max: number
  maxLength: number | null
  choices: { value: number; label: string }[] | null
  tariffIndexOffset?: number | null
  tariffFallbackCode?: number | null
  help: string | null
}

type Section = {
  key: string; label: string; fields: Field[]; showDisplayPreview?: boolean
  graphicalScreenIndex?: number | null; showPanelPreview?: boolean
}
type Layout = {
  supported: true
  editable: boolean
  profile: string
  profiles?: string[]
  length: number
  revision: number
  sha256: string
  sections: Section[]
  numberingPlan?: NumberingPlan | null
  spareBytes?: string | null
  screening?: { descriptions: Record<number, string>; supportsSpeedDial: boolean } | null
} | { supported: false; reason: string; profiles?: string[]; templates?: { profile: string; length: number; screeningSlots?: number | null }[] }

type Props = {
  setId: string
  number: number
  revision: number
  exchangeFile: ExchangeFile | null
  onExchangeFile: (value: ExchangeFile | null) => void
  onSaved: () => void
  onDirty: (dirty: boolean) => void
}

const quickAccessRecord = 55
const isCredential = (field: Field) => field.editor === 'password' || field.editor === 'password-hex' || field.editor === 'password-pin'
const quickAccessNumber = (section: Section) => Number(section.key.slice('key-'.length)) + 1
const screeningNumber = (section: Section) => Number(section.key.slice('rule-'.length)) + 1

export function NativeTableEditor({ setId, number, revision, exchangeFile, onExchangeFile, onSaved, onDirty }: Props) {
  const [layout, setLayout] = useState<Layout | null>(null)
  const [sectionKey, setSectionKey] = useState('')
  const [search, setSearch] = useState('')
  const [edits, setEdits] = useState<Record<string, string>>({})
  const [ruleEdits, setRuleEdits] = useState<NumberingPlanChanges>({})
  const [byteEdits, setByteEdits] = useState<SpareByteEdits>({})
  const [quickAccessLayout, setQuickAccessLayout] = useState('10')
  const [clearedKeys, setClearedKeys] = useState<Set<number>>(new Set())
  const [showAllScreeningSlots, setShowAllScreeningSlots] = useState(false)
  const [screeningActions, setScreeningActions] = useState<Record<number, ScreeningAction>>({})
  const [screeningDescriptions, setScreeningDescriptions] = useState<Record<number, string>>({})
  const [view, setView] = useState<'settings' | 'rules' | 'bytes'>('settings')
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState('')
  const [reload, setReload] = useState(0)
  const [profile, setProfile] = useState('')
  const endpoint = `/api/tables/sets/${setId}/records/${number}/fields`
  const canInitialize = canPerform('table-records', op.add)
  const canEdit = !!layout?.supported && layout.editable && canPerform('table-records', op.update)
  const hasScreeningChanges = Object.keys(screeningActions).length > 0 || Object.keys(screeningDescriptions).length > 0

  useEffect(() => {
    const request = new AbortController()
    const url = profile ? `${endpoint}?profile=${encodeURIComponent(profile)}` : endpoint
    void apiFetch(url, { signal: request.signal }).then(async (response) => {
      if (!response.ok) throw new Error('Could not load this configuration record.')
      const result = await response.json() as Layout
      if (!request.signal.aborted) setLayout(result)
    }).catch((error: unknown) => {
      if (!request.signal.aborted) setMessage(error instanceof Error ? error.message : 'Could not load this record.')
    })
    return () => request.abort()
  }, [endpoint, revision, reload, profile])

  function edit(field: Field, value: string) {
    if (!canEdit) return
    const updated = { ...edits }
    if (field.editor === 'screen-kind' && value !== field.value && layout?.supported) {
      const screen = layout.sections.find((section) => section.fields.includes(field))?.graphicalScreenIndex
      const others = layout.sections.filter((section) => section.graphicalScreenIndex === screen)
        .flatMap((section) => section.fields).filter((item) => item.key !== field.key)
      if ((field.value !== '0' || others.some((item) => item.key in updated)) &&
          !window.confirm('Changing the screen type replaces its content and clears pending edits for this screen. Continue?')) return
      for (const item of others) delete updated[item.key]
    }
    if (field.editor === 'card-standard' && layout?.supported && cardDataKind(value) !== cardDataKind(edits[field.key] ?? field.value)) {
      if (cardDataKind(value) !== cardDataKind(field.value)
          && !window.confirm('Changing card families clears the stored service-code or stored-value data for this range. Continue?')) return
      const prefix = field.key.replace(/\.format$/, '-')
      for (const section of layout.sections.filter((section) => section.key.startsWith(prefix))) {
        for (const item of section.fields) delete updated[item.key]
      }
    }
    if (field.editor === 'discount-method' && value !== (edits[field.key] ?? field.value)) {
      const prefix = field.key.slice(0, -4)
      delete updated[`${prefix}Initial`]
      delete updated[`${prefix}Later`]
    }
    if (value === field.value || (isCredential(field) && value === '')) delete updated[field.key]
    else updated[field.key] = value
    setEdits(updated)
    onDirty(Object.keys(updated).length > 0 || Object.keys(ruleEdits).length > 0 || Object.keys(byteEdits).length > 0
      || clearedKeys.size > 0 || hasScreeningChanges)
    setMessage('')
  }

  function toggleKeyClear(key: number) {
    const updated = new Set(clearedKeys)
    if (!updated.delete(key)) updated.add(key)
    setClearedKeys(updated)
    onDirty(updated.size > 0 || Object.keys(edits).length > 0)
    setMessage('')
  }

  function isClearingField(key: string) {
    const slot = /^key-(\d+)\./.exec(key)
    return slot != null && clearedKeys.has(Number(slot[1]) + 1)
  }

  function controlledScreeningField(key: string) {
    const field = /^rule-(\d+)\.(.+)$/.exec(key)
    if (!field) return false
    const action = screeningActions[Number(field[1]) + 1]
    return !!action && (action.action !== 'speedDial' || ['pattern', 'wildcards', 'translated'].includes(field[2]))
  }

  function stageScreeningAction(slot: number, next?: ScreeningAction) {
    const actions = { ...screeningActions }
    if (next) actions[slot] = next
    else delete actions[slot]
    setScreeningActions(actions)
    onDirty(Object.keys(actions).length > 0 || Object.keys(screeningDescriptions).length > 0 || Object.keys(edits).length > 0)
    setMessage('')
  }

  function describeScreeningSlot(slot: number, value: string) {
    const descriptions = { ...screeningDescriptions }
    if (layout?.supported && value === (layout.screening?.descriptions[slot] ?? '')) delete descriptions[slot]
    else descriptions[slot] = value
    setScreeningDescriptions(descriptions)
    onDirty(Object.keys(descriptions).length > 0 || Object.keys(screeningActions).length > 0 || Object.keys(edits).length > 0)
    setMessage('')
  }

  async function save(event: React.FormEvent) {
    event.preventDefault()
    if (!layout?.supported || !canEdit) return
    const byteEntries = Object.entries(byteEdits)
    if (byteEntries.some(([, value]) => value.length !== 2)) {
      setMessage('Enter two hexadecimal digits for each edited byte.')
      return
    }
    setBusy(true)
    setMessage('')
    try {
      const changes = Object.fromEntries(Object.entries(edits).filter(([key]) => !isClearingField(key) && !controlledScreeningField(key)))
      const response = await apiFetch(endpoint, {
        method: 'PUT', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ revision: layout.revision, sha256: layout.sha256, changes, profile: layout.profile,
          numberingPlan: Object.keys(ruleEdits).length ? ruleEdits : undefined,
          clearQuickAccessKeys: clearedKeys.size ? [...clearedKeys] : undefined,
          screening: hasScreeningChanges ? { actions: Object.values(screeningActions), descriptions: screeningDescriptions } : undefined,
          spareBytes: byteEntries.length ? byteEntries.map(([offset, value]) => ({ offset: Number(offset), value: parseInt(value, 16) })) : undefined }),
      })
      if (!response.ok) {
        const body = await response.json().catch(() => null)
        throw new Error(body?.error ?? 'Could not save this record.')
      }
      setEdits({})
      setRuleEdits({})
      setByteEdits({})
      setClearedKeys(new Set())
      setScreeningActions({})
      setScreeningDescriptions({})
      onDirty(false)
      setMessage('Changes saved to this draft.')
      setReload((count) => count + 1)
      onSaved()
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'Could not save this record.')
    } finally {
      setBusy(false)
    }
  }

  async function initialize(template: { profile: string; length: number }) {
    if (!canInitialize) return
    setBusy(true)
    setMessage('')
    try {
      const response = await apiPostRaw(endpoint, template)
      if (!response.ok) throw new Error((await response.json().catch(() => null))?.error ?? 'Could not create this record.')
      setProfile(template.profile)
      setReload((count) => count + 1)
      onSaved()
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'Could not create this record.')
    } finally {
      setBusy(false)
    }
  }

  function changeProfile(value: string) {
    if ((Object.keys(edits).length > 0 || Object.keys(ruleEdits).length > 0 || Object.keys(byteEdits).length > 0
      || clearedKeys.size > 0 || hasScreeningChanges)
      && !window.confirm('Discard the unsaved changes and switch firmware profiles?')) return
    setEdits({})
    setRuleEdits({})
    setByteEdits({})
    setClearedKeys(new Set())
    setScreeningActions({})
    setScreeningDescriptions({})
    setView('settings')
    onDirty(false)
    setSectionKey('')
    setSearch('')
    setMessage('')
    setProfile(value)
    setLayout(null)
  }

  if (!layout) return <p role="status">{message || 'Loading record fields…'}</p>
  if (!layout.supported) return (
    <div className="native-table-editor">
      <p className="page-intro">{layout.reason}</p>
      {layout.profiles && layout.profiles.length > 1 && <label>Firmware profile
        <select value={profile} onChange={(event) => changeProfile(event.target.value)}>
          <option value="">Select firmware profile</option>
          {layout.profiles.map((option) => <option key={option} value={option}>{option}</option>)}
        </select>
      </label>}
      {canInitialize && layout.templates?.map((template) => (
        <button key={`${template.profile}:${template.length}`} type="button" disabled={busy} onClick={() => void initialize(template)}>
          Start record for {template.profile}{template.screeningSlots != null ? ` (${template.screeningSlots} wire slots)` : ''}
        </button>
      ))}
      {message && <p role="status">{message}</p>}
    </div>
  )

  const needle = search.trim().toLowerCase()
  const isQuickAccess = number === quickAccessRecord
  const legacyScreeningView = !!layout.screening && layout.sections.length === 60
  const settings = layout.sections.filter((section) => (!layout.numberingPlan?.dial || !section.key.startsWith('dial-'))
    && (!isQuickAccess || quickAccessLayout === '10' || quickAccessNumber(section) >= 6)
    && (!legacyScreeningView || showAllScreeningSlots || screeningNumber(section) <= 50))
  const sections = settings.filter((section) => !needle || section.label.toLowerCase().includes(needle)
    || (layout.screening && (screeningDescriptions[screeningNumber(section)] ?? layout.screening.descriptions[screeningNumber(section)])
      ?.toLowerCase().includes(needle))
    || section.fields.some((field) => field.label.toLowerCase().includes(needle) || field.help?.toLowerCase().includes(needle)
      || (field.editor !== 'bitmap' && !isCredential(field)
        && (field.value?.toLowerCase().includes(needle) || edits[field.key]?.toLowerCase().includes(needle)))))
  const current = sections.find((section) => section.key === sectionKey) ?? sections[0]
  const changed = Object.keys(edits).filter(key => !isClearingField(key) && !controlledScreeningField(key)).length
  const ruleChanges = Object.keys(ruleEdits).length > 0
  const byteChanges = Object.keys(byteEdits).length
  const showingRules = layout.numberingPlan && view === 'rules'
  const showingBytes = layout.spareBytes && view === 'bytes'
  const showingSettings = !showingRules && !showingBytes
  const changingKind = current?.graphicalScreenIndex != null && `screen-${current.graphicalScreenIndex}.kind` in edits
  const cardMainSection = current?.key.match(/^card-\d+(?=-(?:service|spill|stored-value|smart-issuers|smart-values)$)/)?.[0]
  const cardStandard = layout.sections.find((section) => section.key === cardMainSection)?.fields
    .find((field) => field.editor === 'card-standard')
  const changingCardFamily = cardStandard && cardDataKind(edits[cardStandard.key] ?? cardStandard.value) !== cardDataKind(cardStandard.value)
  const changingDiscountMethods = current?.fields.filter((field) => field.editor === 'discount-method' && field.key in edits) ?? []
  const replacedDiscountFields = new Set(changingDiscountMethods.flatMap((field) => {
    const prefix = field.key.slice(0, -4)
    return [`${prefix}Initial`, `${prefix}Later`]
  }))
  const effectiveValue = (field: Field) => edits[field.key] ?? field.value
  const keyValue = (section: Section, name: string) => {
    const field = section.fields.find(item => item.key === `${section.key}.${name}`)
    return field && effectiveValue(field)
  }
  const selectedKey = isQuickAccess && current ? quickAccessNumber(current) : null
  const screeningSlot = layout.screening && current ? screeningNumber(current) : null
  const keyCleared = selectedKey != null && clearedKeys.has(selectedKey)
  const carrierReference = selectedKey != null && current && keyValue(current, 'free') === 'true' && keyValue(current, 'featureGroupB') === 'true'
  const carrierRow = (number === 23 || number === 135) && current?.key.startsWith('carrier-') ? Number(current.key.slice(8)) : null
  const displayFields = current?.showDisplayPreview ? current.fields
    : selectedKey != null && current ? current.fields.filter(field => field.key.endsWith('.upper') || field.key.endsWith('.lower')) : []
  const areaField = layout.sections[0]?.fields.find(field => field.key === 'areaCode')
  const exchangeFields = areaField ? layout.sections.slice(1).flatMap(section => section.fields) : []
  const exchangeSegment = areaField && exchangeFields.length === exchangeCount
    ? { area: effectiveValue(areaField), storedArea: areaField.value, maxRate: exchangeFields[0].max,
        fields: exchangeFields.map(field => ({ key: field.key, value: effectiveValue(field) })) } : null

  return (
    <form className="native-table-editor" onSubmit={(event) => void save(event)}>
      <header className="native-table-header">
        <div><strong>{layout.profile}</strong><span>{layout.length.toLocaleString()} bytes · Revision {layout.revision}</span></div>
        {!layout.editable && <span>Published record</span>}
        {layout.editable && !canEdit && <span>Read-only access</span>}
      </header>
      {number >= 131 && number <= 133 && (
        <GraphicalAdvertisingSimulator setId={setId} number={number} sections={layout.sections} edits={edits} />
      )}
      {number === 58 && <section className="table-config-panel" aria-label="Daily restriction preview">
        <h3>Daily restriction preview</h3>
        <p aria-live="polite">{describeServiceRestriction(Object.fromEntries(layout.sections.flatMap(section =>
          section.fields.map(field => [field.key, effectiveValue(field)]))), layout.profile === 'Later packed profile')}</p>
        <p>Restrictions activate when the handset is on hook and its cover is closed. They repeat daily, not on the host clock.</p>
        {layout.sections.some(section => section.fields.some(field => field.key === 'restriction' && effectiveValue(field) === '4')) &&
          <p>Mark the permitted entries in Call-Screening List and Quick Access Keys using <strong>Allow in configurable restricted service</strong>.</p>}
      </section>}
      {number === 68 && <MessagingServicePreview value={key => {
        const field = layout.sections.flatMap(section => section.fields).find(item => item.key === key)
        return field && effectiveValue(field)
      }} />}
      {(number === 32 || number === 34) && <CollectionPolicyPreview number={number} later={layout.profile === 'Later packed profile'} value={key => {
        const field = layout.sections.flatMap(section => section.fields).find(item => item.key === key)
        return field && effectiveValue(field)
      }} />}
      {layout.profiles && layout.profiles.length > 1 && (
        <div className="table-config-selectors">
          <label>Firmware profile
            <select value={layout.profile} disabled={busy} onChange={(event) => changeProfile(event.target.value)}>
              {layout.profiles.map((option) => <option key={option} value={option}>{option}</option>)}
            </select>
          </label>
        </div>
      )}
      {layout.numberingPlan && <div className="row-actions" role="group" aria-label="Numbering-plan editor">
        <button type="button" aria-pressed={!showingRules} onClick={() => setView('settings')}>Settings</button>
        <button type="button" aria-pressed={!!showingRules} onClick={() => setView('rules')}>Numbering rules</button>
      </div>}
      {layout.spareBytes && <div className="row-actions" role="group" aria-label="Spare table editor">
        <button type="button" aria-pressed={showingSettings} onClick={() => setView('settings')}>Settings</button>
        <button type="button" aria-pressed={!!showingBytes} onClick={() => setView('bytes')}>Byte grid</button>
      </div>}
      {exchangeSegment && areaField && <LocalDialingImport segment={exchangeSegment} editable={canEdit && !busy}
        exchangeFile={exchangeFile} onExchangeFile={onExchangeFile} stage={fields => {
        const updated = { ...edits }
        for (const field of [areaField, ...exchangeFields]) {
          if (!(field.key in fields)) continue
          if (fields[field.key] === field.value) delete updated[field.key]
          else updated[field.key] = fields[field.key]
        }
        setEdits(updated)
        onDirty(Object.keys(updated).length > 0)
        setMessage('')
      }} />}
      {showingRules && layout.numberingPlan && <NumberingPlanEditor plan={layout.numberingPlan} changes={ruleEdits}
        editable={canEdit && !busy} onChange={(group, rules) => {
          const updated = { ...ruleEdits }
          if (JSON.stringify(rules) === JSON.stringify(layout.numberingPlan?.[group])) delete updated[group]
          else updated[group] = rules
          setRuleEdits(updated)
          onDirty(changed > 0 || Object.keys(updated).length > 0)
          setMessage('')
        }} />}
      {showingBytes && layout.spareBytes && <SpareTableByteEditor bytes={layout.spareBytes} edits={byteEdits}
        editable={canEdit && !busy} onChange={(updated) => {
          setByteEdits(updated)
          onDirty(changed > 0 || Object.keys(updated).length > 0)
          setMessage('')
        }} />}
      {showingSettings && isQuickAccess && (
        <div className="native-key-panel">
          <div className="table-config-selectors"><label>Panel layout
            <select value={quickAccessLayout} onChange={event => setQuickAccessLayout(event.target.value)}>
              <option value="10">Ten keys</option>
              <option value="5">Five keys (slots 6 through 10)</option>
            </select>
          </label></div>
          {[sections.filter(section => quickAccessNumber(section) <= 5),
            sections.filter(section => quickAccessNumber(section) >= 6)].map((row, index) => row.length > 0 && (
            <div key={index} className="native-key-row" role="group" aria-label={index === 0 ? 'Upper key row' : 'Lower key row'}>
              {row.map(section => {
                const key = quickAccessNumber(section)
                const configured = !!keyValue(section, 'number') || Number(keyValue(section, 'customerReference')) > 0
                  || keyValue(section, 'information') === 'true'
                return <button key={section.key} type="button" className="quiet" aria-pressed={selectedKey === key}
                  onClick={() => setSectionKey(section.key)}>
                  <strong>Key {key}</strong>
                  <small>{clearedKeys.has(key) ? 'Clear pending' : configured ? 'Configured' : 'Blank'}</small>
                </button>
              })}
            </div>
          ))}
        </div>
      )}
      {showingSettings && !isQuickAccess && settings.length > 1 && (
        <div className="table-config-selectors">
          {legacyScreeningView && <label>Screening view
            <select value={showAllScreeningSlots ? 'wire' : 'legacy'} onChange={event => setShowAllScreeningSlots(event.target.value === 'wire')}>
              <option value="legacy">First 50 entries</option>
              <option value="wire">All 60 wire slots</option>
            </select>
          </label>}
          {settings.length > 12 && <label>Find entry<input type="search" value={search} onChange={(event) => setSearch(event.target.value)} /></label>}
          <label>Section or entry
            <select value={current?.key ?? ''} disabled={!current} onChange={(event) => setSectionKey(event.target.value)}>
              {!current && <option value="">No matching entries</option>}
              {sections.map(section => <option key={section.key} value={section.key}>{section.label}</option>)}
            </select>
          </label>
        </div>
      )}
      {showingSettings && current && carrierRow != null && <>
        <CarrierBrandingPreview name={keyValue(current, 'name') ?? ''} />
        <CarrierLibraryPanel key={`${setId}:${number}:${carrierRow}`} setId={setId} number={number} slot={carrierRow}
          revision={layout.revision} sha256={layout.sha256} reference={keyValue(current, 'reference') ?? null}
          editable={canEdit} dirty={changed !== 0 || busy} onApplied={() => { setReload(count => count + 1); onSaved() }} />
      </>}
      {showingSettings && current && (
        <fieldset className="native-table-fields" disabled={!canEdit || busy}>
          <legend>{current.label}</legend>
          {screeningSlot != null && layout.screening && <ScreeningSlotActions key={current.key} slot={screeningSlot}
            action={screeningActions[screeningSlot]}
            description={screeningDescriptions[screeningSlot] ?? layout.screening.descriptions[screeningSlot] ?? ''}
            editable={canEdit} supportsSpeedDial={layout.screening.supportsSpeedDial}
            hasTrailingWildcards={current.fields.some(field => field.key.endsWith('.wildcards'))}
            onAction={action => stageScreeningAction(screeningSlot, action)}
            onDescription={value => describeScreeningSlot(screeningSlot, value)} />}
          {selectedKey != null && canEdit && <div className="row-actions">
            <button type="button" className="quiet" onClick={() => toggleKeyClear(selectedKey)}>
              {keyCleared ? 'Undo clear' : 'Clear key'}
            </button>
            {keyCleared && <span role="status">Key {selectedKey} will be cleared when you save.</span>}
          </div>}
          {changingKind && <p className="native-graphical-status">Save the screen type to load its editor.</p>}
          {changingCardFamily && <p className="native-graphical-status">Save the card standard to load its settings.</p>}
          {changingDiscountMethods.length > 0 && <p className="native-graphical-status">Save the discount methods to edit their new values.</p>}
          {!changingKind && current.showPanelPreview && (
            <GraphicalPanelPreview text={current.fields.slice(0, 4).map(effectiveValue)} keys={current.fields.slice(4, 10).map(effectiveValue)} />
          )}
          {!keyCleared && displayFields.length > 0 && (
            <figure className="native-table-prompt-preview">
              <figcaption>Terminal display preview</figcaption>
              <div className="table-config-vfd" role="img" aria-label={displayFields.map((field) => {
                const text = edits[field.key] ?? field.value
                return `${field.label}, ${text === null ? 'unmapped glyphs' : text.trim() || 'blank'}`
              }).join('. ')}>
                {displayFields.map((field) => (
                  <div key={field.key} aria-hidden="true">
                    {(edits[field.key] ?? field.value ?? 'Unmapped glyphs').padEnd(20)}
                  </div>
                ))}
              </div>
            </figure>
          )}
          {!keyCleared && !changingCardFamily && current.fields.filter((field) => (!changingKind || field.editor === 'screen-kind')
            && !controlledScreeningField(field.key)
            && !replacedDiscountFields.has(field.key)
            && ((number !== 35 && number !== 94) || !field.key.startsWith('start.'))
            && (number !== 35 || !field.key.startsWith('adjust.'))
            && (number !== 152 || !/^period-\d+\.(local|intra|inter|international)$/.test(field.key))).map((field) => (
            <FieldInput key={field.key} field={carrierReference && field.key.endsWith('.rate') ? { ...field, label: 'Carrier reference' } : field}
              value={edits[field.key] ?? field.value ?? ''}
              mandatory={selectedKey != null && (field.key.endsWith('.number') || field.key.endsWith('.customerReference'))
                || screeningSlot != null && field.key.endsWith('.pattern')}
              dirty={field.key in edits} onChange={(value) => edit(field, value)} />
          ))}
        </fieldset>
      )}
      {canEdit && (
        <div className="row-actions">
          <button type="submit" disabled={(changed === 0 && !ruleChanges && byteChanges === 0 && clearedKeys.size === 0 && !hasScreeningChanges) || busy}>{busy ? 'Saving…' : 'Save record'}</button>
          {changed > 0 && <span>{changed} {changed === 1 ? 'field changed' : 'fields changed'}</span>}
          {ruleChanges && <span>Numbering rules changed</span>}
          {byteChanges > 0 && <span>{byteChanges} {byteChanges === 1 ? 'byte changed' : 'bytes changed'}</span>}
          {clearedKeys.size > 0 && <span>{clearedKeys.size} {clearedKeys.size === 1 ? 'key' : 'keys'} to clear</span>}
          {hasScreeningChanges && <span>Screening changes staged</span>}
        </div>
      )}
      {message && <p className="native-table-feedback" role="status">{message}</p>}
    </form>
  )
}

function cardDataKind(standard: string | null) {
  const code = standard === null ? 255 : Number(standard)
  return code >= 1 && code <= 9 ? 'magnetic' : code >= 10 && code <= 16 ? 'stored-value' : 'unknown'
}

function FieldInput({ field, value, dirty, mandatory, onChange }: {
  field: Field; value: string; dirty: boolean; mandatory?: boolean; onChange: (value: string) => void
}) {
  if (field.editor === 'bitmap') {
    return <NativeBitmapField name={field.label} value={field.value === null && !dirty ? null : value}
      savedValue={field.value} onChange={onChange} />
  }
  if (field.editor === 'screen-text') {
    return <GraphicalTextField label={field.label} value={value} unmapped={field.value === null && !dirty} onChange={onChange} />
  }
  if (field.editor === 'screen-link') return <GraphicalLinkField label={field.label} value={value} onChange={onChange} />
  if (field.editor === 'flash-rate') return <GraphicalFlashField label={field.label} value={value} onChange={onChange} />
  if (field.editor === 'screen-icon') {
    const packed = Number(value)
    return <label className="native-table-field" data-dirty={dirty}>
      <span>{field.label}</span>
      <select value={packed & 63} onChange={(event) => onChange(String((packed & 192) | Number(event.target.value)))}>
        {Array.from({ length: 64 }, (_, index) => <option key={index} value={index}>{index}</option>)}
      </select>
    </label>
  }
  if (field.editor === 'checkbox') {
    return <label className="native-table-toggle" data-dirty={dirty}>
      <input type="checkbox" checked={value === 'true'} onChange={(event) => onChange(String(event.target.checked))} />{field.label}
    </label>
  }
  if (field.editor === 'rate-period') return <RatePeriodInput field={field} value={value} dirty={dirty} onChange={onChange} />

  const numeric = field.editor === 'number'
  const calendar = field.editor === 'datetime'
  const time = field.editor === 'time'
  const password = isCredential(field)
  const hex = field.editor === 'password-hex'
  const pin = field.editor === 'password-pin'
  const clearingPin = pin && value === 'clear'
  const passwordLength = pin ? `1,${field.maxLength}` : field.maxLength
  const tariffOffset = field.tariffIndexOffset
  const lastTariffCode = field.tariffFallbackCode == null ? field.max : field.tariffFallbackCode - 1
  const choices = field.choices ?? (tariffOffset == null ? null : [
    { value: 0, label: 'Ask CoinLine' }, { value: 1, label: 'Reject the call' },
    ...Array.from({ length: lastTariffCode - 1 }, (_, index) => ({
      value: index + 2, label: `MT73 rate ${index + 2 + tariffOffset}`,
    })),
    ...(field.tariffFallbackCode == null ? [] : [{ value: field.tariffFallbackCode, label: 'Use configured fallback action' }]),
  ])
  const input = (
    <label className="native-table-field" data-dirty={dirty}>
      <span>{field.label}</span>
      {choices ? (
        <select value={value} onChange={(event) => onChange(event.target.value)}>
          {!choices.some((option) => String(option.value) === value) && <option value={value}>Stored value {value}</option>}
          {choices.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}
        </select>
      ) : (
        <input type={password ? 'password' : numeric ? 'number' : calendar ? 'datetime-local' : time ? 'time' : 'text'}
          data-field-kind={mandatory ? 'mandatory' : undefined}
          value={clearingPin ? '' : value} min={numeric && dirty ? field.min : undefined}
          max={numeric && dirty ? field.max : undefined} step={numeric || calendar || time ? 1 : undefined}
          autoComplete={password ? 'new-password' : undefined} inputMode={password && !hex ? 'numeric' : undefined}
          pattern={password && field.maxLength ? `[${hex ? '0-9A-Fa-f' : '0-9'}]{${passwordLength}}` : undefined}
          maxLength={field.maxLength ?? undefined} required={(numeric || time) && dirty}
          onChange={(event) => {
            const next = event.target.value
            if (!pin || /^[0-9]*$/.test(next)) onChange(next)
          }} />
      )}
      {field.help && <small>{field.help}</small>}
      {field.value === null && !dirty && !password && <small>{
        calendar ? 'No valid terminal date and time is set.' : time ? 'No valid terminal time of day is set.' : 'Stored characters are outside this editor’s format.'
      }</small>}
    </label>
  )
  if (!pin) return input
  return <div className="native-table-pin">
    {input}
    <button type="button" onClick={() => onChange(clearingPin ? '' : 'clear')}>{clearingPin ? 'Keep stored PIN' : 'Clear PIN'}</button>
    {clearingPin && <small role="status">Saving will clear the PIN.</small>}
  </div>
}

function RatePeriodInput({ field, value, dirty, onChange }: {
  field: Field; value: string; dirty: boolean; onChange: (value: string) => void
}) {
  const lastFinite = useRef<string | null>(null)
  const unlimited = value === '-1'
  return <div data-dirty={dirty}>
    <label className="native-table-field">
      <span>{field.label}</span>
      <input type="number" value={unlimited ? '' : value} min={dirty ? 0 : undefined} max={dirty ? field.max : undefined} step={1}
        disabled={unlimited} required={dirty && !unlimited} onChange={(event) => onChange(event.target.value)} />
    </label>
    <label className="native-table-toggle">
      <input type="checkbox" checked={unlimited} onChange={(event) => {
        if (event.target.checked) lastFinite.current = value
        onChange(event.target.checked ? '-1' : lastFinite.current ?? (field.value === '-1' ? '0' : field.value ?? '0'))
      }} />
      Unlimited {field.label.toLowerCase().replace(' (seconds)', '')}
    </label>
  </div>
}

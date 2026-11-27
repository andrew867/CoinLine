import { useEffect, useRef, useState } from 'react'
import { apiFetch, apiPostRaw } from '../api/client'
import { canPerform, consoleSession, permissionOperation as op } from '../api/consoleSession'
import { CraftAccessReveal } from './CraftAccessReveal'
import { NativeBitmapField } from './NativeBitmapField'
import { CarrierLibraryPanel } from './CarrierLibraryPanel'
import { CarrierBrandingPreview } from './CarrierLibraryPage'
import { GraphicalFlashField, GraphicalLinkField, GraphicalPanelPreview, GraphicalTextField } from './NativeGraphicalFields'
import { GraphicalAdvertisingSimulator } from './GraphicalAdvertisingSimulator'
import { GraphicalLinkReviewPanel, type GraphicalLinkReview } from './GraphicalLinkReviewPanel'
import { GraphicalUsageConfigurationPanel } from './GraphicalUsageConfigurationPanel'
import { NumberingPlanEditor, type NumberingPlan, type NumberingPlanChanges } from './NumberingPlanEditor'
import { NumberingPlanOverlayPanel } from './NumberingPlanOverlayPanel'
import { SpareTableByteEditor, type SpareByteEdits } from './SpareTableByteEditor'
import { LocalDialingImport } from './LocalDialingImport'
import { ScreeningSlotActions, type ScreeningAction } from './ScreeningSlotActions'
import { ScreeningBulkPanel } from './ScreeningBulkPanel'
import { exchangeCount, type ExchangeFile } from './localDialingExchangeData'
import { describeServiceRestriction } from './serviceRestrictionSchedule'
import { RestrictedRouteReview, type RestrictedRouteRecord } from './RestrictedRouteReview'
import { MessagingServicePreview } from './MessagingServicePreview'
import { CollectionPolicyPreview } from './CollectionPolicyPreview'
import { InterfaceTimingPanel, interfaceTimingKeys } from './InterfaceTimingPanel'
import { ModemDebounceReview } from './ModemDebounceReview'
import { TimeDiscountCalendar } from './TimeDiscountCalendar'
import { CardSequencePanel } from './CardSequencePanel'
import { QuickAccessCustomerLookup } from './QuickAccessCustomerLookup'
import { authorableDisplayText } from './advertisingGlyphs'
import { advertisingLine, blankAdvertisingLine } from './advertisingPlayback'
import { VisualPromptWorksheet, type VisualPromptCompanion } from './VisualPromptWorksheet'
import { VisualPromptDraftForm, type VisualPromptAuthoringRequest, type VisualPromptAuthoringSpec } from './VisualPromptDraftForm'
import { promptRows, stagePromptChanges, visualPromptRowCount } from './visualPromptWorksheetCodec'
import { RegionalRatingPreview } from './RegionalRatingPreview'
import { LanguageOrderPanel } from './LanguageOrderPanel'
import { languageOrderKeys } from './languageOrderPlan'
import { TerminalTariffPricing } from './TerminalTariffPricing'
import { TerminalTariffReferencePanel } from './TerminalTariffReferencePanel'
import { ManagementAccessDraftForm, type ManagementAccessInput, type ManagementAccessSpec } from './ManagementAccessDraftForm'

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
  isSpare?: boolean
  displayGlyphs?: boolean
  help: string | null
}

type Section = {
  key: string; label: string; fields: Field[]; showDisplayPreview?: boolean
  graphicalScreenIndex?: number | null; showPanelPreview?: boolean
}
type ScreeningFamily = { familyId: string; familyLabel: string; documentedEntries: number;
  institutionalFirst: number | null; institutionalLast: number | null }
type Layout = {
  supported: true
  editable: boolean
  profile: string
  profiles?: string[]
  observedLengthsOnly?: boolean
  length: number
  revision: number
  sha256: string
  sections: Section[]
  graphicalLinks?: GraphicalLinkReview | null
  visualPromptCompanions?: VisualPromptCompanion[] | null
  numberingPlan?: NumberingPlan | null
  numberingFormatCapacity?: number | null
  spareBytes?: string | null
  screening?: { descriptions: Record<number, string>; supportsSpeedDial: boolean; family?: ScreeningFamily | null;
    savedRetainedSlots?: number[] | null } | null
  restrictedRoutes?: RestrictedRouteRecord[] | null
} | { supported: false; reason: string; profiles?: string[]; observedLengthsOnly?: boolean;
  templates?: { profile: string; length: number; screeningSlots?: number | null }[];
  managementAccessTemplates?: ManagementAccessSpec[] }

type Props = {
  setId: string
  number: number
  revision: number
  exchangeFile: ExchangeFile | null
  onExchangeFile: (value: ExchangeFile | null) => void
  onSaved: () => void
  onDirty: (dirty: boolean) => void
  familyId?: string
}

const quickAccessRecord = 55
const isCredential = (field: Field) => field.editor === 'password' || field.editor === 'password-hex' || field.editor === 'password-pin'
const quickAccessNumber = (section: Section) => Number(section.key.slice('key-'.length)) + 1
const screeningNumber = (section: Section) => Number(section.key.slice('rule-'.length)) + 1

export function NativeTableEditor({ setId, number, revision, exchangeFile, onExchangeFile, onSaved, onDirty, familyId }: Props) {
  const [layout, setLayout] = useState<Layout | null>(null)
  const [sectionKey, setSectionKey] = useState('')
  const [search, setSearch] = useState('')
  const [edits, setEdits] = useState<Record<string, string>>({})
  const [ruleEdits, setRuleEdits] = useState<NumberingPlanChanges>({})
  const [byteEdits, setByteEdits] = useState<SpareByteEdits>({})
  const [quickAccessLayout, setQuickAccessLayout] = useState('10')
  const [clearedKeys, setClearedKeys] = useState<Set<number>>(new Set())
  const [screeningView, setScreeningView] = useState<'legacy' | 'institution' | 'wire'>('legacy')
  const [screeningActions, setScreeningActions] = useState<Record<number, ScreeningAction>>({})
  const [screeningDescriptions, setScreeningDescriptions] = useState<Record<number, string>>({})
  const [view, setView] = useState<'settings' | 'rules' | 'bytes' | 'prompts'>('settings')
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState('')
  const [reload, setReload] = useState(0)
  const [profile, setProfile] = useState('')
  const endpoint = `/api/tables/sets/${setId}/records/${number}/fields`
  const screeningFamilyId = [24, 59, 92].includes(number) ? familyId ?? '' : ''
  const canInitialize = canPerform('table-records', op.add)
  const canEdit = !!layout?.supported && layout.editable && canPerform('table-records', op.update)
  const hasScreeningChanges = Object.keys(screeningActions).length > 0 || Object.keys(screeningDescriptions).length > 0

  useEffect(() => {
    const request = new AbortController()
    const query = new URLSearchParams()
    if (profile) query.set('profile', profile)
    if (screeningFamilyId) query.set('familyId', screeningFamilyId)
    const url = query.size ? `${endpoint}?${query}` : endpoint
    void apiFetch(url, { signal: request.signal }).then(async (response) => {
      if (!response.ok) throw new Error('Could not load this configuration record.')
      const result = await response.json() as Layout
      if (!request.signal.aborted) setLayout(result)
    }).catch((error: unknown) => {
      if (!request.signal.aborted) setMessage(error instanceof Error ? error.message : 'Could not load this record.')
    })
    return () => request.abort()
  }, [endpoint, revision, reload, profile, screeningFamilyId])
  useEffect(() => { setScreeningView('legacy') }, [screeningFamilyId])

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
    if (visualPromptRowCount(number, layout.length) !== null) {
      const invalid = Object.entries(edits).find(([, text]) => !authorableDisplayText(text, 20))
      if (invalid) {
        setMessage(`Wire row ${invalid[0].match(/^prompt-(\d+)\.text$/)?.[1] ?? ''} exceeds 20 cells or contains an unsupported glyph.`)
        return
      }
    }
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

  async function initialize(template: { profile: string; length: number; managementAccess?: ManagementAccessInput }) {
    if (!canInitialize) return
    setBusy(true)
    setMessage('')
    try {
      const response = await apiPostRaw(endpoint, template)
      if (!response.ok) throw new Error((await response.json().catch(() => null))?.error ?? 'Could not create this record.')
      setProfile(template.profile)
      setReload((count) => count + 1)
      onDirty(false)
      onSaved()
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'Could not create this record.')
    } finally {
      setBusy(false)
    }
  }

  async function authorPrompts(request: VisualPromptAuthoringRequest) {
    if (!canInitialize) return
    setBusy(true); setMessage('')
    try {
      const response = await apiPostRaw(`${endpoint}/prompt-authoring`, request)
      if (!response.ok) throw new Error((await response.json().catch(() => null))?.error ?? 'Could not create this visual prompt record.')
      setProfile(request.profile)
      setView('prompts')
      setReload(count => count + 1)
      onDirty(false)
      onSaved()
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'Could not create this visual prompt record.')
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
  const promptDraft = layout as Layout & { promptAuthoring?: VisualPromptAuthoringSpec | null; visualPromptCompanions?: VisualPromptCompanion[] | null }
  if (!layout.supported) return (
    <div className="native-table-editor">
      <p className="page-intro">{layout.reason}</p>
      {canInitialize && promptDraft.promptAuthoring && <VisualPromptDraftForm spec={promptDraft.promptAuthoring} busy={busy}
        companions={promptDraft.visualPromptCompanions} onCreate={request => void authorPrompts(request)} onDirty={onDirty} />}
      {layout.observedLengthsOnly && <p className="page-intro">This target has an observed record-length catalogue, not confirmed native field meanings.
        Confirm the selected editor or template against the actual firmware before authoring.</p>}
      {number === 21 && canInitialize && !!layout.managementAccessTemplates?.length &&
        <ManagementAccessDraftForm templates={layout.managementAccessTemplates} busy={busy} onDirty={onDirty}
          onCreate={request => void initialize(request)} />}
      {layout.profiles && layout.profiles.length > 1 && <label>Firmware profile
        <select value={profile} onChange={(event) => changeProfile(event.target.value)}>
          <option value="">Select firmware profile</option>
          {layout.profiles.map((option) => <option key={option} value={option}>{option}</option>)}
        </select>
      </label>}
      {number === 50 && canInitialize && !!layout.templates?.length && <p className="page-intro">
        A new coin record starts with all validator windows and acceptance disabled. Configure the installed validator's coin values, volumes and capacity thresholds before placing the record into service.
      </p>}
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
  const family = layout.screening?.family?.familyId === screeningFamilyId ? layout.screening.family : null
  const packedScreeningSlots = layout.screening ? layout.sections.length : 0
  const documentedScreeningSlots = family?.documentedEntries ?? (packedScreeningSlots === 60 ? 50 : packedScreeningSlots)
  const institutionFirst = family?.institutionalFirst ?? null
  const institutionLast = family?.institutionalLast ?? null
  const legacyScreeningView = !!layout.screening && (documentedScreeningSlots < packedScreeningSlots || institutionFirst != null)
  const institutionSlot = (slot: number) => institutionFirst != null && institutionLast != null && slot >= institutionFirst && slot <= institutionLast
  const retainedSlots = family ? layout.screening?.savedRetainedSlots ?? [] : []
  const outsideSlots = retainedSlots.filter(slot => slot > documentedScreeningSlots)
  const retainedInstitutionSlots = retainedSlots.filter(institutionSlot)
  const inScreeningView = (slot: number) => screeningView === 'wire' || !legacyScreeningView ||
    (screeningView === 'institution' ? institutionSlot(slot) : slot <= documentedScreeningSlots && !institutionSlot(slot))
  const settings = layout.sections.filter((section) => (!layout.numberingPlan?.dial || !section.key.startsWith('dial-'))
    && (!isQuickAccess || quickAccessLayout === '10' || quickAccessNumber(section) >= 6)
    && (!layout.screening || inScreeningView(screeningNumber(section))))
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
  const promptCount = visualPromptRowCount(number, layout.length)
  const worksheetRows = promptCount !== null ? promptRows(layout.sections, promptCount) : null
  const showingPrompts = worksheetRows !== null && view === 'prompts'
  const showingSettings = !showingRules && !showingBytes && !showingPrompts
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
  const customerReferenceField = selectedKey != null && current ? current.fields.find(field => field.key.endsWith('.customerReference')) : undefined
  const screeningSlot = layout.screening && current ? screeningNumber(current) : null
  const keyCleared = selectedKey != null && clearedKeys.has(selectedKey)
  const carrierReference = selectedKey != null && current && keyValue(current, 'free') === 'true' && keyValue(current, 'featureGroupB') === 'true'
  const carrierRow = (number === 23 || number === 135) && current?.key.startsWith('carrier-') ? Number(current.key.slice(8)) : null
  const displayFields = current?.showDisplayPreview ? current.fields
    : selectedKey != null && current ? current.fields.filter(field => field.key.endsWith('.upper') || field.key.endsWith('.lower')) : []
  const visibleFields = current?.fields.filter((field) => (!changingKind || field.editor === 'screen-kind')
    && !controlledScreeningField(field.key)
    && !replacedDiscountFields.has(field.key)
    && (field.editor !== 'screen-icon' || !current.fields.some(item => item.key === `${field.key}Id`))
    && (number !== 94 || field.key !== 'spareParameters' || !current.fields.some(item => item.isSpare))
    && ((number !== 35 && number !== 94) || !field.key.startsWith('start.'))
    && (number !== 35 || !field.key.startsWith('adjust.'))
    && (number !== 152 || !/^period-\d+\.(local|intra|inter|international)$/.test(field.key))) ?? []
  const spareFields = visibleFields.filter(field => field.isSpare)
  const editedSpareCount = spareFields.filter(field => field.key in edits).length
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
      {layout.observedLengthsOnly && <p className="page-intro">Observed firmware profile: this editor is a candidate for the wire record.
        Record-length acknowledgement does not establish that every field has the same meaning on this build.</p>}
      {family && <section aria-label="Screening family workspace" className="table-config-panel">
        <h3>{family.familyLabel} screening</h3>
        <p>Maximizer documents {documentedScreeningSlots} entries; this record has {packedScreeningSlots} packed slots.
          The documented limit does not establish which slots the actual terminal uses.</p>
        {institutionFirst != null && <p>Entries {institutionFirst} through {institutionLast} are reserved for the separate institutional configuration workflow.
          The normal operator view starts with entries 1 through {institutionFirst - 1}. Institutional entries remain available for review of imported records.</p>}
        {retainedInstitutionSlots.length > 0 && <p>Institutional slots with retained settings or descriptions: {retainedInstitutionSlots.join(', ')}.</p>}
        {outsideSlots.length > 0 && <p>Slots beyond the documented list with retained settings or descriptions: {outsideSlots.join(', ')}.</p>}
        {documentedScreeningSlots > packedScreeningSlots && <p>The packed record is shorter than the documented list. Verify the target and layout before delivery.</p>}
        <p>These counts use the saved record. Changing views does not clear entries, modify bytes, change the firmware target, or discard unsaved edits.</p>
      </section>}
      {(number === 22 || number === 134) && <CardSequencePanel setId={setId} number={number} revision={layout.revision}
        sha256={layout.sha256} editable={canEdit} dirty={changed > 0} busy={busy} onBusyChange={setBusy}
        labels={Object.fromEntries(layout.sections.filter(section => /^card-\d+$/.test(section.key)).map(section => {
          const low = keyValue(section, 'start'), high = keyValue(section, 'end')
          return [Number(section.key.slice(5)) + 1, low && high ? `${low} to ${high}` : '']
        }))} onReload={() => { setLayout(null); setReload(count => count + 1); onSaved() }}
        onMoved={(to, carrierChanged) => {
          setMessage(`Card entry moved to position ${to}.${carrierChanged ? ' Matching carrier acceptance was updated.' : ' Carrier acceptance was unchanged.'}`)
          setSectionKey(`card-${to - 1}`); setSearch(''); setLayout(null); setReload(count => count + 1); onSaved()
        }} onConfigured={(action, position, carrierChanged) => {
          const operation = ({ add: 'added', remove: 'removed', refresh: 'refreshed', link: 'linked' } as Record<string, string>)[action] ?? 'updated'
          setMessage(`Card position ${position} ${operation}.${carrierChanged ? ' Matching carrier acceptance was updated.' : ' Carrier acceptance was unchanged.'}`)
          setSectionKey(`card-${position - 1}`); setSearch(''); setLayout(null); setReload(count => count + 1); onSaved()
        }} />}
      {number >= 131 && number <= 133 && <>
        <GraphicalAdvertisingSimulator setId={setId} number={number} sections={layout.sections} edits={edits} />
        <GraphicalLinkReviewPanel review={layout.graphicalLinks ?? null} number={number}
          onOpenSection={key => { setSearch(''); setSectionKey(key) }} />
        <GraphicalUsageConfigurationPanel usage={layout.graphicalLinks?.usage} records={layout.graphicalLinks?.records ?? []}
          number={number} edits={edits} onOpenSection={key => { setSearch(''); setSectionKey(key) }} />
      </>}
      {((number === 150 && layout.length === 401) || (number === 151 && layout.length === 604)) &&
        <RegionalRatingPreview key={`${setId}:${number}`} number={number as 150 | 151}
          value={key => {
            const field = layout.sections.find(section => section.key === key.split('.')[0])?.fields.find(item => item.key === key)
              ?? layout.sections[0]?.fields.find(item => item.key === key)
            return field && effectiveValue(field)
          }} onOpen={key => { setSearch(''); setSectionKey(key) }} />}
      {number === 58 && <section className="table-config-panel" aria-label="Daily restriction preview">
        <h3>Daily restriction preview</h3>
        <p aria-live="polite">{describeServiceRestriction(Object.fromEntries(layout.sections.flatMap(section =>
          section.fields.map(field => [field.key, effectiveValue(field)]))), layout.profile === 'Later packed profile')}</p>
        <p>Restrictions activate when the handset is on hook and its cover is closed. They repeat daily, not on the host clock.</p>
        {layout.sections.some(section => section.fields.some(field => field.key === 'restriction' && effectiveValue(field) === '4')) &&
          <RestrictedRouteReview records={layout.restrictedRoutes ?? null} />}
      </section>}
      {number === 68 && <MessagingServicePreview value={key => {
        const field = layout.sections.flatMap(section => section.fields).find(item => item.key === key)
        return field && effectiveValue(field)
      }} />}
      {(number === 32 || number === 34) && <CollectionPolicyPreview number={number} later={layout.profile === 'Later packed profile'} value={key => {
        const field = layout.sections.flatMap(section => section.fields).find(item => item.key === key)
        return field && effectiveValue(field)
      }} />}
      {number === 152 && layout.length === 95 && <TimeDiscountCalendar key={setId} setId={setId} revision={layout.revision} value={key => {
        const field = layout.sections.flatMap(section => section.fields).find(item => item.key === key)
        return field && effectiveValue(field)
      }} />}
      {number === 30 && layout.length === 68 && <InterfaceTimingPanel key={`${setId}:${layout.revision}:${layout.sha256}`}
        timers={layout.sections.flatMap(section => section.fields).filter(field =>
          field.editor === 'number' && interfaceTimingKeys.some(key => key === field.key)).map(field => ({
          key: field.key, label: field.label.replace(/ \(10 ms ticks\)$/, ''), value: effectiveValue(field), changed: field.key in edits,
        }))} editable={canEdit && !busy} onChange={(key, value) => {
          const field = layout.sections.flatMap(section => section.fields).find(item => item.key === key)
          if (field) edit(field, value)
        }} />}
      {number === 33 && layout.length === 36 && <ModemDebounceReview later={layout.profile === 'Later packed profile'} value={key => {
        const field = layout.sections.flatMap(section => section.fields).find(item => item.key === key)
        return field && effectiveValue(field)
      }} onOpen={key => { setSearch(''); setSectionKey(key) }} />}
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
      {layout.numberingPlan && canEdit && <NumberingPlanOverlayPanel key={`${layout.profile}:${layout.revision}:${layout.sha256}`}
        endpoint={endpoint} revision={layout.revision} sha256={layout.sha256} profile={layout.profile}
        dirty={changed > 0 || ruleChanges || byteChanges > 0} busy={busy} onBusyChange={setBusy}
        onApplied={() => { setMessage('Numbering overlay applied to the draft.'); setLayout(null); setReload(count => count + 1); onSaved() }} />}
      {layout.spareBytes && <div className="row-actions" role="group" aria-label="Spare table editor">
        <button type="button" aria-pressed={showingSettings} onClick={() => setView('settings')}>Settings</button>
        <button type="button" aria-pressed={!!showingBytes} onClick={() => setView('bytes')}>Byte grid</button>
      </div>}
      {worksheetRows && <div className="row-actions" role="group" aria-label="Visual prompt editor">
        <button type="button" aria-pressed={!showingPrompts} onClick={() => setView('settings')}>Individual messages</button>
        <button type="button" aria-pressed={showingPrompts} onClick={() => setView('prompts')}>Visual prompt worksheet</button>
      </div>}
      {showingPrompts && worksheetRows && <VisualPromptWorksheet key={`${number}:${layout.revision}:${layout.sha256}`}
        number={number} rows={worksheetRows} edits={edits} editable={canEdit && !busy} companions={layout.visualPromptCompanions}
        onStage={changes => {
          const updated = stagePromptChanges(worksheetRows, edits, changes)
          setEdits(updated); onDirty(Object.keys(updated).length > 0); setMessage('')
        }}
        onUndo={key => {
          const updated = { ...edits }; delete updated[key]
          setEdits(updated); onDirty(Object.keys(updated).length > 0); setMessage('')
        }} />}
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
        formatCapacity={layout.numberingFormatCapacity ?? 132}
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
            <select value={screeningView} onChange={event => setScreeningView(event.target.value as typeof screeningView)}>
              <option value="legacy">{institutionFirst != null ? `Operator entries 1 through ${institutionFirst - 1}` : `First ${documentedScreeningSlots} entries`}</option>
              {institutionFirst != null && <option value="institution">Institutional entries {institutionFirst} through {institutionLast}</option>}
              <option value="wire">All {packedScreeningSlots} wire slots</option>
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
      {showingSettings && current && screeningSlot != null && layout.screening && canPerform('table-records', op.update) &&
        <ScreeningBulkPanel key={`${setId}:${number}:${screeningSlot}`} setId={setId} number={number} revision={layout.revision}
          sha256={layout.sha256} profile={layout.profile} slot={screeningSlot}
          dirty={busy || Object.keys(edits).length > 0 || hasScreeningChanges}
          onApplied={sourceChanged => { if (sourceChanged) { setReload(count => count + 1); onSaved() } }} />}
      {showingSettings && screeningSlot != null && family && institutionSlot(screeningSlot) &&
        <p className="page-intro">Entry {screeningSlot} is in the documented institutional range. Confirm that any change belongs in this configuration rather than the separate institutional setup.</p>}
      {showingSettings && screeningSlot != null && family && screeningSlot > documentedScreeningSlots &&
        <p className="page-intro">Entry {screeningSlot} is outside the documented list. Its active use on this terminal is not established.</p>}
      {showingSettings && number === 26 && layout.length === 72 && current?.key === 'display' &&
        ['languageCount', ...languageOrderKeys].every(key => current.fields.some(field => field.key === key)) &&
        <LanguageOrderPanel value={key => {
          const field = current.fields.find(item => item.key === key)
          return field && effectiveValue(field)
        }} editable={canEdit && !busy} onStage={changes => {
          if (!canEdit || busy) return
          const updated = { ...edits }
          for (const [key, value] of Object.entries(changes)) {
            const field = current.fields.find(item => item.key === key)
            if (!field || !languageOrderKeys.some(item => item === key)) continue
            if (field.value === value) delete updated[key]
            else updated[key] = value
          }
          setEdits(updated)
          onDirty(Object.keys(updated).length > 0 || Object.keys(ruleEdits).length > 0 || Object.keys(byteEdits).length > 0
            || clearedKeys.size > 0 || hasScreeningChanges)
          setMessage('')
        }} />}
      {showingSettings && current && number === 73 && /^rate-\d+$/.test(current.key) &&
        <TerminalTariffReferencePanel key={`${setId}:${current.key}:${layout.revision}`} setId={setId} rateIndex={Number(current.key.slice(5))} />}
      {showingSettings && current?.key === 'craft' && number === 31 && consoleSession.role() === 'Admin' && !busy &&
        <CraftAccessReveal key={`${endpoint}:${layout.revision}:${layout.sha256}:${layout.profile}:${reload}`} endpoint={endpoint}
          revision={layout.revision} sha256={layout.sha256} profile={layout.profile} />}
      {showingSettings && current && (
        <fieldset className="native-table-fields" disabled={!canEdit || busy}>
          <legend>{current.label}</legend>
          {number === 73 && /^rate-\d+$/.test(current.key) &&
            ['classification', 'initialPeriod', 'initialCharge', 'additionalPeriod', 'additionalCharge'].every(name => current.fields.some(field => field.key === `${current.key}.${name}`)) &&
            <TerminalTariffPricing key={`${current.key}:${layout.revision}:${current.fields.map(field => effectiveValue(field)).join(':')}`} value={name => keyValue(current, name)} editable={canEdit && !busy} onStage={changes => {
              if (!canEdit || busy) return
              const updated = { ...edits }
              for (const [name, value] of Object.entries(changes)) {
                const field = current.fields.find(item => item.key === `${current.key}.${name}`)
                if (!field) continue
                if (field.value === value) delete updated[field.key]
                else updated[field.key] = value
              }
              setEdits(updated)
              onDirty(Object.keys(updated).length > 0 || Object.keys(ruleEdits).length > 0 || Object.keys(byteEdits).length > 0
                || clearedKeys.size > 0 || hasScreeningChanges)
              setMessage('')
            }} />}
          {(number === 132 || number === 133) && current.key === 'rotation' && <p className="page-intro">
            This record retains its own header bytes. The verified graphical terminal uses MT131 to control rotation for all 24 screens.
          </p>}
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
                const text = effectiveValue(field)?.normalize('NFC') ?? null
                return `${field.label}, ${text === null ? 'unmapped glyphs' : text.trim() || 'blank'}`
              }).join('. ')}>
                {displayFields.map((field) => (
                  <div key={field.key} aria-hidden="true">
                    {(effectiveValue(field)?.normalize('NFC') ?? 'Unmapped glyphs').padEnd(20)}
                  </div>
                ))}
              </div>
            </figure>
          )}
          {number === 95 && current.key === 'display' && <PromotionPromptPreview
            prompts={current.fields.filter(field => field.displayGlyphs).map(field => ({ label: field.label, text: effectiveValue(field) }))} />}
          {!keyCleared && !changingCardFamily && visibleFields.filter(field => !field.isSpare).map((field) => (
            <FieldInput key={field.key} field={carrierReference && field.key.endsWith('.rate') ? { ...field, label: 'Carrier reference' } : field}
              value={edits[field.key] ?? field.value ?? ''}
              mandatory={selectedKey != null && (field.key.endsWith('.number') || field.key.endsWith('.customerReference'))
                || screeningSlot != null && field.key.endsWith('.pattern')}
              dirty={field.key in edits} onChange={(value) => edit(field, value)} />
          ))}
          {!keyCleared && customerReferenceField && canEdit && <QuickAccessCustomerLookup key={customerReferenceField.key}
            value={effectiveValue(customerReferenceField) ?? ''} onSelect={value => edit(customerReferenceField, value)} />}
          {!keyCleared && !changingCardFamily && spareFields.length > 0 && <details key={`spares:${current.key}`} className="native-spare-fields">
            <summary>Spare fields{editedSpareCount > 0 ? ` (${editedSpareCount} changed)` : ''}</summary>
            <p className="page-intro">These typed values have no assigned behavior in the verified profile. Normal field edits, blanking and clearing leave them intact. Change them only for a documented firmware use.</p>
            <div className="native-spare-grid">{spareFields.map(field =>
              <FieldInput key={field.key} field={field} value={edits[field.key] ?? field.value ?? ''}
                dirty={field.key in edits} onChange={value => edit(field, value)} />)}</div>
          </details>}
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

function PromotionPromptPreview({ prompts }: { prompts: { label: string; text: string | null }[] }) {
  const [index, setIndex] = useState(0)
  const selected = prompts[index]
  if (!selected) return null
  const line = selected.text === null ? 'Unmapped glyphs'.padEnd(20) : advertisingLine(selected.text)
  return <section className="native-table-prompt-preview" aria-label="Promotion message preview">
    <div className="table-config-selectors">
      <label>Preview promotion prompt<select value={index} onChange={event => setIndex(Number(event.target.value))}>
        {prompts.map((prompt, promptIndex) => <option key={prompt.label} value={promptIndex}>{prompt.label}</option>)}
      </select></label>
    </div>
    <div className="table-config-vfd" role="img" aria-label={`${selected.label}, ${line.trim() || 'blank'}`}>
      <div aria-hidden="true">{blankAdvertisingLine}</div><div aria-hidden="true">{line}</div>
    </div>
    <p className="page-intro">Preview the selected display message. Contest eligibility and animation depend on compatible terminal firmware.</p>
  </section>
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
  const glyphWidth = field.displayGlyphs ? field.maxLength : null
  const invalidGlyphs = glyphWidth != null && dirty && !authorableDisplayText(value, glyphWidth)
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
          maxLength={glyphWidth != null ? glyphWidth * 2 : field.maxLength ?? undefined} required={(numeric || time) && dirty}
          aria-invalid={invalidGlyphs || undefined}
          onChange={(event) => {
            const next = event.target.value
            if (!pin || /^[0-9]*$/.test(next)) onChange(next)
          }} />
      )}
      {field.help && <small>{field.help}</small>}
      {invalidGlyphs && <small role="alert">Enter at most {glyphWidth} display cells using ASCII or supported Western glyphs.</small>}
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

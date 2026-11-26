import { useState } from 'react'
import { Link } from 'react-router-dom'
import { apiPostRaw } from '../api/client'
import { TerminalFamilySelect } from '../components/TerminalFamilySelect'

type Source = { id: string; name: string; status: number; isDefault?: boolean; targetFirmwareVersionId?: string | null; targetFirmware: string | null }
type Choices = { name: string; familyId: string; paymentMode: number; pricing: string; tariffIndex: string;
  firstChargeCents: string; followingChargeCents: string; advertising: string }
type Preview = { previewToken: string; canCreate: boolean; sourceIsStarterDraft?: boolean; targetFirmware: string; changedNumbers: number[]; recordCount: number;
  family: { familyLabel: string; documented: boolean; tables: { name: string; required: boolean; status: string }[] };
  provisioning: { operatingType: string | null; acceptedCoinSlots: number | null; configuredCardRanges: number | null;
    visibleOnHookTextMessages?: number | null; onHookAdvertising?: boolean | null; graphicalOnHookAdvertising?: boolean | null;
    findings: { tableNumber: number; message: string }[] };
  findings: { tableNumber: number; blocking: boolean; message: string }[] }

const steps = ['Choose a starting configuration', 'Payment and pricing', 'Review and create draft']
const validInteger = (raw: string, max: number) => /^\d+$/.test(raw) && Number(raw) <= max

export function ConfigurationSetupWizard({ sources, onCreated }: { sources: Source[]; onCreated: () => void }) {
  const available = sources.filter(source => !!source.targetFirmwareVersionId && (source.status === 1 || (source.status === 0 && source.isDefault)))
  const [step, setStep] = useState(0)
  const [sourceId, setSourceId] = useState('')
  const [choices, setChoices] = useState<Choices>({ name: '', familyId: '', paymentMode: 0, pricing: 'keep', tariffIndex: '',
    firstChargeCents: '25', followingChargeCents: '10', advertising: 'keep' })
  const [review, setReview] = useState<Preview | null>(null)
  const [created, setCreated] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const source = available.find(item => item.id === sourceId)
  const local = ['flat', 'minute', 'split'].includes(choices.pricing)
  const valid = !local || validInteger(choices.tariffIndex, 127) && validInteger(choices.firstChargeCents, 65535)
    && (choices.pricing !== 'split' || validInteger(choices.followingChargeCents, 65535))
  function change<K extends keyof Choices>(key: K, value: Choices[K]) {
    setChoices(previous => ({ ...previous, [key]: value, ...(key === 'familyId' && typeof value === 'string'
      ? { advertising: value.endsWith('-graphical') ? 'graphical' : previous.advertising === 'graphical' ? 'keep' : previous.advertising } : {}) }))
    setReview(null); setError('')
  }

  async function submit(create: boolean) {
    if (!source || busy || !valid || create && !review?.canCreate) return
    setBusy(true); setError('')
    try {
      const response = await apiPostRaw(`/api/tables/sets/${source.id}/setup${create ? '' : '/preview'}`, {
        name: choices.name.trim(), familyId: choices.familyId, paymentMode: choices.paymentMode, pricing: choices.pricing,
        tariffIndex: local ? Number(choices.tariffIndex) : null, firstChargeCents: local ? Number(choices.firstChargeCents) : null,
        followingChargeCents: choices.pricing === 'split' ? Number(choices.followingChargeCents) : null,
        idleAdvertisingMode: choices.advertising,
        previewToken: create ? review?.previewToken : null,
      })
      const body = await response.json().catch(() => null)
      if (!response.ok) {
        if (create && response.status === 409) { setReview(null); setStep(1) }
        throw new Error(body?.error ?? `Setup request failed (${response.status}).`)
      }
      if (create) { setCreated(body.id); onCreated() }
      else { setReview(body); setStep(2) }
    } catch (reason) { setError(reason instanceof Error ? reason.message : 'Could not prepare this configuration.') }
    finally { setBusy(false) }
  }

  return <section className="table-config-panel" aria-label="Guided configuration setup">
    <h2>Guided configuration setup</h2>
    <p>Start from a published configuration or a designated default starter for a matching registered firmware. This creates a reviewed draft only, never an assignment or a download.</p>
    {created ? <>
      <h3>Draft created</h3><p role="status">Your source and its phones are unchanged.</p>
      <ul>
        <li><Link to={`/table-sets/${created}?record=21`}>Review the terminal identity and management access</Link></li>
        <li><Link to={`/table-sets/${created}?record=26`}>Check payment options and terminal features</Link></li>
        <li><Link to={`/table-sets/${created}?record=92`}>Check the local route and its rate reference</Link></li>
        {local && <li><Link to={`/table-sets/${created}?record=73`}>Review the selected terminal tariff</Link></li>}
        {choices.pricing === 'host' && <li><Link to="/rate-plans">Configure and publish the customer's real-time host rates</Link></li>}
        <li><Link to={`/table-sets/${created}`}>Open the draft for family review, publication and assignment</Link></li>
      </ul>
    </> : <>
      <p role="status">Step {step + 1} of {steps.length}: {steps[step]}</p>
      {error && <p role="alert">{error}</p>}
      {step === 0 && <fieldset disabled={busy}>
        <legend>Terminal and configuration</legend>
        {available.length === 0 && <p>There are no published or default starter configurations with a registered firmware target. Choose the intended firmware in the ordinary editor first.</p>}
        <label>Starting configuration<select value={sourceId} onChange={event => { setSourceId(event.target.value); setReview(null); setError('') }}>
          <option value="">Choose a configuration</option>
          {available.map(item => <option key={item.id} value={item.id}>{item.name} ({item.targetFirmware}){item.status === 0 ? ' · Default starter draft' : ''}</option>)}
        </select></label>
        {source?.status === 0 && <p role="status">This starter is an unpublished example. The wizard copies it without changing it; missing site-specific and required records will still need to be completed.</p>}
        <TerminalFamilySelect value={choices.familyId} onChange={value => change('familyId', value)} emptyLabel="Choose family" />
        <label>Draft configuration name<input maxLength={120} value={choices.name} onChange={event => change('name', event.target.value)} /></label>
        <button type="button" disabled={!source || !choices.familyId || !choices.name.trim()} onClick={() => setStep(1)}>Next: payment and pricing</button>
      </fieldset>}
      {step === 1 && <fieldset disabled={busy}>
        <legend>Payment, local rates and display</legend>
        <label>Which payments should the terminal accept?<select value={choices.paymentMode} onChange={event => change('paymentMode', Number(event.target.value))}>
          <option value={0}>Keep the base's operating mode</option><option value={1}>Card only</option><option value={2}>Multi-pay: card and coin</option><option value={3}>Coin only</option>
        </select></label>
        <label>How should the local call be priced?<select value={choices.pricing} onChange={event => change('pricing', event.target.value)}>
          <option value="keep">Keep the base's pricing</option><option value="host">Real-time host rating (review route and host plan separately)</option>
          <option value="flat">One charge for the whole call</option><option value="minute">Same charge for every minute</option><option value="split">Different first and following minutes</option>
        </select></label>
        {local && <>
          <label>MT73 rate row used by the local route (0 to 127)<input type="number" min={0} max={127} step={1} value={choices.tariffIndex}
            onChange={event => change('tariffIndex', event.target.value)} /></label>
          <p>Use the rate index referenced by the local screening or exchange entry. This wizard does not silently change dialing or routing rules.</p>
          <label>{choices.pricing === 'flat' ? 'Whole call charge (cents)' : choices.pricing === 'minute' ? 'Charge each minute (cents)' : 'First minute including connection (cents)'}
            <input type="number" min={0} max={65535} step={1} value={choices.firstChargeCents} onChange={event => change('firstChargeCents', event.target.value)} /></label>
          {choices.pricing === 'split' && <label>Each following minute (cents)<input type="number" min={0} max={65535} step={1}
            value={choices.followingChargeCents} onChange={event => change('followingChargeCents', event.target.value)} /></label>}
        </>}
        <label>Idle advertising mode<select value={choices.advertising} onChange={event => change('advertising', event.target.value)}>
          <option value="keep">Keep the base setting</option><option value="text">Two-line text rotation</option>
          {choices.familyId.endsWith('-graphical') && <option value="graphical">Graphical rotation (preferred for large-screen terminals)</option>}
          <option value="none">No idle advertisements</option>
        </select></label>
        {choices.familyId.endsWith('-graphical') && <p>Graphical rotation disables the MT26 text rotation and enables the MT131 idle loop. Text messages and graphical screens are retained; a normal terminal restart may be required after download.</p>}
        <p>Payment mode does not automatically enable coin windows, card ranges or a real payment provider. These are checked in the review.</p>
        <button type="button" className="quiet" onClick={() => setStep(0)}>Back</button>{' '}
        <button type="button" disabled={!valid || busy} onClick={() => void submit(false)}>{busy ? 'Reviewing…' : 'Review draft'}</button>
      </fieldset>}
      {step === 2 && review && <div>
        {review.sourceIsStarterDraft && <p role="status">This is a copy of an unpublished starter. Complete the required records and site-specific settings before publishing or assigning it.</p>}
        <dl><dt>Target firmware</dt><dd>{review.targetFirmware}</dd><dt>Terminal family</dt><dd>{review.family.familyLabel}</dd>
          <dt>Records copied</dt><dd>{review.recordCount}</dd><dt>Records changed</dt><dd>{review.changedNumbers.length ? review.changedNumbers.map(number => `MT${number}`).join(', ') : 'None'}</dd>
          <dt>Operating mode</dt><dd>{review.provisioning.operatingType ?? 'Not verified'}</dd>
          <dt>Payable coin slots</dt><dd>{review.provisioning.acceptedCoinSlots ?? 'Not verified'}</dd>
          <dt>Card acceptance ranges</dt><dd>{review.provisioning.configuredCardRanges ?? 'Not verified'}</dd>
          {review.provisioning.onHookAdvertising != null && <><dt>Idle text rotation (MT26)</dt><dd>{review.provisioning.onHookAdvertising ? 'Enabled' : 'Disabled'}</dd></>}
          {review.provisioning.graphicalOnHookAdvertising != null && <><dt>Idle graphical rotation (MT131)</dt><dd>{review.provisioning.graphicalOnHookAdvertising ? 'Enabled' : 'Disabled'}</dd></>}
          {review.provisioning.visibleOnHookTextMessages != null && <><dt>Visible idle text messages (MT29)</dt><dd>{review.provisioning.visibleOnHookTextMessages}</dd></>}</dl>
        <h3>Remaining setup checks</h3>
        {!review.family.documented && <p>The selected terminal family has no complete documented checklist.</p>}
        {review.family.tables.filter(row => row.required && row.status !== 'present').map(row => <p key={row.name}>{row.name}: {row.status}</p>)}
        {[...review.provisioning.findings, ...review.findings].map((finding, i) => <p key={i}>MT{finding.tableNumber}: {finding.message}</p>)}
        <p>Review the terminal's own management access, local route and its pricing index, card/coin acceptance and all required families before publishing. The draft is not an installation certificate.</p>
        <button type="button" className="quiet" disabled={busy} onClick={() => setStep(1)}>Back</button>{' '}
        <button type="button" disabled={busy || !review.canCreate} onClick={() => void submit(true)}>{busy ? 'Creating…' : 'Create reviewed draft'}</button>
      </div>}
    </>}
  </section>
}

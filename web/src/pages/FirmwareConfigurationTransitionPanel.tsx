import { useState } from 'react'
import { Link } from 'react-router-dom'
import { apiGet, apiPostRaw } from '../api/client'
import { consoleSession } from '../api/consoleSession'

type Phone = { id: string; displayName: string }
type Options = { observed: { rom: string; software: string; capturedAtUtc: string } | null;
  schedules: { id: string; rom: string; application: string; availableAtUtc: string; completedAtUtc: string | null }[];
  candidates: { id: string; name: string; firmwareLabel: string }[] }
type Plan = { id: string; revision: number; state: 'staged' | 'ready' | 'activated' | 'cancelled'; blocker: string | null;
  sourceTableSetId: string; targetTableSetId: string; sourceControllerRom: string; sourceControllerSoftware: string;
  targetControllerRom: string; targetControllerSoftware: string; targetBatchId: string | null; autoActivateOnContact?: boolean; activatedAutomatically?: boolean }
type Preview = { plan: Plan; reviewToken: string }
const path = '/api/firmware/configuration-transitions'

export function FirmwareConfigurationTransitionPanel({ phones }: { phones: Phone[] }) {
  const [open, setOpen] = useState(false)
  const [phoneId, setPhoneId] = useState('')
  const [options, setOptions] = useState<Options | null>(null)
  const [plans, setPlans] = useState<Plan[]>([])
  const [targetSetId, setTargetSetId] = useState('')
  const [codeScheduleId, setCodeScheduleId] = useState('')
  const [software, setSoftware] = useState('')
  const [automatic, setAutomatic] = useState(false)
  const [preview, setPreview] = useState<Preview | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const admin = consoleSession.role() === 'Admin'
  const active = plans.some(plan => plan.state === 'staged' || plan.state === 'ready')

  async function refresh(id: string) {
    setBusy(true); setError(''); setPreview(null)
    try {
      const query = new URLSearchParams({ terminalId: id })
      const [next, history] = await Promise.all([apiGet<Options>(`${path}/options?${query}`), apiGet<Plan[]>(`${path}?${query}`)])
      setOptions(next); setPlans(history)
    } catch (reason) { setOptions(null); setPlans([]); setError(reason instanceof Error ? reason.message : 'Could not load the transition.') }
    finally { setBusy(false) }
  }

  async function create(stage: boolean) {
    setBusy(true); setError('')
    try {
      const response = await apiPostRaw(`${path}${stage ? '' : '/preview'}`, { terminalId: phoneId, targetSetId, codeScheduleId,
        expectedControllerSoftware: software.trim(), autoActivateOnContact: automatic, reviewToken: stage ? preview?.reviewToken : undefined })
      const result = await response.json().catch(() => null)
      if (!response.ok || !result) throw new Error(result?.error ?? `Request failed (HTTP ${response.status}).`)
      if (stage) { setPreview(null); await refresh(phoneId) }
      else setPreview(result as Preview)
    } catch (reason) { setPreview(null); setError(reason instanceof Error ? reason.message : 'Could not prepare the transition.') }
    finally { setBusy(false) }
  }

  async function change(plan: Plan, action: 'activate' | 'cancel') {
    if (!window.confirm(action === 'activate' ? 'Activate the reviewed target configuration and queue its full download?' :
      'Cancel this configuration plan? The separate code schedule will not be cancelled.')) return
    setBusy(true); setError('')
    try {
      const response = await apiPostRaw(`${path}/${plan.id}/${action}`, { confirm: true, revision: plan.revision })
      if (!response.ok) { const result = await response.json().catch(() => null); throw new Error(result?.error ?? `Request failed (HTTP ${response.status}).`) }
      await refresh(phoneId)
    } catch (reason) { setError(reason instanceof Error ? reason.message : 'Transition action failed.') }
    finally { setBusy(false) }
  }

  function selectPhone(id: string) {
    setPhoneId(id); setOptions(null); setPlans([]); setPreview(null); setTargetSetId(''); setCodeScheduleId(''); setSoftware(''); setAutomatic(false)
    if (id) void refresh(id)
  }

  return <section aria-label="Staged firmware configuration transitions" style={{ border: '1px solid #ddd', borderRadius: 8, padding: 16, marginBottom: 20 }}>
    <h2 style={{ marginTop: 0 }}>Staged configuration activation</h2>
    <p>Retain the installed assignment while preparing a published target configuration. Activation requires an acknowledged code schedule and a later matched controller-version report. This does not deliver code or certify an upgrade.</p>
    {!open ? <button type="button" onClick={() => { setOpen(true); selectPhone(phones[0]?.id ?? '') }}>Manage configuration transitions</button> : <>
      <label>Transition phone<select value={phoneId} disabled={busy} onChange={event => selectPhone(event.target.value)}>
        <option value="">Choose phone</option>{phones.map(phone => <option key={phone.id} value={phone.id}>{phone.displayName}</option>)}
      </select></label>{' '}<button type="button" disabled={!phoneId || busy} onClick={() => void refresh(phoneId)}>Refresh transition status</button>
      {error && <p role="alert">{error}</p>}
      {options && <>
        <p>Latest live controller report: {options.observed ? `${options.observed.rom} / ${options.observed.software}, ${new Date(options.observed.capturedAtUtc).toLocaleString()}` : 'Not observed'}</p>
        {admin && !active && <form onSubmit={event => { event.preventDefault(); void create(false) }} style={{ display: 'grid', gap: 8, maxWidth: 640 }}>
          <label>Published target configuration<select value={targetSetId} disabled={busy} onChange={event => { setTargetSetId(event.target.value); setPreview(null) }}>
            <option value="">Choose target set</option>{options.candidates.map(set => <option value={set.id} key={set.id}>{set.name} ({set.firmwareLabel})</option>)}
          </select></label>
          <label>Existing enabled code schedule<select value={codeScheduleId} disabled={busy} onChange={event => { setCodeScheduleId(event.target.value); setPreview(null) }}>
            <option value="">Choose schedule</option>{options.schedules.map(schedule => <option value={schedule.id} key={schedule.id}>{schedule.rom} / application {schedule.application} ({schedule.completedAtUtc ? 'acknowledged' : 'pending'}, {new Date(schedule.availableAtUtc).toLocaleString()})</option>)}
          </select></label>
          <label>Expected controller software label (MT60)<input maxLength={4} value={software} disabled={busy} onChange={event => { setSoftware(event.target.value); setPreview(null) }} /></label>
          <small>This is the controller software label actually expected after upgrade, not an assumed copy of the code schedule's application revision.</small>
          <label><input type="checkbox" checked={automatic} disabled={busy} onChange={event => { setAutomatic(event.target.checked); setPreview(null) }} /> Automatically activate on the first verified target-version contact</label>
          <small>When selected, the reviewed plan authorizes Manager to activate the target configuration after all schedule and version checks pass. Only a normal table-download request starts transfer.</small>
          <button type="submit" disabled={busy || !targetSetId || !codeScheduleId || !software.trim()}>Preview configuration transition</button>
          {preview && <div aria-label="Reviewed transition">
            <p>{preview.plan.sourceControllerRom} / {preview.plan.sourceControllerSoftware} to {preview.plan.targetControllerRom} / {preview.plan.targetControllerSoftware}</p>
            <p>{preview.plan.autoActivateOnContact ? 'Automatic activation approved on matching target-version contact.' : 'Manual administrator activation required.'}</p>
            <p><Link to={`/table-sets/${preview.plan.sourceTableSetId}`}>Current source configuration</Link> | <Link to={`/table-sets/${preview.plan.targetTableSetId}`}>Prepared target configuration</Link></p>
            <button type="button" disabled={busy} onClick={() => { if (window.confirm(automatic ? 'Stage this configuration and authorize automatic activation after a verified target-version contact?' : 'Stage this configuration without changing the phone or its code schedule?')) void create(true) }}>Stage reviewed configuration</button>
          </div>}
        </form>}
        {plans.length === 0 ? <p>No staged or previous transitions for this phone.</p> : <div style={{ overflowX: 'auto' }}><table cellPadding={7} style={{ width: '100%' }}>
          <thead><tr><th>Observed source</th><th>Expected target</th><th>Configurations</th><th>State</th><th>Action</th></tr></thead>
          <tbody>{plans.map(plan => <tr key={plan.id}>
            <td>{plan.sourceControllerRom} / {plan.sourceControllerSoftware}</td><td>{plan.targetControllerRom} / {plan.targetControllerSoftware}</td>
            <td><Link to={`/table-sets/${plan.sourceTableSetId}`}>Source</Link> | <Link to={`/table-sets/${plan.targetTableSetId}`}>Target</Link></td>
            <td>{plan.state === 'ready' ? 'Ready for activation' : plan.state === 'staged' ? 'Awaiting evidence' : plan.state === 'activated' ? 'Activated in Manager' : 'Cancelled'}
              {plan.autoActivateOnContact && <small style={{ display: 'block' }}>{plan.activatedAutomatically ? 'Activated automatically' : 'Automatic activation approved'}</small>}{plan.blocker && <p>{plan.blocker}</p>}{plan.targetBatchId && <Link to={`/downloads/${plan.targetBatchId}`}>Target download</Link>}</td>
            <td>{admin && (plan.state === 'ready' || plan.state === 'staged') && <><button type="button" disabled={busy || plan.state !== 'ready'} onClick={() => void change(plan, 'activate')}>Activate target configuration</button>{' '}<button type="button" disabled={busy || plan.state === 'ready'} onClick={() => void change(plan, 'cancel')}>Cancel plan</button></>}</td>
          </tr>)}</tbody></table></div>}
      </>}
    </>}
  </section>
}

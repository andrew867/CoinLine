import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { apiFetch, apiGet } from '../api/client'
import { canPerform, permissionOperation as op } from '../api/consoleSession'
import { hostRatePlanSourceLabel } from './hostRatePlanPresentation'

type Selection = { selectionId: string | null; revision: number | null; selectedPlanId: string | null; restoreNationalTollPrefix: boolean;
  effective: { customerId: string; ratePlanId: string | null; publishedVersionId: string | null; name: string | null; source: string; restoreNationalTollPrefix?: boolean };
  options: { id: string; name: string; customerId: string | null; publishedVersionId: string }[]; inheritedPhones: number }

export function HostRatePlanSelectionPanel({ scope, id, active = true }: { scope: 'customers' | 'terminals'; id: string; active?: boolean }) {
  const endpoint = `/api/rate-plans/selection/${scope}/${id}`
  const screen = scope === 'customers' ? 'customers' : 'phones'
  const canRead = active && canPerform('rate-plans', op.read) && canPerform(screen, op.read)
  const canEdit = active && canPerform('rate-plans', op.update) && canPerform(screen, op.update)
  const [data, setData] = useState<Selection | null>(null)
  const [picked, setPicked] = useState('')
  const [restore, setRestore] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')

  async function load() {
    setBusy(true); setError('')
    try {
      const value = await apiGet<Selection>(endpoint)
      if (!value?.effective || !Array.isArray(value.options)) throw new Error('The Manager returned an invalid host rate plan selection.')
      setData(value); setPicked(value.selectedPlanId ?? ''); setRestore(value.restoreNationalTollPrefix ?? false)
    }
    catch (reason) { setData(null); setError(reason instanceof Error ? reason.message : 'Unable to load host rate plan.') }
    finally { setBusy(false) }
  }
  useEffect(() => { if (canRead) void load() }, [endpoint, canRead])
  const changed = data !== null && (picked !== (data.selectedPlanId ?? '') || restore !== (data.restoreNationalTollPrefix ?? false))
  async function save() {
    if (!data || !canEdit || !changed) return
    const text = scope === 'customers' ? `Change the host rate plan for ${data.inheritedPhones} inheriting phones?` : 'Change the host rate plan for this phone?'
    if (!window.confirm(text + ' This affects subsequent real-time quotes, not terminal table downloads or previous calls.')) return
    setBusy(true); setError(''); setNotice('')
    try {
      const response = await apiFetch(endpoint, { method: 'PUT', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ ratePlanId: picked || null, expectedSelectionId: data.selectionId, expectedRevision: data.revision, confirm: true, restoreNationalTollPrefix: !!picked && restore }) })
      if (!response.ok) {
        const body = await response.json().catch(() => null)
        throw new Error(body?.error ?? `Host rate plan selection failed (HTTP ${response.status}).`)
      }
      await load()
      setNotice('Host rate plan selection saved. No terminal table download was requested.')
    } catch (reason) { setError(reason instanceof Error ? reason.message : 'Unable to save host plan selection.') }
    finally { setBusy(false) }
  }

  if (!canRead) return null
  return <section id={scope === 'terminals' ? 'host-rate-plan-selection' : undefined} aria-label="Host rate plan selection" style={{ margin: '16px 0' }}>
    <h3>{scope === 'customers' ? 'Default host rating plan' : 'Host rating plan for this phone'}</h3>
    <p>Determines subsequent real-time rating requests only. Terminal-local pricing and dialing tables are configured separately.</p>
    {busy && !data && <p role="status">Loading host rating plan…</p>}
    {error && <p role="alert">{error} <button type="button" disabled={busy} onClick={() => void load()}>Reload selection</button></p>}
    {notice && <p role="status">{notice}</p>}
    {data && <>
      <p>Effective: {data.effective.ratePlanId ? <Link to={`/rate-plans/${data.effective.ratePlanId}`}>{data.effective.name}</Link> : 'No available plan'}
        {' '}({hostRatePlanSourceLabel(data.effective.source)})</p>
      {data.effective.restoreNationalTollPrefix && <p>Classified national toll requests use the leading 1 for host lookup.</p>}
      {scope === 'customers' && <p>{data.inheritedPhones} active phones inherit this default. Explicit phone selections are retained.</p>}
      <label>{scope === 'customers' ? 'Customer default host rate plan' : 'Phone host rate plan'}{' '}
        <select value={picked} disabled={busy || !canEdit} onChange={event => { setPicked(event.target.value); if (!event.target.value) setRestore(false); setNotice(''); setError('') }}>
          <option value="">{scope === 'customers' ? 'Use existing automatic customer selection' : 'Inherit customer default'}</option>
          {data.selectedPlanId && !data.options.some(plan => plan.id === data.selectedPlanId) &&
            <option value={data.selectedPlanId} disabled>Unavailable selected plan</option>}
          {data.options.map(plan => <option key={plan.id} value={plan.id}>{plan.name}{plan.customerId === null ? ' (shared)' : ''}</option>)}
        </select>
      </label>{' '}
      {picked && <div><label><input type="checkbox" checked={restore} disabled={busy || !canEdit}
        onChange={event => { setRestore(event.target.checked); setNotice(''); setError('') }} /> Restore leading 1 for classified ten-digit NCC toll requests</label>
        <p>Enable only after confirming this phone profile distinguishes local and national toll calls. This affects host lookup only; it cannot change the digits dialed by the phone.</p></div>}
      {canEdit && <button type="button" disabled={busy || !changed} onClick={() => void save()}>Save host rate plan selection</button>}
    </>}
  </section>
}

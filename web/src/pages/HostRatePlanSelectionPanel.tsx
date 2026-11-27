import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { apiFetch, apiGet } from '../api/client'
import { canPerform, permissionOperation as op } from '../api/consoleSession'

type Selection = { selectionId: string | null; revision: number | null; selectedPlanId: string | null;
  effective: { customerId: string; ratePlanId: string | null; publishedVersionId: string | null; name: string | null; source: string };
  options: { id: string; name: string; customerId: string | null; publishedVersionId: string }[]; inheritedPhones: number }
const sourceLabel: Record<string, string> = { terminal: 'Explicit phone selection', customer: 'Customer default',
  legacy: 'Existing automatic customer default', none: 'No published customer plan', 'terminal-unavailable': 'Phone selection unavailable',
  'customer-unavailable': 'Customer selection unavailable', unmatched: 'Unmatched phone' }

export function HostRatePlanSelectionPanel({ scope, id, active = true }: { scope: 'customers' | 'terminals'; id: string; active?: boolean }) {
  const endpoint = `/api/rate-plans/selection/${scope}/${id}`
  const screen = scope === 'customers' ? 'customers' : 'phones'
  const canRead = active && canPerform('rate-plans', op.read) && canPerform(screen, op.read)
  const canEdit = active && canPerform('rate-plans', op.update) && canPerform(screen, op.update)
  const [data, setData] = useState<Selection | null>(null)
  const [picked, setPicked] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')

  async function load() {
    setBusy(true); setError('')
    try {
      const value = await apiGet<Selection>(endpoint)
      if (!value?.effective || !Array.isArray(value.options)) throw new Error('The Manager returned an invalid host rate plan selection.')
      setData(value); setPicked(value.selectedPlanId ?? '')
    }
    catch (reason) { setData(null); setError(reason instanceof Error ? reason.message : 'Unable to load host rate plan.') }
    finally { setBusy(false) }
  }
  useEffect(() => { if (canRead) void load() }, [endpoint, canRead])
  async function save() {
    if (!data || !canEdit || picked === (data.selectedPlanId ?? '')) return
    const text = scope === 'customers' ? `Change the host rate plan for ${data.inheritedPhones} inheriting phones?` : 'Change the host rate plan for this phone?'
    if (!window.confirm(text + ' This affects subsequent real-time quotes, not terminal table downloads or previous calls.')) return
    setBusy(true); setError(''); setNotice('')
    try {
      const response = await apiFetch(endpoint, { method: 'PUT', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ ratePlanId: picked || null, expectedSelectionId: data.selectionId, expectedRevision: data.revision, confirm: true }) })
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
  return <section aria-label="Host rate plan selection" style={{ margin: '16px 0' }}>
    <h3>{scope === 'customers' ? 'Default host rating plan' : 'Host rating plan for this phone'}</h3>
    <p>Determines subsequent real-time rating requests only. Terminal-local pricing and dialing tables are configured separately.</p>
    {busy && !data && <p role="status">Loading host rating plan…</p>}
    {error && <p role="alert">{error} <button type="button" disabled={busy} onClick={() => void load()}>Reload selection</button></p>}
    {notice && <p role="status">{notice}</p>}
    {data && <>
      <p>Effective: {data.effective.ratePlanId ? <Link to={`/rate-plans/${data.effective.ratePlanId}`}>{data.effective.name}</Link> : 'No available plan'}
        {' '}({sourceLabel[data.effective.source] ?? data.effective.source})</p>
      {scope === 'customers' && <p>{data.inheritedPhones} active phones inherit this default. Explicit phone selections are retained.</p>}
      <label>{scope === 'customers' ? 'Customer default host rate plan' : 'Phone host rate plan'}{' '}
        <select value={picked} disabled={busy || !canEdit} onChange={event => { setPicked(event.target.value); setNotice(''); setError('') }}>
          <option value="">{scope === 'customers' ? 'Use existing automatic customer selection' : 'Inherit customer default'}</option>
          {data.selectedPlanId && !data.options.some(plan => plan.id === data.selectedPlanId) &&
            <option value={data.selectedPlanId} disabled>Unavailable selected plan</option>}
          {data.options.map(plan => <option key={plan.id} value={plan.id}>{plan.name}{plan.customerId === null ? ' (shared)' : ''}</option>)}
        </select>
      </label>{' '}
      {canEdit && <button type="button" disabled={busy || picked === (data.selectedPlanId ?? '')} onClick={() => void save()}>Save host rate plan selection</button>}
    </>}
  </section>
}

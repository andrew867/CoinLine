import type { FormEvent } from 'react'
import { useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { apiGet, apiPostRaw } from '../api/client'
import { canPerform, permissionOperation as op } from '../api/consoleSession'
import { EmptyHint, ErrorBanner } from '../components/AppStates'
import { FirmwareVersionSelect, type FirmwareVersionOption } from '../components/FirmwareVersionSelect'
import { TerminalFamilySelect, type TerminalFamily } from '../components/TerminalFamilySelect'
import { rememberDownloadReview, readDownloadReviewPrompt } from '../api/downloadReviews'


type SiteOpt = { id: string; name: string; code: string; customerId: string }
type GroupOpt = { id: string; name: string; customerId: string | null }
type CreateOptions = {
  sites: SiteOpt[]
  groups: GroupOpt[]
  firmwareVersions: FirmwareVersionOption[]
  families: TerminalFamily[]
  canAssignGroup: boolean
}

export function TerminalCreatePage() {
  const nav = useNavigate()
  const canAdd = canPerform('phones', op.add)
  const canRead = canPerform('phones', op.read)
  const [options, setOptions] = useState<CreateOptions | null>(null)
  const [siteId, setSiteId] = useState('')
  const [groupId, setGroupId] = useState('')
  const [firmwareVersionId, setFirmwareVersionId] = useState('')
  const [terminalFamilyId, setTerminalFamilyId] = useState('')
  const [displayName, setDisplayName] = useState('Bench terminal')
  const [terminalIdHex, setTerminalIdHex] = useState('')
  const [err, setErr] = useState<string | null>(null)
  const [notice, setNotice] = useState('')
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    if (!canAdd) return
    let current = true
    void apiGet<CreateOptions>('/api/terminals/create-options').then(value => {
      if (!current) return
      setOptions(value)
      setSiteId(value.sites[0]?.id ?? '')
    }).catch(() => { if (current) setErr('Could not load phone registration choices.') })
    return () => { current = false }
  }, [canAdd])

  const site = options?.sites.find(row => row.id === siteId)
  const groups = options?.groups.filter(group => group.customerId == null || group.customerId === site?.customerId) ?? []

  function changeSite(value: string) {
    const customerId = options?.sites.find(row => row.id === value)?.customerId
    const currentGroup = options?.groups.find(group => group.id === groupId)
    if (currentGroup?.customerId != null && currentGroup.customerId !== customerId) setGroupId('')
    setSiteId(value)
  }

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    if (!canAdd || !site || saving) return
    setErr(null)
    setNotice('')
    setSaving(true)
    try {
      const response = await apiPostRaw('/api/terminals', {
        siteId,
        terminalGroupId: options?.canAssignGroup && groupId ? groupId : null,
        transportEndpointId: null,
        firmwareVersionId: firmwareVersionId || null,
        terminalFamilyId: terminalFamilyId || null,
        terminalIdHex,
        displayName,
        status: 1,
      })
      if (!response.ok) {
        const problem = await response.json().catch(() => null)
        throw new Error(problem?.error || `Could not create terminal (HTTP ${response.status}).`)
      }
      const created = await response.json() as { id: string }
      if (canRead) {
        rememberDownloadReview({ terminalId: created.id, tableSetId: null, reason: 'registration' })
        nav(`/terminals/${created.id}`)
      }
      else {
        setNotice('Phone registered. You can register another phone.')
        setTerminalIdHex('')
      }
    } catch (ex) {
      setErr(ex instanceof Error ? ex.message : 'create failed')
    } finally {
      setSaving(false)
    }
  }

  if (!canAdd) return <p role="alert">Add permission for Phones is required to register a phone.</p>
  return (
    <div>
      <p>
        <Link to="/terminals">← Phones</Link>
      </p>
      <h1>Create terminal</h1>
      <p>First register the phone with its actual line identity, site, terminal family and installed firmware. After registration, review the configuration assignment and initial download separately.</p>
      {canRead && canPerform('table-assignments', op.read) && canPerform('table-assignments', op.update) &&
        <p>On the new phone, open Individual configuration assignments to start from a matching published configuration and review its site-specific settings.</p>}
      {canPerform('table-sets', op.read) && canPerform('table-sets', op.add) && canPerform('table-records', op.read) && canPerform('table-records', op.update) &&
        <p>Need a matching set? <Link to="/table-sets?setup=1">Use guided configuration setup</Link> to choose payment, pricing and advertising before assignment.</p>}
      {!options && !err && <p>Loading phone registration choices…</p>}
      {options && options.sites.length === 0 && (
        <EmptyHint>No sites — create a customer, site, then return here.</EmptyHint>
      )}
      {err && <ErrorBanner message={err} />}
      {notice && <p role="status">{notice}</p>}
      {notice && !canRead && readDownloadReviewPrompt() && <p>Ask an operator with phone access to review the new phone's download requirements.</p>}
      {options && <form onSubmit={(e) => void onSubmit(e)} style={{ display: 'flex', flexDirection: 'column', gap: 12, maxWidth: 400 }}>
        <label>
          Site
          <select
            data-testid="terminal-create-site"
            data-field-kind="mandatory"
            value={siteId}
            onChange={(e) => changeSite(e.target.value)}
            style={{ display: 'block', width: '100%' }}
          >
            {options.sites.map((s) => (
              <option key={s.id} value={s.id}>
                {s.name} ({s.code})
              </option>
            ))}
          </select>
        </label>
        {options.canAssignGroup && <label>
          Phone group
          <select value={groupId} onChange={(e) => setGroupId(e.target.value)} style={{ display: 'block', width: '100%' }}>
            <option value="">No group</option>
            {groups.map((g) => (
              <option key={g.id} value={g.id}>
                {g.name}
              </option>
            ))}
          </select>
        </label>}
        <FirmwareVersionSelect label="Installed firmware" value={firmwareVersionId}
          onChange={setFirmwareVersionId} emptyLabel="Unknown firmware" options={options.firmwareVersions} />
        <TerminalFamilySelect value={terminalFamilyId} onChange={setTerminalFamilyId} options={options.families} />
        <label>
          Display name
          <input
            data-testid="terminal-create-name"
            value={displayName}
            onChange={(e) => setDisplayName(e.target.value)}
            style={{ display: 'block', width: '100%' }}
          />
        </label>
        <label>
          Terminal id (hex)
          <input
            data-testid="terminal-create-hex"
            data-field-kind="key"
            value={terminalIdHex}
            required pattern="[0-9A-Fa-f]{10}" maxLength={10}
            onChange={(e) => setTerminalIdHex(e.target.value.toUpperCase())}
            style={{ display: 'block', width: '100%', fontFamily: 'monospace' }}
          />
        </label>
        <button type="submit" data-testid="terminal-create-submit" disabled={saving || !site}>
          Create terminal
        </button>
      </form>}
    </div>
  )
}

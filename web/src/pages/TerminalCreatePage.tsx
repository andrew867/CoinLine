import type { FormEvent } from 'react'
import { useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { apiPost } from '../api/client'
import { EmptyHint, ErrorBanner } from '../components/AppStates'
import { FirmwareVersionSelect } from '../components/FirmwareVersionSelect'

const headers = { 'X-Operator-Id': 'ui@local', 'X-Operator-Role': 'Admin' }

type SiteOpt = { id: string; name: string; code: string }
type GroupOpt = { id: string; name: string }

export function TerminalCreatePage() {
  const nav = useNavigate()
  const [sites, setSites] = useState<SiteOpt[]>([])
  const [groups, setGroups] = useState<GroupOpt[]>([])
  const [siteId, setSiteId] = useState('')
  const [groupId, setGroupId] = useState('')
  const [firmwareVersionId, setFirmwareVersionId] = useState('')
  const [displayName, setDisplayName] = useState('Bench terminal')
  const [terminalIdHex, setTerminalIdHex] = useState('E2E010203')
  const [err, setErr] = useState<string | null>(null)

  useEffect(() => {
    void fetch('/api/sites', { headers })
      .then((r) => r.json() as Promise<SiteOpt[]>)
      .then((s) => {
        setSites(s)
        setSiteId((prev) => prev || s[0]?.id || '')
      })
      .catch(() => {})
    void fetch('/api/terminal-groups', { headers })
      .then((r) => r.json() as Promise<GroupOpt[]>)
      .then(setGroups)
      .catch(() => {})
  }, [])

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    setErr(null)
    try {
      const created = await apiPost<{ id: string }>('/api/terminals', {
        siteId,
        terminalGroupId: groupId || null,
        transportEndpointId: null,
        firmwareVersionId: firmwareVersionId || null,
        terminalIdHex,
        displayName,
        status: 1,
      })
      nav(`/terminals/${created.id}`)
    } catch (ex) {
      setErr(ex instanceof Error ? ex.message : 'create failed')
    }
  }

  return (
    <div>
      <p>
        <Link to="/terminals">← Phones</Link>
      </p>
      <h1>Create terminal</h1>
      {sites.length === 0 && (
        <EmptyHint>No sites — create a customer, site, then return here.</EmptyHint>
      )}
      {err && <ErrorBanner message={err} />}
      <form onSubmit={(e) => void onSubmit(e)} style={{ display: 'flex', flexDirection: 'column', gap: 12, maxWidth: 400 }}>
        <label>
          Site
          <select
            data-testid="terminal-create-site"
            value={siteId}
            onChange={(e) => setSiteId(e.target.value)}
            style={{ display: 'block', width: '100%' }}
          >
            {sites.map((s) => (
              <option key={s.id} value={s.id}>
                {s.name} ({s.code})
              </option>
            ))}
          </select>
        </label>
        <label>
          Phone group
          <select value={groupId} onChange={(e) => setGroupId(e.target.value)} style={{ display: 'block', width: '100%' }}>
            <option value="">No group</option>
            {groups.map((g) => (
              <option key={g.id} value={g.id}>
                {g.name}
              </option>
            ))}
          </select>
        </label>
        <FirmwareVersionSelect label="Installed firmware" value={firmwareVersionId}
          onChange={setFirmwareVersionId} emptyLabel="Unknown firmware" />
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
            value={terminalIdHex}
            onChange={(e) => setTerminalIdHex(e.target.value)}
            style={{ display: 'block', width: '100%', fontFamily: 'monospace' }}
          />
        </label>
        <button type="submit" data-testid="terminal-create-submit">
          Create terminal
        </button>
      </form>
    </div>
  )
}

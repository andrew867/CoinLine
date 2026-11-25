import { Fragment, useCallback, useEffect, useState } from 'react'
import { apiGet, apiPost } from '../api/client'
import { canPerform, permissionOperation as op } from '../api/consoleSession'
import { FirmwareProfileEditor } from './FirmwareProfileEditor'

type Row = { id: string; label: string; buildId: string | null; tableProfileId: string | null }
type TableProfile = { id: string; label: string; verifiedLengths: Record<string, number>;
  completeCatalogue: boolean; terminalAcknowledgedRecords: number[] }

function profileEvidence(profile: TableProfile) {
  const count = Object.keys(profile.verifiedLengths).length
  const acked = profile.terminalAcknowledgedRecords.length
  return `${count} known record lengths, ${acked} acknowledged by emulated terminals${profile.completeCatalogue ? '. Complete image catalogue.' : '.'}`
}

export function FirmwareVersionsPage() {
  const [rows, setRows] = useState<Row[]>([])
  const [loading, setLoading] = useState(true)
  const [err, setErr] = useState<string | null>(null)
  const [label, setLabel] = useState('')
  const [buildId, setBuildId] = useState('')
  const [profiles, setProfiles] = useState<TableProfile[]>([])
  const [tableProfileId, setTableProfileId] = useState('')
  const [editingId, setEditingId] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const canAdd = canPerform('firmware-registry', op.add)
  const canUpdate = canPerform('firmware-registry', op.update)
  const selectedProfile = profiles.find(profile => profile.id === tableProfileId)

  const refresh = useCallback(async () => {
    setErr(null)
    setLoading(true)
    try {
      const [versions, tableProfiles] = await Promise.all([
        apiGet<Row[]>('/api/firmware/versions'),
        apiGet<TableProfile[]>('/api/firmware/table-profiles'),
      ])
      setRows(versions)
      setProfiles(tableProfiles)
    } catch (e) {
      setErr(e instanceof Error ? e.message : 'Failed to load versions.')
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    void refresh()
  }, [refresh])

  async function onCreate(e: React.FormEvent) {
    e.preventDefault()
    setErr(null)
    try {
      await apiPost('/api/firmware/versions', {
        label: label.trim(),
        buildId: buildId || null,
        tableProfileId: tableProfileId || null,
      })
      setLabel('')
      setBuildId('')
      setTableProfileId('')
      await refresh()
    } catch (e) {
      setErr(e instanceof Error ? e.message : 'create failed')
    }
  }

  return (
    <div>
      <h1>Firmware versions</h1>
      <p style={{ fontSize: 14, color: '#444', maxWidth: 640 }}>
        Register firmware builds here so they can be selected on phones and table sets. Choose a table layout only when it matches the build.
      </p>
      {loading && <p role="status">Loading…</p>}
      {err && <p role="alert" style={{ color: 'crimson' }}>{err}</p>}
      {notice && <p role="status">{notice}</p>}
      {canAdd && <form onSubmit={onCreate} style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'end', gap: 10, marginBottom: 20 }}>
        <label>Firmware label<input style={{ display: 'block' }} required value={label} onChange={(e) => setLabel(e.target.value)} /></label>
        <label>Build ID<input style={{ display: 'block' }} value={buildId} onChange={(e) => setBuildId(e.target.value)} /></label>
        <label>Table layout
          <select style={{ display: 'block' }} value={tableProfileId} onChange={(e) => setTableProfileId(e.target.value)}>
            <option value="">Unverified layout</option>
            {profiles.map((profile) => <option key={profile.id} value={profile.id}>{profile.label}</option>)}
          </select>
        </label>
        <button type="submit" disabled={!label.trim()}>Register firmware</button>
        {selectedProfile && <p className="page-intro" style={{ flexBasis: '100%', margin: 0 }}>{profileEvidence(selectedProfile)}</p>}
      </form>}
      {!loading && !err && rows.length === 0 && (
        <p role="status" style={{ color: '#666' }}>
          No firmware versions registered.
        </p>
      )}
      {!loading && rows.length > 0 && (
        <table cellPadding={8}>
          <thead>
            <tr style={{ textAlign: 'left' }}>
              <th>Label</th>
              <th>Build</th>
              <th>Table layout</th>
              {canUpdate && <th>Actions</th>}
            </tr>
          </thead>
          <tbody>
            {rows.map((r) => {
              const profile = profiles.find(item => item.id === r.tableProfileId)
              return (
              <Fragment key={r.id}><tr>
                <td>{r.label}</td>
                <td>{r.buildId ?? '—'}</td>
                <td>{profile?.label ?? 'Unverified layout'}
                  {profile && <div><small>{profileEvidence(profile)}</small></div>}
                </td>
                {canUpdate && <td><button type="button" disabled={editingId !== null}
                  onClick={() => { setNotice(null); setEditingId(r.id) }}>Review profile change</button></td>}
              </tr>
              {canUpdate && editingId === r.id && <tr><td colSpan={4}>
                <FirmwareProfileEditor id={r.id} currentProfileId={r.tableProfileId} profiles={profiles}
                  onCancel={() => setEditingId(null)} onApplied={() => {
                    setEditingId(null); setNotice('Firmware table profile updated.'); void refresh()
                  }} />
              </td></tr>}</Fragment>
              )
            })}
          </tbody>
        </table>
      )}
    </div>
  )
}

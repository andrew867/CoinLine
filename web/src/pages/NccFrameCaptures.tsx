import { useCallback, useEffect, useState } from 'react'

type ListRow = { id: string; originalFileName: string; byteLength: number; createdAtUtc: string }

const headers = { 'X-Operator-Id': 'ui@local', 'X-Operator-Role': 'Admin' }

export function NccFrameCaptures() {
  const [list, setList] = useState<ListRow[] | null>(null)
  const [loading, setLoading] = useState(true)
  const [listErr, setListErr] = useState<string | null>(null)
  const [uploading, setUploading] = useState(false)
  const [uploadErr, setUploadErr] = useState<string | null>(null)
  const [uploadOk, setUploadOk] = useState<string | null>(null)
  const [detail, setDetail] = useState<Record<string, unknown> | null>(null)
  const [detailLoading, setDetailLoading] = useState(false)
  const [detailErr, setDetailErr] = useState<string | null>(null)
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [deleteErr, setDeleteErr] = useState<string | null>(null)

  const refreshList = useCallback(async () => {
    setListErr(null)
    setLoading(true)
    try {
      const r = await fetch('/api/ncc/frame-captures', { headers })
      if (!r.ok) throw new Error(`List ${r.status}`)
      setList((await r.json()) as ListRow[])
    } catch (e) {
      setListErr(e instanceof Error ? e.message : 'error')
      setList(null)
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    void refreshList()
  }, [refreshList])

  async function onUpload(file: File | null) {
    if (!file || file.size === 0) return
    setUploadErr(null)
    setUploadOk(null)
    setUploading(true)
    try {
      const fd = new FormData()
      fd.append('file', file)
      const r = await fetch('/api/ncc/frame-captures', { method: 'POST', headers, body: fd })
      if (!r.ok) throw new Error(`Upload ${r.status}`)
      const j = await r.json()
      setUploadOk(`Uploaded ${file.name} (${(j as { byteLength?: number }).byteLength ?? '?'} bytes).`)
      await refreshList()
    } catch (e) {
      setUploadErr(e instanceof Error ? e.message : 'upload failed')
    } finally {
      setUploading(false)
    }
  }

  async function loadDetail(id: string) {
    setSelectedId(id)
    setDetail(null)
    setDetailErr(null)
    setDetailLoading(true)
    try {
      const r = await fetch(`/api/ncc/frame-captures/${id}`, { headers })
      if (!r.ok) throw new Error(`Detail ${r.status}`)
      setDetail((await r.json()) as Record<string, unknown>)
    } catch (e) {
      setDetailErr(e instanceof Error ? e.message : 'error')
    } finally {
      setDetailLoading(false)
    }
  }

  async function onDelete(id: string) {
    if (!window.confirm('Delete this capture permanently? This cannot be undone.')) return
    setDeleteErr(null)
    try {
      const r = await fetch(`/api/ncc/frame-captures/${id}?confirm=true`, {
        method: 'DELETE',
        headers,
      })
      if (!r.ok && r.status !== 204) throw new Error(`Delete ${r.status}`)
      if (selectedId === id) {
        setSelectedId(null)
        setDetail(null)
      }
      await refreshList()
    } catch (e) {
      setDeleteErr(e instanceof Error ? e.message : 'delete failed')
    }
  }

  return (
    <div>
      <h1>NCC frame captures</h1>
      <p style={{ color: '#555', maxWidth: 720 }}>
        Upload raw modem captures. Every byte is preserved; stream items include inter-frame gaps and parsed frames.
        Deletion is audited and requires confirmation.
      </p>

      <section style={{ marginBottom: 24 }}>
        <h2>Upload</h2>
        <input
          type="file"
          accept=".bin,application/octet-stream,*/*"
          disabled={uploading}
          onChange={(e) => void onUpload(e.target.files?.[0] ?? null)}
        />
        {uploading && <p>Uploading…</p>}
        {uploadErr && <p style={{ color: 'crimson' }}>{uploadErr}</p>}
        {uploadOk && <p style={{ color: '#0a0' }}>{uploadOk}</p>}
      </section>

      <section>
        <h2>Captures</h2>
        {loading && <p>Loading…</p>}
        {listErr && <p style={{ color: 'crimson' }}>{listErr}</p>}
        {!loading && !listErr && list && list.length === 0 && <p style={{ color: '#666' }}>No captures yet.</p>}
        {!loading && !listErr && list && list.length > 0 && (
          <ul style={{ listStyle: 'none', padding: 0 }}>
            {list.map((row) => (
              <li
                key={row.id}
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  gap: 12,
                  padding: '8px 0',
                  borderBottom: '1px solid #eee',
                }}
              >
                <button type="button" onClick={() => void loadDetail(row.id)}>
                  Inspect
                </button>
                <span>{row.originalFileName}</span>
                <span style={{ color: '#666' }}>{row.byteLength} bytes</span>
                <button type="button" onClick={() => void onDelete(row.id)}>
                  Delete
                </button>
              </li>
            ))}
          </ul>
        )}
      </section>

      {deleteErr && <p style={{ color: 'crimson' }}>{deleteErr}</p>}

      {selectedId && (
        <section style={{ marginTop: 24 }}>
          <h2>Inspect</h2>
          {detailLoading && <p>Loading detail…</p>}
          {detailErr && <p style={{ color: 'crimson' }}>{detailErr}</p>}
          {!detailLoading && !detailErr && detail !== null && (
            <pre style={{ background: '#f6f8fa', padding: 12, overflow: 'auto', maxHeight: '60vh' }}>
              {JSON.stringify(detail, null, 2)}
            </pre>
          )}
        </section>
      )}
    </div>
  )
}

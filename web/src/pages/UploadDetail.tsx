import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { apiPostBareOk } from '../api/client'

const headers = { 'X-Operator-Id': 'ui@local', 'X-Operator-Role': 'Admin' }

type RecordRow = {
  id: string
  rawPayloadHex: string
  decodedMetadataJson: string
  createdAtUtc: string
}

type Detail = {
  id: string
  terminalId: string | null
  status: number
  idempotencyKey: string
  rawPayloadHex: string
  decodedMetadataJson: string | null
  relatedDlogTransactionId: string | null
  createdAtUtc: string
  updatedAtUtc: string
  records: RecordRow[]
}

const statusLabel = (n: number) =>
  (
    {
      0: 'Received',
      1: 'Processing',
      2: 'Completed',
      3: 'Failed',
      4: 'Quarantined',
    } as Record<number, string>
  )[n] ?? String(n)

export function UploadDetail() {
  const { id } = useParams()
  const [d, setD] = useState<Detail | null>(null)
  const [err, setErr] = useState<string | null>(null)
  const [reviewNote, setReviewNote] = useState('')
  const [metaMode, setMetaMode] = useState<'raw' | 'pretty'>('pretty')

  const load = () => {
    if (!id) return
    setErr(null)
    fetch(`/api/uploads/${id}`, { headers })
      .then((r) => {
        if (!r.ok) throw new Error(String(r.status))
        return r.json() as Promise<Detail>
      })
      .then(setD)
      .catch((e) => setErr(e instanceof Error ? e.message : 'error'))
  }

  useEffect(() => {
    load()
  }, [id])

  async function ingest() {
    if (!id) return
    setErr(null)
    try {
      const r = await fetch(`/api/uploads/${id}/ingest`, { method: 'POST', headers })
      if (!r.ok) throw new Error(`ingest ${r.status}`)
      load()
    } catch (ex) {
      setErr(ex instanceof Error ? ex.message : 'error')
    }
  }

  async function reprocess() {
    if (!id) return
    if (!window.confirm('Replace derived records by re-running ingestion?')) return
    setErr(null)
    try {
      await apiPostBareOk(`/api/uploads/${id}/reprocess`, { confirm: true })
      load()
    } catch (ex) {
      setErr(ex instanceof Error ? ex.message : 'error')
    }
  }

  async function markReviewed(e: React.FormEvent) {
    e.preventDefault()
    if (!id) return
    setErr(null)
    try {
      const r = await fetch(`/api/uploads/${id}/operator-review`, {
        method: 'POST',
        headers: { ...headers, 'Content-Type': 'application/json' },
        body: JSON.stringify({ note: reviewNote.trim() || null }),
      })
      if (!r.ok) throw new Error(`review ${r.status}`)
      setReviewNote('')
      load()
    } catch (ex) {
      setErr(ex instanceof Error ? ex.message : 'error')
    }
  }

  if (!id) return <p>Missing id.</p>
  if (!d) return err ? <p style={{ color: 'crimson' }}>{err}</p> : <p>Loading…</p>

  let metadataBlock = d.decodedMetadataJson ?? '{}'
  if (metaMode === 'pretty') {
    try {
      metadataBlock = JSON.stringify(JSON.parse(metadataBlock), null, 2)
    } catch {
      /* keep raw */
    }
  }

  return (
    <div>
      <p>
        <Link to="/uploads">← Uploads</Link>
      </p>
      <h1>Upload batch</h1>
      {err && <p style={{ color: 'crimson' }}>{err}</p>}
      <p>
        <strong>{statusLabel(d.status)}</strong>
        {d.terminalId && (
          <>
            {' '}
            · Terminal <code>{d.terminalId}</code>
          </>
        )}
      </p>
      <p style={{ fontSize: 13, color: '#444' }}>
        Idempotency <code>{d.idempotencyKey}</code>
      </p>

      <div style={{ display: 'flex', gap: 8, marginBottom: 16, flexWrap: 'wrap' }}>
        <button type="button" onClick={() => void ingest()}>
          Run ingest
        </button>
        <button type="button" onClick={() => void reprocess()}>
          Reprocess (confirm)
        </button>
      </div>

      <form onSubmit={markReviewed} style={{ marginBottom: 24 }}>
        <label>
          Operator review note{' '}
          <input value={reviewNote} onChange={(e) => setReviewNote(e.target.value)} style={{ minWidth: 280 }} />
        </label>{' '}
        <button type="submit">Save review</button>
      </form>

      <h2>Decoded metadata</h2>
      <p>
        <button type="button" onClick={() => setMetaMode('raw')}>
          Raw
        </button>{' '}
        <button type="button" onClick={() => setMetaMode('pretty')}>
          Pretty
        </button>
      </p>
      <pre style={{ background: '#f6f8fa', padding: 12, fontSize: 12, maxHeight: 320, overflow: 'auto' }}>
        {metadataBlock}
      </pre>

      <h2>Raw payload (hex)</h2>
      <pre style={{ background: '#f6f8fa', padding: 12, fontSize: 11, wordBreak: 'break-all' }}>{d.rawPayloadHex}</pre>

      <h2 data-testid="upload-records">Derived records ({d.records.length})</h2>
      {d.records.length === 0 && <p style={{ color: '#666' }}>No records yet — run ingest.</p>}
      {d.records.length > 0 && (
        <ol style={{ paddingLeft: 18 }}>
          {d.records.map((r) => (
            <li key={r.id} style={{ marginBottom: 16 }}>
              <div style={{ fontSize: 12 }}>
                <code>{r.id}</code>
              </div>
              <pre style={{ fontSize: 11, background: '#fafafa', padding: 8, wordBreak: 'break-all' }}>
                {r.rawPayloadHex}
              </pre>
              <pre style={{ fontSize: 11, background: '#fff', padding: 8 }}>{r.decodedMetadataJson}</pre>
            </li>
          ))}
        </ol>
      )}
    </div>
  )
}

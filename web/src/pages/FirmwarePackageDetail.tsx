import { useCallback, useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { apiGet, apiPost } from '../api/client'

type Detail = {
  id: string
  name: string
  versionLabel: string
  artifactChecksumHex: string
  artifactSizeBytes: number
  metadataJson: string
  primaryArtifactId: string | null
  hardwareValidationNotice: string
  skuRoutingNotice?: string
  manifests?: { id: string; layoutJson: string }[]
  artifacts: { id: string; kind: string; sha256Hex: string; sizeBytes: number; storageRef: string }[]
  targets: { id: string; sku: string; notes: string }[]
  rules: {
    id: string
    requiredTerminalFirmwareVersionId: string | null
    requiredTargetSkuContains: string | null
    notes: string
  }[]
}

export function FirmwarePackageDetail() {
  const { id } = useParams<{ id: string }>()
  const [x, setX] = useState<Detail | null>(null)
  const [err, setErr] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)

  const [artKind, setArtKind] = useState('primary')
  const [artSha, setArtSha] = useState('')
  const [artSize, setArtSize] = useState('1')

  const [sku, setSku] = useState('')
  const [skuNotes, setSkuNotes] = useState('')

  const [ruleFw, setRuleFw] = useState('')
  const [ruleSku, setRuleSku] = useState('')
  const [ruleNotes, setRuleNotes] = useState('')

  const refresh = useCallback(async () => {
    if (!id) return
    setErr(null)
    try {
      const d = await apiGet<Detail>(`/api/firmware/packages/${id}`)
      setX(d)
    } catch (e) {
      setErr(e instanceof Error ? e.message : 'error')
    } finally {
      setLoading(false)
    }
  }, [id])

  useEffect(() => {
    void refresh()
  }, [refresh])

  async function addArtifact(e: React.FormEvent) {
    e.preventDefault()
    if (!id) return
    setErr(null)
    try {
      await apiPost(`/api/firmware/packages/${id}/artifacts`, {
        kind: artKind,
        sha256Hex: artSha,
        sizeBytes: Number(artSize) || 1,
        storageRef: 'registry:ui',
        metadataJson: '{}',
      })
      setArtSha('')
      await refresh()
    } catch (e) {
      setErr(e instanceof Error ? e.message : 'artifact failed')
    }
  }

  async function addTarget(e: React.FormEvent) {
    e.preventDefault()
    if (!id) return
    setErr(null)
    try {
      await apiPost('/api/firmware/targets', {
        firmwarePackageId: id,
        sku: sku || 'SKU',
        notes: skuNotes || null,
      })
      setSku('')
      setSkuNotes('')
      await refresh()
    } catch (e) {
      setErr(e instanceof Error ? e.message : 'target failed')
    }
  }

  async function addRule(e: React.FormEvent) {
    e.preventDefault()
    if (!id) return
    setErr(null)
    try {
      await apiPost(`/api/firmware/packages/${id}/compatibility-rules`, {
        requiredTerminalFirmwareVersionId: ruleFw ? ruleFw : null,
        requiredTargetSkuContains: ruleSku || null,
        notes: ruleNotes || 'rule',
      })
      setRuleFw('')
      setRuleSku('')
      setRuleNotes('')
      await refresh()
    } catch (e) {
      setErr(e instanceof Error ? e.message : 'rule failed')
    }
  }

  if (!id) return <p>Missing id</p>
  if (loading) return <p role="status">Loading package…</p>
  if (err || !x)
    return (
      <div>
        <p>
          <Link to="/firmware/packages">← Packages</Link>
        </p>
        <p role="alert" style={{ color: 'crimson' }}>
          {err ?? 'Package not found.'}
        </p>
      </div>
    )

  return (
    <div>
      <p>
        <Link to="/firmware/packages">← Packages</Link>
      </p>
      <h1>{x.name}</h1>
      <p style={{ color: '#555', fontSize: 14 }}>{x.hardwareValidationNotice}</p>
      {x.skuRoutingNotice && (
        <p style={{ color: '#555', fontSize: 13 }}>{x.skuRoutingNotice}</p>
      )}
      <p>
        <strong>Version</strong> {x.versionLabel} · <strong>Checksum</strong>{' '}
        <code style={{ fontSize: 11 }}>{x.artifactChecksumHex}</code>
      </p>

      <h2 style={{ fontSize: 18 }}>Artifacts</h2>
      {x.artifacts.length === 0 ? (
        <p role="status" style={{ color: '#666' }}>
          No artifacts registered — add SHA256 metadata below (blob storage is out-of-band).
        </p>
      ) : (
        <table cellPadding={6} style={{ borderCollapse: 'collapse', marginBottom: 16 }}>
          <thead>
            <tr style={{ textAlign: 'left' }}>
              <th>Kind</th>
              <th>SHA256</th>
              <th>Size</th>
            </tr>
          </thead>
          <tbody>
            {x.artifacts.map((a) => (
              <tr key={a.id}>
                <td>{a.kind}</td>
                <td style={{ fontFamily: 'monospace', fontSize: 11 }}>{a.sha256Hex}</td>
                <td>{a.sizeBytes}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      <form onSubmit={addArtifact} style={{ marginBottom: 24 }}>
        <h3 style={{ fontSize: 15 }}>Register artifact</h3>
        <label>
          Kind <input value={artKind} onChange={(e) => setArtKind(e.target.value)} />
        </label>{' '}
        <label>
          SHA256 (64 hex){' '}
          <input
            value={artSha}
            onChange={(e) => setArtSha(e.target.value)}
            style={{ width: 360, fontFamily: 'monospace', fontSize: 11 }}
            spellCheck={false}
          />
        </label>{' '}
        <label>
          Size <input value={artSize} onChange={(e) => setArtSize(e.target.value)} />
        </label>{' '}
        <button type="submit">Add artifact</button>
      </form>

      <h2 style={{ fontSize: 18 }}>Targets (matrix rows)</h2>
      {x.targets.length === 0 ? (
        <p role="status" style={{ color: '#666' }}>
          No SKU targets — define packaging matrix rows below.
        </p>
      ) : (
        <table cellPadding={6} style={{ borderCollapse: 'collapse', marginBottom: 16 }}>
          <thead>
            <tr>
              <th>SKU</th>
              <th>Notes</th>
            </tr>
          </thead>
          <tbody>
            {x.targets.map((t) => (
              <tr key={t.id}>
                <td>{t.sku}</td>
                <td>{t.notes}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      <form onSubmit={addTarget}>
        <input placeholder="SKU" value={sku} onChange={(e) => setSku(e.target.value)} />{' '}
        <input placeholder="Notes" value={skuNotes} onChange={(e) => setSkuNotes(e.target.value)} />{' '}
        <button type="submit">Add target</button>
      </form>

      {x.manifests && x.manifests.length > 0 && (
        <>
          <h2 style={{ fontSize: 18, marginTop: 24 }}>Block manifests</h2>
          <ul>
            {x.manifests.map((m) => (
              <li key={m.id}>
                <code style={{ fontSize: 10 }}>{m.id}</code> — layout JSON (opaque){' '}
                <span style={{ color: '#888' }}>(HARDWARE_VALIDATION_REQUIRED)</span>
              </li>
            ))}
          </ul>
        </>
      )}

      <h2 style={{ fontSize: 18, marginTop: 24 }}>Compatibility rules</h2>
      {x.rules.length === 0 ? (
        <p role="status" style={{ color: '#666' }}>
          No rules — any compatible terminal may enqueue jobs unless you add version gates below.
        </p>
      ) : (
        <ul>
          {x.rules.map((r) => (
            <li key={r.id}>
              FW {r.requiredTerminalFirmwareVersionId ?? '—'} · SKU contains {r.requiredTargetSkuContains ?? '—'} ·{' '}
              {r.notes}
            </li>
          ))}
        </ul>
      )}
      <form onSubmit={addRule} style={{ marginTop: 8 }}>
        <p style={{ fontSize: 13, color: '#555' }}>
          Optional <code>requiredTerminalFirmwareVersionId</code> from <Link to="/firmware/versions">firmware versions</Link>.
        </p>
        <input
          placeholder="Firmware version id (guid)"
          value={ruleFw}
          onChange={(e) => setRuleFw(e.target.value)}
          style={{ width: 360 }}
        />{' '}
        <input placeholder="SKU contains" value={ruleSku} onChange={(e) => setRuleSku(e.target.value)} />{' '}
        <input placeholder="Notes" value={ruleNotes} onChange={(e) => setRuleNotes(e.target.value)} />{' '}
        <button type="submit">Add rule</button>
      </form>
    </div>
  )
}

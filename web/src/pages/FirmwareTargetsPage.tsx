import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { apiGet } from '../api/client'

type Target = { id: string; firmwarePackageId: string; sku: string; notes: string }
type Pkg = { id: string; name: string; versionLabel: string }

export function FirmwareTargetsPage() {
  const [targets, setTargets] = useState<Target[]>([])
  const [pkgs, setPkgs] = useState<Pkg[]>([])
  const [loading, setLoading] = useState(true)
  const [err, setErr] = useState<string | null>(null)

  const refresh = useCallback(async () => {
    setErr(null)
    setLoading(true)
    try {
      const [t, p] = await Promise.all([
        apiGet<Target[]>('/api/firmware/targets'),
        apiGet<Pkg[]>('/api/firmware/packages'),
      ])
      setTargets(t)
      setPkgs(p)
    } catch (e) {
      setErr(e instanceof Error ? e.message : 'Failed to load targets.')
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    void refresh()
  }, [refresh])

  const labelFor = useMemo(() => {
    const m = new Map<string, string>()
    for (const p of pkgs) m.set(p.id, `${p.name} (${p.versionLabel})`)
    return m
  }, [pkgs])

  const byPkg = useMemo(() => {
    const g = new Map<string, Target[]>()
    for (const t of targets) {
      const list = g.get(t.firmwarePackageId) ?? []
      list.push(t)
      g.set(t.firmwarePackageId, list)
    }
    return g
  }, [targets])

  return (
    <div>
      <h1>Firmware target matrix</h1>
      <p style={{ fontSize: 14, color: '#444', maxWidth: 720 }}>
        Rows per package SKU — used with compatibility rules (host-side gate). Does not imply live modem routing until
        HARDWARE_VALIDATION_REQUIRED is cleared.
      </p>
      {loading && <p role="status">Loading…</p>}
      {err && <p role="alert" style={{ color: 'crimson' }}>{err}</p>}

      {!loading && !err && targets.length === 0 && (
        <p role="status" style={{ color: '#666' }}>
          No targets yet — register SKU rows from a package detail page.
        </p>
      )}

      {Array.from(byPkg.entries()).map(([pkgId, rows]) => (
        <section key={pkgId} style={{ marginBottom: 28 }}>
          <h2 style={{ fontSize: 17 }}>
            <Link to={`/firmware/packages/${pkgId}`}>{labelFor.get(pkgId) ?? pkgId}</Link>
          </h2>
          <table cellPadding={8} style={{ borderCollapse: 'collapse' }}>
            <thead>
              <tr style={{ textAlign: 'left', borderBottom: '1px solid #ccc' }}>
                <th>SKU</th>
                <th>Notes</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((r) => (
                <tr key={r.id} style={{ borderBottom: '1px solid #eee' }}>
                  <td>{r.sku}</td>
                  <td>{r.notes}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </section>
      ))}
    </div>
  )
}

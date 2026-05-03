import { Link, Outlet } from 'react-router-dom'
import { FirmwareLiveFlashBanner } from './FirmwareLiveFlashBanner'
import { GlobalSearch } from './GlobalSearch'
import { SimulationBanner } from './SimulationBanner'

const links = [
  ['/', 'Dashboard'],
  ['/customers', 'Customers'],
  ['/sites', 'Sites'],
  ['/terminals', 'Terminals'],
  ['/ncc-sessions', 'NCC Sessions'],
  ['/ncc-frame-captures', 'NCC frame captures'],
  ['/dlog', 'DLOG Transactions'],
  ['/table-definitions', 'Table definitions'],
  ['/table-versions', 'Table versions'],
  ['/table-sets', 'Table sets'],
  ['/downloads', 'Downloads'],
  ['/uploads', 'Uploads'],
  ['/rate-plans', 'Rate plans'],
  ['/number-classes', 'Number classes'],
  ['/rating-quote', 'Rating quote'],
  ['/call-records', 'Call records'],
  ['/card-products', 'Card Products'],
  ['/card-accounts', 'Card Accounts'],
  ['/craft', 'Craft Sessions'],
  ['/firmware/packages', 'Firmware packages'],
  ['/firmware/targets', 'Firmware targets'],
  ['/firmware/versions', 'Firmware versions'],
  ['/firmware/jobs', 'Firmware jobs'],
  ['/audit', 'Audit Events'],
  ['/status', 'Platform status'],
  ['/settings', 'Settings'],
] as const

export function Layout() {
  return (
    <div style={{ display: 'flex', minHeight: '100vh', fontFamily: 'system-ui' }}>
      <aside style={{ width: 260, borderRight: '1px solid #ddd', padding: 12 }}>
        <div style={{ fontWeight: 700, marginBottom: 12 }}>CoinLine Management Console</div>
        <p style={{ fontSize: 12, color: '#555', marginTop: -8, marginBottom: 12 }}>
          Operator role (placeholder): headers send <code>X-Operator-Role: Admin</code> — wire to IdP / RBAC later.
        </p>
        <nav style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
          {links.map(([to, label]) => (
            <Link key={to} to={to} style={{ textDecoration: 'none', color: '#0b5' }}>
              {label}
            </Link>
          ))}
        </nav>
      </aside>
      <main style={{ flex: 1, padding: 16 }}>
        <SimulationBanner />
        <FirmwareLiveFlashBanner />
        <GlobalSearch />
        <Outlet />
      </main>
    </div>
  )
}

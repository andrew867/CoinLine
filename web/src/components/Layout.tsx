import { useEffect, useState } from 'react'
import { NavLink, Outlet, useLocation } from 'react-router-dom'
import { FirmwareLiveFlashBanner } from './FirmwareLiveFlashBanner'
import { GlobalSearch } from './GlobalSearch'
import { consoleSession, type ScreenPermissions } from '../api/consoleSession'
import { clearDownloadReviews, readDownloadReviews } from '../api/downloadReviews'
import { DownloadReviewFooter } from './DownloadReviewReminder'

const screenByPath: Record<string, string> = {
  '/': 'overview',
  '/terminals': 'phones',
  '/call-records': 'calls',
  '/downloads': 'downloads',
  '/field-events': 'phone-status',
  '/field-event-destinations': 'field-event-destinations',
  '/rate-plans': 'rate-plans',
  '/rating-quote': 'rating',
  '/card-accounts': 'card-accounts',
  '/card-reconciliation': 'reconciliation',
  '/payment-transactions': 'card-transactions',
  '/customers': 'customers',
  '/sites': 'sites',
  '/terminal-groups': 'groups',
  '/table-sets': 'table-sets',
  '/carrier-library': 'carrier-library',
  '/card-master': 'card-master',
  '/quick-access-customers': 'quick-access-customers',
  '/card-products': 'card-products',
  '/ncc-sessions': 'ncc-sessions',
  '/dlog': 'message-log',
  '/uploads': 'uploads',
  '/audit': 'audit',
  '/craft': 'craft',
  '/firmware/packages': 'firmware-registry',
  '/firmware/targets': 'firmware-registry',
  '/firmware/versions': 'firmware-registry',
  '/firmware/jobs': 'firmware-jobs',
  '/table-definitions': 'table-definitions',
  '/table-versions': 'table-records',
  '/number-classes': 'number-classes',
  '/ncc-frame-captures': 'frame-captures',
  '/status': 'transport',
  '/smartcard-types': 'smartcards',
  '/inmate-features': 'inmate-features',
}

export function mayOpenScreen(path: string, role: string | null, permissions: ScreenPermissions | null) {
  if (path === '/operators') return role === null || role === 'Admin'
  if (role === 'Admin' || permissions === null) return true
  if (path === '/terminal-groups') return ((permissions.groups ?? 0) | (permissions.memberships ?? 0)) !== 0
  if (path === '/terminals/new') return ((permissions.phones ?? 0) & 4) !== 0
  if (path === '/table-versions') return ((permissions['table-records'] ?? 0) & 1) !== 0
  const root = Object.keys(screenByPath).find((base) => path === base || (base !== '/' && path.startsWith(base + '/')))
  return root === undefined || (permissions[screenByPath[root]] ?? 0) !== 0
}

const groups = [
  {
    title: 'Today',
    links: [
      ['/', 'Home'],
      ['/terminals', 'Phones'],
      ['/call-records', 'Calls'],
      ['/downloads', 'Downloads'],
      ['/field-events', 'Terminal status'],
      ['/field-event-destinations', 'Event destinations'],
    ],
  },
  {
    title: 'Money',
    links: [
      ['/rate-plans', 'Rate plans'],
      ['/rating-quote', 'Price a call'],
      ['/card-accounts', 'Card accounts'],
      ['/card-reconciliation', 'Reconciliation'],
      ['/payment-transactions', 'Card payments'],
    ],
  },
  {
    title: 'Setup',
    links: [
      ['/customers', 'Customers'],
      ['/sites', 'Sites'],
      ['/terminal-groups', 'Phone groups'],
      ['/table-sets', 'Table sets'],
      ['/table-versions', 'Configuration library'],
      ['/carrier-library', 'Carrier library'],
      ['/card-master', 'Card Master List'],
      ['/quick-access-customers', 'Quick Access Customers'],
      ['/card-products', 'Card products'],
      ['/operators', 'People'],
    ],
  },
  {
    title: 'Records',
    links: [
      ['/ncc-sessions', 'Phone sessions'],
      ['/dlog', 'Message log'],
      ['/uploads', 'Uploads'],
      ['/audit', 'Audit'],
    ],
  },
  {
    title: 'More',
    links: [
      ['/craft', 'Technician craft'],
      ['/firmware/packages', 'Firmware'],
      ['/firmware/jobs', 'Firmware jobs'],
      ['/table-definitions', 'Table definitions'],
      ['/number-classes', 'Number classes'],
      ['/ncc-frame-captures', 'Frame captures'],
      ['/status', 'Platform status'],
      ['/settings', 'Settings'],
    ],
  },
] as const

export function Layout() {
  const location = useLocation()
  const [navOpen, setNavOpen] = useState(false)
  const role = consoleSession.role()
  const permissions = consoleSession.permissions()
  const allow = (path: string) => mayOpenScreen(path, role, permissions)
  useEffect(() => {
    const onStorage = (event: StorageEvent) => {
      if (event.storageArea === localStorage && (event.key === 'coinline.token' || event.key === null))
        clearDownloadReviews()
    }
    window.addEventListener('storage', onStorage)
    return () => window.removeEventListener('storage', onStorage)
  }, [])
  return (
    <div className="shell">
      <aside className="sidebar">
        <div className="sidebar-header">
          <div>
            <p className="brand">CoinLine</p>
            <p className="brand-note">Payphone manager</p>
          </div>
          <button type="button" className="sidebar-toggle" aria-controls="primary-navigation" aria-expanded={navOpen}
            onClick={() => setNavOpen((value) => !value)}>Menu</button>
        </div>
        <nav id="primary-navigation" aria-label="Main" data-open={navOpen} className="sidebar-navigation">
          {groups.map((group) => ({ ...group, links: group.links.filter(([to]) => allow(to)) }))
            .filter((group) => group.links.length > 0).map((group) => (
            <div className="nav-group" key={group.title}>
              <p className="nav-label">{group.title}</p>
              {group.links.map(([to, label]) => (
                <NavLink key={to} to={to} end={to === '/'} onClick={() => setNavOpen(false)}>
                  {label}
                </NavLink>
              ))}
            </div>
          ))}
          <button type="button" className="sign-out" onClick={() => {
            if (readDownloadReviews().length > 0 && !window.confirm('There are pending download reviews in this tab. Sign out without reviewing them?')) return
            consoleSession.clear()
            window.location.assign('/login')
          }}>Sign out</button>
        </nav>
      </aside>
      <main className="content">
        {allow(location.pathname) && location.pathname.startsWith('/firmware') && <FirmwareLiveFlashBanner />}
        {(permissions === null || role === 'Admin' || ((permissions.overview ?? 0) & 1) !== 0) && <GlobalSearch />}
        {allow(location.pathname) ? <Outlet /> : (
          <section>
            <h1>Screen unavailable</h1>
            <p>Your account does not have permission for this screen. Choose another page from the menu, or contact an administrator.</p>
          </section>
        )}
        <DownloadReviewFooter canReadPhones={role === 'Admin' || permissions === null || ((permissions.phones ?? 0) & 1) !== 0} />
      </main>
    </div>
  )
}

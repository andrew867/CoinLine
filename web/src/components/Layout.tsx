import { NavLink, Outlet, useLocation } from 'react-router-dom'
import { FirmwareLiveFlashBanner } from './FirmwareLiveFlashBanner'
import { GlobalSearch } from './GlobalSearch'
import { consoleSession } from '../api/consoleSession'

const groups = [
  {
    title: 'Today',
    links: [
      ['/', 'Home'],
      ['/terminals', 'Phones'],
      ['/call-records', 'Calls'],
      ['/downloads', 'Downloads'],
      ['/field-events', 'Alarms'],
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
  return (
    <div className="shell">
      <aside className="sidebar">
        <p className="brand">CoinLine</p>
        <p className="brand-note">Payphone manager</p>
        {groups.map((group) => (
          <div className="nav-group" key={group.title}>
            <p className="nav-label">{group.title}</p>
            {group.links.map(([to, label]) => (
              <NavLink key={to} to={to} end={to === '/'}>
                {label}
              </NavLink>
            ))}
          </div>
        ))}
        <button
          type="button"
          className="sign-out"
          onClick={() => {
            consoleSession.clear()
            window.location.assign('/login')
          }}
        >
          Sign out
        </button>
      </aside>
      <main className="content">
        {location.pathname.startsWith('/firmware') && <FirmwareLiveFlashBanner />}
        <GlobalSearch />
        <Outlet />
      </main>
    </div>
  )
}

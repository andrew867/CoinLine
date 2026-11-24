import { useState } from 'react'
import { NavLink, Outlet, useLocation } from 'react-router-dom'
import { FirmwareLiveFlashBanner } from './FirmwareLiveFlashBanner'
import { GlobalSearch } from './GlobalSearch'

const groups = [
  {
    title: 'Today',
    links: [
      ['/', 'Home'],
      ['/terminals', 'Phones'],
      ['/call-records', 'Calls'],
      ['/downloads', 'Downloads'],
      ['/field-events', 'Terminal status'],
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
  const [navOpen, setNavOpen] = useState(false)
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
          {groups.map((group) => (
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
            localStorage.removeItem('coinline.token')
            window.location.assign('/login')
          }}>Sign out</button>
        </nav>
      </aside>
      <main className="content">
        {location.pathname.startsWith('/firmware') && <FirmwareLiveFlashBanner />}
        <GlobalSearch />
        <Outlet />
      </main>
    </div>
  )
}

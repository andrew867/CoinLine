import { Link } from 'react-router-dom'
import { EmptyHint } from '../components/AppStates'

export function NotFoundPage() {
  return (
    <div style={{ maxWidth: 560 }}>
      <h1>Page not found</h1>
      <EmptyHint>No content for this URL — use the links below or the sidebar.</EmptyHint>
      <p>The route does not exist in this operator UI build.</p>
      <ul>
        <li>
          <Link to="/">Dashboard</Link>
        </li>
        <li>
          <Link to="/status">Platform status</Link>
        </li>
        <li>
          <Link to="/audit">Audit events</Link>
        </li>
      </ul>
    </div>
  )
}

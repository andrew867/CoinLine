type LoadingProps = { label?: string }

export function LoadingBlock({ label = 'Loading…' }: LoadingProps) {
  return (
    <p role="status" style={{ color: '#555' }}>
      {label}
    </p>
  )
}

type ErrProps = { message: string }

export function ErrorBanner({ message }: ErrProps) {
  return (
    <p role="alert" style={{ color: 'crimson', maxWidth: 720 }}>
      {message}
    </p>
  )
}

type EmptyProps = { children: React.ReactNode }

export function EmptyHint({ children }: EmptyProps) {
  return (
    <p role="status" style={{ color: '#666', maxWidth: 720 }}>
      {children}
    </p>
  )
}

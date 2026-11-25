export function dispositionWords(status: number): string {
  return (
    ({
      0: 'Unknown',
      1: 'Completed',
      2: 'Blocked',
      3: 'Free call',
      4: 'Emergency',
      5: 'Failed',
    } as Record<number, string>)[status] ?? 'Unknown'
  )
}

export function reconciliationWords(status: number): string {
  return (
    ({
      0: 'Unknown',
      1: 'Pending',
      2: 'Matched',
      3: 'Exception',
    } as Record<number, string>)[status] ?? 'Unknown'
  )
}

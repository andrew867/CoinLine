import { discountZones } from './timeDiscountPreview'

export function discountCalendarFixture() {
  const field = (key: string, label: string, value: number, max: number, choices: { value: number; label: string }[] | null = null) => ({
    key, label, value: String(value), editor: choices ? 'select' : 'number', min: 0, max, maxLength: null, choices, help: null,
  })
  const labels = ['Weekdays', 'Saturday', 'Sunday', 'Holidays']
  const windows = [[8, 0, 12, 0], [11, 0, 18, 0], [22, 0, 2, 0], [0, 0, 0, 0], [10, 0, 12, 0], [0, 0, 0, 0], [0, 0, 24, 0], [0, 0, 0, 0]]
  const sections = [
    ...Array.from({ length: 10 }, (_, index) => ({ key: `holiday-${index}`, label: `Holiday ${index + 1}`, fields: [
      field(`holiday-${index}.month`, 'Month', index === 0 ? 12 : index === 9 ? 2 : 0, 12),
      field(`holiday-${index}.day`, 'Day of month', index === 0 ? 25 : index === 9 ? 29 : 0, 31),
    ] })),
    ...windows.map(([sh, sm, eh, em], index) => ({ key: `period-${index}`, label: `${labels[Math.floor(index / 2)]}, period ${index % 2 + 1}`, fields: [
      field(`period-${index}.startHour`, 'Starts at hour', sh, 23), field(`period-${index}.startMinute`, 'Start minute', sm, 59),
      field(`period-${index}.endHour`, 'Ends at hour', eh, 24), field(`period-${index}.endMinute`, 'End minute', em, 59),
      ...discountZones.flatMap(zone => [
        field(`period-${index}.${zone}Index`, `${zone} discount`, zone === 'local' ? index : 8, 15,
          Array.from({ length: 10 }, (_, value) => ({ value, label: `MT93 discount ${value + 1}` }))),
        field(`period-${index}.${zone}Payment`, `${zone} payment`, index === 0 ? 2 : index === 1 ? 1 : 3, 3,
          ['Disabled', 'Coins', 'Cards', 'Coins and cards'].map((label, value) => ({ value, label }))),
      ]),
    ] })),
  ]
  return { sections, values: Object.fromEntries(sections.flatMap(section => section.fields.map(entry => [entry.key, entry.value]))) }
}

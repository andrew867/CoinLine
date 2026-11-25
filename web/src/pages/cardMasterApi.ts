export type CardMasterEntry = { id: string; code: string; name: string; tableNumber: number; revision: number; archived: boolean;
  standard: string; low: string | null; high: string | null }
export type CardMasterField = { key: string; label: string; editor: string; value: string | null; min: number; max: number;
  maxLength: number | null; choices: { value: number; label: string }[] | null; help: string | null; targetMapping: boolean }
export type CardMasterSection = { key: string; label: string; fields: CardMasterField[] }
export type CardMasterDetails = { entry: CardMasterEntry; sections: CardMasterSection[] }

export const cardMasterEndpoint = '/api/card-master'
export async function cardMasterResponse<T>(response: Response): Promise<T> {
  const body = await response.json().catch(() => null)
  if (!response.ok) throw new Error(body?.error ?? `Card master request failed (${response.status}).`)
  return body as T
}

import { apiFetch } from './client'
import type { Technician } from '../types'

export function getTechnicians(token: string): Promise<Technician[]> {
  return apiFetch<Technician[]>('/technicians', { token })
}

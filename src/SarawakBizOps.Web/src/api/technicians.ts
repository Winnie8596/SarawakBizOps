import { apiFetch } from './client'
import type { Technician } from '../types'

export function getTechnicians(): Promise<Technician[]> {
  return apiFetch<Technician[]>('/technicians')
}

import { apiFetch } from './client'
import type { CreateUserInput, UpdateUserInput, UserSummary } from '../types'

export function getUsers(): Promise<UserSummary[]> {
  return apiFetch<UserSummary[]>('/users')
}

export function createUser(input: CreateUserInput): Promise<UserSummary> {
  return apiFetch<UserSummary>('/users', { method: 'POST', body: JSON.stringify(input) })
}

export function updateUser(id: string, input: UpdateUserInput): Promise<UserSummary> {
  return apiFetch<UserSummary>(`/users/${id}`, { method: 'PATCH', body: JSON.stringify(input) })
}

export function resetUserPassword(id: string, newPassword: string): Promise<void> {
  return apiFetch<void>(`/users/${id}/reset-password`, {
    method: 'POST',
    body: JSON.stringify({ newPassword })
  })
}

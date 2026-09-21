import { apiFetch } from './client'
import type { CreateUserInput, UpdateUserInput, UserSummary } from '../types'

export function getUsers(token: string): Promise<UserSummary[]> {
  return apiFetch<UserSummary[]>('/users', { token })
}

export function createUser(token: string, input: CreateUserInput): Promise<UserSummary> {
  return apiFetch<UserSummary>('/users', { method: 'POST', token, body: JSON.stringify(input) })
}

export function updateUser(token: string, id: string, input: UpdateUserInput): Promise<UserSummary> {
  return apiFetch<UserSummary>(`/users/${id}`, { method: 'PATCH', token, body: JSON.stringify(input) })
}

export function resetUserPassword(token: string, id: string, newPassword: string): Promise<void> {
  return apiFetch<void>(`/users/${id}/reset-password`, {
    method: 'POST',
    token,
    body: JSON.stringify({ newPassword })
  })
}

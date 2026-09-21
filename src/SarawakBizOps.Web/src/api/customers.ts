import { apiFetch } from './client'
import type { Customer, CustomerInput } from '../types'

export function getCustomers(token: string): Promise<Customer[]> {
  return apiFetch<Customer[]>('/customers', { token })
}

export function getCustomer(token: string, id: number): Promise<Customer> {
  return apiFetch<Customer>(`/customers/${id}`, { token })
}

export function createCustomer(token: string, input: CustomerInput): Promise<Customer> {
  return apiFetch<Customer>('/customers', {
    method: 'POST',
    token,
    body: JSON.stringify(input)
  })
}

export function updateCustomer(token: string, id: number, input: CustomerInput): Promise<void> {
  return apiFetch<void>(`/customers/${id}`, {
    method: 'PUT',
    token,
    body: JSON.stringify(input)
  })
}

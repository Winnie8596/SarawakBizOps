import { apiFetch } from './client'
import type { Customer, CustomerInput, ServiceHistory } from '../types'

export function getCustomers(): Promise<Customer[]> {
  return apiFetch<Customer[]>('/customers')
}

export function getCustomer(id: number): Promise<Customer> {
  return apiFetch<Customer>(`/customers/${id}`)
}

export function createCustomer(input: CustomerInput): Promise<Customer> {
  return apiFetch<Customer>('/customers', {
    method: 'POST',
    body: JSON.stringify(input)
  })
}

export function updateCustomer(id: number, input: CustomerInput): Promise<void> {
  return apiFetch<void>(`/customers/${id}`, {
    method: 'PUT',
    body: JSON.stringify(input)
  })
}

export function getCustomerHistory(id: number): Promise<ServiceHistory> {
  return apiFetch<ServiceHistory>(`/customers/${id}/history`)
}

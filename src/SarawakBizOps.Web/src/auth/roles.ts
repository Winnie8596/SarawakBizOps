// Roles that work with customers, equipment and service history (PRD §5).
export const OFFICE_ROLES = ['Admin', 'Manager', 'ServiceStaff'] as const

// Phase 6: the warehouse runs inventory; a Manager may look (low stock feeds the dashboard).
export const INVENTORY_ROLES = ['Admin', 'Manager', 'WarehouseStaff'] as const
export const INVENTORY_EDIT_ROLES = ['Admin', 'WarehouseStaff'] as const

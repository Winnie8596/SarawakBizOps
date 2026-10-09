// Compile-time only: every request body the app builds by hand (src/types/index.ts) must fit the API's request schema
// from the OpenAPI document. If a DTO gains a required field or changes a type, `npm run build` fails here.
import type { components } from '../api/schema'
import type {
  CreateUserInput,
  CustomerInput,
  DiagnosisInput,
  EquipmentInput,
  EquipmentUpdateInput,
  PartInput,
  PartUpdateInput,
  PendingPartsInput,
  ServiceRequestInput,
  StockMovementInput,
  UpdateUserInput
} from '.'

type Schemas = components['schemas']
type Fits<Input extends Request, Request> = [Input, Request]

export type RequestBodiesFitTheApi = [
  Fits<CustomerInput, Schemas['CustomerRequest']>,
  Fits<EquipmentInput, Schemas['EquipmentRequest']>,
  Fits<EquipmentUpdateInput, Schemas['EquipmentUpdateRequest']>,
  Fits<CreateUserInput, Schemas['CreateUserRequest']>,
  Fits<UpdateUserInput, Schemas['UpdateUserRequest']>,
  Fits<ServiceRequestInput, Schemas['CreateServiceRequestRequest']>,
  Fits<PendingPartsInput, Schemas['PendingPartsRequest']>,
  Fits<DiagnosisInput, Schemas['DiagnosisUpdateRequest']>,
  Fits<PartInput, Schemas['CreatePartRequest']>,
  Fits<PartUpdateInput, Schemas['UpdatePartRequest']>,
  Fits<StockMovementInput, Schemas['CreateInventoryTransactionRequest']>
]

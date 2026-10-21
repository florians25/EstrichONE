import type { BauakteProject } from "./project";
/** Typen der EstrichONE-API v1 (contracts/openapi/v1.json). Feldnamen 1:1 wie im Vertrag. */

export interface UserSummary {
  id: string;
  displayName: string;
  userName: string;
  email: string | null;
}

export interface WebSessionResponse {
  user: UserSummary;
  mustChangePassword: boolean;
  accessTokenExpiresAt: string;
}

export interface ServerInfo {
  product: string;
  serverName: string;
  serverVersion: string;
  apiVersions: string[];
  minClientVersion: string;
  authMethods: string[];
  instanceEpoch: string;
  setupRequired: boolean;
}

export type BauakteStatus = "inquiry" | "planned" | "inProgress" | "drying" | "completed" | "cancelled";

export interface BauakteListItem {
  id: string;
  number: string | null;
  title: string;
  status: BauakteStatus;
  street: string | null;
  postalCode: string | null;
  city: string | null;
  customerId: string | null;
  updatedAt: string;
  version: number;
  responsibleUserId: string | null;
  latitude: number | null;
  longitude: number | null;
}

export interface BauakteMapPoint {
  id: string;
  number: string | null;
  title: string;
  status: BauakteStatus;
  street: string | null;
  city: string | null;
  latitude: number;
  longitude: number;
  responsibleUserId: string | null;
}

export interface AuditLogItem {
  id: string;
  occurredAt: string;
  action: string;
  actorUserId: string | null;
  actorName: string | null;
  entityType: string | null;
  entityId: string | null;
  ipAddress: string | null;
  details: Record<string, string | null>;
}

export interface AuditLogPage {
  items: AuditLogItem[];
  nextCursor: string | null;
}

export interface BauaktePage {
  items: BauakteListItem[];
  nextCursor: string | null;
}

export interface BauakteDetail extends BauakteListItem {
  latitude: number | null;
  longitude: number | null;
  description: string | null;
  templateKey: string | null;
  responsibleUserId: string | null;
  createdByUserId: string;
  createdAt: string;
  plannedStart: string | null;
  plannedEnd: string | null;
  siteContactName: string | null;
  siteContactPhone: string | null;
  siteContactEmail: string | null;
  siteNotes: string | null;
  cancellationReason: string | null;
  /** Kennwerte des Bauvorhabens (Sync-Feld „project“). */
  project?: BauakteProject | null;
}

export type BauakteHintSeverity = "info" | "success" | "warning" | "danger";

export interface BauakteHint {
  kind: string;
  severity: BauakteHintSeverity;
  text: string;
}

export interface BauakteMember {
  userId: string;
  displayName: string;
  email: string | null;
  role: "responsible" | "member";
  isActive: boolean;
}

export type DocumentCategory = "offer" | "order" | "invoice" | "plan" | "delivery" | "acceptance" | "protocol" | "customer" | "office" | "other";

export interface FileResponse {
  id: string;
  bauakteId: string | null;
  kind: "photo" | "video" | "document";
  state: "awaitingUpload" | "stored";
  variants: "notApplicable" | "pending" | "ready" | "failed";
  originalName: string;
  contentType: string;
  sizeBytes: number;
  caption: string | null;
  capturedAt: string | null;
  createdAt: string;
  uploadedByUserId: string;
  category: DocumentCategory | null;
  folder: string | null;
  roomId: string | null;
  workItemId: string | null;
  previousVersionId: string | null;
  latitude: number | null;
  longitude: number | null;
  version: number;
}

export interface RoleResponse {
  id: string;
  name: string;
  description: string | null;
  isSystem: boolean;
  permissions: string[];
  version: number;
}

export interface UserListItem {
  id: string;
  displayName: string;
  userName: string;
  email: string | null;
  phone: string | null;
  isActive: boolean;
  lastLoginAt: string | null;
  roleIds: string[];
  version: number;
}

export interface UserWithPassword {
  user: UserListItem;
  oneTimePassword: string;
}

export type DeviceStatus = "pending" | "active" | "locked" | "retired";
export type DeviceKind = "personal" | "pool";

export interface DeviceResponse {
  id: string;
  name: string;
  platform: "ios" | "android" | "windows" | "other";
  kind: DeviceKind;
  status: DeviceStatus;
  model: string | null;
  osVersion: string | null;
  appVersion: string | null;
  assignedUserId: string | null;
  registeredAt: string;
  approvedAt: string | null;
  lastSeenAt: string | null;
  version: number;
}

export interface EnrollmentCodeResponse {
  code: string;
  kind: DeviceKind;
  deviceName: string | null;
  expiresAt: string;
  link: string | null;
}

export interface BackupRunInfo {
  status: string;
  message: string;
  at: string;
}

export interface BackupStatusResponse {
  configured: boolean;
  healthy: boolean;
  summary: string;
  lastRun: BackupRunInfo | null;
  lastSuccess: BackupRunInfo | null;
  lastCheck: BackupRunInfo | null;
  lastRestoreTest: BackupRunInfo | null;
  runRequested: boolean;
}

/** Fehlerformat RFC 9457 mit stabilem `code`. */
export interface Problem {
  status?: number;
  title?: string;
  detail?: string;
  code?: string;
  errors?: Record<string, string[]>;
}

export interface DirectoryUser {
  id: string;
  displayName: string;
  isActive: boolean;
}

export interface BauakteHistoryEntry {
  at: string;
  action: string;
  actorUserId: string | null;
  actorName: string | null;
  text: string;
  changes?: { field: string; label: string; from: string | null; to: string | null }[] | null;
}

export type DiaryWeather = "sunny" | "cloudy" | "rain" | "snow" | "frost" | "wind";

export interface DiaryEntry {
  id: string;
  bauakteId: string;
  authorUserId: string;
  day: string;
  text: string;
  weather: DiaryWeather | null;
  temperatureCelsius: number | null;
  workersPresent: number | null;
  createdAt: string;
  editedAt: string | null;
  version: number;
  startTime: string | null;
  endTime: string | null;
  crew: string | null;
  photoFileIds: string[] | null;
}

export type CheckResult = "ok" | "notOk" | "notApplicable";

export interface Inspection {
  id: string;
  bauakteId: string;
  kind: "initialInspection" | "cmMeasurement";
  performedByUserId: string;
  performedAt: string;
  correctsInspectionId: string | null;
  remarks: string | null;
  notes?: string | null;
  notRequired: boolean;
  checks: Record<string, CheckResult>;
  photoFileIds: string[];
  location: string | null;
  binder: "cement" | "calciumSulfate" | null;
  heated: boolean | null;
  thicknessMm: number | null;
  measuredCmPercent: number | null;
  limitCmPercent: number | null;
  isReadyForCovering: boolean | null;
}

export type TicketCategory = "signIn" | "sync" | "appError" | "deviceDefect" | "officeIt" | "access" | "other";
export type TicketPriority = "low" | "normal" | "high" | "urgent";
export type TicketStatus = "new" | "inProgress" | "waitingForReporter" | "resolved" | "closed";

export interface TicketSummary {
  id: string;
  number: string;
  title: string;
  category: TicketCategory;
  priority: TicketPriority;
  status: TicketStatus;
  reporterBlocked: boolean;
  reporterUserId: string | null;
  reporterDisplayName: string | null;
  assigneeUserId: string | null;
  assigneeName: string | null;
  source: "web" | "desktop" | "ios" | "android";
  createdAt: string;
  lastActivityAt: string;
  version: number;
}

export interface TicketComment {
  id: string;
  authorUserId: string;
  authorName: string | null;
  text: string;
  isInternal: boolean;
  createdAt: string;
}

export interface TicketEvent {
  actorUserId: string | null;
  actorName: string | null;
  kind: string;
  fromValue: string | null;
  toValue: string | null;
  at: string;
}

export interface TicketDetail {
  summary: TicketSummary;
  description: string | null;
  deviceId: string | null;
  deviceName: string | null;
  bauakteId: string | null;
  resolution: string | null;
  resolvedAt: string | null;
  closedAt: string | null;
  diagnostics: Record<string, unknown> | null;
  attachmentFileIds: string[];
  comments: TicketComment[];
  events: TicketEvent[];
}

export type ContactKind = "company" | "person";

export interface ContactResponse {
  id: string;
  kind: ContactKind;
  name: string;
  customerNumber: string | null;
  street: string | null;
  postalCode: string | null;
  city: string | null;
  phone: string | null;
  email: string | null;
  notes: string | null;
  version: number;
}

export interface NoteDto {
  id: string;
  bauakteId: string;
  authorUserId: string;
  text: string;
  isPinned: boolean;
  color: string | null;
  createdAt: string;
  editedAt: string | null;
  version: number;
}

export type WorkItemKind = "task" | "defect";
export type WorkItemStatus = "open" | "inProgress" | "done";

export interface WorkItemDto {
  id: string;
  bauakteId: string | null;
  kind: WorkItemKind;
  status: WorkItemStatus;
  title: string;
  description: string | null;
  dueDate: string | null;
  assigneeUserId: string | null;
  createdByUserId: string;
  completedAt: string | null;
  updatedAt: string;
  version: number;
}

export interface ScreedTypeResponse {
  id: string;
  name: string;
  designation: string | null;
  binder: string;
  isActive: boolean;
}

export type RoomStatus = "planned" | "laid" | "ready";

export interface SectionResponse {
  id: string;
  name: string;
  sortOrder: number;
}

export interface RoomResponse {
  id: string;
  sectionId: string | null;
  name: string;
  floor: string | null;
  areaSquareMeters: number | null;
  screedTypeId: string | null;
  screedTypeName: string | null;
  thicknessMillimeters: number | null;
  insulationMillimeters: number | null;
  underfloorHeating: boolean;
  status: RoomStatus;
  notes: string | null;
  version: number;
}

export interface RoomOverview {
  sections: SectionResponse[];
  rooms: RoomResponse[];
  totalAreaSquareMeters: number;
  heatedAreaSquareMeters: number;
}

export type ResourceKind = "vehicle" | "pump" | "machine";
export type AbsenceKind = "vacation" | "sick" | "training" | "other";

export interface ResourceResponse {
  id: string;
  kind: ResourceKind;
  name: string;
  identifier: string | null;
  notes: string | null;
  isActive: boolean;
  version: number;
}

export interface CrewResponse {
  id: string;
  name: string;
  leaderUserId: string | null;
  memberUserIds: string[];
  color: string | null;
  isActive: boolean;
  sortOrder: number;
  version: number;
}

export interface AssignmentView {
  id: string;
  day: string;
  startTime: string | null;
  activity: string;
  bauakteId: string;
  bauakteNumber: string | null;
  bauakteTitle: string;
  bauakteAddress: string | null;
  crewId: string | null;
  crewName: string | null;
  crewColor: string | null;
  people: { id: string; name: string }[];
  resources: { id: string; name: string; kind: ResourceKind }[];
  notes: string | null;
  warnings: string[];
  version: number;
  kind: AssignmentKind;
  endTime: string | null;
  bauakteStatus: BauakteStatus | null;
}

export type AssignmentKind = "screed" | "initialInspection" | "secondInspection" | "cmMeasurement" | "acceptance" | "survey" | "check"
  | "customerAppointment" | "delivery" | "other";

export interface BacklogItem {
  bauakteId: string;
  number: string | null;
  title: string;
  status: BauakteStatus;
  street: string | null;
  postalCode: string | null;
  city: string | null;
  plannedStart: string | null;
  plannedEnd: string | null;
  customerName: string | null;
  totalAreaSquareMeters: number;
  screedTypes: string[];
  upcomingAssignments: number;
  nextAssignment: string | null;
}

export interface AbsenceView {
  id: string;
  userId: string;
  userName: string;
  from: string;
  to: string;
  kind: AbsenceKind;
  note: string | null;
}

export interface PlanningRange {
  from: string;
  to: string;
  assignments: AssignmentView[];
  absences: AbsenceView[];
}

export type CallKind = "call" | "inquiry";
export type CallDirection = "inbound" | "outbound";
export type CallSource = "phone" | "email" | "website" | "personal" | "other";
export type CallPriority = "low" | "normal" | "high";
export type InquiryStatus = "open" | "inProgress" | "converted" | "cancelled";

export interface CallResponse {
  id: string;
  kind: CallKind;
  direction: CallDirection;
  source: CallSource;
  priority: CallPriority;
  callerName: string | null;
  callerPhone: string | null;
  callerEmail: string | null;
  company: string | null;
  customerId: string | null;
  customerName: string | null;
  bauakteId: string | null;
  bauakteLabel: string | null;
  subject: string | null;
  notes: string | null;
  followUp: boolean;
  dueDate: string | null;
  nextStep: string | null;
  inquiryStatus: InquiryStatus;
  areaSquareMeters: number | null;
  screedType: string | null;
  desiredStart: string | null;
  siteStreet: string | null;
  sitePostalCode: string | null;
  siteCity: string | null;
  convertedBauakteId: string | null;
  convertedBauakteNumber: string | null;
  handledByUserId: string;
  handledByName: string | null;
  calledAt: string;
  updatedAt: string;
  version: number;
}

export interface ConvertInquiryResponse {
  inquiry: CallResponse;
  bauakteId: string;
  bauakteNumber: string | null;
}

export interface CompanyResponse {
  name: string;
  street: string | null;
  postalCode: string | null;
  city: string | null;
  phone: string | null;
  email: string | null;
  website: string | null;
  version: number;
}

export type MaterialCategory = "screed" | "binder" | "aggregate" | "insulation" | "membrane" | "edgeStrip" | "additive" | "other";
export type OrderStatus = "draft" | "ordered" | "confirmed" | "delivered" | "cancelled";

export interface SupplierResponse {
  id: string;
  name: string;
  customerNumber: string | null;
  contactName: string | null;
  phone: string | null;
  email: string | null;
  street: string | null;
  postalCode: string | null;
  city: string | null;
  orderNotes: string | null;
  isActive: boolean;
  version: number;
}

export interface MaterialResponse {
  id: string;
  name: string;
  articleNumber: string | null;
  category: MaterialCategory;
  unit: string;
  supplierId: string | null;
  supplierName: string | null;
  price: number | null;
  notes: string | null;
  isActive: boolean;
  version: number;
}

export interface OrderLineResponse {
  id: string;
  position: number;
  materialId: string | null;
  articleNumber: string | null;
  description: string;
  quantity: number;
  unit: string;
  unitPrice: number | null;
  total: number | null;
}

export interface OrderResponse {
  id: string;
  number: string;
  status: OrderStatus;
  supplierId: string;
  supplierName: string;
  supplierCustomerNumber: string | null;
  supplierEmail: string | null;
  supplierPhone: string | null;
  bauakteId: string | null;
  bauakteNumber: string | null;
  bauakteTitle: string | null;
  deliveryDate: string | null;
  deliveryTime: string | null;
  deliveryStreet: string | null;
  deliveryPostalCode: string | null;
  deliveryCity: string | null;
  siteContact: string | null;
  notes: string | null;
  lines: OrderLineResponse[];
  total: number | null;
  createdByUserId: string;
  createdByName: string | null;
  orderedAt: string | null;
  deliveredAt: string | null;
  updatedAt: string;
  version: number;
}

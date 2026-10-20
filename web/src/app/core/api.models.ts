// Typen der EstrichONE-API v1 (contracts/openapi/v1.json). Feldnamen 1:1 wie im Vertrag.

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

export type BauakteStatus = 'inquiry' | 'planned' | 'inProgress' | 'drying' | 'completed' | 'cancelled';

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
}

export interface FileResponse {
  id: string;
  bauakteId: string | null;
  kind: 'photo' | 'video' | 'document';
  state: 'awaitingUpload' | 'stored';
  variants: 'notApplicable' | 'pending' | 'ready' | 'failed';
  originalName: string;
  contentType: string;
  sizeBytes: number;
  caption: string | null;
  capturedAt: string | null;
  createdAt: string;
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

export type DeviceStatus = 'pending' | 'active' | 'locked' | 'retired';
export type DeviceKind = 'personal' | 'pool';

export interface DeviceResponse {
  id: string;
  name: string;
  platform: 'ios' | 'android' | 'windows' | 'other';
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

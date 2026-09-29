import { DatabaseSync } from 'node:sqlite';

/**
 * Xuất / Nhập (Export / Import) toàn bộ dữ liệu của License Server.
 * Dùng để Admin sao lưu định kỳ hoặc di trú (migrate) sang máy chủ mới
 * mà không cần copy thủ công file SQLite.
 */

export const BACKUP_FORMAT = 'vision2026-license-server-backup';
export const BACKUP_VERSION = 1;

export interface BackupMeta {
  format: string;
  version: number;
  exportedAt: string;
  source?: string;
  counts: Record<string, number>;
}

export interface BackupKeys {
  privateKey: string;
  publicKey: string;
}

export interface BackupPayload {
  meta?: Partial<BackupMeta>;
  licenses: any[];
  machines: any[];
  clientRegistrations: any[];
  auditLogs: any[];
  keys?: BackupKeys | null;
}

export interface ImportOptions {
  /** true = xóa sạch dữ liệu hiện tại rồi phục hồi (mặc định). false = chỉ bổ sung bản ghi mới. */
  replace?: boolean;
}

export interface ImportResult {
  replaced: boolean;
  imported: {
    licenses: number;
    machines: number;
    clientRegistrations: number;
    auditLogs: number;
  };
}

// Thứ tự cột cố định để đảm bảo export/import khớp nhau giữa các phiên bản.
const LICENSE_COLUMNS = [
  'id', 'license_key', 'customer_name', 'edition', 'license_type',
  'max_machines', 'allowed_features', 'max_cameras', 'issued_at',
  'expires_at', 'status', 'notes'
];

const MACHINE_COLUMNS = [
  'id', 'license_id', 'machine_fingerprint', 'machine_name', 'os_version',
  'app_version', 'local_ip', 'public_ip', 'activated_at', 'last_heartbeat',
  'is_revoked', 'revoked_reason', 'revoked_at', 'is_suspended'
];

const REGISTRATION_COLUMNS = [
  'id', 'machine_fingerprint', 'formatted_machine_code', 'machine_name',
  'os_version', 'app_version', 'local_ip', 'public_ip', 'status',
  'assigned_license_id', 'signed_package', 'registered_at', 'last_seen_at',
  'approved_at', 'notes'
];

// audit_logs.id là AUTOINCREMENT -> không ghi đè id khi phục hồi.
const AUDIT_COLUMNS = [
  'machine_fingerprint', 'license_key', 'action', 'ip_address', 'details', 'created_at'
];

type SqlValue = string | number | bigint | null;

function normalize(value: unknown): SqlValue {
  if (value === undefined || value === null) return null;
  if (typeof value === 'boolean') return value ? 1 : 0;
  if (typeof value === 'number' || typeof value === 'string' || typeof value === 'bigint') return value;
  return JSON.stringify(value);
}

function countRows(db: DatabaseSync, table: string): number {
  const row = db.prepare(`SELECT COUNT(*) AS c FROM ${table}`).get() as { c?: number | bigint };
  return Number(row?.c ?? 0);
}

/** Xuất toàn bộ dữ liệu (không kèm khóa RSA — phần này do controller ghép thêm). */
export function exportDatabase(db: DatabaseSync): BackupPayload {
  const licenses = db
    .prepare(`SELECT ${LICENSE_COLUMNS.join(', ')} FROM licenses ORDER BY issued_at ASC`)
    .all() as any[];
  const machines = db
    .prepare(`SELECT ${MACHINE_COLUMNS.join(', ')} FROM machines`)
    .all() as any[];
  const clientRegistrations = db
    .prepare(`SELECT ${REGISTRATION_COLUMNS.join(', ')} FROM client_registrations`)
    .all() as any[];
  const auditLogs = db
    .prepare(`SELECT ${AUDIT_COLUMNS.join(', ')} FROM audit_logs ORDER BY id ASC`)
    .all() as any[];

  return {
    meta: {
      format: BACKUP_FORMAT,
      version: BACKUP_VERSION,
      exportedAt: new Date().toISOString(),
      counts: {
        licenses: licenses.length,
        machines: machines.length,
        clientRegistrations: clientRegistrations.length,
        auditLogs: auditLogs.length
      }
    },
    licenses,
    machines,
    clientRegistrations,
    auditLogs,
    keys: null
  };
}

function insertRows(
  db: DatabaseSync,
  table: string,
  columns: string[],
  rows: any[],
  mode: 'replace' | 'merge'
): number {
  if (rows.length === 0) return 0;

  const verb = mode === 'replace' ? 'INSERT OR REPLACE' : 'INSERT OR IGNORE';
  const placeholders = columns.map(() => '?').join(', ');
  const stmt = db.prepare(
    `${verb} INTO ${table} (${columns.join(', ')}) VALUES (${placeholders})`
  );

  let count = 0;
  for (const row of rows) {
    if (!row || typeof row !== 'object') continue;
    const values = columns.map((col) => normalize(row[col]));
    stmt.run(...values);
    count++;
  }
  return count;
}

/**
 * Phục hồi dữ liệu từ payload export.
 * - mode 'replace': xóa toàn bộ dữ liệu hiện có rồi ghi lại (dùng khi chuyển server).
 * - mode 'merge': chỉ thêm các bản ghi chưa tồn tại (bỏ qua trùng khóa).
 */
export function importDatabase(
  db: DatabaseSync,
  payload: BackupPayload,
  options: ImportOptions = {}
): ImportResult {
  const replace = options.replace !== false;

  const licenses = Array.isArray(payload?.licenses) ? payload.licenses : [];
  const machines = Array.isArray(payload?.machines) ? payload.machines : [];
  const registrations = Array.isArray(payload?.clientRegistrations) ? payload.clientRegistrations : [];
  const auditLogs = Array.isArray(payload?.auditLogs) ? payload.auditLogs : [];

  const mode: 'replace' | 'merge' = replace ? 'replace' : 'merge';

  db.exec('BEGIN IMMEDIATE');
  try {
    if (replace) {
      // Xóa con trước, cha sau để tránh vi phạm khóa ngoại.
      db.exec('DELETE FROM audit_logs');
      db.exec('DELETE FROM machines');
      db.exec('DELETE FROM client_registrations');
      db.exec('DELETE FROM licenses');
    }

    const nLicenses = insertRows(db, 'licenses', LICENSE_COLUMNS, licenses, mode);
    const nRegistrations = insertRows(db, 'client_registrations', REGISTRATION_COLUMNS, registrations, mode);
    const nMachines = insertRows(db, 'machines', MACHINE_COLUMNS, machines, mode);
    // Nhật ký (audit) là dữ liệu lịch sử: chỉ phục hồi khi ở chế độ ghi đè toàn bộ,
    // tránh nhân bản trùng lặp khi nhập bổ sung nhiều lần.
    const nAudit = replace ? insertRows(db, 'audit_logs', AUDIT_COLUMNS, auditLogs, mode) : 0;

    db.exec('COMMIT');

    return {
      replaced: replace,
      imported: {
        licenses: nLicenses,
        machines: nMachines,
        clientRegistrations: nRegistrations,
        auditLogs: nAudit
      }
    };
  } catch (err) {
    try {
      db.exec('ROLLBACK');
    } catch {
      /* ignore rollback error */
    }
    throw err;
  }
}

/** Thống kê nhanh số bản ghi hiện có (phục vụ kiểm tra trước/sau khi phục hồi). */
export function getTableCounts(db: DatabaseSync) {
  return {
    licenses: countRows(db, 'licenses'),
    machines: countRows(db, 'machines'),
    clientRegistrations: countRows(db, 'client_registrations'),
    auditLogs: countRows(db, 'audit_logs')
  };
}

import { DatabaseSync } from 'node:sqlite';
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { LicenseCrypto } from '../crypto/licenseCrypto';

export interface LicenseRecord {
  id: string;
  license_key: string;
  customer_name: string;
  edition: string;
  license_type: string;
  max_machines: number;
  allowed_features: string; // JSON string
  max_cameras: number;
  issued_at: string;
  expires_at: string | null;
  status: string; // Active, Suspended, Revoked, Expired
  notes?: string;
}

export interface MachineRecord {
  id: string;
  license_id: string;
  machine_fingerprint: string;
  machine_name: string;
  os_version: string;
  app_version: string;
  local_ip: string;
  public_ip: string;
  activated_at: string;
  last_heartbeat: string;
  is_revoked: number; // 0 or 1
  revoked_reason: string | null;
  revoked_at: string | null;
  is_suspended: number; // 0 or 1
  // Joined fields from licenses
  customer_name?: string;
  license_key?: string;
  edition?: string;
}

export class DatabaseManager {
  private static instance: DatabaseManager | null = null;
  private db: DatabaseSync;

  private constructor(dbPath: string = './data/license.db') {
    const dir = path.dirname(dbPath);
    if (!fs.existsSync(dir)) {
      fs.mkdirSync(dir, { recursive: true });
    }

    this.db = new DatabaseSync(dbPath);
    this.initTables();
    this.seedDefaultDataIfEmpty();
  }

  public static getInstance(dbPath?: string): DatabaseManager {
    if (!this.instance) {
      this.instance = new DatabaseManager(dbPath);
    }
    return this.instance;
  }

  private initTables(): void {
    // 1. Bảng licenses
    this.db.exec(`
      CREATE TABLE IF NOT EXISTS licenses (
        id TEXT PRIMARY KEY,
        license_key TEXT UNIQUE NOT NULL,
        customer_name TEXT NOT NULL,
        edition TEXT NOT NULL DEFAULT 'Enterprise',
        license_type TEXT NOT NULL DEFAULT 'Perpetual',
        max_machines INTEGER NOT NULL DEFAULT 1,
        allowed_features TEXT NOT NULL,
        max_cameras INTEGER NOT NULL DEFAULT 4,
        issued_at TEXT NOT NULL,
        expires_at TEXT,
        status TEXT NOT NULL DEFAULT 'Active',
        notes TEXT
      );
    `);

    // 2. Bảng machines
    this.db.exec(`
      CREATE TABLE IF NOT EXISTS machines (
        id TEXT PRIMARY KEY,
        license_id TEXT NOT NULL,
        machine_fingerprint TEXT NOT NULL,
        machine_name TEXT NOT NULL,
        os_version TEXT,
        app_version TEXT,
        local_ip TEXT,
        public_ip TEXT,
        activated_at TEXT NOT NULL,
        last_heartbeat TEXT NOT NULL,
        is_revoked INTEGER NOT NULL DEFAULT 0,
        revoked_reason TEXT,
        revoked_at TEXT,
        is_suspended INTEGER NOT NULL DEFAULT 0,
        FOREIGN KEY(license_id) REFERENCES licenses(id) ON DELETE CASCADE,
        UNIQUE(license_id, machine_fingerprint)
      );
    `);

    // 3. Bảng audit_logs
    this.db.exec(`
      CREATE TABLE IF NOT EXISTS audit_logs (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        machine_fingerprint TEXT,
        license_key TEXT,
        action TEXT NOT NULL,
        ip_address TEXT,
        details TEXT,
        created_at TEXT NOT NULL
      );
    `);
  }

  private seedDefaultDataIfEmpty(): void {
    const count = this.db.prepare('SELECT COUNT(*) as count FROM licenses').get() as { count: number };
    if (count && count.count === 0) {
      console.log('Seeding initial demo Enterprise license...');
      const demoKey = 'V26-ENT-DEMO-2026-8888';
      const features = [
        'InspectionEngine',
        'HighSpeedCamera',
        'PlcBridge',
        'OqcScanner',
        'AI_OCR_Industrial',
        'LightingController',
        'DatabaseIntegration',
        'MultiCameraSupport',
        'SurfaceCompare',
        'ContourCompare'
      ];

      this.createLicense({
        license_key: demoKey,
        customer_name: 'Industrial Demo Customer',
        edition: 'Enterprise',
        license_type: 'Perpetual',
        max_machines: 10,
        allowed_features: JSON.stringify(features),
        max_cameras: 8,
        issued_at: new Date().toISOString(),
        expires_at: null, // Vĩnh viễn
        status: 'Active',
        notes: 'Demo key for system initial trial'
      });
      console.log(`Demo license created: ${demoKey} (Allows 10 machines, Perpetual)`);
    }
  }

  // --- LICENSES OPERATIONS ---

  public createLicense(license: Omit<LicenseRecord, 'id'>): LicenseRecord {
    const id = crypto.randomUUID();
    const stmt = this.db.prepare(`
      INSERT INTO licenses (
        id, license_key, customer_name, edition, license_type,
        max_machines, allowed_features, max_cameras, issued_at,
        expires_at, status, notes
      ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
    `);

    stmt.run(
      id,
      license.license_key,
      license.customer_name,
      license.edition,
      license.license_type,
      license.max_machines,
      license.allowed_features,
      license.max_cameras,
      license.issued_at,
      license.expires_at,
      license.status,
      license.notes || null
    );

    return { id, ...license };
  }

  public getLicenseByKey(licenseKey: string): LicenseRecord | undefined {
    return this.db.prepare('SELECT * FROM licenses WHERE license_key = ?').get(licenseKey) as LicenseRecord | undefined;
  }

  public getLicenseById(id: string): LicenseRecord | undefined {
    return this.db.prepare('SELECT * FROM licenses WHERE id = ?').get(id) as LicenseRecord | undefined;
  }

  public listLicenses(): LicenseRecord[] {
    return this.db.prepare('SELECT * FROM licenses ORDER BY issued_at DESC').all() as unknown as LicenseRecord[];
  }

  public updateLicenseStatus(licenseId: string, status: string): void {
    this.db.prepare('UPDATE licenses SET status = ? WHERE id = ?').run(status, licenseId);
  }

  // --- MACHINES OPERATIONS ---

  public getMachine(licenseId: string, machineFingerprint: string): MachineRecord | undefined {
    return this.db.prepare(`
      SELECT * FROM machines 
      WHERE license_id = ? AND machine_fingerprint = ?
    `).get(licenseId, machineFingerprint) as MachineRecord | undefined;
  }

  public getMachineByFingerprint(machineFingerprint: string): MachineRecord | undefined {
    return this.db.prepare(`
      SELECT m.*, l.customer_name, l.license_key, l.edition
      FROM machines m
      JOIN licenses l ON m.license_id = l.id
      WHERE m.machine_fingerprint = ?
      LIMIT 1
    `).get(machineFingerprint) as MachineRecord | undefined;
  }

  public countActiveMachinesForLicense(licenseId: string): number {
    const row = this.db.prepare(`
      SELECT COUNT(*) as count FROM machines 
      WHERE license_id = ? AND is_revoked = 0
    `).get(licenseId) as { count: number };
    return row ? Number(row.count) : 0;
  }

  public registerOrUpdateMachine(data: {
    license_id: string;
    machine_fingerprint: string;
    machine_name: string;
    os_version: string;
    app_version: string;
    local_ip: string;
    public_ip: string;
  }): MachineRecord {
    const existing = this.getMachine(data.license_id, data.machine_fingerprint);
    const now = new Date().toISOString();

    if (existing) {
      this.db.prepare(`
        UPDATE machines SET
          machine_name = ?,
          os_version = ?,
          app_version = ?,
          local_ip = ?,
          public_ip = ?,
          last_heartbeat = ?
        WHERE id = ?
      `).run(
        data.machine_name,
        data.os_version,
        data.app_version,
        data.local_ip,
        data.public_ip,
        now,
        existing.id
      );

      return {
        ...existing,
        machine_name: data.machine_name,
        os_version: data.os_version,
        app_version: data.app_version,
        local_ip: data.local_ip,
        public_ip: data.public_ip,
        last_heartbeat: now
      };
    } else {
      const id = crypto.randomUUID();
      this.db.prepare(`
        INSERT INTO machines (
          id, license_id, machine_fingerprint, machine_name,
          os_version, app_version, local_ip, public_ip,
          activated_at, last_heartbeat, is_revoked, is_suspended
        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 0, 0)
      `).run(
        id,
        data.license_id,
        data.machine_fingerprint,
        data.machine_name,
        data.os_version,
        data.app_version,
        data.local_ip,
        data.public_ip,
        now,
        now
      );

      return {
        id,
        ...data,
        activated_at: now,
        last_heartbeat: now,
        is_revoked: 0,
        revoked_reason: null,
        revoked_at: null,
        is_suspended: 0
      };
    }
  }

  public updateHeartbeat(machineFingerprint: string, publicIp: string, appVersion?: string): MachineRecord | undefined {
    const now = new Date().toISOString();
    const machine = this.getMachineByFingerprint(machineFingerprint);
    if (!machine) return undefined;

    if (appVersion) {
      this.db.prepare(`
        UPDATE machines SET last_heartbeat = ?, public_ip = ?, app_version = ?
        WHERE id = ?
      `).run(now, publicIp, appVersion, machine.id);
    } else {
      this.db.prepare(`
        UPDATE machines SET last_heartbeat = ?, public_ip = ?
        WHERE id = ?
      `).run(now, publicIp, machine.id);
    }

    return { ...machine, last_heartbeat: now, public_ip: publicIp };
  }

  public setMachineRevoked(machineFingerprint: string, revoked: boolean, reason?: string): boolean {
    const now = new Date().toISOString();
    const result = this.db.prepare(`
      UPDATE machines SET 
        is_revoked = ?,
        revoked_reason = ?,
        revoked_at = ?
      WHERE machine_fingerprint = ?
    `).run(revoked ? 1 : 0, reason || null, revoked ? now : null, machineFingerprint);

    return Number(result.changes) > 0;
  }

  public setMachineSuspended(machineFingerprint: string, suspended: boolean): boolean {
    const result = this.db.prepare(`
      UPDATE machines SET is_suspended = ?
      WHERE machine_fingerprint = ?
    `).run(suspended ? 1 : 0, machineFingerprint);

    return Number(result.changes) > 0;
  }

  public deleteMachine(machineFingerprint: string): boolean {
    const result = this.db.prepare('DELETE FROM machines WHERE machine_fingerprint = ?').run(machineFingerprint);
    return Number(result.changes) > 0;
  }

  public listAllMachines(): MachineRecord[] {
    return this.db.prepare(`
      SELECT m.*, l.customer_name, l.license_key, l.edition, l.status as license_status
      FROM machines m
      JOIN licenses l ON m.license_id = l.id
      ORDER BY m.last_heartbeat DESC
    `).all() as unknown as MachineRecord[];
  }

  // --- AUDIT LOGS & STATS ---

  public logAudit(entry: {
    machine_fingerprint?: string;
    license_key?: string;
    action: string;
    ip_address?: string;
    details?: any;
  }): void {
    const now = new Date().toISOString();
    const detailsJson = entry.details ? JSON.stringify(entry.details) : null;

    this.db.prepare(`
      INSERT INTO audit_logs (machine_fingerprint, license_key, action, ip_address, details, created_at)
      VALUES (?, ?, ?, ?, ?, ?)
    `).run(
      entry.machine_fingerprint || null,
      entry.license_key || null,
      entry.action,
      entry.ip_address || null,
      detailsJson,
      now
    );
  }

  public getDashboardStats() {
    const totalLicenses = Number((this.db.prepare('SELECT COUNT(*) as c FROM licenses').get() as any)?.c || 0);
    const totalMachines = Number((this.db.prepare('SELECT COUNT(*) as c FROM machines').get() as any)?.c || 0);
    const revokedMachines = Number((this.db.prepare('SELECT COUNT(*) as c FROM machines WHERE is_revoked = 1').get() as any)?.c || 0);

    // Máy online nếu có heartbeat trong vòng 12 giờ qua
    const twelveHoursAgo = new Date(Date.now() - 12 * 3600 * 1000).toISOString();
    const activeOnlineMachines = Number((this.db.prepare(`
      SELECT COUNT(*) as c FROM machines 
      WHERE is_revoked = 0 AND is_suspended = 0 AND last_heartbeat >= ?
    `).get(twelveHoursAgo) as any)?.c || 0);

    return {
      totalLicenses,
      totalMachines,
      activeOnlineMachines,
      revokedMachines
    };
  }
}

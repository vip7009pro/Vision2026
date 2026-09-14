import { DatabaseSync } from 'node:sqlite';
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { LicenseCrypto, LicensePayload } from '../crypto/licenseCrypto';

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

export interface ClientRegistrationRecord {
  id: string;
  machine_fingerprint: string;
  formatted_machine_code: string;
  machine_name: string;
  os_version: string;
  app_version: string;
  local_ip: string;
  public_ip: string;
  status: 'Pending' | 'Approved' | 'Rejected';
  assigned_license_id: string | null;
  signed_package: string | null;
  registered_at: string;
  last_seen_at: string;
  approved_at: string | null;
  notes?: string;
  // Joined fields if approved
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

    // 4. Bảng client_registrations (Tự động đăng ký & Chờ duyệt)
    this.db.exec(`
      CREATE TABLE IF NOT EXISTS client_registrations (
        id TEXT PRIMARY KEY,
        machine_fingerprint TEXT UNIQUE NOT NULL,
        formatted_machine_code TEXT NOT NULL,
        machine_name TEXT NOT NULL,
        os_version TEXT,
        app_version TEXT,
        local_ip TEXT,
        public_ip TEXT,
        status TEXT NOT NULL DEFAULT 'Pending',
        assigned_license_id TEXT,
        signed_package TEXT,
        registered_at TEXT NOT NULL,
        last_seen_at TEXT NOT NULL,
        approved_at TEXT,
        notes TEXT,
        FOREIGN KEY(assigned_license_id) REFERENCES licenses(id) ON DELETE SET NULL
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

    // Đồng bộ sang bảng client_registrations để chặn ngay lập tức API auto-register / check-registration
    if (revoked) {
      this.db.prepare(`
        UPDATE client_registrations SET
          status = 'Rejected',
          notes = ?,
          signed_package = null
        WHERE machine_fingerprint = ?
      `).run(reason || 'Thu hồi bởi Quản trị viên', machineFingerprint);
    } else {
      // Khôi phục máy trạm: kiểm tra xem máy có license hợp lệ không, nếu có thì tái ký số
      const machine = this.getMachineByFingerprint(machineFingerprint);
      if (machine) {
        const license = this.getLicenseById(machine.license_id);
        if (license && license.status === 'Active') {
          const payload: LicensePayload = {
            licenseId: license.id,
            licenseKey: license.license_key,
            customerName: license.customer_name,
            edition: license.edition as 'Basic' | 'Pro' | 'Enterprise',
            machineFingerprint,
            machineName: machine.machine_name || 'Industrial-PC',
            licenseType: (license.license_type as any) || 'Perpetual',
            issuedDateUtc: license.issued_at,
            expirationDateUtc: license.expires_at || null,
            allowedFeatures: license.allowed_features ? JSON.parse(license.allowed_features) : [],
            maxCameraCount: license.max_cameras,
            heartbeatIntervalHours: 1,
            gracePeriodDays: 1
          };
          const signedPackage = LicenseCrypto.signPayload(payload);
          this.db.prepare(`
            UPDATE client_registrations SET
              status = 'Approved',
              notes = 'Đã khôi phục bản quyền',
              signed_package = ?
            WHERE machine_fingerprint = ?
          `).run(JSON.stringify(signedPackage), machineFingerprint);
        }
      }
    }

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
    // Xóa sạch đăng ký để máy tính có thể tự do đăng ký lại như một máy mới
    this.db.prepare('DELETE FROM client_registrations WHERE machine_fingerprint = ?').run(machineFingerprint);
    return Number(result.changes) > 0;
  }

  public deleteLicense(licenseId: string): boolean {
    const license = this.getLicenseById(licenseId);
    if (!license) return false;

    const now = new Date().toISOString();
    // 1. Thu hồi các máy trạm đang gắn với license này
    this.db.prepare(`
      UPDATE machines SET
        is_revoked = 1,
        revoked_reason = 'License đã bị Quản trị viên xóa hoàn toàn',
        revoked_at = ?
      WHERE license_id = ?
    `).run(now, licenseId);

    // 2. Xóa các đăng ký máy trạm liên quan để các máy đó có thể tự do gửi yêu cầu cấp license mới
    this.db.prepare('DELETE FROM client_registrations WHERE assigned_license_id = ?').run(licenseId);

    // 3. Xóa license khỏi bảng licenses
    const res = this.db.prepare('DELETE FROM licenses WHERE id = ?').run(licenseId);

    // 4. Ghi audit log
    this.logAudit({
      license_key: license.license_key,
      action: 'ADMIN_DELETE_LICENSE',
      details: { customerName: license.customer_name, edition: license.edition }
    });

    return Number(res.changes) > 0;
  }

  public changeMachinePlan(
    machineFingerprint: string,
    options: {
      edition: 'Basic' | 'Pro' | 'Enterprise';
      licenseType: string;
      durationDays?: number;
    }
  ): { success: boolean; message: string; package?: any } {
    const machine = this.getMachineByFingerprint(machineFingerprint);
    if (!machine) {
      return { success: false, message: 'Không tìm thấy máy trạm tương ứng.' };
    }

    const currentLicense = this.getLicenseById(machine.license_id);
    if (!currentLicense) {
      return { success: false, message: 'Không tìm thấy license hiện tại của máy trạm.' };
    }

    const now = new Date();
    let newExpiresAt: string | null = null;
    const duration = options.durationDays !== undefined ? options.durationDays : 0;
    if (duration > 0 && options.licenseType !== 'Perpetual') {
      newExpiresAt = new Date(now.getTime() + duration * 86400 * 1000).toISOString();
    }

    const edition = options.edition || currentLicense.edition;
    const licenseType = options.licenseType || (duration > 0 ? 'Trial' : 'Perpetual');

    // Xác định features theo gói
    let features: string[] = [];
    if (edition === 'Enterprise') {
      features = [
        'InspectionEngine', 'HighSpeedCamera', 'PlcBridge', 'OqcScanner',
        'AI_OCR_Industrial', 'LightingController', 'DatabaseIntegration',
        'MultiCameraSupport', 'SurfaceCompare', 'ContourCompare'
      ];
    } else if (edition === 'Pro') {
      features = [
        'InspectionEngine', 'HighSpeedCamera', 'PlcBridge', 'OqcScanner',
        'LightingController', 'DatabaseIntegration'
      ];
    } else {
      features = ['InspectionEngine', 'PlcBridge'];
    }

    // Cập nhật license
    this.db.prepare(`
      UPDATE licenses SET
        edition = ?,
        license_type = ?,
        expires_at = ?,
        allowed_features = ?
      WHERE id = ?
    `).run(edition, licenseType, newExpiresAt, JSON.stringify(features), currentLicense.id);

    // Mở khóa máy nếu trước đó bị thu hồi hoặc tạm khóa
    this.db.prepare(`
      UPDATE machines SET
        is_revoked = 0,
        revoked_reason = null,
        revoked_at = null,
        is_suspended = 0
      WHERE machine_fingerprint = ?
    `).run(machineFingerprint);

    // Ký số RSA-2048 gói mới
    const payload: LicensePayload = {
      licenseId: currentLicense.id,
      licenseKey: currentLicense.license_key,
      customerName: currentLicense.customer_name,
      edition: edition as 'Basic' | 'Pro' | 'Enterprise',
      machineFingerprint,
      machineName: machine.machine_name || 'Industrial-PC',
      licenseType: licenseType as 'Perpetual' | 'Subscription' | 'Trial',
      issuedDateUtc: currentLicense.issued_at,
      expirationDateUtc: newExpiresAt || null,
      allowedFeatures: features,
      maxCameraCount: currentLicense.max_cameras,
      heartbeatIntervalHours: 1,
      gracePeriodDays: 1
    };

    const signedPackage = LicenseCrypto.signPayload(payload);

    // Đồng bộ sang client_registrations
    this.db.prepare(`
      UPDATE client_registrations SET
        status = 'Approved',
        signed_package = ?,
        notes = ?
      WHERE machine_fingerprint = ?
    `).run(
      JSON.stringify(signedPackage),
      `Đã chuyển sang gói ${edition} (${licenseType})`,
      machineFingerprint
    );

    this.logAudit({
      machine_fingerprint: machineFingerprint,
      license_key: currentLicense.license_key,
      action: 'ADMIN_CHANGE_MACHINE_PLAN',
      details: { edition, licenseType, expiresAt: newExpiresAt }
    });

    return {
      success: true,
      message: `Đã đổi gói thành công sang ${edition} (${licenseType})!`,
      package: signedPackage
    };
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
    const pendingRegistrations = Number((this.db.prepare("SELECT COUNT(*) as c FROM client_registrations WHERE status = 'Pending'").get() as any)?.c || 0);

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
      revokedMachines,
      pendingRegistrations
    };
  }

  // --- CLIENT REGISTRATION & 1-CLICK APPROVAL ---

  public upsertClientRegistration(data: {
    machine_fingerprint: string;
    formatted_machine_code: string;
    machine_name: string;
    os_version?: string;
    app_version?: string;
    local_ip?: string;
    public_ip?: string;
  }): { registration: ClientRegistrationRecord; isNew: boolean } {
    const existing = this.db.prepare(`
      SELECT * FROM client_registrations WHERE machine_fingerprint = ?
    `).get(data.machine_fingerprint) as unknown as ClientRegistrationRecord | undefined;

    const now = new Date().toISOString();

    if (existing) {
      let targetStatus: 'Pending' | 'Approved' | 'Rejected' = existing.status;
      let targetPackage: string | null = existing.signed_package ?? null;
      let targetLicenseId: string | null = existing.assigned_license_id ?? null;
      let targetNotes: string | null = existing.notes ?? null;

      // 1. Nếu trước đó là Approved nhưng license liên kết đã bị xóa hoặc không còn Active -> chuyển về Pending để Admin cấp lại
      if (existing.status === 'Approved') {
        if (existing.assigned_license_id) {
          const lic = this.getLicenseById(existing.assigned_license_id);
          if (!lic || lic.status !== 'Active') {
            targetStatus = 'Pending';
            targetPackage = null;
            targetLicenseId = null;
            targetNotes = null;
          }
        } else {
          targetStatus = 'Pending';
          targetPackage = null;
          targetNotes = null;
        }
      }

      // 2. Nếu trước đó là Rejected (do xóa máy, xóa license hoặc reject tạm thời)
      // Kiểm tra xem máy có đang bị thu hồi cố ý bởi Admin trong bảng machines không:
      const machine = this.getMachineByFingerprint(data.machine_fingerprint);
      if (existing.status === 'Rejected') {
        if (!machine || machine.is_revoked !== 1) {
          // Máy KHÔNG bị Admin cố tình revoke -> máy mở app lên để xin cấp phép lại -> tự động chuyển về Pending!
          targetStatus = 'Pending';
          targetPackage = null;
          targetLicenseId = null;
          targetNotes = null;
        }
      }

      this.db.prepare(`
        UPDATE client_registrations
        SET machine_name = ?, os_version = ?, app_version = ?, local_ip = ?, public_ip = ?,
            status = ?, signed_package = ?, assigned_license_id = ?, notes = ?, last_seen_at = ?
        WHERE id = ?
      `).run(
        data.machine_name || existing.machine_name,
        data.os_version || existing.os_version || 'Windows',
        data.app_version || existing.app_version || '2.1.0',
        data.local_ip || existing.local_ip || '127.0.0.1',
        data.public_ip || existing.public_ip || '',
        targetStatus,
        targetPackage,
        targetLicenseId,
        targetNotes,
        now,
        existing.id
      );

      const updated = this.db.prepare(`
        SELECT r.*, l.customer_name, l.license_key, l.edition
        FROM client_registrations r
        LEFT JOIN licenses l ON r.assigned_license_id = l.id
        WHERE r.id = ?
      `).get(existing.id) as unknown as ClientRegistrationRecord;

      return { registration: updated, isNew: false };
    } else {
      const id = crypto.randomUUID();
      this.db.prepare(`
        INSERT INTO client_registrations (
          id, machine_fingerprint, formatted_machine_code, machine_name,
          os_version, app_version, local_ip, public_ip, status,
          registered_at, last_seen_at
        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, 'Pending', ?, ?)
      `).run(
        id,
        data.machine_fingerprint,
        data.formatted_machine_code,
        data.machine_name,
        data.os_version || 'Windows',
        data.app_version || '2.1.0',
        data.local_ip || '127.0.0.1',
        data.public_ip || '',
        now,
        now
      );

      const created = this.db.prepare(`
        SELECT * FROM client_registrations WHERE id = ?
      `).get(id) as unknown as ClientRegistrationRecord;

      this.logAudit({
        machine_fingerprint: data.machine_fingerprint,
        action: 'CLIENT_AUTO_REGISTERED',
        ip_address: data.public_ip || data.local_ip,
        details: { machine_name: data.machine_name, formatted_code: data.formatted_machine_code }
      });

      return { registration: created, isNew: true };
    }
  }

  public getRegistrationByFingerprint(fingerprint: string): ClientRegistrationRecord | null {
    const row = this.db.prepare(`
      SELECT r.*, l.customer_name, l.license_key, l.edition
      FROM client_registrations r
      LEFT JOIN licenses l ON r.assigned_license_id = l.id
      WHERE r.machine_fingerprint = ?
    `).get(fingerprint) as unknown as ClientRegistrationRecord | undefined;

    return row || null;
  }

  public listPendingRegistrations(): ClientRegistrationRecord[] {
    return this.db.prepare(`
      SELECT * FROM client_registrations
      WHERE status = 'Pending'
      ORDER BY last_seen_at DESC
    `).all() as unknown as ClientRegistrationRecord[];
  }

  public approveClientRegistration(
    registrationId: string,
    options: {
      license_id?: string;
      customer_name?: string;
      edition?: string;
      license_type?: string;
      expiration_days?: number;
      max_cameras?: number;
      allowed_features?: string[];
      notes?: string;
    }
  ): { success: boolean; package?: any; message?: string } {
    const reg = this.db.prepare(`
      SELECT * FROM client_registrations WHERE id = ?
    `).get(registrationId) as unknown as ClientRegistrationRecord | undefined;

    if (!reg) {
      return { success: false, message: 'Không tìm thấy thông tin đăng ký của máy trạm.' };
    }

    const now = new Date();
    const nowIso = now.toISOString();

    let license: LicenseRecord | undefined;

    // 1. Nếu Admin chọn gán vào một License Key có sẵn trong hệ thống
    if (options.license_id && options.license_id !== 'NEW') {
      license = this.getLicenseById(options.license_id);
      if (!license || license.status !== 'Active') {
        return { success: false, message: 'Khóa bản quyền được chọn không tồn tại hoặc đã bị khóa/hết hạn.' };
      }

      // Kiểm tra số lượng máy trạm đã kích hoạt trên license này
      const activeCount = this.countActiveMachinesForLicense(license.id);
      const isAlreadyOnThisLic = this.getMachine(license.id, reg.machine_fingerprint);
      if (!isAlreadyOnThisLic && activeCount >= license.max_machines) {
        return {
          success: false,
          message: `Khóa bản quyền [${license.license_key}] đã đạt tối đa số máy cho phép (${license.max_machines} máy). Vui lòng chọn khóa khác hoặc tăng số lượng máy.`
        };
      }
    } else {
      // Tự động tạo license mới
      const customerName = options.customer_name?.trim() || `Client - ${reg.machine_name}`;
      const edition = options.edition || 'Enterprise';
      const licenseType = options.expiration_days && options.expiration_days > 0 ? 'Subscription' : 'Perpetual';
      const expiresAt = options.expiration_days && options.expiration_days > 0
        ? new Date(now.getTime() + options.expiration_days * 86400000).toISOString()
        : null;

      const licenseKey = LicenseCrypto.generateLicenseKey('ENT');
      const defaultFeatures = options.allowed_features && options.allowed_features.length > 0
        ? options.allowed_features
        : (edition === 'Enterprise' ? [
          'InspectionEngine', 'HighSpeedCamera', 'PlcBridge', 'OqcScanner', 'AI_OCR_Industrial',
          'LightingController', 'DatabaseIntegration', 'MultiCameraSupport', 'SurfaceCompare', 'ContourCompare'
        ] : edition === 'Pro' ? [
          'InspectionEngine', 'HighSpeedCamera', 'PlcBridge', 'OqcScanner', 'LightingController', 'DatabaseIntegration'
        ] : ['InspectionEngine', 'PlcBridge']);

      license = this.createLicense({
        license_key: licenseKey,
        customer_name: customerName,
        edition: edition,
        license_type: licenseType,
        max_machines: 1,
        allowed_features: JSON.stringify(defaultFeatures),
        max_cameras: options.max_cameras || 4,
        issued_at: nowIso,
        expires_at: expiresAt,
        status: 'Active',
        notes: options.notes || `Tự động phê duyệt từ máy trạm ${reg.machine_name}`
      });
    }

    // 2. Ký số RSA-2048 cho máy trạm
    const features: string[] = JSON.parse(license.allowed_features || '[]');
    const payload: LicensePayload = {
      licenseId: license.id,
      licenseKey: license.license_key,
      customerName: license.customer_name,
      edition: (license.edition as 'Basic' | 'Pro' | 'Enterprise') || 'Enterprise',
      machineFingerprint: reg.machine_fingerprint,
      machineName: reg.machine_name,
      licenseType: (license.license_type as any) || 'Perpetual',
      issuedDateUtc: license.issued_at,
      expirationDateUtc: license.expires_at,
      allowedFeatures: features,
      maxCameraCount: license.max_cameras,
      heartbeatIntervalHours: 1,
      gracePeriodDays: 1
    };

    const signedPackage = LicenseCrypto.signPayload(payload);
    const packageJson = JSON.stringify(signedPackage);

    // 3. Cập nhật client_registrations
    this.db.prepare(`
      UPDATE client_registrations
      SET status = 'Approved', assigned_license_id = ?, signed_package = ?, approved_at = ?
      WHERE id = ?
    `).run(license.id, packageJson, nowIso, reg.id);

    // 4. Đồng bộ vào bảng machines
    this.registerOrUpdateMachine({
      license_id: license.id,
      machine_fingerprint: reg.machine_fingerprint,
      machine_name: reg.machine_name,
      os_version: reg.os_version,
      app_version: reg.app_version,
      local_ip: reg.local_ip,
      public_ip: reg.public_ip
    });

    this.logAudit({
      machine_fingerprint: reg.machine_fingerprint,
      license_key: license.license_key,
      action: 'REGISTRATION_APPROVED',
      details: {
        customerName: license.customer_name,
        edition: license.edition,
        licenseType: license.license_type,
        expiresAt: license.expires_at
      }
    });

    return { success: true, package: signedPackage, message: `Phê duyệt và ký số bản quyền máy trạm thành công với khóa [${license.license_key}]!` };
  }

  public rejectClientRegistration(registrationId: string, reason?: string): boolean {
    const reg = this.db.prepare(`
      SELECT * FROM client_registrations WHERE id = ?
    `).get(registrationId) as unknown as ClientRegistrationRecord | undefined;

    if (!reg) return false;

    this.db.prepare(`
      UPDATE client_registrations
      SET status = 'Rejected', notes = ?
      WHERE id = ?
    `).run(reason || 'Bị từ chối bởi Quản trị viên', registrationId);

    this.logAudit({
      machine_fingerprint: reg.machine_fingerprint,
      action: 'REGISTRATION_REJECTED',
      details: { reason }
    });

    return true;
  }

  public resetClientRegistration(machineFingerprint: string): boolean {
    const res = this.db.prepare('DELETE FROM client_registrations WHERE machine_fingerprint = ?').run(machineFingerprint);
    return Number(res.changes) > 0;
  }
}

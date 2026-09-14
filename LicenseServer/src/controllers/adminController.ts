import { Request, Response } from 'express';
import jwt from 'jsonwebtoken';
import { DatabaseManager } from '../database';
import { LicenseCrypto } from '../crypto/licenseCrypto';
import { config } from '../config';

export class AdminController {
  /**
   * POST /api/v1/admin/login
   */
  public static async login(req: Request, res: Response): Promise<void> {
    const { username, password } = req.body;

    if (username === config.admin.username && password === config.admin.password) {
      const token = jwt.sign(
        { username, role: 'admin' },
        config.admin.jwtSecret,
        { expiresIn: '7d' }
      );

      res.json({
        success: true,
        message: 'Đăng nhập thành công!',
        token,
        username
      });
    } else {
      res.status(401).json({
        success: false,
        message: 'Tên đăng nhập hoặc mật khẩu quản trị không đúng.'
      });
    }
  }

  /**
   * GET /api/v1/admin/dashboard
   */
  public static async getDashboard(req: Request, res: Response): Promise<void> {
    const db = DatabaseManager.getInstance(config.dbPath);
    const stats = db.getDashboardStats();
    res.json({ success: true, stats });
  }

  /**
   * GET /api/v1/admin/clients
   */
  public static async listClients(req: Request, res: Response): Promise<void> {
    const db = DatabaseManager.getInstance(config.dbPath);
    const machines = db.listAllMachines();
    res.json({ success: true, clients: machines });
  }

  /**
   * POST /api/v1/admin/machine/revoke
   * Thu hồi bản quyền từ xa
   */
  public static async revokeMachine(req: Request, res: Response): Promise<void> {
    const db = DatabaseManager.getInstance(config.dbPath);
    const { machineFingerprint, reason } = req.body;

    if (!machineFingerprint) {
      res.status(400).json({ success: false, message: 'Thiếu machineFingerprint.' });
      return;
    }

    const ok = db.setMachineRevoked(machineFingerprint, true, reason || 'Thu hồi bởi Quản trị viên');
    if (ok) {
      db.logAudit({
        machine_fingerprint: machineFingerprint,
        action: 'ADMIN_REVOKE_MACHINE',
        details: { reason }
      });
      res.json({ success: true, message: 'Đã thu hồi bản quyền máy trạm thành công!' });
    } else {
      res.status(404).json({ success: false, message: 'Không tìm thấy máy trạm tương ứng.' });
    }
  }

  /**
   * POST /api/v1/admin/machine/suspend
   * Tạm khóa máy trạm
   */
  public static async suspendMachine(req: Request, res: Response): Promise<void> {
    const db = DatabaseManager.getInstance(config.dbPath);
    const { machineFingerprint, suspend } = req.body;

    const ok = db.setMachineSuspended(machineFingerprint, suspend !== false);
    if (ok) {
      res.json({ success: true, message: `Đã ${suspend !== false ? 'tạm khóa' : 'mở khóa'} máy trạm thành công!` });
    } else {
      res.status(404).json({ success: false, message: 'Không tìm thấy máy trạm.' });
    }
  }

  /**
   * POST /api/v1/admin/machine/activate
   * Bỏ thu hồi / Kích hoạt lại máy trạm
   */
  public static async activateMachine(req: Request, res: Response): Promise<void> {
    const db = DatabaseManager.getInstance(config.dbPath);
    const { machineFingerprint } = req.body;

    const ok = db.setMachineRevoked(machineFingerprint, false);
    if (ok) {
      db.setMachineSuspended(machineFingerprint, false);
      res.json({ success: true, message: 'Đã khôi phục trạng thái hoạt động cho máy trạm!' });
    } else {
      res.status(404).json({ success: false, message: 'Không tìm thấy máy trạm.' });
    }
  }

  /**
   * POST /api/v1/admin/machine/transfer
   * Hủy liên kết máy cũ để cho phép chuyển sang máy mới
   */
  public static async transferMachine(req: Request, res: Response): Promise<void> {
    const db = DatabaseManager.getInstance(config.dbPath);
    const { machineFingerprint } = req.body;

    const ok = db.deleteMachine(machineFingerprint);
    if (ok) {
      db.logAudit({
        machine_fingerprint: machineFingerprint,
        action: 'ADMIN_TRANSFER_DELETED_OLD_MACHINE'
      });
      res.json({ success: true, message: 'Đã hủy liên kết máy cũ. Khách hàng có thể kích hoạt máy tính mới ngay.' });
    } else {
      res.status(404).json({ success: false, message: 'Không tìm thấy máy trạm.' });
    }
  }

  /**
   * GET /api/v1/admin/licenses
   */
  public static async listLicenses(req: Request, res: Response): Promise<void> {
    const db = DatabaseManager.getInstance(config.dbPath);
    const licenses = db.listLicenses();
    res.json({ success: true, licenses });
  }

  /**
   * POST /api/v1/admin/license/create
   * Tạo License Key mới
   */
  public static async createLicense(req: Request, res: Response): Promise<void> {
    const db = DatabaseManager.getInstance(config.dbPath);
    const {
      customerName,
      edition,
      licenseType,
      maxMachines,
      expiresAt,
      allowedFeatures,
      maxCameras,
      customKey,
      notes
    } = req.body;

    if (!customerName) {
      res.status(400).json({ success: false, message: 'Tên khách hàng không được để trống.' });
      return;
    }

    const finalEdition = edition || 'Enterprise';
    const key = (customKey && customKey.trim().length > 5)
      ? customKey.trim().toUpperCase()
      : LicenseCrypto.generateLicenseKey(finalEdition.substring(0, 3));

    const defaultFeatures = [
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

    const license = db.createLicense({
      license_key: key,
      customer_name: customerName.trim(),
      edition: finalEdition,
      license_type: licenseType || 'Perpetual',
      max_machines: parseInt(maxMachines || '1', 10),
      allowed_features: JSON.stringify(allowedFeatures || defaultFeatures),
      max_cameras: parseInt(maxCameras || '4', 10),
      issued_at: new Date().toISOString(),
      expires_at: expiresAt || null,
      status: 'Active',
      notes: notes || null
    });

    db.logAudit({
      license_key: key,
      action: 'ADMIN_CREATE_LICENSE',
      details: { customerName, edition: finalEdition }
    });

    res.json({
      success: true,
      message: 'Tạo mã License Key mới thành công!',
      license
    });
  }

  /**
   * GET /api/v1/admin/pending-registrations
   */
  public static async listPendingRegistrations(req: Request, res: Response): Promise<void> {
    const db = DatabaseManager.getInstance(config.dbPath);
    const rawList = db.listPendingRegistrations();
    const pending = rawList.map(r => ({
      ...r,
      fingerprint: r.machine_fingerprint,
      formattedCode: r.formatted_machine_code,
      machineName: r.machine_name,
      osVersion: r.os_version,
      appVersion: r.app_version,
      ipAddress: r.local_ip || r.public_ip,
      updatedAt: r.last_seen_at
    }));
    res.json({ success: true, pending, registrations: pending, count: pending.length });
  }

  /**
   * POST /api/v1/admin/registration/approve
   * 1-Click Approve hoặc Custom Approve
   */
  public static async approveRegistration(req: Request, res: Response): Promise<void> {
    const db = DatabaseManager.getInstance(config.dbPath);
    const {
      registrationId,
      licenseId,
      customerName,
      edition,
      licenseType,
      expirationDays,
      maxCameras,
      allowedFeatures,
      notes
    } = req.body;

    if (!registrationId) {
      res.status(400).json({ success: false, message: 'Thiếu registrationId.' });
      return;
    }

    const expDays = req.body.expirationDays !== undefined ? req.body.expirationDays : req.body.durationDays;
    const result = db.approveClientRegistration(registrationId, {
      license_id: licenseId,
      customer_name: customerName,
      edition: edition || 'Enterprise',
      license_type: licenseType || (expDays && parseInt(expDays, 10) > 0 ? 'Trial' : 'Perpetual'),
      expiration_days: expDays !== undefined ? parseInt(expDays, 10) : undefined,
      max_cameras: maxCameras ? parseInt(maxCameras, 10) : 4,
      allowed_features: allowedFeatures,
      notes
    });

    if (result.success) {
      res.json(result);
    } else {
      res.status(400).json(result);
    }
  }

  /**
   * POST /api/v1/admin/machine/delete
   * Xóa hoàn toàn máy trạm khỏi hệ thống
   */
  public static async deleteMachine(req: Request, res: Response): Promise<void> {
    const db = DatabaseManager.getInstance(config.dbPath);
    const { machineFingerprint } = req.body;
    if (!machineFingerprint) {
      res.status(400).json({ success: false, message: 'Thiếu machineFingerprint.' });
      return;
    }
    const success = db.deleteMachine(machineFingerprint);
    res.json({ success, message: success ? 'Đã xóa hoàn toàn máy trạm khỏi hệ thống.' : 'Không tìm thấy máy trạm.' });
  }

  /**
   * POST /api/v1/admin/registration/reject
   */
  public static async rejectRegistration(req: Request, res: Response): Promise<void> {
    const db = DatabaseManager.getInstance(config.dbPath);
    const { registrationId, reason } = req.body;

    if (!registrationId) {
      res.status(400).json({ success: false, message: 'Thiếu registrationId.' });
      return;
    }

    const ok = db.rejectClientRegistration(registrationId, reason);
    if (ok) {
      res.json({ success: true, message: 'Đã từ chối yêu cầu cấp phép cho máy trạm này.' });
    } else {
      res.status(404).json({ success: false, message: 'Không tìm thấy yêu cầu đăng ký.' });
    }
  }

  /**
   * POST /api/v1/admin/license/delete
   * Xóa vĩnh viễn một License và thu hồi toàn bộ máy trạm đang dùng
   */
  public static async deleteLicense(req: Request, res: Response): Promise<void> {
    const db = DatabaseManager.getInstance(config.dbPath);
    const { licenseId } = req.body;

    if (!licenseId) {
      res.status(400).json({ success: false, message: 'Thiếu licenseId.' });
      return;
    }

    const ok = db.deleteLicense(licenseId);
    if (ok) {
      res.json({ success: true, message: 'Đã xóa vĩnh viễn License Key và thu hồi toàn bộ máy trạm liên kết thành công!' });
    } else {
      res.status(404).json({ success: false, message: 'Không tìm thấy License Key cần xóa.' });
    }
  }

  /**
   * POST /api/v1/admin/machine/change-plan
   * Thay đổi gói cước (Basic/Pro/Enterprise) hoặc thời hạn (Vĩnh viễn/1 năm/Dùng thử) cho máy trạm cụ thể
   */
  public static async changeMachinePlan(req: Request, res: Response): Promise<void> {
    const db = DatabaseManager.getInstance(config.dbPath);
    const { machineFingerprint, edition, licenseType, durationDays } = req.body;

    if (!machineFingerprint) {
      res.status(400).json({ success: false, message: 'Thiếu machineFingerprint.' });
      return;
    }

    const result = db.changeMachinePlan(machineFingerprint, {
      edition: edition || 'Enterprise',
      licenseType: licenseType || 'Perpetual',
      durationDays: durationDays !== undefined ? parseInt(durationDays, 10) : 0
    });

    if (result.success) {
      res.json(result);
    } else {
      res.status(400).json(result);
    }
  }

  /**
   * POST /api/v1/admin/registration/reset
   */
  public static async resetRegistration(req: Request, res: Response): Promise<void> {
    const db = DatabaseManager.getInstance(config.dbPath);
    const { machineFingerprint } = req.body;
    if (!machineFingerprint) {
      res.status(400).json({ success: false, message: 'Thiếu machineFingerprint.' });
      return;
    }
    db.resetClientRegistration(machineFingerprint);
    res.json({ success: true, message: 'Đã xóa đăng ký cũ của máy trạm để test đăng ký mới.' });
  }
}

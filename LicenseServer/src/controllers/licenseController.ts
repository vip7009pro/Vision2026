import { Request, Response } from 'express';
import { DatabaseManager } from '../database';
import { LicenseCrypto, LicensePayload } from '../crypto/licenseCrypto';
import { config } from '../config';

export class LicenseController {
  /**
   * POST /api/v1/license/activate
   * Kích hoạt máy trạm Online
   */
  public static async activate(req: Request, res: Response): Promise<void> {
    const db = DatabaseManager.getInstance(config.dbPath);
    const clientIp = (req.headers['x-forwarded-for'] as string) || req.socket.remoteAddress || '';

    const {
      licenseKey,
      machineFingerprint,
      machineName,
      osVersion,
      appVersion,
      localIp
    } = req.body;

    if (!licenseKey || !machineFingerprint) {
      res.status(400).json({
        success: false,
        message: 'Yêu cầu thiếu licenseKey hoặc machineFingerprint.'
      });
      return;
    }

    const license = db.getLicenseByKey(licenseKey.trim());
    if (!license) {
      db.logAudit({
        license_key: licenseKey,
        machine_fingerprint: machineFingerprint,
        action: 'ACTIVATE_FAILED_KEY_NOT_FOUND',
        ip_address: clientIp
      });

      res.status(404).json({
        success: false,
        message: 'Mã bản quyền (License Key) không tồn tại trên hệ thống.'
      });
      return;
    }

    if (license.status !== 'Active') {
      res.status(403).json({
        success: false,
        message: `Mã bản quyền này đang ở trạng thái '${license.status}' (Không khả dụng).`
      });
      return;
    }

    // Kiểm tra ngày hết hạn của gói License
    if (license.expires_at) {
      const expiry = new Date(license.expires_at);
      if (expiry.getTime() < Date.now()) {
        res.status(403).json({
          success: false,
          message: 'Mã bản quyền này đã hết hạn sử dụng.'
        });
        return;
      }
    }

    // Kiểm tra xem máy này đã từng được cấp phép cho key này chưa
    const existingMachine = db.getMachine(license.id, machineFingerprint);
    if (!existingMachine) {
      // Máy mới -> kiểm tra số lượng máy tối đa cho phép
      const currentActiveCount = db.countActiveMachinesForLicense(license.id);
      if (currentActiveCount >= license.max_machines) {
        db.logAudit({
          license_key: licenseKey,
          machine_fingerprint: machineFingerprint,
          action: 'ACTIVATE_REJECTED_SLOT_FULL',
          ip_address: clientIp,
          details: { currentActiveCount, maxAllowed: license.max_machines }
        });

        res.status(403).json({
          success: false,
          message: `Mã bản quyền đã đạt giới hạn tối đa (${license.max_machines} máy). Vui lòng hủy bớt máy cũ hoặc liên hệ nhà cung cấp.`
        });
        return;
      }
    } else {
      // Nếu máy đã tồn tại nhưng trước đó bị Admin thu hồi
      if (existingMachine.is_revoked === 1) {
        res.status(403).json({
          success: false,
          message: `Máy tính này đã bị thu hồi bản quyền từ xa: "${existingMachine.revoked_reason || 'Chưa rõ lý do'}". Vui lòng liên hệ Admin.`
        });
        return;
      }
    }

    // Đăng ký hoặc cập nhật thông tin máy
    const machine = db.registerOrUpdateMachine({
      license_id: license.id,
      machine_fingerprint: machineFingerprint,
      machine_name: machineName || 'Industrial-PC',
      os_version: osVersion || 'Windows',
      app_version: appVersion || '2.1.0',
      local_ip: localIp || '127.0.0.1',
      public_ip: clientIp
    });

    // Tạo payload để ký số
    const features: string[] = JSON.parse(license.allowed_features || '[]');
    const payload: LicensePayload = {
      licenseId: license.id,
      licenseKey: license.license_key,
      customerName: license.customer_name,
      edition: license.edition as any,
      machineFingerprint: machine.machine_fingerprint,
      machineName: machine.machine_name,
      licenseType: license.license_type as any,
      issuedDateUtc: license.issued_at,
      expirationDateUtc: license.expires_at,
      allowedFeatures: features,
      maxCameraCount: license.max_cameras,
      heartbeatIntervalHours: 12,
      gracePeriodDays: 7
    };

    // Ký số payload bằng RSA Private Key
    const signedPackage = LicenseCrypto.signPayload(payload, config.keysDir);

    db.logAudit({
      license_key: licenseKey,
      machine_fingerprint: machineFingerprint,
      action: 'ACTIVATE_SUCCESS',
      ip_address: clientIp,
      details: { machineName: machine.machine_name }
    });

    res.json({
      success: true,
      message: 'Kích hoạt bản quyền thành công!',
      license: signedPackage
    });
  }

  /**
   * POST /api/v1/license/heartbeat
   * Định kỳ gửi từ máy trạm để duy trì bản quyền và nhận lệnh từ xa
   */
  public static async heartbeat(req: Request, res: Response): Promise<void> {
    const db = DatabaseManager.getInstance(config.dbPath);
    const clientIp = (req.headers['x-forwarded-for'] as string) || req.socket.remoteAddress || '';
    const { licenseKey, machineFingerprint, appVersion } = req.body;

    if (!machineFingerprint) {
      res.status(400).json({ success: false, message: 'Thiếu machineFingerprint.' });
      return;
    }

    const machine = db.updateHeartbeat(machineFingerprint, clientIp, appVersion);
    const serverTimeUtc = new Date().toISOString();

    if (!machine) {
      res.status(404).json({
        success: false,
        status: 'Unregistered',
        message: 'Máy trạm chưa được đăng ký trong hệ thống.',
        serverTimeUtc
      });
      return;
    }

    if (machine.is_revoked === 1) {
      res.json({
        success: true,
        status: 'Revoked',
        message: `Bản quyền đã bị thu hồi từ xa: "${machine.revoked_reason || 'Hủy sử dụng'}".`,
        revokedAt: machine.revoked_at,
        serverTimeUtc
      });
      return;
    }

    if (machine.is_suspended === 1) {
      res.json({
        success: true,
        status: 'Suspended',
        message: 'Bản quyền đang bị tạm khóa.',
        serverTimeUtc
      });
      return;
    }

    res.json({
      success: true,
      status: 'Active',
      message: 'Heartbeat OK. Bản quyền đang hoạt động bình thường.',
      serverTimeUtc
    });
  }

  /**
   * GET /api/v1/license/public-key
   * Tải về Public Key định dạng PEM để Client xác thực
   */
  public static async getPublicKey(req: Request, res: Response): Promise<void> {
    try {
      const publicKey = LicenseCrypto.getPublicKey(config.keysDir);
      res.type('text/plain').send(publicKey);
    } catch (err: any) {
      res.status(500).json({ success: false, message: err.message });
    }
  }

  /**
   * POST /api/v1/license/offline-sign
   * Ký số thủ công tạo file .lic từ file .req (Dành cho Admin hoặc máy không mạng)
   */
  public static async offlineSign(req: Request, res: Response): Promise<void> {
    const db = DatabaseManager.getInstance(config.dbPath);
    const {
      requestCode, // Chuỗi Base64 hoặc JSON parse từ file .req
      licenseKey,
      customerName,
      edition,
      licenseType,
      expiresAt,
      allowedFeatures,
      maxCameras
    } = req.body;

    if (!requestCode || !licenseKey) {
      res.status(400).json({ success: false, message: 'Yêu cầu thiếu requestCode hoặc licenseKey.' });
      return;
    }

    try {
      // Giải mã requestCode
      let reqData: any;
      try {
        const decodedStr = Buffer.from(requestCode, 'base64').toString('utf8');
        reqData = JSON.parse(decodedStr);
      } catch {
        reqData = typeof requestCode === 'string' ? JSON.parse(requestCode) : requestCode;
      }

      const { machineFingerprint, machineName, osVersion, appVersion } = reqData;
      if (!machineFingerprint) {
        res.status(400).json({ success: false, message: 'File yêu cầu không chứa machineFingerprint hợp lệ.' });
        return;
      }

      // Kiểm tra hoặc tự động tạo License trong DB nếu chưa có
      let license = db.getLicenseByKey(licenseKey);
      if (!license) {
        license = db.createLicense({
          license_key: licenseKey,
          customer_name: customerName || 'Offline Customer',
          edition: edition || 'Enterprise',
          license_type: licenseType || 'Perpetual',
          max_machines: 5,
          allowed_features: JSON.stringify(allowedFeatures || [
            'InspectionEngine', 'HighSpeedCamera', 'PlcBridge', 'OqcScanner', 'AI_OCR_Industrial', 'DatabaseIntegration'
          ]),
          max_cameras: maxCameras || 4,
          issued_at: new Date().toISOString(),
          expires_at: expiresAt || null,
          status: 'Active',
          notes: 'Created via Offline Signing'
        });
      }

      // Ghi nhận máy vào DB
      db.registerOrUpdateMachine({
        license_id: license.id,
        machine_fingerprint: machineFingerprint,
        machine_name: machineName || 'Offline-PC',
        os_version: osVersion || 'Windows',
        app_version: appVersion || '2.1.0',
        local_ip: 'OFFLINE-OT-NETWORK',
        public_ip: 'OFFLINE'
      });

      const payload: LicensePayload = {
        licenseId: license.id,
        licenseKey: license.license_key,
        customerName: license.customer_name,
        edition: license.edition as any,
        machineFingerprint: machineFingerprint,
        machineName: machineName || 'Offline-PC',
        licenseType: license.license_type as any,
        issuedDateUtc: license.issued_at,
        expirationDateUtc: license.expires_at,
        allowedFeatures: JSON.parse(license.allowed_features),
        maxCameraCount: license.max_cameras,
        heartbeatIntervalHours: 0, // 0 = Không yêu cầu heartbeat online
        gracePeriodDays: 30
      };

      const signedPackage = LicenseCrypto.signPayload(payload, config.keysDir);
      const licenseFileContent = Buffer.from(JSON.stringify(signedPackage, null, 2)).toString('base64');

      db.logAudit({
        license_key: licenseKey,
        machine_fingerprint: machineFingerprint,
        action: 'OFFLINE_SIGN_SUCCESS',
        details: { customerName: license.customer_name }
      });

      res.json({
        success: true,
        message: 'Ký số License Offline thành công!',
        licenseFileName: `License_${machineName || 'Machine'}_${license.license_key}.lic`,
        licenseFileBase64: licenseFileContent,
        signedPackage
      });
    } catch (err: any) {
      res.status(500).json({ success: false, message: `Lỗi xử lý file yêu cầu: ${err.message}` });
    }
  }
}

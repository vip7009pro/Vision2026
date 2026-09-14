import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';

export interface LicensePayload {
  licenseId: string;
  licenseKey: string;
  customerName: string;
  edition: 'Basic' | 'Pro' | 'Enterprise';
  machineFingerprint: string;
  machineName: string;
  licenseType: 'Perpetual' | 'Subscription' | 'Trial';
  issuedDateUtc: string;
  expirationDateUtc: string | null;
  allowedFeatures: string[];
  maxCameraCount: number;
  heartbeatIntervalHours: number;
  gracePeriodDays: number;
}

export interface SignedLicensePackage {
  payload: LicensePayload;
  signature: string; // Base64
}

export interface OfflineRequestPayload {
  machineFingerprint: string;
  machineName: string;
  osVersion: string;
  requestTimestampUtc: string;
  appVersion: string;
}

export class LicenseCrypto {
  private static privateKeyPem: string | null = null;
  private static publicKeyPem: string | null = null;

  /**
   * Đảm bảo cặp khóa RSA-2048 tồn tại trong thư mục keys/.
   * Nếu chưa có, tự động sinh cặp khóa mới và ghi vào đĩa.
   */
  public static ensureKeyPair(keysDir: string = './keys'): { privateKey: string; publicKey: string } {
    if (this.privateKeyPem && this.publicKeyPem) {
      return { privateKey: this.privateKeyPem, publicKey: this.publicKeyPem };
    }

    if (!fs.existsSync(keysDir)) {
      fs.mkdirSync(keysDir, { recursive: true });
    }

    const privateKeyPath = path.join(keysDir, 'private.pem');
    const publicKeyPath = path.join(keysDir, 'public.pem');

    if (fs.existsSync(privateKeyPath) && fs.existsSync(publicKeyPath)) {
      this.privateKeyPem = fs.readFileSync(privateKeyPath, 'utf8');
      this.publicKeyPem = fs.readFileSync(publicKeyPath, 'utf8');
      return { privateKey: this.privateKeyPem, publicKey: this.publicKeyPem };
    }

    console.log('Generating new RSA-2048 keypair for License Server...');
    const { privateKey, publicKey } = crypto.generateKeyPairSync('rsa', {
      modulusLength: 2048,
      publicKeyEncoding: {
        type: 'spki',
        format: 'pem'
      },
      privateKeyEncoding: {
        type: 'pkcs8',
        format: 'pem'
      }
    });

    fs.writeFileSync(privateKeyPath, privateKey, { encoding: 'utf8', mode: 0o600 });
    fs.writeFileSync(publicKeyPath, publicKey, { encoding: 'utf8', mode: 0o644 });

    this.privateKeyPem = privateKey;
    this.publicKeyPem = publicKey;
    console.log('RSA-2048 keypair generated successfully.');

    return { privateKey, publicKey };
  }

  public static getPublicKey(keysDir: string = './keys'): string {
    return this.ensureKeyPair(keysDir).publicKey;
  }

  public static getPrivateKey(keysDir: string = './keys'): string {
    return this.ensureKeyPair(keysDir).privateKey;
  }

  /**
   * Tạo chuỗi Canonical JSON (sắp xếp khóa thuộc tính theo bảng chữ cái)
   * Đảm bảo dữ liệu băm đồng nhất 100% giữa Node.js và C# .NET.
   */
  public static toCanonicalJson(obj: any): string {
    if (obj === null || typeof obj !== 'object') {
      return JSON.stringify(obj);
    }
    if (Array.isArray(obj)) {
      return '[' + obj.map(item => this.toCanonicalJson(item)).join(',') + ']';
    }
    const sortedKeys = Object.keys(obj).sort();
    const parts = sortedKeys.map(key => {
      const valStr = this.toCanonicalJson(obj[key]);
      return `"${key}":${valStr}`;
    });
    return '{' + parts.join(',') + '}';
  }

  /**
   * Ký số LicensePayload bằng RSA-2048 Private Key (SHA-256)
   */
  public static signPayload(payload: LicensePayload, keysDir: string = './keys'): SignedLicensePackage {
    const privateKey = this.getPrivateKey(keysDir);
    const canonicalJson = this.toCanonicalJson(payload);

    const sign = crypto.createSign('SHA256');
    sign.update(canonicalJson, 'utf8');
    sign.end();

    const signature = sign.sign(privateKey, 'base64');
    return {
      payload,
      signature
    };
  }

  /**
   * Xác thực chữ ký số bằng Public Key
   */
  public static verifySignature(payload: LicensePayload, signatureBase64: string, keysDir: string = './keys'): boolean {
    try {
      const publicKey = this.getPublicKey(keysDir);
      const canonicalJson = this.toCanonicalJson(payload);

      const verify = crypto.createVerify('SHA256');
      verify.update(canonicalJson, 'utf8');
      verify.end();

      return verify.verify(publicKey, signatureBase64, 'base64');
    } catch {
      return false;
    }
  }

  /**
   * Sinh mã License Key ngẫu nhiên theo định dạng chuẩn: V26-ENT-XXXX-XXXX-XXXX
   */
  public static generateLicenseKey(edition: string = 'ENT'): string {
    const chars = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789'; // Loại trừ 0, O, 1, I để tránh nhầm lẫn
    const part = (len: number) => {
      let res = '';
      const bytes = crypto.randomBytes(len);
      for (let i = 0; i < len; i++) {
        res += chars[bytes[i] % chars.length];
      }
      return res;
    };
    return `V26-${edition.toUpperCase()}-${part(4)}-${part(4)}-${part(4)}`;
  }
}

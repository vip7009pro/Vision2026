// Smoke test cho tính năng Export/Import toàn bộ dữ liệu License Server.
// Chạy: node scratch/test_license_backup.js  (sau khi đã npm run build trong LicenseServer)
const path = require('path');
const fs = require('fs');
const os = require('os');

const tmpDir = fs.mkdtempSync(path.join(os.tmpdir(), 'v26lic-'));
process.env.DB_PATH = path.join(tmpDir, 'test.db');
process.env.KEYS_DIR = path.join(tmpDir, 'keys');

const buildDir = path.join(__dirname, '..', 'LicenseServer', 'dist');
const { DatabaseManager } = require(path.join(buildDir, 'database', 'index.js'));
const { LicenseCrypto } = require(path.join(buildDir, 'crypto', 'licenseCrypto.js'));

const db = DatabaseManager.getInstance(process.env.DB_PATH);

const initial = db.exportAllData();
console.log('[1] initial counts:', initial.meta.counts);

const lic = db.createLicense({
  license_key: 'V26-TEST-KEY-0001',
  customer_name: 'Test Co',
  edition: 'Pro',
  license_type: 'Perpetual',
  max_machines: 2,
  allowed_features: JSON.stringify(['InspectionEngine']),
  max_cameras: 2,
  issued_at: new Date().toISOString(),
  expires_at: null,
  status: 'Active',
  notes: 'smoke test'
});

db.registerOrUpdateMachine({
  license_id: lic.id,
  machine_fingerprint: 'FP-TEST-1',
  machine_name: 'PC-TEST',
  os_version: 'Windows 11',
  app_version: '1.0.0',
  local_ip: '192.168.1.10',
  public_ip: '1.2.3.4'
});
db.logAudit({ license_key: 'V26-TEST-KEY-0001', action: 'SMOKE_TEST', details: { ok: true } });

const snapshot = db.exportAllData();
snapshot.keys = LicenseCrypto.exportKeyPair(process.env.KEYS_DIR);
console.log('[2] snapshot counts:', snapshot.meta.counts, '| hasKeys:', !!snapshot.keys.privateKey);

// --- Xóa sạch dữ liệu để mô phỏng server mới rỗng ---
db.getConnection().exec('DELETE FROM audit_logs; DELETE FROM machines; DELETE FROM client_registrations; DELETE FROM licenses;');
console.log('[3] after wipe:', JSON.stringify(db.getTableCounts()));

const replaceRes = db.importAllData(snapshot, { replace: true });
console.log('[4] import(replace):', JSON.stringify(replaceRes.imported), '=> counts', JSON.stringify(db.getTableCounts()));
console.log('    license exists:', !!db.getLicenseByKey('V26-TEST-KEY-0001'), '| machine exists:', !!db.getMachineByFingerprint('FP-TEST-1'));

const mergeRes = db.importAllData(snapshot, { replace: false });
console.log('[5] import(merge):', JSON.stringify(mergeRes.imported), '=> counts', JSON.stringify(db.getTableCounts()), '(không được nhân đôi license/machine)');

// --- Kiểm tra khôi phục khóa RSA ---
const originalPriv = fs.readFileSync(path.join(process.env.KEYS_DIR, 'private.pem'), 'utf8');
fs.writeFileSync(path.join(process.env.KEYS_DIR, 'private.pem'), 'BROKEN');
fs.writeFileSync(path.join(process.env.KEYS_DIR, 'public.pem'), 'BROKEN');
LicenseCrypto.resetCache();
LicenseCrypto.importKeyPair(process.env.KEYS_DIR, snapshot.keys.privateKey, snapshot.keys.publicKey);
const restored = LicenseCrypto.exportKeyPair(process.env.KEYS_DIR).privateKey;
console.log('[6] RSA key restored correctly:', restored === originalPriv);

const finalCounts = db.getTableCounts();
const ok =
  finalCounts.licenses === 2 &&
  finalCounts.machines === 1 &&
  finalCounts.auditLogs === snapshot.meta.counts.auditLogs &&
  restored === originalPriv;

console.log(ok ? '\n✅ ALL SMOKE TESTS PASSED' : '\n❌ SMOKE TEST FAILED');
// Dọn dẹp (có thể bị khóa file DB trên Windows -> bỏ qua lỗi).
try {
  fs.rmSync(tmpDir, { recursive: true, force: true });
} catch {
  /* ignore */
}
process.exit(ok ? 0 : 1);

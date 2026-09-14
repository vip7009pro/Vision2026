import { Router } from 'express';
import { LicenseController } from '../controllers/licenseController';

const router = Router();

router.post('/activate', LicenseController.activate);
router.post('/heartbeat', LicenseController.heartbeat);
router.get('/public-key', LicenseController.getPublicKey);
router.post('/offline-sign', LicenseController.offlineSign);

// Auto-registration & 1-click approval workflow
router.post('/auto-register', LicenseController.autoRegister);
router.get('/check-registration', LicenseController.checkRegistration);

export default router;

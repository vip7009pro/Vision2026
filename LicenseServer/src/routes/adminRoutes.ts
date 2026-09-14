import { Router } from 'express';
import { AdminController } from '../controllers/adminController';
import { requireAdminAuth } from '../middlewares/auth';

const router = Router();

// Public auth route
router.post('/login', AdminController.login);

// Protected routes (Requires Admin JWT Token)
router.use(requireAdminAuth);

router.get('/dashboard', AdminController.getDashboard);
router.get('/clients', AdminController.listClients);
router.post('/machine/revoke', AdminController.revokeMachine);
router.post('/machine/suspend', AdminController.suspendMachine);
router.post('/machine/activate', AdminController.activateMachine);
router.post('/machine/transfer', AdminController.transferMachine);

router.get('/licenses', AdminController.listLicenses);
router.post('/license/create', AdminController.createLicense);

export default router;

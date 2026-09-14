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
router.post('/machine/delete', AdminController.deleteMachine);

router.get('/licenses', AdminController.listLicenses);
router.post('/license/create', AdminController.createLicense);
router.post('/license/delete', AdminController.deleteLicense);

// Machine plan change (Edition & Duration)
router.post('/machine/change-plan', AdminController.changeMachinePlan);

// Pending registrations & 1-click approvals
router.get('/pending-registrations', AdminController.listPendingRegistrations);
router.post('/registration/approve', AdminController.approveRegistration);
router.post('/registration/reject', AdminController.rejectRegistration);
router.post('/registration/reset', AdminController.resetRegistration);

export default router;

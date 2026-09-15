import express from 'express';
import cors from 'cors';
import path from 'node:path';
import fs from 'node:fs';
import { config } from './config';
import { LicenseCrypto } from './crypto/licenseCrypto';
import { DatabaseManager } from './database';
import licenseRoutes from './routes/licenseRoutes';
import adminRoutes from './routes/adminRoutes';

const app = express();

// Middleware
app.use(cors());
app.use(express.json({ limit: '10mb' }));
app.use(express.urlencoded({ extended: true, limit: '10mb' }));

// Khởi tạo các dịch vụ nền tảng (RSA Keypair & Database)
console.log('=======================================================');
console.log('🚀 INITIALIZING VISION2026 STANDALONE LICENSE SERVER');
console.log('=======================================================');

LicenseCrypto.ensureKeyPair(config.keysDir);
DatabaseManager.getInstance(config.dbPath);

// Phục vụ file tĩnh cho Web Admin Dashboard
const publicDir = fs.existsSync(path.join(__dirname, 'public'))
  ? path.join(__dirname, 'public')
  : fs.existsSync(path.join(__dirname, '../src/public'))
  ? path.join(__dirname, '../src/public')
  : path.join(__dirname, '../public');

app.use(express.static(publicDir));

// Health check endpoint
app.get('/health', (req, res) => {
  res.json({
    status: 'ok',
    service: 'Vision2026 Standalone License Server',
    version: '1.0.0',
    timestampUtc: new Date().toISOString()
  });
});

// API Routes
app.use('/api/v1/license', licenseRoutes);
app.use('/api/v1/admin', adminRoutes);

// Fallback SPA route cho Web Admin Dashboard
app.get('*', (req, res) => {
  res.sendFile(path.join(publicDir, 'index.html'));
});

// Start listening
const server = app.listen(config.port, config.host, () => {
  console.log(`✅ License Server is running at: http://${config.host}:${config.port}`);
  console.log(`🌐 Local Web Admin Dashboard: http://localhost:${config.port}/`);
  console.log(`📡 Network / Public NAT: http://${config.host === '0.0.0.0' ? '127.0.0.1' : config.host}:${config.port}/`);
  console.log(`🔑 Public Key API: http://localhost:${config.port}/api/v1/license/public-key`);
  console.log('=======================================================\n');
});

export default server;

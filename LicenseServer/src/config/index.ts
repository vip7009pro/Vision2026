import dotenv from 'dotenv';
import path from 'node:path';

dotenv.config();

export const config = {
  port: parseInt(process.env.PORT || '4000', 10),
  host: process.env.HOST || '0.0.0.0',
  nodeEnv: process.env.NODE_ENV || 'development',
  dbPath: process.env.DB_PATH || './data/license.db',
  keysDir: process.env.KEYS_DIR || './keys',
  admin: {
    username: process.env.ADMIN_USERNAME || 'admin',
    password: process.env.ADMIN_PASSWORD || 'admin@vision2026',
    jwtSecret: process.env.ADMIN_JWT_SECRET || 'vision2026-standalone-license-secret-jwt-key'
  }
};

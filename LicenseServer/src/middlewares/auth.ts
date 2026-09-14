import { Request, Response, NextFunction } from 'express';
import jwt from 'jsonwebtoken';
import { config } from '../config';

export interface AuthRequest extends Request {
  adminUser?: {
    username: string;
    role: string;
  };
}

export function requireAdminAuth(req: AuthRequest, res: Response, next: NextFunction): void {
  const authHeader = req.headers.authorization;
  if (!authHeader || !authHeader.startsWith('Bearer ')) {
    res.status(401).json({ success: false, message: 'Unauthorized: Thiếu Authorization header.' });
    return;
  }

  const token = authHeader.substring(7);
  try {
    const decoded = jwt.verify(token, config.admin.jwtSecret) as { username: string; role: string };
    req.adminUser = decoded;
    next();
  } catch (err) {
    res.status(401).json({ success: false, message: 'Unauthorized: Token không hợp lệ hoặc đã hết hạn.' });
  }
}

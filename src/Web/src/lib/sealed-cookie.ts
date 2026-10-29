import { createCipheriv, createDecipheriv, hkdfSync, randomBytes } from 'node:crypto';
import { deflateRawSync, inflateRawSync } from 'node:zlib';

let key: Buffer | undefined;

function sessionKey(): Buffer {
  const secret = process.env.SESSION_SECRET;
  if (!secret) throw new Error('SESSION_SECRET environment variable is not set (needed for sign-in).');
  return (key ??= Buffer.from(hkdfSync('sha256', secret, '', 'web-session-cookie', 32)));
}

/**
 * Encrypts and authenticates a cookie value (AES-256-GCM; the cookie name is the additional data, so one cookie's value
 * cannot be replayed as another's). Deflated first, which keeps a session with its tokens well under 4 KB.
 */
export function seal(name: string, data: unknown): string {
  const iv = randomBytes(12);
  const cipher = createCipheriv('aes-256-gcm', sessionKey(), iv).setAAD(Buffer.from(name));
  const body = Buffer.concat([cipher.update(deflateRawSync(JSON.stringify(data))), cipher.final()]);
  return Buffer.concat([iv, cipher.getAuthTag(), body]).toString('base64url');
}

/** The sealed value, or undefined when it is missing, tampered with or sealed with another secret. */
export function unseal<T>(name: string, value: string | undefined): T | undefined {
  if (!value) return undefined;
  try {
    const raw = Buffer.from(value, 'base64url');
    const decipher = createDecipheriv('aes-256-gcm', sessionKey(), raw.subarray(0, 12)).setAAD(Buffer.from(name));
    decipher.setAuthTag(raw.subarray(12, 28));
    return JSON.parse(inflateRawSync(Buffer.concat([decipher.update(raw.subarray(28)), decipher.final()])).toString('utf8')) as T;
  } catch {
    return undefined;
  }
}

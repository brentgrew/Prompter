/**
 * Prompter Chrome Extension - Web Crypto Module
 * 100% compatible with Prompter desktop CryptoService.cs (PBKDF2 + AES-GCM).
 */

const PrompterCrypto = {
  SALT_SIZE: 16,
  KEY_SIZE: 32, // 256 bits
  ITERATIONS: 100000,
  NONCE_SIZE: 12, // 96 bits for AES-GCM
  TAG_SIZE: 16,   // 128 bits for AES-GCM

  generateSaltBase64() {
    const salt = new Uint8Array(this.SALT_SIZE);
    window.crypto.getRandomValues(salt);
    return this.uint8ToBase64(salt);
  },

  async deriveKey(password, saltBytes) {
    const enc = new TextEncoder();
    const baseKey = await window.crypto.subtle.importKey(
      'raw',
      enc.encode(password),
      'PBKDF2',
      false,
      ['deriveKey', 'deriveBits']
    );

    return await window.crypto.subtle.deriveKey(
      {
        name: 'PBKDF2',
        salt: saltBytes,
        iterations: this.ITERATIONS,
        hash: 'SHA-256'
      },
      baseKey,
      { name: 'AES-GCM', length: 256 },
      false,
      ['encrypt', 'decrypt']
    );
  },

  async deriveBits(password, saltBytes) {
    const enc = new TextEncoder();
    const baseKey = await window.crypto.subtle.importKey(
      'raw',
      enc.encode(password),
      'PBKDF2',
      false,
      ['deriveBits']
    );

    return await window.crypto.subtle.deriveBits(
      {
        name: 'PBKDF2',
        salt: saltBytes,
        iterations: this.ITERATIONS,
        hash: 'SHA-256'
      },
      baseKey,
      this.KEY_SIZE * 8
    );
  },

  async hashPassword(password, saltBase64) {
    const saltBytes = this.base64ToUint8(saltBase64);
    const bits = await this.deriveBits(password, saltBytes);
    return this.uint8ToBase64(new Uint8Array(bits));
  },

  async verifyPassword(password, saltBase64, expectedHashBase64) {
    try {
      const actualHash = await this.hashPassword(password, saltBase64);
      return actualHash === expectedHashBase64;
    } catch (e) {
      return false;
    }
  },

  async encrypt(plainText, password, saltBase64) {
    const saltBytes = this.base64ToUint8(saltBase64);
    const key = await this.deriveKey(password, saltBytes);

    const nonce = new Uint8Array(this.NONCE_SIZE);
    window.crypto.getRandomValues(nonce);

    const enc = new TextEncoder();
    const plainBytes = enc.encode(plainText);

    // WebCrypto AES-GCM appends the 16-byte tag to the ciphertext
    const cipherWithTag = await window.crypto.subtle.encrypt(
      {
        name: 'AES-GCM',
        iv: nonce,
        tagLength: 128
      },
      key,
      plainBytes
    );

    const cipherArray = new Uint8Array(cipherWithTag);
    // In Prompter desktop CryptoService.cs:
    // Format: Nonce (12 bytes) + Tag (16 bytes) + CipherText (remainder)
    // WebCrypto output is: CipherText (length - 16) + Tag (16 bytes)
    const tag = cipherArray.slice(cipherArray.length - this.TAG_SIZE);
    const cipherText = cipherArray.slice(0, cipherArray.length - this.TAG_SIZE);

    const combined = new Uint8Array(this.NONCE_SIZE + this.TAG_SIZE + cipherText.length);
    combined.set(nonce, 0);
    combined.set(tag, this.NONCE_SIZE);
    combined.set(cipherText, this.NONCE_SIZE + this.TAG_SIZE);

    return this.uint8ToBase64(combined);
  },

  async decrypt(cipherBase64, password, saltBase64) {
    const saltBytes = this.base64ToUint8(saltBase64);
    const key = await this.deriveKey(password, saltBytes);
    const combined = this.base64ToUint8(cipherBase64);

    if (combined.length < this.NONCE_SIZE + this.TAG_SIZE) {
      throw new Error('Invalid encrypted payload format.');
    }

    const nonce = combined.slice(0, this.NONCE_SIZE);
    const tag = combined.slice(this.NONCE_SIZE, this.NONCE_SIZE + this.TAG_SIZE);
    const cipherText = combined.slice(this.NONCE_SIZE + this.TAG_SIZE);

    // Reconstruct WebCrypto expected format: CipherText + Tag
    const webCryptoPayload = new Uint8Array(cipherText.length + this.TAG_SIZE);
    webCryptoPayload.set(cipherText, 0);
    webCryptoPayload.set(tag, cipherText.length);

    const decrypted = await window.crypto.subtle.decrypt(
      {
        name: 'AES-GCM',
        iv: nonce,
        tagLength: 128
      },
      key,
      webCryptoPayload
    );

    const dec = new TextDecoder();
    return dec.decode(decrypted);
  },

  // Helpers
  uint8ToBase64(bytes) {
    let binary = '';
    const len = bytes.byteLength;
    for (let i = 0; i < len; i++) {
      binary += String.fromCharCode(bytes[i]);
    }
    return window.btoa(binary);
  },

  base64ToUint8(base64) {
    const binary = window.atob(base64);
    const len = binary.length;
    const bytes = new Uint8Array(len);
    for (let i = 0; i < len; i++) {
      bytes[i] = binary.charCodeAt(i);
    }
    return bytes;
  }
};

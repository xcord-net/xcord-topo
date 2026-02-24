import { describe, it, expect } from 'vitest';
import { validateField, validateAllFields } from '../lib/credential-validation';
import type { CredentialField } from '../types/deploy';

const makeField = (overrides: Partial<CredentialField> = {}): CredentialField => ({
  key: 'test',
  label: 'Test',
  type: 'text',
  sensitive: false,
  required: true,
  validation: [],
  ...overrides,
});

describe('validateField', () => {
  it('returns error for required empty field', () => {
    const field = makeField({ key: 'token', label: 'Token', required: true });
    expect(validateField(field, '', false)).toBe('Token is required');
  });

  it('returns error for required whitespace-only field', () => {
    const field = makeField({ key: 'token', label: 'Token', required: true });
    expect(validateField(field, '   ', false)).toBe('Token is required');
  });

  it('returns null for required sensitive field already saved', () => {
    const field = makeField({ key: 'token', label: 'Token', required: true, sensitive: true });
    expect(validateField(field, '', true)).toBeNull();
  });

  it('returns null for optional empty field', () => {
    const field = makeField({ required: false });
    expect(validateField(field, '', false)).toBeNull();
  });

  it('returns error for minLength violation', () => {
    const field = makeField({
      validation: [{ type: 'minLength', value: '10', message: 'Too short' }],
    });
    expect(validateField(field, 'abc', false)).toBe('Too short');
  });

  it('returns null when minLength satisfied', () => {
    const field = makeField({
      validation: [{ type: 'minLength', value: '3', message: 'Too short' }],
    });
    expect(validateField(field, 'abc', false)).toBeNull();
  });

  it('returns error for maxLength violation', () => {
    const field = makeField({
      validation: [{ type: 'maxLength', value: '5', message: 'Too long' }],
    });
    expect(validateField(field, 'toolong', false)).toBe('Too long');
  });

  it('returns error for pattern violation', () => {
    const field = makeField({
      validation: [{ type: 'pattern', value: '^[a-z]+$', message: 'Lowercase only' }],
    });
    expect(validateField(field, 'ABC', false)).toBe('Lowercase only');
  });

  it('returns null when pattern matches', () => {
    const field = makeField({
      validation: [{ type: 'pattern', value: '^[a-z]+$', message: 'Lowercase only' }],
    });
    expect(validateField(field, 'abc', false)).toBeNull();
  });

  it('validates domain pattern', () => {
    const field = makeField({
      validation: [{
        type: 'pattern',
        value: '^(?:[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\\.)+[a-zA-Z]{2,}$',
        message: 'Invalid domain',
      }],
    });
    expect(validateField(field, 'example.com', false)).toBeNull();
    expect(validateField(field, 'not a domain!', false)).toBe('Invalid domain');
  });

  it('validates AWS access key pattern', () => {
    const field = makeField({
      validation: [{ type: 'pattern', value: '^AKIA[A-Z0-9]{16}$', message: 'Invalid key' }],
    });
    expect(validateField(field, 'AKIAIOSFODNN7EXAMPLE', false)).toBeNull();
    expect(validateField(field, 'badkey', false)).toBe('Invalid key');
  });

  it('validates SSH key pattern', () => {
    const field = makeField({
      required: false,
      validation: [{ type: 'pattern', value: '^ssh-(rsa|ed25519|ecdsa)\\s+[A-Za-z0-9+/=]+', message: 'Invalid SSH key' }],
    });
    expect(validateField(field, 'ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIExample', false)).toBeNull();
    expect(validateField(field, 'not-an-ssh-key', false)).toBe('Invalid SSH key');
  });

  it('skips validation rules for optional empty field', () => {
    const field = makeField({
      required: false,
      validation: [{ type: 'pattern', value: '^ssh-', message: 'Invalid' }],
    });
    expect(validateField(field, '', false)).toBeNull();
  });

  it('first rule failure wins', () => {
    const field = makeField({
      validation: [
        { type: 'minLength', value: '5', message: 'First error' },
        { type: 'minLength', value: '3', message: 'Second error' },
      ],
    });
    expect(validateField(field, 'ab', false)).toBe('First error');
  });
});

describe('validateAllFields', () => {
  it('returns errors for multiple invalid fields', () => {
    const schema: CredentialField[] = [
      makeField({ key: 'a', label: 'A', required: true }),
      makeField({ key: 'b', label: 'B', required: true }),
    ];
    const errors = validateAllFields(schema, {}, new Set());
    expect(errors).toEqual({ a: 'A is required', b: 'B is required' });
  });

  it('returns empty object when all valid', () => {
    const schema: CredentialField[] = [
      makeField({ key: 'a', label: 'A', required: true }),
    ];
    const errors = validateAllFields(schema, { a: 'value' }, new Set());
    expect(errors).toEqual({});
  });

  it('respects saved keys for sensitive fields', () => {
    const schema: CredentialField[] = [
      makeField({ key: 'token', label: 'Token', required: true, sensitive: true }),
    ];
    const errors = validateAllFields(schema, {}, new Set(['token']));
    expect(errors).toEqual({});
  });
});

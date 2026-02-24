import type { CredentialField, ValidationRule } from '../types/deploy';

export function validateField(field: CredentialField, value: string, isSaved: boolean): string | null {
  const hasValue = value.trim().length > 0;

  if (field.required && !hasValue && !(field.sensitive && isSaved)) {
    return `${field.label} is required`;
  }

  if (!hasValue) return null;

  for (const rule of field.validation) {
    const error = evaluateRule(rule, value);
    if (error) return error;
  }

  return null;
}

export function validateAllFields(
  schema: CredentialField[],
  values: Record<string, string>,
  savedKeys: Set<string>,
): Record<string, string> {
  const errors: Record<string, string> = {};
  for (const field of schema) {
    const error = validateField(field, values[field.key] ?? '', savedKeys.has(field.key));
    if (error) errors[field.key] = error;
  }
  return errors;
}

function evaluateRule(rule: ValidationRule, value: string): string | null {
  switch (rule.type) {
    case 'minLength': {
      const min = parseInt(rule.value ?? '0', 10);
      return value.length < min ? rule.message : null;
    }
    case 'maxLength': {
      const max = parseInt(rule.value ?? '0', 10);
      return value.length > max ? rule.message : null;
    }
    case 'pattern': {
      if (!rule.value) return null;
      return new RegExp(rule.value).test(value) ? null : rule.message;
    }
    default:
      return null;
  }
}

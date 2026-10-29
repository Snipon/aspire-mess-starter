import { logs, SeverityNumber, type AnyValueMap } from '@opentelemetry/api-logs';

// Server-side logger: writes to the console and emits an OpenTelemetry log record (dashboard and Seq when otel.mjs
// started the SDK, a no-op otherwise). Records carry the active trace context, so they appear under the request span.
const logger = logs.getLogger('web');

function emit(severityNumber: SeverityNumber, severityText: string, message: string, attributes: AnyValueMap) {
  logger.emit({ severityNumber, severityText, body: message, attributes });
}

function errorAttributes(error: unknown): AnyValueMap {
  return error instanceof Error
    ? { 'exception.type': error.name, 'exception.message': error.message, 'exception.stacktrace': error.stack }
    : error === undefined
      ? {}
      : { 'exception.message': String(error) };
}

export const log = {
  info(message: string, attributes: AnyValueMap = {}) {
    console.info(message);
    emit(SeverityNumber.INFO, 'Information', message, attributes);
  },
  warn(message: string, error?: unknown, attributes: AnyValueMap = {}) {
    console.warn(message, ...(error === undefined ? [] : [error]));
    emit(SeverityNumber.WARN, 'Warning', message, { ...attributes, ...errorAttributes(error) });
  },
  error(message: string, error?: unknown, attributes: AnyValueMap = {}) {
    console.error(message, ...(error === undefined ? [] : [error]));
    emit(SeverityNumber.ERROR, 'Error', message, { ...attributes, ...errorAttributes(error) });
  },
};

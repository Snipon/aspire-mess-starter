// @ts-check
// OpenTelemetry for the Astro server, preloaded with `node --import ./otel.mjs` (npm run dev/start, Dockerfile) so
// node:http and fetch (undici) are instrumented before Astro loads. Mirrors ServiceDefaults:
// - OTEL_EXPORTER_OTLP_ENDPOINT (+ _PROTOCOL/_HEADERS/_CERTIFICATE, injected by Aspire): Aspire dashboard.
// - ConnectionStrings__seq (+ optional Aspire__Seq__ApiKey): Seq at <seq>/ingest/otlp over http/protobuf.
// With neither set, no SDK is started and the OpenTelemetry APIs stay no-ops.
import { createRequire } from 'node:module';
import { NodeSDK, logs, tracing } from '@opentelemetry/sdk-node';
import { HttpInstrumentation } from '@opentelemetry/instrumentation-http';
import { UndiciInstrumentation } from '@opentelemetry/instrumentation-undici';
import { OTLPTraceExporter as GrpcTraceExporter } from '@opentelemetry/exporter-trace-otlp-grpc';
import { OTLPLogExporter as GrpcLogExporter } from '@opentelemetry/exporter-logs-otlp-grpc';
import { OTLPTraceExporter as ProtoTraceExporter } from '@opentelemetry/exporter-trace-otlp-proto';
import { OTLPLogExporter as ProtoLogExporter } from '@opentelemetry/exporter-logs-otlp-proto';

const dashboard = Boolean(process.env.OTEL_EXPORTER_OTLP_ENDPOINT);
const seqUrl = process.env.ConnectionStrings__seq?.replace(/\/+$/, '');

// Vite dev-server internals and static assets; only page requests are worth a span.
const ignoredPaths = /^\/(@|node_modules\/|src\/|_astro\/|favicon\.svg)/;
// Search terms are user input; keep them out of span attributes (the .NET services redact query strings too).
const redactedQueryParams = ['q'];

if (dashboard || seqUrl) {
  const spanProcessors = [];
  const logRecordProcessors = [];

  if (dashboard) {
    // The exporters read endpoint, headers and certificate from the OTEL_EXPORTER_OTLP_* variables themselves.
    const grpc = process.env.OTEL_EXPORTER_OTLP_PROTOCOL === 'grpc';
    spanProcessors.push(new tracing.BatchSpanProcessor(grpc ? new GrpcTraceExporter() : new ProtoTraceExporter()));
    logRecordProcessors.push(
      new logs.BatchLogRecordProcessor({ exporter: grpc ? new GrpcLogExporter() : new ProtoLogExporter() }),
    );
  }

  if (seqUrl) {
    const apiKey = process.env.Aspire__Seq__ApiKey;
    /** @type {Record<string, string>} */
    const headers = apiKey ? { 'X-Seq-ApiKey': apiKey } : {};
    // Exporters merge the OTEL_EXPORTER_OTLP_* headers and certificates (the dashboard's API key and dev cert) into
    // their own config when constructed; hide them meanwhile so they are not sent to Seq.
    const dashboardOnly = Object.keys(process.env).filter((k) =>
      /^OTEL_EXPORTER_OTLP_.*(HEADERS|CERTIFICATE|KEY)$/.test(k),
    );
    const saved = dashboardOnly.map((k) => [k, process.env[k]]);
    dashboardOnly.forEach((k) => delete process.env[k]);
    spanProcessors.push(
      new tracing.BatchSpanProcessor(new ProtoTraceExporter({ url: `${seqUrl}/ingest/otlp/v1/traces`, headers })),
    );
    logRecordProcessors.push(
      new logs.BatchLogRecordProcessor({
        exporter: new ProtoLogExporter({ url: `${seqUrl}/ingest/otlp/v1/logs`, headers }),
      }),
    );
    Object.assign(process.env, Object.fromEntries(saved));
  }

  const sdk = new NodeSDK({
    serviceName: process.env.OTEL_SERVICE_NAME ?? 'web',
    spanProcessors,
    logRecordProcessors,
    // Metrics follow OTEL_METRICS_EXPORTER/OTEL_EXPORTER_OTLP_* (dashboard only); Seq does not ingest metrics.
    ...(dashboard ? {} : { metricReaders: [] }),
    instrumentations: [
      new HttpInstrumentation({
        ignoreIncomingRequestHook: (req) => ignoredPaths.test(req.url ?? ''),
        redactedQueryParamsServer: redactedQueryParams,
      }),
      // Undici sets url.full/url.query unredacted; attributes returned by startSpanHook overwrite them.
      new UndiciInstrumentation({
        startSpanHook: (request) => {
          const url = new URL(request.path, request.origin);
          for (const param of redactedQueryParams) {
            if (url.searchParams.has(param)) url.searchParams.set(param, 'REDACTED');
          }
          return { 'url.full': url.toString(), 'url.query': url.search };
        },
      }),
    ],
  });
  sdk.start();
  // The http instrumentation patches through require(); Astro and Vite import node:http as ESM, which bypasses that
  // hook. Requiring it once patches the shared module object (Server.prototype.emit), so incoming requests get spans.
  createRequire(import.meta.url)('node:http');

  // Flush buffered spans and logs when the container (or Aspire) stops the process.
  process.once('SIGTERM', () => {
    sdk.shutdown().finally(() => process.exit(0));
  });
}

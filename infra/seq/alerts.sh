#!/usr/bin/env bash
# Creates (or updates) the Seq signals, notification app instances and alerts for this solution.
# Idempotent: entities are matched by title, so re-running converges them to the definitions below.
#
# Signals use the message templates and OpenTelemetry attributes the apps actually emit (see
# docs/deployment.md, "Alerts"). seqcli runs in a container; alerts have no seqcli command, so
# they go through Seq's HTTP API with curl + jq.
#
# Required:
#   SEQ_URL                 the seq_url Terraform output, or locally a tunnel to Seq's API (README, Observability)
#   SEQ_API_KEY             Seq API key with Read, Write, Project, Organization and System permissions
#                           (any value for a local Seq without authentication)
# Notifications (both optional; without either, alerts are only shown in the Seq UI):
#   ALERT_EMAILS            comma-separated recipients; needs SMTP_HOST and SMTP_FROM
#   SMTP_HOST, SMTP_PORT (default 587), SMTP_USERNAME, SMTP_PASSWORD, SMTP_FROM,
#   SMTP_SECURITY (RequireTls (default), RequireStartTls, RequireImplicitTls or None)
#   ALERT_WEBHOOK_URL       webhook that receives {"text": "<alert message>"} (e.g. a Slack incoming webhook)
# Tuning (optional):
#   ERROR_SPIKE_THRESHOLD   errors per 5 minutes that trigger "Error spike" (default 10)
#   SEQ_RETENTION           e.g. 30d: ensure a delete-all-events retention policy with this age
#   SEQ_REQUIRE_INGEST_API_KEY=true   reject log ingestion without an API key (production)
#   CONTAINER_RUNTIME       podman or docker (default: whichever is installed, podman first)
#   SEQCLI_IMAGE            default docker.io/datalust/seqcli:2025.2
set -euo pipefail

: "${SEQ_URL:?SEQ_URL is required}"
: "${SEQ_API_KEY:?SEQ_API_KEY is required}"
SEQ_URL="${SEQ_URL%/}"
ERROR_SPIKE_THRESHOLD="${ERROR_SPIKE_THRESHOLD:-10}"
SEQCLI_IMAGE="${SEQCLI_IMAGE:-docker.io/datalust/seqcli:2025.2}"
CONTAINER_RUNTIME="${CONTAINER_RUNTIME:-$(command -v podman >/dev/null && echo podman || echo docker)}"

for tool in curl jq "$CONTAINER_RUNTIME"; do
  command -v "$tool" >/dev/null || { echo "error: $tool is required" >&2; exit 1; }
done

# seqcli reads the connection from SEQCLI_CONNECTION_* so the key never appears on a command line.
seqcli() {
  "$CONTAINER_RUNTIME" run --rm -i --network host \
    -e SEQCLI_CONNECTION_SERVERURL="$SEQ_URL" -e SEQCLI_CONNECTION_APIKEY \
    "$SEQCLI_IMAGE" "$@"
}
export SEQCLI_CONNECTION_APIKEY="$SEQ_API_KEY"

seq_api() { # method path [json-body]
  curl -fsS -X "$1" -H "X-Seq-ApiKey: $SEQ_API_KEY" -H "Content-Type: application/json" \
    ${3:+--data "$3"} "$SEQ_URL/api/$2"
}

# --- signals ------------------------------------------------------------------------------------

ensure_signal() { # title description filter -> prints signal id
  local title=$1 description=$2 filter=$3 existing
  existing=$(seqcli signal list -t "$title" --json | head -n 1)
  if [[ -z $existing ]]; then
    seqcli signal create -t "$title" --description "$description" -f "$filter" --no-group >/dev/null
    existing=$(seqcli signal list -t "$title" --json | head -n 1)
    echo "signal created: $title" >&2
  else
    jq -c --arg d "$description" --arg f "$filter" \
      '.Description = $d | .Filters = [{Description: null, DescriptionIsExcluded: false, Filter: $f, FilterNonStrict: null}]' \
      <<<"$existing" | seqcli signal update --json-stdin
    echo "signal updated: $title" >&2
  fi
  jq -r .Id <<<"$existing"
}

# Log records only (spans also carry a level), at error or worse. Same level spellings as Seq's
# built-in "Errors" signal.
sig_errors=$(ensure_signal "App errors" \
  "Error/critical log events from any app (spans excluded)." \
  "not(has(@Start)) and @Level in ['e', 'err', 'eror', 'erro', 'error', 'f', 'fa', 'fat', 'ftl', 'fatal', 'c', 'crit', 'critical'] ci")

# The api answers 401 without logging (src/Api/Endpoints/SharedSecret.cs, SearchEndpoints.cs), so match its
# ASP.NET Core server spans instead.
sig_webhook=$(ensure_signal "Webhook secret rejected" \
  "401 on the api webhook/reindex endpoints: wrong or missing secret header." \
  "has(@Start) and @SpanKind = 'Server' and http.response.status_code = 401 and http.route in ['/api/webhooks/content', '/api/search/reindex']")

# SearchIndexInitializer warnings plus any Meilisearch exception (adapter or client library).
sig_search=$(ensure_signal "Search indexing failures" \
  "Background reindex failed or the index was found empty; Meilisearch errors." \
  "@MessageTemplate in ['Reindex failed; retrying in {Delay}', 'Search index is empty; rebuilding from the content source'] or (not(has(@Start)) and @Exception like '%Meilisearch%')")

# The Umbraco adapter's typed HttpClient (AddHttpClient<IContentSource, UmbracoContentSource>) logs
# under this category. 404 is a normal "page not found"; everything else >= 400 or a transport
# failure means the Delivery API is down, misconfigured (Api-Key) or erroring.
sig_delivery=$(ensure_signal "Delivery API failures" \
  "Umbraco Delivery API calls from the api that failed (transport error or HTTP status >= 400 except 404)." \
  "@Scope.name = 'System.Net.Http.HttpClient.IContentSource.LogicalHandler' and (@MessageTemplate like 'HTTP request failed%' or (StatusCode >= 400 and StatusCode <> 404))")

# --- notification apps --------------------------------------------------------------------------

ensure_app() { # package-id -> prints installed app id
  local id
  id=$(seqcli app list --json | jq -r --arg p "$1" 'select(.Package.PackageId == $p) | .Id' | head -n 1)
  if [[ -z $id ]]; then
    id=$(seqcli app install --package-id "$1" | awk '{print $1}')
    echo "app installed: $1" >&2
  fi
  echo "$id"
}

ensure_appinstance() { # title app-id NAME=VALUE... -> prints app instance id
  local title=$1 app=$2 existing settings
  shift 2
  settings=$(printf '%s\n' "$@" | jq -R 'capture("^(?<k>[^=]+)=(?<v>.*)$")' | jq -s 'map({(.k): .v}) | add')
  existing=$(seqcli appinstance list -t "$title" --json | head -n 1)
  if [[ -z $existing ]]; then
    local args=()
    for kv in "$@"; do args+=(-p "$kv"); done
    seqcli appinstance create -t "$title" --app "$app" "${args[@]}" >/dev/null
    existing=$(seqcli appinstance list -t "$title" --json | head -n 1)
    echo "app instance created: $title" >&2
  else
    jq -c --argjson s "$settings" '.Settings = (.Settings + $s)' <<<"$existing" | seqcli appinstance update --json-stdin
    echo "app instance updated: $title" >&2
  fi
  jq -r .Id <<<"$existing"
}

channels=()
if [[ -n ${ALERT_EMAILS:-} ]]; then
  : "${SMTP_HOST:?SMTP_HOST is required when ALERT_EMAILS is set}"
  : "${SMTP_FROM:?SMTP_FROM is required when ALERT_EMAILS is set}"
  mail_app=$(ensure_app Seq.App.Mail.Smtp)
  channels+=("$(ensure_appinstance "Alert email" "$mail_app" \
    "Host=$SMTP_HOST" "Port=${SMTP_PORT:-587}" "ProtocolSecurity=${SMTP_SECURITY:-RequireTls}" "Username=${SMTP_USERNAME:-}" "Password=${SMTP_PASSWORD:-}" \
    "From=$SMTP_FROM" "To=$ALERT_EMAILS")")
fi
if [[ -n ${ALERT_WEBHOOK_URL:-} ]]; then
  http_app=$(ensure_app Seq.App.HttpRequest)
  channels+=("$(ensure_appinstance "Alert webhook" "$http_app" \
    "Url=$ALERT_WEBHOOK_URL" "Method=POST" "MediaType=application/json" "BodyIsTemplate=True" \
    "Body={ {text: @m} }")")
fi

# --- alerts -------------------------------------------------------------------------------------

# The alert template is a personal alert of the calling user; the alerts below are made shared (OwnerId null).
# Personal ones with the same title (e.g. from older runs of this script) are matched too and converted.
current_user=$(seq_api GET "users/current" | jq -r .Id)
existing_alerts=$(jq -s 'add' <(seq_api GET "alerts/?shared=true") <(seq_api GET "alerts/?ownerId=$current_user"))
alert_template=$(seq_api GET "alerts/template")
channels_json=$(printf '%s\n' "${channels[@]}" | jq -R 'select(length > 0) | {NotificationAppInstanceId: .}' | jq -s .)

ensure_alert() { # title description signal-id having time-grouping suppression
  local title=$1 description=$2 signal=$3 having=$4 grouping=$5 suppression=$6 current body
  current=$(jq -c --arg t "$title" 'map(select(.Title == $t)) | first // empty' <<<"$existing_alerts")
  body=$(jq -c --arg t "$title" --arg d "$description" --arg s "$signal" --arg h "$having" \
    --arg g "$grouping" --arg x "$suppression" --argjson c "$channels_json" \
    '.OwnerId = null | .Title = $t | .Description = $d | .SignalExpression = {Kind: "Signal", SignalId: $s} | .Where = null
     | .Select = [{Value: "count(*)", Label: "count"}] | .Having = $h
     | .TimeGrouping = $g | .SuppressionTime = $x | .NotificationLevel = "Error"
     | .NotificationChannels = $c' <<<"${current:-$alert_template}")
  if [[ -z $current ]]; then
    seq_api POST "alerts/" "$body" >/dev/null
    echo "alert created: $title" >&2
  else
    seq_api PUT "alerts/$(jq -r .Id <<<"$current")" "$body" >/dev/null
    echo "alert updated: $title" >&2
  fi
}

ensure_alert "Error spike" "More than $ERROR_SPIKE_THRESHOLD error log events in 5 minutes." \
  "$sig_errors" "count > $ERROR_SPIKE_THRESHOLD" "00:05:00" "00:30:00"
ensure_alert "Webhook secret rejected" "Requests to the webhook/reindex endpoints with a wrong secret." \
  "$sig_webhook" "count > 5" "00:05:00" "01:00:00"
ensure_alert "Search indexing failing" "Reindex keeps failing (or the index keeps coming back empty)." \
  "$sig_search" "count >= 3" "00:10:00" "01:00:00"
ensure_alert "Delivery API failing" "The api cannot read content from Umbraco." \
  "$sig_delivery" "count > 0" "00:05:00" "00:30:00"

# --- server settings ----------------------------------------------------------------------------

if [[ -n ${SEQ_RETENTION:-} ]]; then
  # Replace whatever policies exist with a single delete-all-events policy of the requested age.
  policies=$(seqcli retention list --json)
  if [[ -n $policies ]]; then
    while read -r id; do seqcli retention remove -i "$id" >/dev/null; done < <(jq -r .Id <<<"$policies")
  fi
  seqcli retention create --after "$SEQ_RETENTION" --delete-all-events >/dev/null
  echo "retention: delete events after $SEQ_RETENTION" >&2
fi

if [[ ${SEQ_REQUIRE_INGEST_API_KEY:-false} == true ]]; then
  seqcli setting set -n RequireApiKeyForWritingEvents -v True
  echo "setting: ingestion requires an API key" >&2
fi

echo "done: ${#channels[@]} notification channel(s)" >&2

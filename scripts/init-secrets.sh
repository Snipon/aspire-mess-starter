#!/usr/bin/env bash
# Creates the AppHost user secrets (Parameters:*) that are still missing. Existing values are never changed, so it is
# safe to run any number of times.
#
#   scripts/init-secrets.sh                      # fill in missing secrets, print the backoffice login
#   scripts/init-secrets.sh --rotate <name>      # regenerate one parameter, e.g. --rotate delivery-api-key
#
# Needs the Aspire CLI (`aspire secret`), openssl and jq. The store is the AppHost's user secrets
# (UserSecretsId in apphost.cs, see `aspire secret path`). Local Seq runs without authentication, so it has no parameter.
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."
apphost=apphost.cs

# Aspire parameter names: ours from apphost.cs, plus the ones SQL Server and Meilisearch create (<resource>-password,
# <resource>-masterKey).
params=(umbraco-admin-email umbraco-admin-password delivery-api-key hmac-secret webhook-secret reindex-secret
  sql-password meilisearch-masterKey)

secret() { aspire secret "$@" --apphost "$apphost" --nologo --non-interactive; }

# Random alphanumeric string of length $1 with at least one lower case letter, upper case letter and digit
# (SQL Server's password policy).
alnum() {
  local s
  while :; do
    s=$(openssl rand -base64 $(($1 * 2)) | tr -dc 'A-Za-z0-9' | cut -c1-"$1")
    [[ ${#s} -eq $1 && $s =~ [a-z] && $s =~ [A-Z] && $s =~ [0-9] ]] && break
  done
  printf '%s' "$s"
}

generate() {
  case $1 in
    umbraco-admin-email) printf 'admin@example.com' ;;
    hmac-secret) openssl rand -base64 64 | tr -d '\n' ;; # base64 of 64 random bytes
    *) alnum 32 ;;
  esac
}

# Aspire names persistent containers and volumes after the SHA-256 of the lower-cased AppHost project path
# (<dir>/apphost.cs.csproj for a file-based AppHost).
path_hash=$(printf '%s' "$(pwd -P)/$apphost.csproj" | tr '[:upper:]' '[:lower:]' | openssl dgst -sha256 -r | cut -d' ' -f1)
runtime=${ASPIRE_CONTAINER_RUNTIME:-podman}

reset_commands() {
  local resource=$1
  cat <<EOF
    aspire stop
    $runtime rm -f $resource-${path_hash:0:8}
    $runtime volume rm apphost-${path_hash:0:10}-$resource-data
EOF
}

store=$(secret path)

# Before apphost.cs set an explicit UserSecretsId, the .NET SDK derived one from the AppHost path
# (apphost-<same hash>). Copy every key of that store the new one lacks, so the existing persistent containers keep
# their passwords and ports, and warn when the new store already has a different value for a container password
# (`aspire run` before this script generates new ones).
legacy="$(dirname "$(dirname "$store")")/apphost-$path_hash/secrets.json"
if [[ -s $legacy && $legacy != "$store" ]]; then
  current='{}'
  [[ -s $store ]] && current=$(jq -c . "$store")
  while IFS=$'\t' read -r key value; do
    secret set "$key" "$value" >/dev/null
    echo "  copy  $key (from $legacy)"
  done < <(jq -r --argjson cur "$current" \
    'to_entries[] | select(.key as $k | $cur | has($k) | not) | "\(.key)\t\(.value)"' "$legacy")

  for name in sql-password meilisearch-masterKey; do
    key="Parameters:$name"
    old=$(jq -r --arg k "$key" '.[$k] // empty' "$legacy")
    new=$(jq -r --arg k "$key" '.[$k] // empty' <<<"$current")
    if [[ -n $old && -n $new && $old != "$new" ]]; then
      echo "WARNING: $name differs from $legacy, whose value the existing '${name%%-*}' volume may still use." >&2
      echo "Unless you rotated it on purpose, restore it and restart the AppHost:" >&2
      echo "  aspire secret set $key \"\$(jq -r '.\"$key\"' $legacy)\" --apphost $apphost" >&2
    fi
  done
fi

if [[ ${1:-} == --rotate ]]; then
  name=${2:?usage: $0 --rotate <name>}
  [[ " ${params[*]} " == *" $name "* ]] || { echo "Unknown parameter '$name'. One of: ${params[*]}" >&2; exit 1; }

  case $name in
    sql-password | meilisearch-masterKey)
      resource=${name%%-*}
      echo "WARNING: $name is set when the persistent '$resource' container and its data volume are first created." >&2
      echo "A new value only works after removing both (this deletes the data; the search index is rebuilt by the api):" >&2
      reset_commands "$resource" >&2
      [[ $resource == sql ]] && echo "  then also clear local media, see README 'Start from a clean database'." >&2
      read -r -p "Rotate $name anyway? [y/N] " answer || true
      [[ ${answer:-} == [yY] ]] || { echo "Not rotated."; exit 1; }
      ;;
    umbraco-admin-email | umbraco-admin-password)
      echo "Note: Umbraco only uses $name for the unattended install; change it in the backoffice as well." >&2
      ;;
  esac

  secret set "Parameters:$name" "$(generate "$name")" >/dev/null
  echo "Rotated $name. Restart the AppHost to apply it."
  exit 0
elif [[ $# -gt 0 ]]; then
  echo "usage: $0 [--rotate <name>]" >&2
  exit 1
fi

for name in "${params[@]}"; do
  # `aspire secret get` exits with 10 only when the key does not exist; any other failure aborts.
  rc=0
  secret get "Parameters:$name" >/dev/null 2>&1 || rc=$?
  case $rc in
    0) echo "  keep  $name" ;;
    10)
      secret set "Parameters:$name" "$(generate "$name")" >/dev/null
      echo "  set   $name"
      ;;
    *) echo "aspire secret get Parameters:$name failed (exit $rc)" >&2; exit 1 ;;
  esac
done

echo
echo "Secrets: $store"
echo "Umbraco backoffice: https://localhost:44350/umbraco"
echo "  email:    $(secret get Parameters:umbraco-admin-email)"
echo "  password: $(secret get Parameters:umbraco-admin-password)"

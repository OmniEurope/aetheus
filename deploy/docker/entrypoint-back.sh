#!/bin/sh
# SPDX-License-Identifier: EUPL-1.2
# SPDX-FileCopyrightText: 2026 Aetheus contributors
set -e

echo "=== Aetheus Backend Entrypoint ==="

# Generate a long-lived self-signed certificate to encrypt Data Protection keys
# at rest, on first start. The cert lives in the persistent dp-keys volume so
# it survives container recreations alongside the keys it protects.
DP_DIR="${DataProtection__KeyPath:-/app/data/dp-keys}"
DP_CERT="${DP_DIR}/dp-cert.pfx"
mkdir -p "$DP_DIR"
if [ ! -f "$DP_CERT" ]; then
    echo ">>> Generating Data Protection encryption certificate..."
    openssl req -x509 -newkey rsa:2048 -nodes -days 36500 \
        -keyout /tmp/dp.key -out /tmp/dp.crt \
        -subj "/CN=Aetheus Data Protection" >/dev/null 2>&1
    openssl pkcs12 -export -out "$DP_CERT" \
        -inkey /tmp/dp.key -in /tmp/dp.crt \
        -password pass: >/dev/null 2>&1
    chmod 600 "$DP_CERT"
    rm -f /tmp/dp.key /tmp/dp.crt
    echo ">>> Data Protection certificate created"
fi

# Run EF migrations only in the explicit migration job. Application replicas
# must never race each other during a blue-green overlap.
# efbundle is idempotent: it exits 0 when the database is already up to date
# ("No migrations were applied"), so a non-zero exit is ALWAYS a real failure
# (e.g. an FK violation in a migration). Aborting here surfaces the failure in
# the migration step instead of masking it and crash-looping the API against a
# half-migrated schema.
if [ "${AETHEUS_RUN_MIGRATIONS:-false}" = "true" ] && [ -f /app/efbundle ]; then
    echo ">>> Running EF migrations..."
    if /app/efbundle --connection "$ConnectionStrings__Default"; then
        echo ">>> Migrations complete"
    else
        _ef_rc=$?
        echo "FATAL: EF migrations failed (exit ${_ef_rc}). Refusing to start the API" >&2
        echo "       against a half-migrated database. Inspect the error above and" >&2
        echo "       fix the migration before redeploying." >&2
        exit "$_ef_rc"
    fi
fi

if [ "${AETHEUS_MIGRATE_ONLY:-false}" = "true" ]; then
    if [ "${AETHEUS_RUN_MIGRATIONS:-false}" != "true" ]; then
        echo "FATAL: AETHEUS_MIGRATE_ONLY requires AETHEUS_RUN_MIGRATIONS=true" >&2
        exit 64
    fi
    echo ">>> Migration-only job complete"
    exit 0
fi

echo ">>> Starting Aetheus API..."
exec dotnet Aetheus.Back.dll

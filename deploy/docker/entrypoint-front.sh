#!/bin/sh
# SPDX-License-Identifier: EUPL-1.2
# SPDX-FileCopyrightText: 2026 Aetheus contributors
set -e

# StaticServer applies API_BASE_URL and APP_VERSION through System.Text.Json before serving files.
# Keeping dynamic values out of sed prevents replacement metacharacters from corrupting JSON.
exec dotnet StaticServer.dll

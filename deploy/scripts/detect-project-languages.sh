#!/bin/sh
# SPDX-License-Identifier: EUPL-1.2
set -eu

PROJECT_ROOT="${1:-.}"
test -d "$PROJECT_ROOT" || {
    echo "Language detection root does not exist: $PROJECT_ROOT" >&2
    exit 2
}

# tests/security-rules holds the annotated corpus of the house OpenGrep rules (`# ruleid:` / `# ok:`
# samples, never run or compiled). It is not product code in any language, so it must not switch a
# language lint on by itself.
find_product_file() {
    find "$PROJECT_ROOT" \
        \( -type d \( \
            -name .git -o -name .claude -o -name .codex \
            -o -name bin -o -name obj -o -name node_modules -o -name .venv \
            -o -name target -o -name build -o -name TestResults \
            -o -name .analysis-coverage -o -name coverage \
            -o -path '*/tests/security-rules' \
        \) -prune \) -o \
        \( "$@" -type f -print -quit \)
}

has_product_file() {
    find_product_file "$@" | grep -q .
}

HAS_DOTNET=false
HAS_JAVASCRIPT=false
HAS_PYTHON=false
HAS_JAVA=false

if has_product_file -name '*.csproj'; then
    HAS_DOTNET=true
fi
if has_product_file \( -name '*.js' -o -name '*.jsx' -o -name '*.ts' -o -name '*.tsx' \); then
    HAS_JAVASCRIPT=true
fi
if has_product_file -name '*.py'; then
    HAS_PYTHON=true
fi
if has_product_file -name '*.java'; then
    HAS_JAVA=true
fi

printf '##aetheus[setvariable name=HAS_DOTNET]%s\n' "$HAS_DOTNET"
printf '##aetheus[setvariable name=HAS_JAVASCRIPT]%s\n' "$HAS_JAVASCRIPT"
printf '##aetheus[setvariable name=HAS_PYTHON]%s\n' "$HAS_PYTHON"
printf '##aetheus[setvariable name=HAS_JAVA]%s\n' "$HAS_JAVA"

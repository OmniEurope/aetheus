# SPDX-License-Identifier: EUPL-1.2
#
# Sourced, never run: derives the names of the disposable QA stack from the project that owns the
# run, so a second project copying the QA pipeline gets its own stacks and database without editing
# a script (PLAN-006 lot 10). For the Aetheus project every derived value is the literal it replaces:
# APPNAME aetheus, database aetheus_e2e, user aetheus, stack aetheus-qa-rollback-<run>.
#
# Inputs, read from the environment:
#   BUILD_PROJECTNAME  the project name the control plane injects into every run (required)
#
# Defines QA_APP, QA_DB_NAME, QA_DB_USER and the function qa_stack <run id>.
QA_APP="$(printf '%s' "${BUILD_PROJECTNAME:-}" | tr '[:upper:]' '[:lower:]' \
  | sed -e 's/[^a-z0-9][^a-z0-9]*/-/g' -e 's/^-//' -e 's/-$//')"
if [ -z "$QA_APP" ]; then
  echo "BUILD_PROJECTNAME is missing or has no usable character; the QA stack cannot be named." >&2
  exit 1
fi
# PostgreSQL identifiers start with a letter; a project named "2048" still gets a valid role.
case "$QA_APP" in [a-z]*) QA_DB_USER="$(printf '%s' "$QA_APP" | tr '-' '_')" ;;
  *) QA_DB_USER="p_$(printf '%s' "$QA_APP" | tr '-' '_')" ;;
esac
QA_DB_NAME="${QA_DB_USER}_e2e"
qa_stack() { printf '%s-qa-rollback-%s' "$QA_APP" "$1"; }

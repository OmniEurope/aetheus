#!/bin/sh
# Reject destructive schema operations in the Up() method of pending EF migrations.
set -eu

if [ "$#" -eq 0 ]; then
  echo ">>> No pending EF migration requires an expand-contract check."
  exit 0
fi

VIOLATIONS=""
for migration_file in "$@"; do
  [ -f "$migration_file" ] || {
    echo "Pending migration file is missing: $migration_file" >&2
    exit 1
  }

  SQL_LINE="$(grep -n 'migrationBuilder[[:space:]]*\.[[:space:]]*Sql[[:space:]]*(' "$migration_file" | head -1 | cut -d: -f1 || true)"
  if [ -n "$SQL_LINE" ]; then
    REVIEW_LINE="$(grep -n 'AETHEUS_EXPAND_CONTRACT_SQL_REVIEWED:' "$migration_file" | head -1 | cut -d: -f1 || true)"
    if [ -z "$REVIEW_LINE" ] || [ "$REVIEW_LINE" -ge "$SQL_LINE" ]; then
      VIOLATIONS="${VIOLATIONS}${migration_file}: migrationBuilder.Sql(...) requires a preceding AETHEUS_EXPAND_CONTRACT_SQL_REVIEWED rationale.
"
    fi
  fi

  MATCHES="$(awk '
    /protected override void Up[[:space:]]*\(MigrationBuilder migrationBuilder\)/ {
      in_up = 1
      saw_up = 1
      next
    }
    /protected override void Down[[:space:]]*\(MigrationBuilder migrationBuilder\)/ {
      in_up = 0
    }
    in_up && /migrationBuilder\.(Drop(Column|Table|ForeignKey|PrimaryKey|UniqueConstraint|CheckConstraint|Index|Sequence)|Rename(Column|Table|Index|Sequence)|AlterColumn)(<[^>]+>)?[[:space:]]*\(/ {
      print FILENAME ":" FNR ": " $0
    }
    END {
      if (!saw_up) exit 2
    }
  ' "$migration_file")" || {
    echo "Could not inspect the Up() method in pending migration: $migration_file" >&2
    exit 1
  }

  if [ -n "$MATCHES" ]; then
    VIOLATIONS="${VIOLATIONS}${MATCHES}
"
  fi
done

if [ -n "$VIOLATIONS" ]; then
  echo "Destructive/non-expand pending migration detected. Ship expand and contract in separate releases:" >&2
  printf '%s' "$VIOLATIONS" >&2
  exit 1
fi

echo ">>> Every pending EF migration is expand-compatible."

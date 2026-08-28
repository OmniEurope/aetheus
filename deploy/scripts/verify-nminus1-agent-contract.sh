#!/usr/bin/env sh
# SPDX-License-Identifier: EUPL-1.2
set -eu

backend_url="${1:?backend URL is required}"
admin_password_file="${2:?admin password file is required}"
agent_release_directory="${3:?N-1 agent release directory is required}"
expected_commit="${4:?N-1 commit is required}"
evidence_path="${5:?evidence output path is required}"
node_binary="${6:-node}"

case "$backend_url" in
  http://127.0.0.1:*|http://localhost:*) ;;
  *) echo "N-1 agent proof is restricted to a loopback QA backend." >&2; exit 2 ;;
esac
case "$expected_commit" in
  *[!0-9a-fA-F]*|'') echo "N-1 commit must be hexadecimal." >&2; exit 2 ;;
esac
test -s "$admin_password_file"
test -s "$agent_release_directory/agent-release-manifest.json"

temporary_root="$(mktemp -d "${TMPDIR:-/tmp}/aetheus-agent-nminus1.XXXXXX")"
agent_pid=""
cleanup() {
  if [ -n "$agent_pid" ]; then
    kill "$agent_pid" >/dev/null 2>&1 || true
    wait "$agent_pid" >/dev/null 2>&1 || true
  fi
  rm -rf "$temporary_root"
}
trap cleanup EXIT HUP INT TERM

archive_name="$("$node_binary" - "$agent_release_directory" "$expected_commit" <<'NODE'
const crypto = require("node:crypto");
const fs = require("node:fs");
const path = require("node:path");
const [directory, expectedCommit] = process.argv.slice(2);
const manifest = JSON.parse(fs.readFileSync(path.join(directory, "agent-release-manifest.json"), "utf8"));
if (manifest.commit !== expectedCommit) throw new Error("N-1 agent manifest commit mismatch.");
if (manifest.protocolVersion !== 2) {
  throw new Error("N-1 software agent does not implement the current backend protocol.");
}
const archive = manifest.archives.find(item =>
  item.platform === "linux" && item.architecture === "x64");
if (!archive) throw new Error("N-1 Linux agent archive is missing.");
const bytes = fs.readFileSync(path.join(directory, archive.fileName));
const sha = crypto.createHash("sha256").update(bytes).digest("hex");
if (bytes.length !== archive.sizeBytes || sha !== archive.sha256.toLowerCase()) {
  throw new Error("N-1 Linux agent archive integrity mismatch.");
}
process.stdout.write(archive.fileName);
NODE
)"
archive_path="$agent_release_directory/$archive_name"
tar -xzf "$archive_path" -C "$temporary_root"
archive_sha="$(sha256sum "$archive_path" | cut -d ' ' -f 1)"

admin_password="$(cat "$admin_password_file")"
printf '{"username":"admin","password":"%s"}' "$admin_password" |
  curl -fsS -H 'Content-Type: application/json' --data-binary @- \
    "$backend_url/api/auth/login" > "$temporary_root/login.json"
admin_token="$("$node_binary" -e \
  'process.stdout.write(JSON.parse(require("node:fs").readFileSync(process.argv[1],"utf8")).token)' \
  "$temporary_root/login.json")"
test -n "$admin_token"

printf '{"expirationHours":1}' |
  curl -fsS -H 'Content-Type: application/json' \
    -H "Authorization: Bearer $admin_token" --data-binary @- \
    "$backend_url/api/auth/registration-tokens" > "$temporary_root/registration-token.json"
registration_token="$("$node_binary" -e \
  'process.stdout.write(JSON.parse(require("node:fs").readFileSync(process.argv[1],"utf8")).token)' \
  "$temporary_root/registration-token.json")"
registration_token_id="$("$node_binary" -e \
  'process.stdout.write(String(JSON.parse(require("node:fs").readFileSync(process.argv[1],"utf8")).id))' \
  "$temporary_root/registration-token.json")"

write_evidence() {
  status="$1"
  enrollment="$2"
  claim="$3"
  publication="$4"
  evidence_server_id="${5:-0}"
  evidence_task_id="${6:-0}"
  mkdir -p "$(dirname "$evidence_path")"
  "$node_binary" - "$evidence_path" "$expected_commit" "$archive_name" "$archive_sha" \
    "$status" "$enrollment" "$claim" "$publication" "$evidence_server_id" "$evidence_task_id" <<'NODE'
const fs = require("node:fs");
const [output, commit, archiveName, archiveSha256, status, enrollment, claim, publication, serverId, taskId] = process.argv.slice(2);
fs.writeFileSync(output, `${JSON.stringify({
  schema: 1,
  mode: "NMinusOne",
  status,
  previousAgentCommit: commit,
  archiveName,
  archiveSha256,
  proofs: {
    enrollmentAndHeartbeat: enrollment === "true",
    taskClaimAndStart: claim === "true",
    taskResultPublication: publication === "true"
  },
  serverId: Number(serverId) || null,
  taskId: Number(taskId) || null
}, null, 2)}\n`);
NODE
}

fail_compatibility() {
  message="$1"
  enrollment="$2"
  claim="$3"
  publication="$4"
  cat "$temporary_root/agent.log" >&2 || true
  write_evidence Failed "$enrollment" "$claim" "$publication" "${server_id:-0}" "${task_id:-0}"
  echo "$message" >&2
  exit 10
}

cat > "$temporary_root/appsettings.json" <<JSON
{
  "Aetheus": {
    "ServerUrl": "$backend_url",
    "Name": "qa-agent-nminus1-${registration_token_id}",
    "RegistrationToken": "$registration_token",
    "WorkDirectory": "$temporary_root/work",
    "PollingIntervalSeconds": 1,
    "HeartbeatIntervalSeconds": 1,
    "HeartbeatCollectionTimeoutSeconds": 10,
    "MaxConcurrentTasks": 1
  }
}
JSON

if [ -x "$temporary_root/Aetheus.Agent.Linux" ]; then
  (cd "$temporary_root" && ./Aetheus.Agent.Linux) > "$temporary_root/agent.log" 2>&1 &
else
  (cd "$temporary_root" && dotnet Aetheus.Agent.Linux.dll) > "$temporary_root/agent.log" 2>&1 &
fi
agent_pid=$!

server_id=""
attempt=0
while [ "$attempt" -lt 60 ]; do
  attempt=$((attempt + 1))
  curl -fsS -H "Authorization: Bearer $admin_token" \
    "$backend_url/api/auth/registration-tokens/$registration_token_id" \
    > "$temporary_root/token-status.json"
  server_id="$("$node_binary" -e \
    'const x=JSON.parse(require("node:fs").readFileSync(process.argv[1],"utf8")); process.stdout.write(x.usedByServerId ? String(x.usedByServerId) : "")' \
    "$temporary_root/token-status.json")"
  [ -n "$server_id" ] && break
  sleep 1
done
if [ -z "$server_id" ]; then
  fail_compatibility "The retained N-1 agent did not enroll and heartbeat against backend N." false false false
fi

printf '{"serverId":%s,"name":"N-1 protocol proof","command":"echo NMinusOneProtocol","executor":0,"timeoutSeconds":60}' \
  "$server_id" |
  curl -fsS -H 'Content-Type: application/json' \
    -H "Authorization: Bearer $admin_token" --data-binary @- \
    "$backend_url/api/tasks" > "$temporary_root/task.json"
task_id="$("$node_binary" -e \
  'process.stdout.write(String(JSON.parse(require("node:fs").readFileSync(process.argv[1],"utf8")).id))' \
  "$temporary_root/task.json")"

task_status=""
attempt=0
while [ "$attempt" -lt 60 ]; do
  attempt=$((attempt + 1))
  curl -fsS -H "Authorization: Bearer $admin_token" \
    "$backend_url/api/tasks/$task_id" > "$temporary_root/task-status.json"
  task_status="$("$node_binary" -e \
    'process.stdout.write(String(JSON.parse(require("node:fs").readFileSync(process.argv[1],"utf8")).status))' \
    "$temporary_root/task-status.json")"
  case "$task_status" in
    3|Success) break ;;
    4|5|6|Failed|Timeout|Cancelled)
      fail_compatibility "The retained N-1 agent failed its claim/start/complete proof: $task_status" true true false
      ;;
  esac
  sleep 1
done
case "$task_status" in 3|Success) ;; *)
  fail_compatibility "The retained N-1 agent did not publish a successful task result." true true false
esac

write_evidence Passed true true true "$server_id" "$task_id"

echo "Retained N-1 agent contract passed: heartbeat, claim/start and result publication."

#!/usr/bin/env bash
# Rehydrate dotnet user-secrets from the sops-encrypted copy in this repo.
#
#   ./scripts/restore-secrets.sh          write ~/.microsoft/usersecrets/<id>/
#   ./scripts/restore-secrets.sh --check  compare, change nothing
#
# Needs an age key that is a recipient in .sops.yaml -- the primary key at
# ~/.config/sops/age/keys.txt on a machine you use, or the offline recovery key
# via SOPS_AGE_KEY_FILE.

set -Eeuo pipefail

root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
enc=$root/secrets/user-secrets.json
csproj=$root/src/QrSimple.Api/QrSimple.Api.csproj

die() { printf 'restore-secrets: %s\n' "$*" >&2; exit 1; }

command -v sops >/dev/null || die 'sops is required -- mise install'
command -v jq   >/dev/null || die 'jq is required'
[[ -f $enc ]] || die "missing $enc"

# The UserSecretsId is the csproj's, never a copy that can drift.
id=$(grep -oE '<UserSecretsId>[^<]+</UserSecretsId>' "$csproj" \
     | sed 's/<[^>]*>//g') || true
[[ -n $id ]] || die "no <UserSecretsId> in $csproj"

dest=$HOME/.microsoft/usersecrets/$id/secrets.json

if [[ ${1:-} == --check ]]; then
    [[ -f $dest ]] || { echo "absent: $dest"; exit 1; }
    if diff -q <(sops --decrypt "$enc" | jq -S 'del(.sops)') \
               <(sed '1s/^\xEF\xBB\xBF//' "$dest" | jq -S .) >/dev/null; then
        echo "in sync: $dest"
    else
        echo "DRIFT: $dest differs from secrets/user-secrets.json"; exit 1
    fi
    exit 0
fi

install -d -m 700 "$(dirname "$dest")"
umask 077
# Write without a BOM. dotnet reads BOM-less JSON fine; sops cannot parse a BOM,
# which is why the encrypted copy has none either.
sops --decrypt "$enc" | jq -S 'del(.sops)' > "$dest"
chmod 600 "$dest"
printf 'wrote %s (%s keys)\n' "$dest" "$(jq -r 'keys | length' "$dest")"

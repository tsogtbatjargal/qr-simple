#!/usr/bin/env bash
# VS Code is a Flatpak; its sandbox can't execute the host's /usr/bin/podman
# directly (missing shared libs against the sandbox's own linker). flatpak-spawn
# --host actually runs the command on the host instead of through the sandbox's
# bind-mounted view of it, which works. Used as dev.containers.dockerPath so
# the Dev Containers extension talks to podman instead of a nonexistent docker.
#
# Dev Containers starts `podman events` only a few milliseconds before
# `podman run` and waits for the resulting start event. Crossing the Flatpak
# portal can take long enough for the subscriber to miss that event, leaving
# VS Code waiting forever even though the container is already running. Replay
# events from the instant this wrapper was entered to close that race.
if [[ ${1-} == "events" ]]; then
  event_since=$(date --iso-8601=seconds)
  exec flatpak-spawn --host podman "$@" --since "$event_since"
fi

exec flatpak-spawn --host podman "$@"

#!/usr/bin/env bash
# VS Code is a Flatpak; its sandbox can't execute the host's /usr/bin/podman
# directly (missing shared libs against the sandbox's own linker). flatpak-spawn
# --host actually runs the command on the host instead of through the sandbox's
# bind-mounted view of it, which works. Used as dev.containers.dockerPath so
# the Dev Containers extension talks to podman instead of a nonexistent docker.
exec flatpak-spawn --host podman "$@"

# Security and privacy

Didanihaa is designed for a trusted local network. It is not an Internet-facing media hosting service.

## Current trust model

- The Control gateway listens on the LAN. Administration and the viewer share its origin.
- Administration currently has no login or per-user authorization. A trusted LAN client can access management APIs and obtain a mutation token.
- Host/IP validation, same-origin checks and separate mutation tokens help prevent unintended browser-origin requests. They do not authenticate users on the network.
- Media listens on loopback only. Public media and thumbnail endpoints check publication and availability; physical source paths are not part of their public contracts.
- Windows Service operations use a short-lived UAC helper. Firewall configuration is a separate, scoped operation; build/start does not apply it.

Do not port-forward the gateway or expose it through a public reverse proxy. Access control for untrusted networks requires additional design and validation.

## Local data

SQLite contains source paths, media names, metadata, publication choices, favorites and aggregate playback counts. Configuration and logs can reveal local paths and network details. Treat runtime data/, publish/, temp/, local configuration and development notes as private.

The application does not send analytics or media to an external service. Build and dependency-audit tools may contact public package registries. FFmpeg/FFprobe are optional local executables supplied and maintained by the operator.

An existing personal publish/ folder is not a clean public release artifact. Share binaries only from a clean build with fresh example configuration and no personal catalog, source folders, logs, thumbnails or credentials.

## Reporting a vulnerability

When the repository's private vulnerability reporting is enabled, use **Security → Report a vulnerability**. Do not post a secret, private media, machine path or exploit containing personal data in a public issue.

If private reporting is unavailable, open an issue containing only a request for a private contact channel. The maintainer should enable private reporting before accepting detailed security reports.

This project is under active development. Unit tests use isolated catalogs; real TV acceptance and interactive Windows Service/UAC/NTFS/reboot behavior require separate validation.

# Security policy

Miller Driver Updater runs as administrator and installs drivers and programs, so security reports are taken seriously.

## Supported versions

Only the latest release gets fixes. The app offers new releases itself (the Update banner), so staying current is one click.

## Reporting a vulnerability

Please **do not open a public issue**. Use GitHub's private reporting instead:
**Security** tab of this repository → **Report a vulnerability**.

Include what you found, how to reproduce it, and the app version (bottom-right corner of the window). You will get an
answer as soon as possible; fixes ship as a new release, and the report is credited unless you prefer otherwise.

## What is in scope

- Anything that makes the app run, install or download something the person did not choose, or from a source other
  than Windows Update, winget, nvidia.com and this repository's releases.
- The self-update path: GitHub release lookup, SHA-256 check against GitHub's asset digest, Setup `/UPDATE`.
- The NVIDIA path: download host check (`*.nvidia.com`) and the NVIDIA Corporation Authenticode check before running.
- Elevation: the app asks for administrator rights once at launch; anything that lets another program use them.
- Files the app writes under `%LOCALAPPDATA%\Miller Driver Updater` (reports, logs, driver backups).

## How releases can be checked

Each release lists `SHA256SUMS.txt`, and GitHub shows a SHA-256 digest for every file. The app itself refuses an
update whose checksum does not match GitHub's. Releases are not code-signed yet, so Windows SmartScreen and some
antivirus heuristics warn about them.

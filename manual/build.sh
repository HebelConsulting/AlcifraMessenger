#!/usr/bin/env bash
# Builds the module manual REPRODUCIBLY: the PDF's timestamp is pinned to the last commit that touched the manual's
# source, so the same source always yields the same bytes.
#
# Why that matters: the PDF ships INSIDE the module package, and the core files it into each tenant — adding a NEW
# VERSION of the manual document whenever the bytes differ (core ADR 0891). A build stamped with "now" would make
# every module release file a "changed" manual on every tenant, with nothing actually changed.
#
# Fonts are Typst's bundled ones (template.typ is deliberately font-safe), so a Linux runner and a Mac agree.
# CI (manual.yml) runs this and fails when the committed PDF is not what the source builds — the committed PDF had
# drifted behind its source once already.
set -euo pipefail
cd "$(dirname "$0")"
SOURCE_DATE_EPOCH="$(git log -1 --format=%ct -- manual.typ template.typ colors.typ)"
export SOURCE_DATE_EPOCH
# --ignore-system-fonts: Typst prefers an installed system font over its bundled copy, and an Ubuntu runner HAS
# DejaVu installed — so without this flag Linux embedded a different DejaVu build than a Mac and the bytes differed
# (found by this repository's own CI on its first run). Bundled fonts only, identical everywhere.
typst compile --ignore-system-fonts manual.typ "${1:-SimplArchive-Messenger-Module-Manual.pdf}"

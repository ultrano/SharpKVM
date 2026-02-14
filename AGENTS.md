# AGENTS.md

## Purpose
This repository includes implementation-based specification documents.
Agents should use them as the primary reference when tasks require current behavior understanding.

## Primary References
- Detailed spec: `docs/SharpKVM_상세명세서.md`
- UI layout image (window): `docs/images/ui-layout-main.svg`
- UI layout image (stage/dock/snap/free): `docs/images/ui-layout-stage.svg`

## When To Use
Use the spec and UI layout files first when the user asks for:
- feature analysis or current behavior explanation
- requirements/specification writing or updates
- UI layout/flow explanation
- test case or QA checklist derivation from current behavior
- onboarding/context summary of this project

## Agent Guidance
1. Prefer these docs before re-deriving from source files, unless code has changed after the docs were written.
2. If code and docs differ, treat code as source of truth and update the docs in the same task.
3. When producing outputs, cite these file paths explicitly.
4. Keep both SVG layout files in sync with major UI structure changes.


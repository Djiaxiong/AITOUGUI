# AITOUGUI

**[简体中文](README.md) | English**

`AITOUGUI` is an experimental Unity UGUI project for AI-assisted UI generation and landing workflows.  
This repository contains two main parts:

- an AI-side workflow and Python toolchain for generating site/page packages
- a Unity `AIToUGUI Lite` plugin that parses `compiled_site_bundle.json` and builds preview hierarchies in-scene

## Project Positioning

What is published here is a **Lite, runnable, readable, extensible version** of the pipeline. It is meant to show the overall architecture, data contracts, and the basic Unity landing path.

The internal full version used in real production depends on some **paid plugins** and more complete runtime capabilities. Because of that, the Unity plugin included in this repository is not the full commercial version. It is a dependency-reduced Lite build that can run independently.

That distinction matters:

- `AIToUGUI Lite` is primarily for **parsing and preview**
- it keeps the basic UGUI/TMP construction path
- but some higher-end visuals, adapters, and runtime behaviors from the full version are **not included here**

## What The Lite Version Is

The Lite version is not intended to reproduce the full version 1:1. Its purpose is to provide a minimal usable loop without relying on paid assets:

1. generate a site package on the AI side
2. load `compiled_site_bundle.json` on the Unity side
3. build UGUI/TMP preview hierarchies under a preview mount in scene
4. inspect page structure, text, base layout, base controls, and part of the visual information

Based on the current code and tests, the Lite version explicitly stays away from several full-version dependencies, including but not limited to:

- advanced shadow and glow effects
- shape adapters and richer visual wrappers
- animation binders and page entrance motion
- some page-level runtime components used by the full version
- presentation-layer features tied to third-party paid plugins

In practice, this means:

- page **structure** is usually inspectable
- page **base layout** is usually inspectable
- page **text and basic controls** are usually inspectable
- page **final polish and full effects** are not equivalent to the production version

If your goal is to evaluate the workflow, data protocol, and Unity-side landing approach, this repository is enough.  
If your goal is to reproduce the full commercial-quality output directly, treat this as a constrained demonstration build.

## Included Content

### 1. AI Workflow And Toolchain

Directory: `Assets/AI_UGUI_Creator/ai-create-ugui/`

Included:

- generation specs
- checklists
- usage docs
- site package contract docs
- Python tools

These parts are responsible for turning UI planning, page HTML/CSS expression, intermediate structure, and validation into a site package that Unity can consume.

Key documents:

- [Assets/AI_UGUI_Creator/ai-create-ugui/使用指南.md](H:/AIProject/AITOUGUI/Assets/AI_UGUI_Creator/ai-create-ugui/使用指南.md)
- [Assets/AI_UGUI_Creator/ai-create-ugui/SKILL.md](H:/AIProject/AITOUGUI/Assets/AI_UGUI_Creator/ai-create-ugui/SKILL.md)

### 2. Unity Lite Plugin

Directory: `Assets/AI_UGUI_Creator/AIToUGUI_LITE/`

Included:

- `Runtime/`: Lite contracts, parser, preview mount components
- `Editor/`: Unity editor window and preview building logic
- `Tests/`: basic editor tests

Unity menu entry:

- `Tools/AIToUGUI/Lite Studio`

Main uses of Lite Studio:

- load `compiled_site_bundle.json`
- parse multi-page bundles
- build one selected page or all pages for preview
- attach generated preview content under `AIToUGUILitePreviewMount` in scene

## Sample Content

Directory: `Assets/Examples/`

The repository currently includes several example site packages and outputs for different UI styles and page structures, such as:

- `FPSLoadout_SkillGenerated`
- `FPSOperatorSemanticUI`
- `SunnyPopTrails_PortraitSuite`

These examples usually contain:

- `source/` original page and planning files
- `compiled_site_bundle.json` compiled site package
- `preview.html` HTML preview
- `reports/` validation and repair reports
- `snapshots/` compiled snapshots

## Use Cases

This repository is a better fit for:

- studying the intermediate protocol between AI generation and Unity UGUI
- reading a simplified implementation from page description to Unity preview generation
- building your own Lite prototype for a fuller internal tool
- referencing a multi-page bundle parsing and preview workflow
- understanding how HTML/CSS-like expression is constrained into Unity-friendly data structures

## Environment

- Unity `2022.3.62f2c1`
- main packages used:
  - `com.unity.ugui`
  - `com.unity.textmeshpro`
  - `com.unity.vectorgraphics`
  - `com.unity.test-framework`

## Quick Start

1. Open the project with Unity `2022.3.62f2c1`
2. Wait for packages to finish importing
3. Open a test scene, or prepare your own scene with a `Canvas`
4. Create an object with `AIToUGUILitePreviewMount` attached
5. Open `Tools/AIToUGUI/Lite Studio`
6. Assign a `compiled_site_bundle.json`
7. Click `Parse JSON`, then use `Parse All` or `Parse Selected`

## Repository Layout

- `Assets/AI_UGUI_Creator/AIToUGUI_LITE/`: Unity Lite plugin
- `Assets/AI_UGUI_Creator/ai-create-ugui/`: AI workflow, specs, Python tools
- `Assets/Examples/`: example site packages and preview outputs
- `Assets/Scenes/`: Unity scenes
- `Packages/`: Unity package dependencies
- `ProjectSettings/`: project configuration

## Known Boundaries

- this is not the full commercial plugin
- this is not a full-fidelity effects build
- the visual output in examples does not represent the final internal production version
- some full-version components, render helpers, and third-party plugin-based features were intentionally removed

So if the GitHub preview looks weaker than the internal full version, that is expected.  
The reason is not a broken data pipeline. The public repository is intentionally constrained to a distributable Lite scope.

## Notes

- Unity local cache folders such as `Library/`, `Logs/`, and `UserSettings/` are excluded
- the root README is repository-level documentation; deeper module docs live under `Assets/AI_UGUI_Creator/`
- if this repository is later made public-facing, it would benefit from one more pass on screenshots, About text, and Topics

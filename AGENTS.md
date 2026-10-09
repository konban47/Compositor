# Notes for AI agents

This fork maintains Compositor for Windows in `windows/` (.NET 10, Avalonia, Skia). The original macOS Swift/AppKit sources are retained for upstream reference.

## Windows development

- Build: `dotnet build windows/Compositor.slnx -c Release -warnaserror`.
- Core tests: `dotnet test windows/tests/Compositor.Core.Tests -c Release`.
- UI regression: run the Desktop DLL with `--interaction-checks screenshot.png` and `--windows-checks screenshot.png`, using `COMPOSITOR_LANGUAGE=en` and `zh-CN`. Use a temporary `COMPOSITOR_SETTINGS_DIR`.
- Put UI translations in `windows/src/Compositor.Desktop/Locales/zh-CN.json`. Keep project fields, shortcut identifiers, imported names and user text stable.
- Package: `windows/scripts/package.ps1`; Inno Setup 6 is required only with `-Installer`.
- Document upstream parity and platform limitations in `windows/README.md` and the release update notes. Do not claim Windows 10 manual testing from Windows Server CI.

## Designing or editing a Compositor project

If you've been asked to make or change an image in a `.comp` project, you don't need the app's source code. Read [docs/writing-comp-files.md](docs/writing-comp-files.md): it covers the file format, the rules that make a project load, and how to write it safely while it's open, so the person can watch the canvas update as you work.

## Working on the app itself

- Build: open `Compositor.xcodeproj` and run the **Compositor** scheme, or `xcodebuild -project Compositor.xcodeproj -scheme Compositor -destination 'platform=macOS' build`.
- Tests: the `CompositorTests` target (`xcodebuild ... test -only-testing:CompositorTests`). CI runs these on every push.
- Match the surrounding code: its naming, its comment style and density.
- American spelling in code, comments and UI ("color", not "colour").
- The project file format is described in [docs/project-format.md](docs/project-format.md). A change to what's saved means a format version bump there and in `ProjectManifest.current`.

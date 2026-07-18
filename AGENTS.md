# Repository Guidelines

## Project Structure & Module Organization

- `linux_installer.sln` contains one .NET 9 Avalonia desktop project: `LinuxInstaller/LinuxInstaller.csproj`.
- `LinuxInstaller/Views/` and `ViewModels/` implement the MVVM UI; keep matching names such as `DistroPickerView.axaml` and `DistroPickerViewModel.cs`.
- Domain data belongs in `Models/`; Windows, disk, navigation, download, and configuration logic belongs in `Services/`.
- Shared controls, converters, styles, fonts, and images live under `UserControls/`, `Converters/`, `Themes/`, and `Assets/`.
- `prebuilt/` contains boot and installer configuration templates. There is currently no test project.

## Build, Test, and Development Commands

- `dotnet restore linux_installer.sln` — restore NuGet dependencies.
- `dotnet build linux_installer.sln --no-restore` — compile the solution; resolve new warnings introduced by your change.
- `dotnet run --project LinuxInstaller/LinuxInstaller.csproj` — launch the desktop app locally on Windows.
- `dotnet test linux_installer.sln --no-build` — run automated tests once a test project is added; the current solution discovers none.

Use the .NET 9 SDK. No repository-specific formatter or analyzer configuration is committed, so match nearby code and keep formatting changes focused.

## Coding Style & Naming Conventions

- Use four spaces in C#, file-scoped namespaces, nullable-safe code, and concise methods.
- Use `PascalCase` for types, properties, methods, and public members; use `_camelCase` for private fields.
- Suffix asynchronous methods with `Async`. Keep UI behavior in view models and operating-system work in services.
- Register dependencies in `SplatRegistrations.cs`; avoid constructing services directly in views.

## Testing Guidelines

- Add future tests in `LinuxInstaller.Tests/`, mirror production namespaces, and name files `{TypeName}Tests.cs`.
- For UI changes, manually exercise navigation, validation, and the affected workflow. Include screenshots for visible changes.
- Test disk, EFI, BCD, and partition behavior only in a disposable virtual machine or behind mocks/fakes.

## Commit & Pull Request Guidelines

- Follow the history's imperative, Conventional Commit-style subjects, for example `feat: add distro validation` or `refactor: simplify navigation`.
- Pull requests should explain scope, link relevant issues, list validation commands, and call out known limitations or new warnings.
- AI-authored commits must include `Co-Authored-By: <model name and attribution email>`.

## Security & Configuration Tips

- Never commit credentials, machine-specific disk identifiers, or real destructive commands in `prebuilt/`.
- Treat `DiskpartService` and `BootManagerService` as privileged code; preserve dry-run behavior until changes are explicitly reviewed.

# Contributing to Companella!

Thank you for your interest in contributing to Companella! This document provides guidelines and information for contributors.

Companella! is an osu! mania helper tool that provides features like rate changing, BPM analysis, session tracking, skill analysis, and more.

For questions or discussions, please use [GitHub Issues](https://github.com/your-repo/companella/issues).

---

## Table of Contents

- [Code of Conduct](#code-of-conduct)
- [Getting Started](#getting-started)
- [Branching Strategy](#branching-strategy)
- [Contribution Workflow](#contribution-workflow)
- [Coding Guidelines](#coding-guidelines)
- [Commit Messages](#commit-messages)
- [Pull Request Guidelines](#pull-request-guidelines)
- [Breaking Changes](#breaking-changes)
- [Testing](#testing)

---

## Code of Conduct

### Our Standards

- Be respectful and professional in all interactions
- Use inclusive and welcoming language
- Accept constructive criticism gracefully
- Focus on what is best for the community and project
- Show empathy towards other community members

### Enforcement

Instances of unacceptable behavior may be reported to the project maintainer. All complaints will be reviewed and investigated, resulting in a response deemed necessary and appropriate to the circumstances.

---

## Getting Started

### Prerequisites

- **Windows 10** or later
- **.NET 8.0 SDK** or later
- **ffmpeg** (optional, required for rate changing features)
- **PowerShell 5.0** or later

### Building the Project

For development and testing:

```powershell
cd OsuMappingHelper
dotnet run
```

For a full release package (includes all tools):

```powershell
.\build.ps1
```

### Important Note on Rust Code

The `minacalc-rs`, `minacalc-rs-505`, `msd-calculator`, and `msd-calculator-505` directories contain Rust code that comes from external submodules or versioned copies. **Do not modify this code directly.** If changes are needed, they should be made in the upstream repository.

---

## Branching Strategy

This project uses a two-branch workflow:

| Branch | Purpose | Access |
|--------|---------|--------|
| `live` | Production-ready code (default branch) | PR/Merge only |
| `main` | Development branch with confirmed deployable code | Direct commits allowed for maintainers |

### Feature and Bugfix Branches

Create branches from `main` using these naming conventions:

- **Features**: `feature-<descriptive-name>`
  - Example: `feature-bulk-export`
  - Example: `feature-session-analytics`

- **Bug Fixes**: `bugfix-<issue-number>`
  - Example: `bugfix-42`
  - Example: `bugfix-157`

---

## Contribution Workflow

### Step 1: Open a GitHub Issue First

**This is required for all contributions.** Before starting any work:

1. Check existing issues to avoid duplicates
2. Open a new issue describing the feature or bug
3. Wait for maintainer feedback before proceeding
4. Reference the issue number in your branch name and PR

### Step 2: Fork and Clone

```powershell
git clone https://github.com/your-username/companella.git
cd companella/Client
```

### Step 3: Create Your Branch

```powershell
git checkout main
git pull origin main
git checkout -b feature-your-feature-name
# or
git checkout -b bugfix-123
```

### Step 4: Make Your Changes

- Follow the [Coding Guidelines](#coding-guidelines)
- Keep changes focused on a single concern
- Update documentation as needed

### Step 5: Commit Your Changes

Use [Conventional Commits](#commit-messages) format:

```powershell
git add .
git commit -m "feat: add bulk export functionality"
```

### Step 6: Push and Create Pull Request

```powershell
git push origin feature-your-feature-name
```

Then create a Pull Request on GitHub:
- Target branch: `main`
- Reference the issue number (e.g., "Closes #42")
- Provide a clear description of changes

### Step 7: Review Process

- A single maintainer approval is required
- Address any requested changes
- Once approved, the maintainer will merge to `main`
- Releases are created by merging `main` into `live`

---

## Coding Guidelines

### C# Style (Microsoft Conventions)

#### Indentation

Use **tabs** for indentation, not spaces.

#### Namespaces

Use file-scoped namespaces:

```csharp
namespace OsuMappingHelper.Services;

public class MyService
{
    // ...
}
```

#### Naming Conventions

| Element | Convention | Example |
|---------|------------|---------|
| Types (classes, interfaces, enums) | PascalCase | `MapClassification`, `IFileParser` |
| Methods | PascalCase | `CalculateDeviations()`, `ParseFile()` |
| Properties | PascalCase | `IsEnabled`, `CurrentRate` |
| Public Fields | PascalCase | `DefaultNameFormat` |
| Private Fields | _camelCase (underscore prefix) | `_processDetector`, `_currentOsuFile` |
| Local Variables | camelCase | `timingPoints`, `bpmResult` |
| Parameters | camelCase | `osuFile`, `rate`, `progressCallback` |
| Constants | PascalCase | `DefaultNameFormat`, `MaxLevel` |
| Win32 API Constants | SCREAMING_SNAKE_CASE | `WS_POPUP`, `SWP_NOMOVE` |

#### XML Documentation

XML documentation comments are **required** for all public APIs:

```csharp
/// <summary>
/// Creates a rate-changed copy of the beatmap.
/// </summary>
/// <param name="osuFile">The original beatmap.</param>
/// <param name="rate">The rate multiplier (e.g., 1.2 for 120%).</param>
/// <returns>Path to the new .osu file.</returns>
public async Task<string> CreateRateChangedBeatmapAsync(OsuFile osuFile, double rate)
{
    // ...
}
```

#### Async/Await

Use async/await for long-running operations:

```csharp
public async Task<bool> CheckFfmpegAvailableAsync()
{
    // Long-running operation
    await Task.Run(() => process.WaitForExit(5000));
    return process.ExitCode == 0;
}
```

#### Dependency Injection

Use osu!framework's dependency injection system:

```csharp
// Resolving dependencies
[Resolved]
private OsuProcessDetector ProcessDetector { get; set; } = null!;

// Registering dependencies
_dependencies.CacheAs(_processDetector);
```

#### UI Thread Marshalling

Use `Schedule()` for operations that must run on the UI thread:

```csharp
Schedule(() =>
{
    _loadingOverlay.UpdateStatus("Processing...");
    LoadBeatmap(newPath);
});
```

### Architecture Patterns

#### Models (`OsuMappingHelper/Models/`)

Simple data classes with properties and XML documentation:

```csharp
namespace OsuMappingHelper.Models;

/// <summary>
/// Represents timing deviation analysis results.
/// </summary>
public class TimingDeviation
{
    /// <summary>
    /// The deviation in milliseconds from the expected hit time.
    /// </summary>
    public double DeviationMs { get; set; }
    
    /// <summary>
    /// The column/lane where the hit occurred.
    /// </summary>
    public int Column { get; set; }
}
```

#### Components (`OsuMappingHelper/Components/`)

UI elements inheriting from osu!framework Drawables:

```csharp
namespace OsuMappingHelper.Components;

/// <summary>
/// Panel for rate changer settings.
/// </summary>
public partial class RateChangerPanel : Container
{
    [Resolved]
    private UserSettingsService UserSettingsService { get; set; } = null!;
    
    [BackgroundDependencyLoader]
    private void load()
    {
        // Initialize UI elements
    }
}
```

#### Services (`OsuMappingHelper/Services/`)

Business logic classes with constructor injection:

```csharp
namespace OsuMappingHelper.Services;

/// <summary>
/// Analyzes MSD (ManiaStarDifficulty) for beatmaps.
/// </summary>
public class MsdAnalyzer
{
    private readonly string _calculatorPath;
    
    public MsdAnalyzer(string calculatorPath)
    {
        _calculatorPath = calculatorPath;
    }
    
    public async Task<MsdResult?> AnalyzeSingleRateAsync(string osuPath, float rate)
    {
        // Implementation
    }
}
```

#### Screens (`OsuMappingHelper/Screens/`)

Compose components and wire event handlers:

```csharp
namespace OsuMappingHelper.Screens;

/// <summary>
/// Main screen of the application.
/// </summary>
public partial class MainScreen : osu.Framework.Screens.Screen
{
    private RateChangerPanel _rateChangerPanel = null!;
    
    [BackgroundDependencyLoader]
    private void load()
    {
        // Create and wire up components
        _rateChangerPanel.ApplyRateClicked += OnApplyRateClicked;
    }
}
```

### Rust Code

**Do not modify** the Rust code in:
- `minacalc-rs/`
- `minacalc-rs-505/`
- `msd-calculator/`
- `msd-calculator-505/`

These directories contain code from external submodules or versioned copies maintained for backward compatibility. If changes are needed, submit them to the upstream repository.

---

## Commit Messages

This project uses **Conventional Commits** format:

```
<type>: <description>

[optional body]

[optional footer]
```

### Types

| Type | Description |
|------|-------------|
| `feat` | New feature |
| `fix` | Bug fix |
| `docs` | Documentation only changes |
| `style` | Formatting, whitespace (no code change) |
| `refactor` | Code restructuring without behavior change |
| `test` | Adding or updating tests |
| `chore` | Maintenance tasks, dependency updates |

### Examples

```
feat: add bulk rate export to session planner

fix: correct timing deviation calculation for hold notes

docs: update README with new installation steps

refactor: extract BPM calculation to separate service

chore: update osu.Framework to 2024.1.0
```

---

## Pull Request Guidelines

### Requirements

1. **Reference the issue** - Include "Closes #XX" or "Fixes #XX" in your PR description
2. **Keep PRs focused** - One feature or bug fix per PR
3. **Ensure compilation** - Your code must compile without errors
4. **Update documentation** - Add XML documentation for new public APIs
5. **Test your changes** - Manual testing at minimum, document what you tested

### PR Description Template

```markdown
## Summary
Brief description of changes

## Related Issue
Closes #XX

## Changes Made
- Change 1
- Change 2

## Testing Done
- Tested scenario A
- Tested scenario B

## Screenshots (if applicable)
```

### Review Process

- Single maintainer approval is required
- Respond to feedback promptly
- Make requested changes in new commits (don't force-push during review)
- Maintainer will squash-merge approved PRs

---

## Breaking Changes

Breaking changes require special handling:

### What Constitutes a Breaking Change

- Removing or renaming public APIs
- Changing method signatures
- Altering default behavior
- Removing features
- Changing file formats or data structures

### Process for Breaking Changes

1. **Discuss first** - Open an issue explaining why the breaking change is necessary
2. **Get approval** - Wait for maintainer approval before proceeding
3. **Document clearly** - In your PR description, include:

```markdown
BREAKING CHANGE: Removed `OldMethod()` in favor of `NewMethod()`

Migration path:
- Replace calls to `OldMethod(x, y)` with `NewMethod(x, y, defaultValue)`
- Update any stored settings that referenced the old format
```

4. **Update version** - Breaking changes trigger a major version bump

---

## Testing

### Current Policy

Testing is **encouraged but not required**. The project does not currently have automated tests, but contributions to add testing infrastructure are welcome.

### Manual Testing Requirements

When submitting a PR, document what manual testing you performed:

```markdown
## Testing Done
- Verified rate changer creates correct audio at 1.5x
- Tested with 4K, 7K, and 9K maps
- Confirmed MSD calculation matches expected values
- Checked UI scales correctly at different resolutions
```

### Future Testing Contributions

If you want to contribute testing infrastructure:

1. Open an issue proposing the testing approach
2. Discuss with maintainer before implementation
3. Consider using xUnit for unit tests
4. Integration tests should mock external dependencies (osu! process, ffmpeg)

---

## Questions?

If you have any questions about contributing, please open a [GitHub Issue](https://github.com/your-repo/companella/issues) with the "question" label.

Thank you for contributing to Companella!


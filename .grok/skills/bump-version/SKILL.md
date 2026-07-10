---
name: bump-version
description: >
  Bump the AgentWiki package/CLI version in Directory.Build.props and
  AgentWikiConstants.Version, then optionally commit. Use when the user asks to
  bump version, release a patch/minor/major, set version X.Y.Z, or runs /bump-version.
---

# Bump AgentWiki version

## Goal

Keep version numbers in sync across:

1. `Directory.Build.props` (`Version`, `AssemblyVersion`, `FileVersion`, `InformationalVersion`)
2. `src/AgentWiki.Core/Constants/AgentWikiConstants.cs` (`public const string Version`)

## Steps

1. Confirm desired bump with the user if not specified:
   - `patch` (default) → x.y.**z+1**
   - `minor` → x.**y+1**.0
   - `major` → **x+1**.0.0
   - or exact `X.Y.Z`

2. Run the repo script (from any cwd):

```bash
./.grok/skills/bump-version/scripts/bump-version.sh patch
# or: minor | major | 1.2.3
```

3. Verify:

```bash
grep -E 'Version|AssemblyVersion|InformationalVersion' Directory.Build.props
grep 'Version =' src/AgentWiki.Core/Constants/AgentWikiConstants.cs
dotnet run --project src/AgentWiki.Cli -- --version
```

4. Unless the user says not to, create a git commit:

```bash
git add Directory.Build.props src/AgentWiki.Core/Constants/AgentWikiConstants.cs
git commit -m "chore: bump version to <NEW>"
```

5. Tell the user the new version and that they may also run:

```bash
./scripts/pack-and-install-tool.sh
```

to pack/install the global `agent-wiki` tool with that version.

## Rules

- Do **not** change version only in one file — always both props + constants.
- Do not push unless the user explicitly asks.
- Prefer the script over hand-editing so formats stay consistent.

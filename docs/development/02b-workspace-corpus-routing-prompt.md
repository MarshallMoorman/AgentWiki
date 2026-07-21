# Implementer prompt — Step 02b

Read and follow fully:

**`docs/development/02b-workspace-corpus-routing-requirements.md`**

Also: `docs/HANDOFF.md`, `src/AgentWiki.App/Services/Workspace*`, `AgentWikiConfig`, Step 02 requirements for context.

## Scope

Workspace corpus + routing cards; full human **manifest** (purpose, rules, layer, team, **applications/services**, **brands** Rise/Shine/Elastic/Blueprint); full **`memberDefaults`** in `workspace.json`; **`workspace member replace-configs`**; git-based staleness; web links (GH+ADO); local-clone orchestration; Phase 2-ready meta.

**Do not** implement vectors, Azure AI Search, or MCP.

## Quality gates (every phase A–H)

1. `dotnet build AgentWiki.slnx`  
2. `dotnet test AgentWiki.slnx` (full suite — protect single-repo mode)  
3. High-confidence tests for that phase  
4. Update AGENTS.md / README.md / docs/HANDOFF.md when user-facing  
5. Version + pack when shipping CLI changes  
6. **Commit** the phase  

## Process

Implement phases A→H from the requirements. Prefer reuse of single-repo init/generate. Offline fallback always works.

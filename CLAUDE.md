# CLAUDE.md

The project instructions live in AGENTS.md so that every coding agent reads the same text:

@AGENTS.md

## Claude Code only

- `.claude/launch.json` defines two preview servers: `backend` (https://localhost:7001 and http://localhost:5040)
  and `frontend` (http://localhost:5173). Start them through the preview tool rather than a shell.
- Stop the `backend` preview before running `dotnet build` or `dotnet test` in the same configuration; on Windows
  the running API locks its DLLs.

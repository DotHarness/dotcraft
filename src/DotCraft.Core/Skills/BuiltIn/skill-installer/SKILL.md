---
name: skill-installer
description: "Use when installing a DotCraft skill from a local folder, archive, GitHub checkout, or market-provided download and then checking whether it needs workspace-specific adaptation."
tools: Exec, SkillView
---

# Skill Installer

Use this workflow when the user wants to install, import, test, or adapt a DotCraft skill. The installation surface is ordinary shell and file work plus the DotCraft skill verifier; there is no dedicated install tool.

## Workflow

1. Prepare a candidate directory.
   - If the user provides a local directory, inspect it directly.
   - If the user provides an archive or URL, download/extract it into a temporary directory in the workspace.
   - If the skill is inside a repository, clone or copy the repository, then point at the subdirectory containing `SKILL.md`.
2. Ensure the candidate directory is a DotCraft skill bundle.
   - `SKILL.md` must be at the candidate root.
   - The frontmatter `name` must exist. If the user or market flow provides a local install name, use that as the CLI `--name` value; it does not have to match the frontmatter display name exactly.
   - Preserve supporting files where they are. Ordinary relative files such as root markdown/json files, `docs/`, `references/`, `scripts/`, `assets/`, and `agents/openai.yaml` can remain in the bundle when verification accepts them.
3. Run verification:

```powershell
dotcraft skill verify --candidate "<candidate-dir>" --name "<local-skill-name>" --json
```

Omit `--name` only when the frontmatter name is already the desired local install name.

4. If verification fails, decide whether the failure can be solved by command arguments before editing files.
   - If the problem is a display/local-name mismatch such as `name: Git` but the local install name should be `git`, retry with `--name "git"`.
   - Do not move or rewrite files only because they are root markdown/json files, `docs/`, or `references/`.
   - Only make minimal candidate edits for real structural or safety problems: missing `SKILL.md`, missing required frontmatter, unsafe paths, path traversal, hidden control files, oversized files, or genuinely broken references that the skill itself requires.
   - Never use `WriteFile` to regenerate a new `SKILL.md` just to make the skill look more like a DotCraft-native skill. Preserve the imported instructions.
5. Install after verification succeeds:

```powershell
dotcraft skill install --candidate "<candidate-dir>" --name "<local-skill-name>" --source "<where this came from>" --json
```

Use `--overwrite` only when the user explicitly wants to replace the current workspace source skill.

## Post-install Review

After installation, load the installed instructions with `SkillView(name)`. Then review the skill against the current workspace and environment by doing only checks that the skill itself makes relevant.

Examples of useful checks:

- If it assumes `python`, check the actual Python command/version or virtual environment.
- If it assumes a CLI tool, check whether that tool exists and whether the expected command shape works.
- If it references scripts or assets, confirm those files exist in the installed skill.
- If it assumes paths, package managers, shells, or config files, check those in the workspace.

Only write a workspace adaptation when you find a concrete mismatch. If `SkillManage` is available, use it to patch the effective skill so future runs remember the workspace-specific fix.

Use `SkillManage` with complete action-specific arguments. Do not call it with placeholder or null values. If a `SkillManage` call fails because a required argument is missing, retry with the required argument populated.

Examples:

```text
SkillManage(
  action: "patch",
  name: "<installed-skill-name>",
  oldString: "## Existing Section\nOriginal text that appears exactly once.",
  newString: "## Existing Section\nOriginal text plus the workspace-specific adaptation."
)
```

```text
SkillManage(
  action: "edit",
  name: "<installed-skill-name>",
  content: "<the complete updated SKILL.md content>"
)
```

```text
SkillManage(
  action: "write_file",
  name: "<installed-skill-name>",
  filePath: "scripts/check-environment.ps1",
  fileContent: "<complete helper script content>"
)
```

Never use `EditFile`, `WriteFile`, or shell redirection to modify files under `.craft/skills/<installed-skill-name>/` for workspace adaptation. Those files are the installed source skill. Adaptations must go through `SkillManage`. If `SkillManage` is unavailable or keeps failing, report the mismatch and the suggested adaptation instead of editing the source skill.

Do not invent compatibility rules that the skill does not imply. Do not rewrite a freshly installed skill just to summarize the installation.

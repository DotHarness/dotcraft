# Share a plugin

Sharing starts when the user asks for it. Establish the audience first:

| Audience | Route |
|---|---|
| People working in this repository | Commit `.craft/plugins/<id>` with the workspace |
| A team, an organization, or a public audience outside official review | A marketplace repository the author controls. See `https://www.dotcraft.net/extend/plugin-market` |
| Every DotCraft user | A contribution to `DotHarness/dotcraft-plugins`, below |

## Contribute to the official registry

1. Confirm the plugin has been exercised and is meant for a public audience. The copy is in English.
2. Run `dotcraft plugin verify --candidate "<plugin-root>" --for-publish --json` and fix every error.
3. Fork `DotHarness/dotcraft-plugins` and create a branch.
4. Copy the plugin to `plugins/<id>/`, leaving out development artifacts. A Desktop plugin includes its built `desktop/dist/`, and a .NET plugin includes its built `lib/`.
5. Add its entry to `.craft/plugins/marketplace.json`. `category` equals the manifest's `interface.category`:

   ```json
   {
     "name": "<id>",
     "source": { "source": "local", "path": "./plugins/<id>" },
     "policy": { "installation": "AVAILABLE", "authentication": "ON_INSTALL" },
     "category": "<category>"
   }
   ```

6. Prepare the pull request text from what the plugin actually does:
   - its purpose and contributions
   - what was tested, and on which DotCraft version
   - what data and network access it uses
   - the origin and license of its code and icon
   - for compiled or bundled code, the public source repository and commit
7. Stop and show the prepared branch and the pull request text.

Forking, pushing, and opening a pull request are outward-facing, so do each only when the user asks for it in the conversation. If the GitHub CLI or sign-in is unavailable, give the user the exact commands and text.

An update to a listed plugin keeps its `id` and increases `version`.

Report the state actually reached: verified locally, branch prepared, pull request opened, or merged.

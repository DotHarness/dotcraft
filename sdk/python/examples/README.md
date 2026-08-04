# Python SDK examples

`run_profile.py` is the smallest end-to-end Run-profile example. It shows both
connection modes, streamed output, approval and user-input callbacks, a runtime
dynamic tool, and clean shutdown through the async context manager.

Install the SDK from this directory, then connect to a local workspace:

```bash
python -m pip install -e .
python examples/run_profile.py --workspace .
```

To connect to an existing AppServer, pass its WebSocket URL. If it requires a
token, provide the token through `DOTCRAFT_APPSERVER_TOKEN`; do not put secrets
in source code or command history.

```bash
python examples/run_profile.py --remote ws://localhost:PORT/ws --workspace /server/workspace
```

Pass a prompt as the final argument to replace the built-in greeting prompt.
Approval requests default to decline unless you explicitly enter `y` or `yes`.

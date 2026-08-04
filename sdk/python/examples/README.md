# Python SDK examples

Start with one focused example:

| Example | Purpose |
| --- | --- |
| `first_run.py` | Connect locally, start a thread, run once, and close cleanly. |
| `continue_thread.py` | Resume a thread by ID and continue its conversation. |
| `multimodal_input.py` | Send text and a local image as structured input parts. |
| `models_and_mcp.py` | List models and inspect MCP runtime status for a thread. |
| `run_profile.py` | Comprehensive local/remote, streaming, callback, and Runtime Dynamic Tool example. |

`run_profile.py` is the complete end-to-end Run-profile example. It shows both
connection modes, streamed output, approval and user-input callbacks, a runtime
dynamic tool, and clean shutdown through the async context manager.

Install the SDK from this directory, then connect to a local workspace:

```bash
python -m pip install -e .
python examples/first_run.py .
python examples/continue_thread.py .
python examples/multimodal_input.py . /absolute/path/to/image.png
python examples/models_and_mcp.py .
```

Run the comprehensive example with:

```bash
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

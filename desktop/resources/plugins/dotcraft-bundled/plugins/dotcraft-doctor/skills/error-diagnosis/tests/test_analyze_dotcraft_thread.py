from __future__ import annotations

import importlib.util
import json
import pathlib
import sqlite3
import tempfile
import unittest


SCRIPT = pathlib.Path(__file__).parents[1] / "scripts" / "analyze_dotcraft_thread.py"
SPEC = importlib.util.spec_from_file_location("analyze_dotcraft_thread", SCRIPT)
assert SPEC and SPEC.loader
ANALYZER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(ANALYZER)


def record(kind: str, payload_name: str, payload: object) -> str:
    return json.dumps(
        {"kind": kind, "timestamp": "2026-07-20T00:00:00Z", payload_name: payload},
        separators=(",", ":"),
    )


def item(item_id: str, turn_id: str, item_type: str, payload: object) -> dict[str, object]:
    return {
        "id": item_id,
        "turnId": turn_id,
        "type": item_type,
        "status": "Completed",
        "payload": payload,
    }


class ThreadAnalyzerTests(unittest.TestCase):
    def analyze(self, lines: list[str]) -> dict[str, object]:
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory) / "thread_fixture.jsonl"
            path.write_text("\n".join(lines) + "\n", encoding="utf-8")
            return ANALYZER.load_thread(path, 100)

    def test_replacement_discards_incremental_items_and_reports_replacement_terminal_state(self) -> None:
        result = self.analyze(
            [
                record("turn_started", "turnStarted", {"turn": {"id": "turn_1"}}),
                record(
                    "item_appended",
                    "itemAppended",
                    {"turnId": "turn_1", "item": item("old", "turn_1", "Error", {"message": "old"})},
                ),
                record(
                    "turn_state_replaced",
                    "turnStateReplaced",
                    {
                        "turn": {
                            "id": "turn_1",
                            "status": "Completed",
                            "items": [
                                item("tool", "turn_1", "ToolCall", {"toolName": "safe"}),
                                item("error", "turn_1", "Error", {"message": "replacement"}),
                            ],
                        }
                    },
                ),
            ]
        )

        self.assertEqual(["error"], [entry["item_id"] for entry in result["errors"]])
        self.assertIn("replacement", result["errors"][0]["payload_preview"])
        self.assertNotIn("old", result["errors"][0]["payload_preview"])
        self.assertEqual(["tool"], [entry["item_id"] for entry in result["tool_items"]])
        self.assertEqual("error", result["terminal_items"][0]["item_id"])
        self.assertEqual({"ToolCall": 1, "Error": 1}, result["item_type_counts"])

    def test_rollback_removes_tail_turn_from_all_current_summaries(self) -> None:
        result = self.analyze(
            [
                record("turn_started", "turnStarted", {"turn": {"id": "turn_1"}}),
                record("turn_started", "turnStarted", {"turn": {"id": "turn_2"}}),
                record(
                    "turn_state_replaced",
                    "turnStateReplaced",
                    {"turn": {"id": "turn_2", "items": [item("error", "turn_2", "Error", {})]}},
                ),
                record(
                    "model_history_messages_appended",
                    "modelHistoryMessagesAppended",
                    {"turnId": "turn_2", "messages": [{"schemaVersion": 1, "contents": []}]},
                ),
                record("thread_rolled_back", "threadRolledBack", {"numTurns": 1}),
            ]
        )

        self.assertEqual(["turn_1"], [turn["turn_id"] for turn in result["turns"]])
        self.assertEqual([], result["errors"])
        self.assertEqual([], result["model_history_batches"])
        self.assertEqual(["turn_2"], result["rollbacks"][0]["removed_turn_ids"])

    def test_model_batches_and_checkpoints_never_expose_payloads(self) -> None:
        secret = "DO_NOT_EXPOSE_MODEL_PAYLOAD"
        result = self.analyze(
            [
                record("turn_started", "turnStarted", {"turn": {"id": "turn_1"}}),
                record("turn_started", "turnStarted", {"turn": {"id": "turn_2"}}),
                record(
                    "item_appended",
                    "itemAppended",
                    {
                        "turnId": "turn_1",
                        "item": item(
                            "error",
                            "turn_1",
                            "Error",
                            {
                                "message": "safe diagnostic",
                                "ProtectedData": secret,
                                "AdditionalProperties": {"secret": secret},
                            },
                        ),
                    },
                ),
                record(
                    "model_history_messages_appended",
                    "modelHistoryMessagesAppended",
                    {
                        "turnId": "turn_1",
                        "messages": [
                            {
                                "schemaVersion": 1,
                                "additionalProperties": {"secret": secret},
                                "contents": [{"kind": "text", "payload": {"text": secret}}],
                            }
                        ],
                    },
                ),
                record(
                    "model_history_messages_appended",
                    "modelHistoryMessagesAppended",
                    {
                        "turnId": "turn_2",
                        "messages": [{"schemaVersion": 1, "contents": [{"kind": "future", "payload": secret}]}],
                    },
                ),
                record(
                    "context_compacted",
                    "contextCompacted",
                    {
                        "checkpointId": "checkpoint_1",
                        "coveredThroughTurnId": "turn_1",
                        "replacementHistory": [
                            {"schemaVersion": 1, "contents": [{"kind": "text", "payload": secret}]}
                        ],
                    },
                ),
            ]
        )

        serialized = json.dumps(result)
        self.assertNotIn(secret, serialized)
        self.assertFalse(result["model_history_batches"][0]["rejected"])
        self.assertEqual(["text"], result["model_history_batches"][0]["content_kinds"])
        self.assertTrue(result["model_history_batches"][1]["rejected"])
        self.assertTrue(result["checkpoints"][0]["decoded"])
        self.assertNotIn("replacementHistory", serialized)

    def test_malformed_line_and_checkpoint_are_reported_without_stopping(self) -> None:
        result = self.analyze(
            [
                "{not-json",
                "[]",
                record(
                    "context_compacted",
                    "contextCompacted",
                    {"coveredThroughTurnId": "turn_1", "replacementHistory": {"bad": True}},
                ),
                record("turn_started", "turnStarted", {"turn": {"id": "turn_1"}}),
            ]
        )

        self.assertEqual(2, len(result["parse_errors"]))
        self.assertFalse(result["checkpoints"][0]["decoded"])
        self.assertEqual(["turn_1"], [turn["turn_id"] for turn in result["turns"]])

    def test_database_without_legacy_session_table_is_normal(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory) / "state.db"
            connection = sqlite3.connect(path)
            try:
                connection.execute("create table marker (id integer)")
                connection.commit()
            finally:
                connection.close()

            result = ANALYZER.load_db(path, "thread_1", [], 100)

        self.assertNotIn("legacy_thread_session", result)
        self.assertNotIn("open_error", result)

    def test_malformed_payload_types_are_recorded_and_following_records_are_processed(self) -> None:
        result = self.analyze(
            [
                record("turn_completed", "turnCompleted", []),
                record("item_appended", "itemAppended", {"item": "not-an-object"}),
                record("turn_started", "turnStarted", {"turn": {"id": "turn_1"}}),
            ]
        )

        self.assertEqual(["turn_1"], [turn["turn_id"] for turn in result["turns"]])
        self.assertGreaterEqual(len(result["parse_errors"]), 2)
        self.assertNotIn("AttributeError", json.dumps(result))

    def test_invalid_utf8_is_reported_without_traceback(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory) / "thread_fixture.jsonl"
            path.write_bytes(b'{"kind":"turn_started"}\n' + bytes([0xFF]))

            result = ANALYZER.load_thread(path, 100)

        self.assertIn("read_error", result)
        self.assertIn("Unicode", result["read_error"])

    def test_sensitive_values_are_redacted_from_diagnostic_result(self) -> None:
        secret = "sk-test-secret-value"
        result = self.analyze(
            [
                record("turn_started", "turnStarted", {"turn": {"id": "turn_1"}}),
                record(
                    "item_appended",
                    "itemAppended",
                    {
                        "turnId": "turn_1",
                        "item": item(
                            "error",
                            "turn_1",
                            "Error",
                            {
                                "token": secret,
                                "message": f"Authorization: Bearer {secret}",
                            },
                        ),
                    },
                ),
            ]
        )

        serialized = json.dumps(result)
        self.assertNotIn(secret, serialized)
        self.assertIn("[REDACTED]", serialized)

    def test_sqlite_missing_columns_degrades_to_safe_partial_result(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory) / "state.db"
            connection = sqlite3.connect(path)
            try:
                connection.execute("create table trace_events (session_key text, type text)")
                connection.execute(
                    "insert into trace_events(session_key, type) values ('session_1', 'Error')"
                )
                connection.commit()
            finally:
                connection.close()

            result = ANALYZER.load_db(path, "thread_1", ["session_1"], 100)

        self.assertNotIn("open_error", result)
        self.assertIn("trace_events", result)
        self.assertEqual(1, result["trace_events"]["session_1"]["type_counts"][0]["count"])
        self.assertEqual(1, len(result["trace_events"]["session_1"]["error_like_events"]))
        self.assertEqual("", result["trace_events"]["session_1"]["error_like_events"][0]["content_preview"])

    def test_invalid_event_json_is_not_echoed(self) -> None:
        secret = "raw-secret-event-json"
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory) / "state.db"
            connection = sqlite3.connect(path)
            try:
                connection.execute(
                    "create table trace_events (id integer, session_key text, type text, event_json text)"
                )
                connection.execute(
                    "insert into trace_events values (1, 'session_1', 'Error', ?)",
                    (secret,),
                )
                connection.commit()
            finally:
                connection.close()

            result = ANALYZER.load_db(path, "thread_1", ["session_1"], 100)

        self.assertNotIn(secret, json.dumps(result))
        self.assertTrue(any(w["code"] == "invalid_event_json" for w in result["warnings"]))


if __name__ == "__main__":
    unittest.main()

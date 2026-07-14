from dotcraft.models import (
    DynamicToolFunction,
    DynamicToolNamespace,
    DynamicToolResult,
    dynamic_tool_image,
    dynamic_tool_text,
)


def test_dynamic_tool_namespace_serializes_tagged_union() -> None:
    declaration = DynamicToolNamespace(
        name="desktop",
        description="Desktop tools.",
        tools=[
            DynamicToolFunction(
                name="ListThreads",
                description="List threads.",
                input_schema={"type": "object"},
                defer_loading=True,
            )
        ],
    )

    wire = declaration.to_wire()
    assert wire["type"] == "namespace"
    assert wire["tools"][0] == {
        "type": "function",
        "name": "ListThreads",
        "description": "List threads.",
        "inputSchema": {"type": "object"},
        "deferLoading": True,
    }


def test_dynamic_tool_result_uses_v2_audience_fields() -> None:
    result = DynamicToolResult(
        success=True,
        content_items=[dynamic_tool_text("Done")],
        structured_content={"count": 1},
    )

    assert result.to_wire() == {
        "success": True,
        "contentItems": [{"type": "text", "text": "Done"}],
        "structuredContent": {"count": 1},
    }


def test_dynamic_tool_image_requires_exactly_one_source() -> None:
    assert dynamic_tool_image("image/png", url="https://example.test/image.png")["url"]
    assert dynamic_tool_image("image/png", data_base64="aGVsbG8=")["dataBase64"]

    try:
        dynamic_tool_image("image/png")
    except ValueError as error:
        assert "Exactly one" in str(error)
    else:
        raise AssertionError("Expected an invalid source combination to fail")

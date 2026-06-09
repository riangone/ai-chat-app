#!/usr/bin/env python3
"""
Headroom context compression helper.
Reads JSON from stdin: {"messages": [...], "model": "gemini"}
Writes JSON to stdout: {"messages": [...], "original_tokens": N, "compressed_tokens": N}
"""
import sys
import json

def main():
    try:
        data = json.loads(sys.stdin.read())
    except Exception as e:
        print(json.dumps({"error": f"Invalid input JSON: {e}"}))
        sys.exit(1)

    messages = data.get("messages", [])
    model = data.get("model", "gemini")

    if not messages:
        print(json.dumps({"messages": [], "original_tokens": 0, "compressed_tokens": 0}))
        return

    try:
        from headroom import compress
        compressed = compress(messages, model=model)
        # compress() returns a list of messages
        if isinstance(compressed, list):
            result_messages = compressed
        else:
            result_messages = messages

        original_len = sum(len(m.get("content", "")) for m in messages if isinstance(m, dict))
        compressed_len = sum(len(m.get("content", "")) for m in result_messages if isinstance(m, dict))

        print(json.dumps({
            "messages": result_messages,
            "original_chars": original_len,
            "compressed_chars": compressed_len
        }))
    except ImportError:
        # headroom not installed — pass through unchanged
        print(json.dumps({
            "messages": messages,
            "original_chars": 0,
            "compressed_chars": 0,
            "warning": "headroom-ai not installed, compression skipped"
        }))
    except Exception as e:
        # On any error, pass through unchanged rather than breaking the request
        print(json.dumps({
            "messages": messages,
            "original_chars": 0,
            "compressed_chars": 0,
            "error": str(e)
        }))

if __name__ == "__main__":
    main()

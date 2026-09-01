#!/usr/bin/env python
"""Create and validate Revit BuildPlan JSON from text and image inputs."""

from __future__ import annotations

import argparse
import base64
import json
import mimetypes
import os
import sys
import urllib.error
import urllib.request
from pathlib import Path
from typing import Any


ROOT = Path(__file__).resolve().parent
DEFAULT_SCHEMA_PATH = ROOT / "schemas" / "revit_build_plan.schema.json"
DEFAULT_MODEL = "gpt-5.5"
RESPONSES_URL = "https://api.openai.com/v1/responses"


SYSTEM_PROMPT = """You are an expert BIM planning agent for Autodesk Revit 2027.
Convert the user's natural-language and visual input into a safe Revit BuildPlan JSON.

Rules:
- Output only JSON that matches the provided schema.
- Use millimeters for all coordinates and dimensions.
- Prefer dryRun=true for every operation unless the user explicitly asks to write.
- If scale, wall thickness, level, family, or type is uncertain, state assumptions and keep confidence lower.
- Only use create_wall, set_parameter, count_elements, list_levels, list_wall_types, list_family_symbols, get_active_document, place_door, place_window, and create_room for operations that the current bridge can execute.
- Use create_level and create_floor only as planned future operations when the input clearly asks for them.
- Use unsupported when the request cannot be represented safely.
- Never invent exact dimensions from an image without a scale marker or known reference; use assumptions and warnings.
- Keep operation ids stable and ordered: op-001, op-002, ...
"""


def main() -> int:
    parser = argparse.ArgumentParser(description="AI planner for Revit Codex Bridge.")
    subparsers = parser.add_subparsers(dest="command", required=True)

    plan_parser = subparsers.add_parser("plan", help="Create a BuildPlan JSON with the OpenAI Responses API.")
    plan_parser.add_argument("--prompt", help="Natural-language modeling request.")
    plan_parser.add_argument("--prompt-file", help="UTF-8 text file containing the modeling request.")
    plan_parser.add_argument("--image", action="append", default=[], help="Image path. Repeat for multiple images.")
    plan_parser.add_argument("--context", help="Optional project context JSON file.")
    plan_parser.add_argument("--out", default="planner/buildplan.generated.json", help="Output BuildPlan JSON path.")
    plan_parser.add_argument("--model", default=os.environ.get("OPENAI_MODEL", DEFAULT_MODEL))
    plan_parser.add_argument("--api-key", default=os.environ.get("OPENAI_API_KEY"))
    plan_parser.add_argument("--reasoning-effort", default=os.environ.get("OPENAI_REASONING_EFFORT", "medium"))
    plan_parser.add_argument("--write-request", help="Write the OpenAI request JSON instead of calling the API.")

    validate_parser = subparsers.add_parser("validate", help="Validate a BuildPlan file with local checks.")
    validate_parser.add_argument("--plan", required=True, help="BuildPlan JSON path.")

    export_parser = subparsers.add_parser("export-commands", help="Export executable bridge commands from a BuildPlan.")
    export_parser.add_argument("--plan", required=True, help="BuildPlan JSON path.")
    export_parser.add_argument("--out-dir", default="planner/commands", help="Directory for command JSON files.")
    export_parser.add_argument("--write", action="store_true", help="Export dryRun=false for operations marked writable.")

    args = parser.parse_args()

    try:
        if args.command == "plan":
            return run_plan(args)

        if args.command == "validate":
            plan = read_json(Path(args.plan))
            errors = validate_build_plan(plan)
            print_validation(errors)
            return 1 if errors else 0

        if args.command == "export-commands":
            plan = read_json(Path(args.plan))
            errors = validate_build_plan(plan)
            if errors:
                print_validation(errors)
                return 1

            exported = export_commands(plan, Path(args.out_dir), allow_writes=args.write)
            print(json.dumps({"exported": exported}, ensure_ascii=False, indent=2))
            return 0

        raise ValueError(f"Unknown command: {args.command}")
    except Exception as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 1


def run_plan(args: argparse.Namespace) -> int:
    prompt = collect_prompt(args.prompt, args.prompt_file)
    if not prompt and not args.image:
        raise ValueError("Provide --prompt, --prompt-file, or at least one --image.")

    schema = read_json(DEFAULT_SCHEMA_PATH)
    context = read_json(Path(args.context)) if args.context else None
    request_body = build_openai_request(
        prompt=prompt,
        image_paths=[Path(path) for path in args.image],
        context=context,
        schema=schema,
        model=args.model,
        reasoning_effort=args.reasoning_effort,
    )

    if args.write_request:
        write_json(Path(args.write_request), request_body)
        print(f"Wrote request JSON: {args.write_request}")
        return 0

    if not args.api_key:
        raise ValueError("OPENAI_API_KEY is not set. Use --write-request to inspect the request without calling the API.")

    response = call_openai_responses(request_body, args.api_key)
    text = extract_response_text(response)
    plan = json.loads(text)
    errors = validate_build_plan(plan)

    if errors:
        print_validation(errors)
        write_json(Path(args.out), plan)
        return 1

    write_json(Path(args.out), plan)
    print(f"Wrote BuildPlan: {args.out}")
    return 0


def collect_prompt(prompt: str | None, prompt_file: str | None) -> str:
    chunks: list[str] = []

    if prompt:
        chunks.append(prompt.strip())

    if prompt_file:
        chunks.append(Path(prompt_file).read_text(encoding="utf-8").strip())

    return "\n\n".join(chunk for chunk in chunks if chunk)


def build_openai_request(
    prompt: str,
    image_paths: list[Path],
    context: dict[str, Any] | None,
    schema: dict[str, Any],
    model: str,
    reasoning_effort: str,
) -> dict[str, Any]:
    user_content: list[dict[str, Any]] = []

    if prompt:
        user_content.append({"type": "input_text", "text": prompt})

    if context:
        user_content.append(
            {
                "type": "input_text",
                "text": "Project context JSON:\n" + json.dumps(context, ensure_ascii=False, indent=2),
            }
        )

    for image_path in image_paths:
        user_content.append({"type": "input_image", "image_url": image_to_data_url(image_path)})

    return {
        "model": model,
        "input": [
            {
                "role": "system",
                "content": [{"type": "input_text", "text": SYSTEM_PROMPT}],
            },
            {
                "role": "user",
                "content": user_content,
            },
        ],
        "reasoning": {"effort": reasoning_effort},
        "text": {
            "format": {
                "type": "json_schema",
                "name": "revit_build_plan",
                "schema": strip_schema_metadata(schema),
                "strict": True,
            }
        },
    }


def strip_schema_metadata(schema: dict[str, Any]) -> dict[str, Any]:
    stripped = dict(schema)
    stripped.pop("$schema", None)
    stripped.pop("title", None)
    return stripped


def image_to_data_url(path: Path) -> str:
    if not path.exists():
        raise FileNotFoundError(path)

    mime_type = mimetypes.guess_type(path.name)[0] or "application/octet-stream"
    encoded = base64.b64encode(path.read_bytes()).decode("ascii")
    return f"data:{mime_type};base64,{encoded}"


def call_openai_responses(body: dict[str, Any], api_key: str) -> dict[str, Any]:
    request = urllib.request.Request(
        RESPONSES_URL,
        data=json.dumps(body).encode("utf-8"),
        headers={
            "Authorization": f"Bearer {api_key}",
            "Content-Type": "application/json",
        },
        method="POST",
    )

    try:
        with urllib.request.urlopen(request, timeout=180) as response:
            return json.loads(response.read().decode("utf-8"))
    except urllib.error.HTTPError as exc:
        body_text = exc.read().decode("utf-8", errors="replace")
        raise RuntimeError(f"OpenAI API returned HTTP {exc.code}: {body_text}") from exc


def extract_response_text(response: dict[str, Any]) -> str:
    if response.get("output_text"):
        return str(response["output_text"])

    output = response.get("output", [])

    for item in output:
        if item.get("type") != "message":
            continue

        for content in item.get("content", []):
            if content.get("type") in {"output_text", "text"} and content.get("text"):
                return str(content["text"])

    raise ValueError("Could not find output text in OpenAI response.")


def validate_build_plan(plan: dict[str, Any]) -> list[str]:
    errors: list[str] = []
    required = ["schemaVersion", "intentSummary", "sourceAnalysis", "target", "safety", "assumptions", "operations"]

    for key in required:
        if key not in plan:
            errors.append(f"Missing top-level field: {key}")

    if errors:
        return errors

    if plan["schemaVersion"] != "1.0":
        errors.append("schemaVersion must be '1.0'.")

    target = plan.get("target") or {}
    if target.get("revitVersion") != "2027":
        errors.append("target.revitVersion should be '2027' for this project.")

    if target.get("units") != "mm":
        errors.append("target.units must be 'mm'.")

    operations = plan.get("operations")
    if not isinstance(operations, list):
        errors.append("operations must be a list.")
        return errors

    for index, operation in enumerate(operations, start=1):
        label = f"operations[{index - 1}]"

        if not isinstance(operation, dict):
            errors.append(f"{label} must be an object.")
            continue

        command = operation.get("command")
        payload = operation.get("payload") or {}

        if not operation.get("id"):
            errors.append(f"{label}.id is required.")

        if command not in supported_plan_commands():
            errors.append(f"{label}.command is not supported by the BuildPlan schema: {command}")

        if command == "create_wall":
            for field in ["start", "end", "heightMm"]:
                if payload.get(field) is None:
                    errors.append(f"{label}.payload.{field} is required for create_wall.")

            if payload.get("levelName") is None and payload.get("levelId") is None:
                errors.append(f"{label}.payload needs levelName or levelId for create_wall.")

        if command == "set_parameter":
            for field in ["elementId", "parameterName"]:
                if payload.get(field) is None:
                    errors.append(f"{label}.payload.{field} is required for set_parameter.")

        if command in {"place_door", "place_window"}:
            if payload.get("hostElementId") is None:
                errors.append(f"{label}.payload.hostElementId is required for {command}.")

            if payload.get("location") is None:
                errors.append(f"{label}.payload.location is required for {command}.")

            if payload.get("levelName") is None and payload.get("levelId") is None:
                errors.append(f"{label}.payload needs levelName or levelId for {command}.")

        if command == "create_room":
            if payload.get("location") is None:
                errors.append(f"{label}.payload.location is required for create_room.")

            if payload.get("levelName") is None and payload.get("levelId") is None:
                errors.append(f"{label}.payload needs levelName or levelId for create_room.")

        if command == "list_family_symbols" and not payload.get("category"):
            errors.append(f"{label}.payload.category is required for list_family_symbols.")

        if operation.get("confidence") is not None:
            confidence = float(operation["confidence"])
            if confidence < 0 or confidence > 1:
                errors.append(f"{label}.confidence must be between 0 and 1.")

    return errors


def supported_plan_commands() -> set[str]:
    return {
        "get_active_document",
        "list_levels",
        "list_wall_types",
        "list_family_symbols",
        "count_elements",
        "create_wall",
        "set_parameter",
        "place_door",
        "place_window",
        "create_room",
        "create_level",
        "create_floor",
        "unsupported",
    }


def export_commands(plan: dict[str, Any], out_dir: Path, allow_writes: bool) -> list[str]:
    out_dir.mkdir(parents=True, exist_ok=True)
    exported: list[str] = []

    for operation in plan.get("operations", []):
        command = operation["command"]
        bridge_command = to_bridge_command(operation, allow_writes)

        if bridge_command is None:
            continue

        path = out_dir / f"{operation['id']}.{command}.json"
        write_json(path, bridge_command)
        exported.append(str(path))

    return exported


def to_bridge_command(operation: dict[str, Any], allow_writes: bool) -> dict[str, Any] | None:
    command = operation["command"]
    payload = operation.get("payload") or {}

    if command in {"get_active_document", "list_levels", "list_wall_types"}:
        return {"command": command}

    if command == "list_family_symbols":
        return {
            "command": command,
            "category": payload["category"],
        }

    if command == "count_elements":
        result: dict[str, Any] = {"command": command}
        if payload.get("category"):
            result["category"] = payload["category"]
        return result

    if command == "create_wall":
        result = {
            "command": "create_wall",
            "start": payload["start"],
            "end": payload["end"],
            "heightMm": payload["heightMm"],
            "dryRun": True,
        }

        copy_if_present(payload, result, "levelName")
        copy_if_present(payload, result, "levelId")
        copy_if_present(payload, result, "wallTypeName")
        copy_if_present(payload, result, "wallTypeId")

        if allow_writes and operation.get("dryRun") is False:
            result["dryRun"] = False

        return result

    if command == "set_parameter":
        result = {
            "command": "set_parameter",
            "elementId": payload["elementId"],
            "parameterName": payload["parameterName"],
            "value": payload.get("value"),
            "dryRun": True,
        }

        copy_if_present(payload, result, "doubleUnit")

        if allow_writes and operation.get("dryRun") is False:
            result["dryRun"] = False

        return result

    if command in {"place_door", "place_window"}:
        result = {
            "command": command,
            "hostElementId": payload["hostElementId"],
            "location": payload["location"],
            "dryRun": True,
        }

        copy_if_present(payload, result, "levelName")
        copy_if_present(payload, result, "levelId")
        copy_if_present(payload, result, "familyName")
        copy_if_present(payload, result, "typeName")

        if allow_writes and operation.get("dryRun") is False:
            result["dryRun"] = False

        return result

    if command == "create_room":
        result = {
            "command": command,
            "location": payload["location"],
            "dryRun": True,
        }

        copy_if_present(payload, result, "levelName")
        copy_if_present(payload, result, "levelId")

        if allow_writes and operation.get("dryRun") is False:
            result["dryRun"] = False

        return result

    return None


def copy_if_present(source: dict[str, Any], target: dict[str, Any], key: str) -> None:
    if source.get(key) is not None:
        target[key] = source[key]


def print_validation(errors: list[str]) -> None:
    if not errors:
        print("BuildPlan validation passed.")
        return

    print("BuildPlan validation failed:", file=sys.stderr)
    for error in errors:
        print(f"- {error}", file=sys.stderr)


def read_json(path: Path) -> dict[str, Any]:
    return json.loads(path.read_text(encoding="utf-8"))


def write_json(path: Path, data: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    raise SystemExit(main())

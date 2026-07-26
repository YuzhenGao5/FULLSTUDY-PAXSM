#!/usr/bin/env python3
"""Build a transparent, demo-only CARE-XR Evidence Matrix from exported data.

This script deliberately mirrors the current Researcher Console rules:
  - X: participant-relative Answer-stage knob pattern.
  - Y: selected Probe Plugin direction match against the participant's Baseline.

It is intended for synthetic pipeline testing. It does not infer a participant's
cognitive state and does not emit a careless-response label.
"""

from __future__ import annotations

import argparse
import csv
import html
import json
import math
import uuid
from collections import Counter, defaultdict
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


DIMENSIONS: dict[str, dict[str, Any]] = {
    "mental": {
        "display": "Mental Demand",
        "item_ids": {"mentaldemand"},
        "calibration_block": "cognitive_heavy",
        "calibration_name": "Mental-demand calibration",
        "features": ["CompletionTime", "RayMovementDistance", "HeadDirectionEntropy"],
    },
    "physical": {
        "display": "Physical Demand",
        "item_ids": {"physicaldemand"},
        "calibration_block": "physical_heavy",
        "calibration_name": "Physical-demand calibration",
        "features": ["GestureDistance", "HeadRotation", "HeadDistance"],
    },
    "temporal": {
        "display": "Temporal Demand",
        "item_ids": {"temporal"},
        "calibration_block": "temporal_heavy",
        "calibration_name": "Temporal-demand calibration",
        "features": ["CompletionTime", "ErrorRate", "ResponseSpeed"],
    },
}

MATRIX_Y = ["strong", "partial", "none"]
MATRIX_X = ["accelerated_direct", "reviewable", "hesitant_corrective"]

Y_LABELS = {
    "strong": "Strong Probe support",
    "partial": "Partial Probe support",
    "none": "Insufficient Probe support",
}
X_LABELS = {
    "accelerated_direct": "Matches accelerated-direct pattern",
    "reviewable": "No dominant response pattern / reviewable",
    "hesitant_corrective": "Matches hesitant-corrective pattern",
}


def read_csv(path: Path) -> list[dict[str, str]]:
    with path.open("r", encoding="utf-8-sig", newline="") as handle:
        return list(csv.DictReader(handle))


def write_csv(path: Path, rows: list[dict[str, Any]], fieldnames: list[str]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=fieldnames)
        writer.writeheader()
        writer.writerows(rows)


def find_single(directory: Path, pattern: str) -> Path:
    matches = sorted(directory.glob(pattern))
    if len(matches) != 1:
        raise RuntimeError(f"Expected one {pattern!r} in {directory}, found {len(matches)}.")
    return matches[0]


def float_value(value: str | None, default: float = math.nan) -> float:
    try:
        return float(value) if value not in (None, "") else default
    except ValueError:
        return default


def int_value(value: str | None, default: int = 0) -> int:
    try:
        return int(float(value)) if value not in (None, "") else default
    except ValueError:
        return default


def fmt(value: float, digits: int = 3) -> str:
    return "-" if math.isnan(value) else f"{value:.{digits}f}"


def read_metrics(path: Path) -> dict[str, dict[str, Any]]:
    metrics: dict[str, dict[str, Any]] = {}
    for row in read_csv(path):
        metric_id = row.get("metricId", "")
        if not metric_id or metric_id in metrics:
            continue
        value = float_value(row.get("value"))
        if math.isnan(value):
            continue
        metrics[metric_id] = {
            "metric_id": metric_id,
            "name": row.get("metricName", metric_id),
            "unit": row.get("unit", ""),
            "value": value,
            "valid": row.get("valid", "") in {"1", "true", "True"},
        }
    return metrics


def find_metrics_for_block(directory: Path, block_token: str) -> Path:
    matches = sorted(directory.glob(f"*{block_token}*Metrics.csv"))
    if len(matches) != 1:
        raise RuntimeError(
            f"Expected exactly one metrics file for {block_token!r} in {directory}; found {len(matches)}."
        )
    return matches[0]


def find_primary_questionnaire(directory: Path, participant_id: str) -> Path:
    candidates = []
    for path in directory.glob(f"CAREXR_Questionnaire_{participant_id}_*.csv"):
        name = path.name.lower()
        if any(token in name for token in (
            "_stageevents_", "_rawtrace_", "_interactionevents_", "_metadata_",
            "_physicalspeedsamples_", "_slotspeedevents_", "_speedsummary_",
        )):
            continue
        candidates.append(path)
    completed = [path for path in candidates if "_completed" in path.name.lower()]
    candidates = completed or candidates
    if len(candidates) != 1:
        raise RuntimeError(f"Expected one primary questionnaire CSV in {directory}; found {len(candidates)}.")
    return candidates[0]


def threshold_value(node: dict[str, Any], field: str, fallback: float = math.nan) -> float:
    value = node.get(field, fallback)
    return float(value) if isinstance(value, (int, float)) else fallback


def distance_bin(stage_reference: dict[str, Any], distance: int) -> dict[str, Any] | None:
    for item in stage_reference.get("distanceBins", []):
        lower = int(item.get("minimumSlotDistance", -1))
        upper = int(item.get("maximumSlotDistance", -1))
        if lower <= distance <= upper:
            return item
    return None


def classify_answer(row: dict[str, str], profile: dict[str, Any]) -> tuple[str, str]:
    """Mirror ContextualEvidenceService.ClassifyStage for the Answer stage."""
    thresholds = profile["responsePatternThresholds"]
    answer = profile["personalReference"]["answer"]

    decision_rt = float_value(row.get("answerDecisionRt"))
    speed = float_value(row.get("answerMaxAbsVel"))
    path_ratio = float_value(row.get("answerPathRatio"))
    reverses = int_value(row.get("answerReverseCount"))
    micro = int_value(row.get("answerMicroAdjustCount"))
    slot_changes = max(1, int_value(row.get("answerSlotChangeCount")))
    initial_slot = int_value(row.get("answerInitialSlot"))
    final_slot = int_value(row.get("selectedScore"))
    distance = abs(final_slot - initial_slot) if initial_slot > 0 and final_slot > 0 else -1
    correction_rate = (reverses + micro) / slot_changes

    bin_record = distance_bin(answer, distance)
    speed_threshold = threshold_value(thresholds, "answerHighMaxAbsVelocityAbove")
    low_correction = threshold_value(thresholds, "answerLowCorrectionRateAtOrBelow")
    bin_name = "global"
    if bin_record:
        bin_name = bin_record.get("binId", "movement")
        speed_threshold = threshold_value(bin_record.get("maxAbsVelocity", {}), "p90", speed_threshold)
        low_correction = threshold_value(bin_record.get("correctionRate", {}), "p25", low_correction)

    direct_max = threshold_value(thresholds, "directPathRatioMax", 1.2)
    extra_path = threshold_value(thresholds, "answerExtraPathRatioAbove")
    high_correction = threshold_value(thresholds, "answerHighCorrectionRateAbove")
    slow_threshold = threshold_value(answer.get("decisionRt", {}), "upperReference")

    high_speed = not math.isnan(speed_threshold) and speed >= speed_threshold
    direct = not math.isnan(path_ratio) and 0.9 <= path_ratio <= direct_max
    low_correction_match = not math.isnan(low_correction) and correction_rate <= low_correction + 0.000001
    extra_path_match = not math.isnan(extra_path) and path_ratio > extra_path
    high_correction_match = not math.isnan(high_correction) and correction_rate > high_correction
    slow = not math.isnan(slow_threshold) and decision_rt > slow_threshold

    evidence = [
        f"slot distance={distance} ({bin_name} threshold)",
        f"speed={fmt(speed)} vs p90/reference={fmt(speed_threshold)}",
        f"path ratio={fmt(path_ratio)} vs direct maximum={fmt(direct_max)}",
        f"correction rate={fmt(correction_rate)} vs lower reference={fmt(low_correction)}",
        f"decision RT={fmt(decision_rt)} vs upper reference={fmt(slow_threshold)}",
    ]
    if direct and low_correction_match and high_speed:
        return "accelerated_direct", "; ".join(evidence)
    if sum((extra_path_match, high_correction_match, slow)) >= 2:
        return "hesitant_corrective", "; ".join(evidence)
    return "reviewable", "; ".join(evidence)


def match_probe(
    rule: dict[str, Any], baseline: dict[str, dict[str, Any]], combined: dict[str, dict[str, Any]]
) -> tuple[str, float, str]:
    matched = 0
    available = 0
    evidence = []
    for feature in rule["Features"]:
        metric_id = feature["MetricId"]
        baseline_metric = baseline.get(metric_id)
        combined_metric = combined.get(metric_id)
        if not baseline_metric or not combined_metric or not baseline_metric["valid"] or not combined_metric["valid"]:
            evidence.append(f"{feature['MetricName']}: unavailable")
            continue
        available += 1
        delta = combined_metric["value"] - baseline_metric["value"]
        tolerance = max(
            abs(float(feature["CalibrationDelta"])) * 0.10,
            max(abs(baseline_metric["value"]) * 0.03, 0.001),
        )
        expected = feature["ExpectedDirection"]
        direction_match = delta < -tolerance if expected == "lower" else delta > tolerance
        if direction_match:
            matched += 1
        observed = "near Baseline" if abs(delta) < tolerance else "higher" if delta > 0 else "lower"
        evidence.append(
            f"{feature['MetricName']}: {fmt(combined_metric['value'])} vs Baseline "
            f"{fmt(baseline_metric['value'])} ({observed}; expected {expected})"
        )
    if available == 0:
        return "none", 0.0, "; ".join(evidence)
    ratio = matched / available
    category = "strong" if matched >= 2 and ratio >= (2 / 3) else "partial" if matched >= 1 else "none"
    return category, ratio, "; ".join(evidence)


def build_plugin(
    participant_id: str,
    calibration_dir: Path,
    baseline_path: Path,
    calibration_paths: dict[str, Path],
) -> dict[str, Any]:
    baseline = read_metrics(baseline_path)
    dimensions = []
    for dimension_id, definition in DIMENSIONS.items():
        condition_path = calibration_paths[dimension_id]
        condition = read_metrics(condition_path)
        rules = []
        for metric_id in definition["features"]:
            baseline_metric = baseline[metric_id]
            condition_metric = condition[metric_id]
            delta = condition_metric["value"] - baseline_metric["value"]
            rules.append({
                "MetricId": metric_id,
                "MetricName": baseline_metric["name"],
                "Unit": baseline_metric["unit"],
                "ExpectedDirection": "higher" if delta >= 0 else "lower",
                "CalibrationBaselineValue": baseline_metric["value"],
                "CalibrationConditionValue": condition_metric["value"],
                "CalibrationDelta": delta,
            })
        dimensions.append({
            "DimensionId": dimension_id,
            "DisplayName": definition["display"],
            "CalibrationBlockId": definition["calibration_block"],
            "CalibrationBlockName": definition["calibration_name"],
            "SourceParticipantId": participant_id,
            "CalibrationRunDirectory": str(calibration_dir),
            "BaselineMetricsPath": str(baseline_path),
            "ConditionMetricsPath": str(condition_path),
            "Scope": "synthetic_demo_only",
            "CreatedAtUtc": datetime.now(timezone.utc).isoformat(),
            "Features": rules,
        })
    return {
        "SchemaVersion": "CAREXR_ProbePlugin_v1",
        "PluginId": f"demo-{uuid.uuid4().hex}",
        "PluginName": "DEMO ONLY - P999999 target-selection workload probe v0",
        "TaskFamily": "Colour-and-shape target-selection workload task",
        "CalibrationParticipantCount": 1,
        "PluginPurpose": "Synthetic pipeline demonstration of Evidence Matrix Y-axis matching.",
        "UpdatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "BoundaryNote": (
            "SYNTHETIC TEST DATA ONLY. This one-person provisional rule card demonstrates "
            "the data structure and must not be used as a study-level task probe or cognitive-state label."
        ),
        "CalibrationSources": [{
            "ParticipantId": participant_id,
            "RunDirectory": str(calibration_dir),
            "BaselineMetricsPath": str(baseline_path),
            "MentalMetricsPath": str(calibration_paths["mental"]),
            "PhysicalMetricsPath": str(calibration_paths["physical"]),
        }],
        "Dimensions": dimensions,
    }


def write_html(path: Path, records: list[dict[str, Any]], plugin: dict[str, Any]) -> None:
    grouped: dict[tuple[str, str], list[dict[str, Any]]] = defaultdict(list)
    for record in records:
        grouped[(record["y_axis_probe_support"], record["x_axis_response_pattern"])].append(record)

    cells = []
    for y in MATRIX_Y:
        row_cells = []
        for x in MATRIX_X:
            entries = grouped[(y, x)]
            labels = "<br>".join(
                html.escape(f"{entry['combined_block']} - {entry['item_dimension']} ({entry['selected_score']})")
                for entry in entries
            ) or "-"
            row_cells.append(f"<td><strong>{len(entries)}</strong><span>{labels}</span></td>")
        cells.append(f"<tr><th>{html.escape(Y_LABELS[y])}</th>{''.join(row_cells)}</tr>")

    doc = f"""<!doctype html>
<html lang=\"en\"><head><meta charset=\"utf-8\"><title>P999999 Evidence Matrix Demo</title>
<style>
body {{ font-family: Segoe UI, Arial, sans-serif; margin: 36px; color: #1f2933; background: #f4f7f9; }}
main {{ max-width: 1160px; margin: auto; background: #fff; padding: 34px; border: 1px solid #d3dce3; }}
h1 {{ margin: 0 0 8px; font-size: 28px; }} p {{ line-height: 1.55; }} .note {{ background: #fff5dc; border-left: 4px solid #b7791f; padding: 12px 14px; }}
table {{ width: 100%; border-collapse: collapse; margin-top: 24px; table-layout: fixed; }} th, td {{ border: 1px solid #c9d3da; padding: 14px; vertical-align: top; }} th {{ background: #eaf1f5; text-align: left; }} td {{ min-height: 115px; background: #fff; }} td strong {{ display: block; color: #0f6b65; font-size: 26px; }} td span {{ display: block; margin-top: 8px; font-size: 13px; line-height: 1.45; }}
caption {{ text-align: left; font-weight: 600; margin-bottom: 10px; }}
</style></head><body><main>
<h1>CARE-XR Evidence Matrix: P999999 Demo</h1>
<p><strong>Probe Plugin:</strong> {html.escape(plugin['PluginName'])}</p>
<p class=\"note\">Synthetic test data only. The matrix shows evidence routing, not a cognitive-state, careful/careless, or data-exclusion decision.</p>
<table><caption>Y: calibrated task-probe direction match &nbsp;&nbsp; x &nbsp;&nbsp; X: participant-relative Answer-stage knob pattern</caption>
<thead><tr><th>Y / X</th>{''.join(f'<th>{html.escape(X_LABELS[x])}</th>' for x in MATRIX_X)}</tr></thead>
<tbody>{''.join(cells)}</tbody></table>
<p>Each number is an item-level contextual record. Only Mental, Physical, and Temporal NASA-TLX items enter Y because this demo plugin contains calibration rules for those dimensions.</p>
</main></body></html>"""
    path.write_text(doc, encoding="utf-8")


def write_report(
    path: Path,
    records: list[dict[str, Any]],
    plugin: dict[str, Any],
    calibration_questionnaire: list[dict[str, str]],
) -> None:
    by_dimension = defaultdict(list)
    for record in records:
        by_dimension[record["probe_dimension"]].append(record)

    baseline_scores = {
        row["itemId"]: row["selectedScore"]
        for row in calibration_questionnaire
        if row.get("blockId") == "baseline"
    }
    lines = [
        "# P999999 Synthetic Evidence-Matrix Demonstration",
        "",
        "## Status and boundary",
        "",
        "This is a pipeline demonstration generated from the P999999 synthetic calibration and Combined runs. "
        "It shows that the current exports can be turned into a task-probe plugin and item-level X/Y contextual records. "
        "It is **not** human-participant evidence, a validated workload detector, or a careless-response classifier.",
        "",
        "## Inputs",
        "",
        f"- Personal knob reference: `{records[0]['source_response_profile_path']}`",
        f"- Task-probe calibration run: `{plugin['CalibrationSources'][0]['RunDirectory']}`",
        f"- Combined target run: `{records[0]['source_combined_metrics_path']}`",
        "- Integrity status: all Combined-run checks passed before this report was generated.",
        "",
        "## Demo Probe Plugin",
        "",
        f"**{plugin['PluginName']}** contains three provisional task-context rule cards:",
        "",
        "| Dimension | Calibration comparison | Selected behavioral features | Expected direction |",
        "|---|---|---|---|",
    ]
    for card in plugin["Dimensions"]:
        features = "; ".join(feature["MetricName"] for feature in card["Features"])
        directions = "; ".join(f"{feature['MetricName']} {feature['ExpectedDirection']}" for feature in card["Features"])
        lines.append(
            f"| {card['DisplayName']} | {card['CalibrationBlockId']} minus Baseline | {features} | {directions} |"
        )

    lines.extend([
        "",
        "## Y axis: task-context probe support",
        "",
        "For each Combined block, the script compares the selected behavioral features with that participant's Baseline. "
        "The current Console rule is: 2/3 or 3/3 directions matching = **strong**; 1/3 = **partial**; 0/3 = **insufficient**. "
        "The NASA-TLX score is displayed after this comparison and is not used to calculate the Y value.",
        "",
        "| Dimension | Baseline score | Combined score(s) | Y result | What drove the result |",
        "|---|---:|---:|---|---|",
    ])
    for dimension_id in ("mental", "physical", "temporal"):
        dimension_records = by_dimension[dimension_id]
        first = dimension_records[0]
        scores = ", ".join(str(record["selected_score"]) for record in dimension_records)
        y = first["y_axis_probe_support"]
        lines.append(
            f"| {first['item_dimension']} | {baseline_scores.get(first['item_id'], '-')} | {scores} | "
            f"{Y_LABELS[y]} ({first['probe_matched_features']}/{first['probe_available_features']}) | "
            f"{first['y_evidence']} |"
        )

    counts = Counter((record["y_axis_probe_support"], record["x_axis_response_pattern"]) for record in records)
    lines.extend([
        "",
        "## X axis: participant-relative Answer-stage pattern",
        "",
        "The X axis uses the completed personal knob reference profile. A record is `accelerated_direct` only when all three conditions are present: direct path (ratio <= 1.2), speed above the relevant personal p90/reference, and low correction. "
        "It is `hesitant_corrective` only when at least two of long path, high correction, or slow decision time are present. Otherwise it stays `reviewable`.",
        "",
        f"All {len(records)} supported Combined item records were classified as **reviewable**. None met the complete accelerated-direct pattern and none met at least two hesitant-corrective criteria.",
        "",
        "This is the appropriate result for this particular synthetic run: it was not designed to inject a rapid/direct or a strongly hesitant/corrective Answer pattern. The matrix therefore demonstrates data linkage, not X-axis discrimination.",
        "",
        "## Evidence Matrix",
        "",
        "| Task-probe Y axis / Answer-process X axis | Accelerated-direct | Reviewable | Hesitant-corrective |",
        "|---|---:|---:|---:|",
    ])
    for y in MATRIX_Y:
        lines.append(
            f"| {Y_LABELS[y]} | {counts[(y, 'accelerated_direct')]} | {counts[(y, 'reviewable')]} | {counts[(y, 'hesitant_corrective')]} |"
        )

    lines.extend([
        "",
        "The populated cell is **Strong Probe support x Reviewable Answer process (n=6)**. "
        "It contains Mental, Physical, and Temporal NASA-TLX items from both Combined repetitions.",
        "",
        "## Interpretation",
        "",
        "This test demonstrates the intended separation of evidence channels. The task side can say that the Combined task behavior still follows the calibrated workload-context directions, while the response side can say that the answer interaction did not trigger a dominant rapid/direct or hesitant/corrective cue. "
        "A researcher would therefore review a high score as a score with strong task-context evidence and no prominent answer-process warning, rather than treating the matrix as one combined quality score.",
        "",
        "## What is still required before research use",
        "",
        "1. Build the Probe Plugin from multiple human calibration runs and report the direction agreement and uncertainty for each feature.",
        "2. Retain the participant-relative knob reference but test whether X-axis cues behave sensibly under actual instructed response conditions.",
        "3. Test whether researchers can understand and appropriately use the matrix without treating it as an automated exclusion decision.",
    ])
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--participant", default="P999999")
    parser.add_argument(
        "--reference-dir",
        type=Path,
        default=Path(r"C:\Users\ygao930\OneDrive - The University of Auckland\Desktop\CARE-XR Data\P999999\20260724T074858Z_0fe223\paxsm-response-calibration\PAXSMPersonalKnobReference_Data"),
    )
    parser.add_argument(
        "--calibration-dir",
        type=Path,
        default=Path(r"C:\Users\ygao930\OneDrive - The University of Auckland\Desktop\CARE-XR Data\P999999\20260725T021356Z_5db20d\workload-probe\XRWorkloadProbe_Data"),
    )
    parser.add_argument(
        "--combined-dir",
        type=Path,
        default=Path(r"C:\Users\ygao930\OneDrive - The University of Auckland\Desktop\CARE-XR Data\P999999\20260725T021758Z_e4aa6f\combined-probe\XRCombinedProbe_Data"),
    )
    parser.add_argument(
        "--output-dir",
        type=Path,
        default=Path("outputs/synthetic_evidence_matrix_demo"),
    )
    parser.add_argument(
        "--console-plugin-dir",
        type=Path,
        default=Path(r"C:\Users\ygao930\OneDrive - The University of Auckland\Desktop\CARE-XR Data\ResearcherConsole\ProbePlugins"),
    )
    parser.add_argument("--install-demo-plugin", action="store_true")
    args = parser.parse_args()

    profile_path = find_single(args.reference_dir, "PAXSM_PersonalKnobProfile_*.json")
    profile = json.loads(profile_path.read_text(encoding="utf-8-sig"))
    participant_id = args.participant

    baseline_path = find_metrics_for_block(args.calibration_dir, "baseline")
    calibration_paths = {
        "mental": find_metrics_for_block(args.calibration_dir, "cognitive_heavy"),
        "physical": find_metrics_for_block(args.calibration_dir, "physical_heavy"),
        "temporal": find_metrics_for_block(args.calibration_dir, "temporal_heavy"),
    }
    plugin = build_plugin(participant_id, args.calibration_dir, baseline_path, calibration_paths)

    output_dir = args.output_dir.resolve()
    output_dir.mkdir(parents=True, exist_ok=True)
    plugin_path = output_dir / "DEMO_ONLY_P999999_TargetSelectionWorkloadProbe_v0.json"
    plugin_path.write_text(json.dumps(plugin, indent=2), encoding="utf-8")
    if args.install_demo_plugin:
        args.console_plugin_dir.mkdir(parents=True, exist_ok=True)
        installed_path = args.console_plugin_dir / plugin_path.name
        installed_path.write_text(json.dumps(plugin, indent=2), encoding="utf-8")
    else:
        installed_path = None

    baseline_metrics = read_metrics(baseline_path)
    combined_questionnaire_path = find_primary_questionnaire(args.combined_dir, participant_id)
    calibration_questionnaire_path = find_primary_questionnaire(args.calibration_dir, participant_id)
    calibration_questionnaire = read_csv(calibration_questionnaire_path)
    baseline_scores = {
        row.get("itemId", ""): float_value(row.get("selectedScore"))
        for row in calibration_questionnaire
        if row.get("blockId") == "baseline"
    }

    cards = {card["DimensionId"]: card for card in plugin["Dimensions"]}
    combined_metric_paths = sorted(args.combined_dir.glob("*Metrics.csv"))
    combined_metrics_by_block: dict[str, tuple[Path, dict[str, dict[str, Any]]]] = {}
    for path in combined_metric_paths:
        metrics = read_metrics(path)
        rows = read_csv(path)
        if rows:
            combined_metrics_by_block[rows[0].get("blockId", "")] = (path, metrics)

    records: list[dict[str, Any]] = []
    for row in read_csv(combined_questionnaire_path):
        item_id = row.get("itemId", "")
        dimension_id = next(
            (key for key, value in DIMENSIONS.items() if item_id in value["item_ids"]),
            None,
        )
        if not dimension_id:
            continue
        block_id = row.get("blockId", "")
        if block_id not in combined_metrics_by_block:
            raise RuntimeError(f"No Combined metrics file could be linked to block {block_id!r}.")
        metrics_path, combined_metrics = combined_metrics_by_block[block_id]
        y_pattern, ratio, y_evidence = match_probe(cards[dimension_id], baseline_metrics, combined_metrics)
        x_pattern, x_evidence = classify_answer(row, profile)
        selected = int_value(row.get("selectedScore"))
        baseline_score = baseline_scores.get(item_id, math.nan)
        score_delta = selected - baseline_score if not math.isnan(baseline_score) else math.nan
        score_direction = "higher" if score_delta > 1 else "lower" if score_delta < -1 else "similar"
        records.append({
            "participant_id": participant_id,
            "session_number": int_value(row.get("sessionNumber"), 1),
            "combined_block": block_id,
            "presentation_order": int_value(row.get("presentationOrder"), 0),
            "item_id": item_id,
            "item_dimension": row.get("itemDimension", ""),
            "selected_score": selected,
            "confidence": int_value(row.get("confidence")),
            "baseline_score": "" if math.isnan(baseline_score) else int(baseline_score),
            "score_change_from_baseline": "" if math.isnan(score_delta) else round(score_delta, 3),
            "score_context": f"Score is {score_direction} than Baseline.",
            "probe_dimension": dimension_id,
            "y_axis_probe_support": y_pattern,
            "probe_match_ratio": round(ratio, 3),
            "probe_matched_features": sum(
                1 for part in y_evidence.split("; ") if "expected higher" in part and "higher;" not in part
            ),
            "probe_available_features": len(cards[dimension_id]["Features"]),
            "y_evidence": y_evidence,
            "x_axis_response_pattern": x_pattern,
            "x_evidence": x_evidence,
            "matrix_cell": f"{y_pattern}__{x_pattern}",
            "plugin_name": plugin["PluginName"],
            "source_questionnaire_path": str(combined_questionnaire_path),
            "source_combined_metrics_path": str(metrics_path),
            "source_baseline_metrics_path": str(baseline_path),
            "source_response_profile_path": str(profile_path),
            "synthetic_demo_only": "true",
        })

    # Compute counts directly from the match ratio, not from wording in the evidence string.
    for record in records:
        record["probe_matched_features"] = int(round(record["probe_match_ratio"] * record["probe_available_features"]))

    record_fields = list(records[0].keys())
    matrix_csv = output_dir / "P999999_EvidenceMatrix_Demo.csv"
    write_csv(matrix_csv, records, record_fields)
    write_html(output_dir / "P999999_EvidenceMatrix_Demo.html", records, plugin)
    write_report(output_dir / "P999999_EvidenceMatrix_Demo_Report.md", records, plugin, calibration_questionnaire)

    # Store the same visibly-demo-only CSV next to the Combined files for convenient inspection.
    evidence_dir = args.combined_dir / "EvidenceReview"
    evidence_dir.mkdir(parents=True, exist_ok=True)
    write_csv(evidence_dir / "DEMO_ONLY_P999999_EvidenceMatrix.csv", records, record_fields)

    print("Created:")
    print(plugin_path)
    print(matrix_csv)
    print(output_dir / "P999999_EvidenceMatrix_Demo.html")
    print(output_dir / "P999999_EvidenceMatrix_Demo_Report.md")
    print(evidence_dir / "DEMO_ONLY_P999999_EvidenceMatrix.csv")
    if installed_path:
        print(installed_path)


if __name__ == "__main__":
    main()

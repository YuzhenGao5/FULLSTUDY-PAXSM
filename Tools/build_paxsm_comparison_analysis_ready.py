#!/usr/bin/env python3
"""Build the two-row, single-factor PAXSM Comparison analysis table."""

from __future__ import annotations

import argparse
import json
from datetime import datetime, timezone
from pathlib import Path

import numpy as np
import pandas as pd


SCHEMA_VERSION = "CAREXR_PAXSMComparison_AnalysisReady_v3"
METHODS = ("paxsm", "point_click")
EXPECTED_INPUT_ITEMS = 8
NASA_ITEMS = (
    ("Mental", "nasa_tlx_mental"),
    ("Physical", "nasa_tlx_physical"),
    ("Temporal", "nasa_tlx_temporal"),
    ("Performance", "nasa_tlx_performance"),
    ("Effort", "nasa_tlx_effort"),
    ("Frustration", "nasa_tlx_frustration"),
)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Create the single-factor PAXSMComparison_AnalysisReady CSV."
    )
    parser.add_argument("input_dir", type=Path, help="PAXSMComparison_Data directory")
    parser.add_argument("--output", type=Path, help="Optional output CSV path")
    return parser.parse_args()


def newest_completed(root: Path, prefix: str) -> Path:
    matches = sorted(root.glob(f"{prefix}*_completed.csv"), key=lambda p: p.stat().st_mtime)
    if not matches:
        raise FileNotFoundError(f"No completed file found for {prefix!r} in {root}")
    return matches[-1]


def numeric(series: pd.Series) -> pd.Series:
    return pd.to_numeric(series, errors="coerce")


def safe_mean(series: pd.Series) -> float:
    values = numeric(series).dropna()
    return float(values.mean()) if len(values) else np.nan


def safe_median(series: pd.Series) -> float:
    values = numeric(series).dropna()
    return float(values.median()) if len(values) else np.nan


def safe_sd(series: pd.Series) -> float:
    values = numeric(series).dropna()
    return float(values.std(ddof=1)) if len(values) > 1 else np.nan


def safe_nonnegative_mean(series: pd.Series) -> float:
    values = numeric(series)
    values = values[values.ge(0)].dropna()
    return float(values.mean()) if len(values) else np.nan


def page_stage_total(pages: pd.DataFrame, block_id: str, stage: str) -> float:
    if pages.empty:
        return np.nan
    data = pages[pages["blockId"].eq(block_id) & pages["stage"].eq(stage)]
    if data.empty:
        return np.nan
    return float(numeric(data["pageRt"]).clip(lower=0).fillna(0).sum())


def run_id_from(root: Path) -> str:
    for parent in (root, *root.parents):
        manifest = parent / "experiment_run_manifest.json"
        if manifest.exists():
            try:
                return str(json.loads(manifest.read_text(encoding="utf-8-sig")).get("runId", ""))
            except (OSError, json.JSONDecodeError):
                return ""
    return ""


def input_summary(questionnaire: pd.DataFrame, method: str) -> dict[str, object]:
    data = questionnaire[questionnaire["blockId"].eq(f"input_check_{method}")].copy()
    targets = numeric(data["itemId"].str.extract(r"_(\d+)$", expand=False))
    selected = numeric(data["selectedScore"])
    valid = targets.notna() & selected.gt(0)
    errors = (selected[valid] - targets[valid]).abs()
    first = numeric(data["answerFirstInteractionRt"])
    first = first[first.ge(0)]
    response_mode = data["responseMode"].dropna().astype(str)
    return {
        "inputResponseMode": response_mode.iloc[0] if len(response_mode) else "",
        "inputItems": int(len(data)),
        "inputExactMatches": int(errors.eq(0).sum()),
        "inputAccuracy": float(errors.eq(0).mean()) if len(errors) else np.nan,
        "inputMeanAbsoluteError": float(errors.mean()) if len(errors) else np.nan,
        "inputMaxAbsoluteError": int(errors.max()) if len(errors) else np.nan,
        "inputMeanAnswerRt": safe_mean(data["answerRt"]),
        "inputMedianAnswerRt": safe_median(data["answerRt"]),
        "inputValidFirstInteractionItems": int(len(first)),
        "inputMeanFirstInteractionRt": float(first.mean()) if len(first) else np.nan,
        "inputConfirmAttempts": int(numeric(data["answerConfirmAttemptCount"]).fillna(0).sum()),
        "inputConfirmCancels": int(numeric(data["answerConfirmCancelCount"]).fillna(0).sum()),
        "inputIncompleteItems": int((~valid).sum()),
    }


def sus_summary(
    questionnaire: pd.DataFrame, pages: pd.DataFrame, method: str
) -> dict[str, object]:
    data = questionnaire[questionnaire["blockId"].eq(f"sus_{method}")].copy()
    data = data.sort_values("itemIndex")
    responses: dict[int, int] = {}
    total_rt = 0.0
    contribution = 0.0
    for row in data.itertuples(index=False):
        item = int(row.itemIndex)
        response = int(row.selectedScore)
        if 1 <= item <= 10 and 1 <= response <= 5 and item not in responses:
            responses[item] = response
            contribution += response - 1 if item % 2 else 5 - response
        total_rt += max(0.0, float(row.readRt)) + max(0.0, float(row.answerRt))
    page_rt = page_stage_total(pages, f"sus_{method}", "Answer")
    result: dict[str, object] = {
        "susAnsweredItems": len(responses),
        "susScore": contribution * 2.5 if responses else np.nan,
        "susComplete": int(len(responses) == 10),
        "susTotalRt": page_rt if not np.isnan(page_rt) else (total_rt if responses else np.nan),
    }
    for item in range(1, 11):
        result[f"sus{item:02d}"] = responses.get(item, np.nan)
    return result


def method_summary(
    arithmetic: pd.DataFrame,
    questionnaire: pd.DataFrame,
    pages: pd.DataFrame,
    method: str,
    collect_confidence: bool,
) -> dict[str, object]:
    task = arithmetic[arithmetic["method"].eq(method)].copy()
    demand_column = "taskDemand" if "taskDemand" in task.columns else "difficulty"
    result: dict[str, object] = {
        "methodPresentationOrder": int(task["presentationOrder"].min()) if len(task) else np.nan,
        "taskForm": str(task["form"].iloc[0]) if len(task) else "",
        "taskDemand": str(task[demand_column].iloc[0]) if len(task) else "",
        "taskTrials": int(len(task)),
        "taskCorrect": int(numeric(task["isCorrect"]).fillna(0).sum()),
        "taskAccuracy": safe_mean(task["isCorrect"]),
        "taskTimeouts": int(numeric(task["timeout"]).fillna(0).sum()),
        "taskMeanDecisionRt": safe_mean(task["decisionRt"]),
        "taskMedianDecisionRt": safe_median(task["decisionRt"]),
        "taskSdDecisionRt": safe_sd(task["decisionRt"]),
        "taskTotalDecisionRt": float(numeric(task["decisionRt"]).fillna(0).sum()) if len(task) else np.nan,
        "taskMeanPointerPath": safe_mean(task["pointerPath"]),
        "taskMeanPointerPeakSpeed": safe_mean(task["pointerPeakSpeed"]),
        "taskMeanHoverChanges": safe_mean(task["hoverChangeCount"]),
    }

    nasa = questionnaire[questionnaire["blockId"].eq(f"comparison_{method}")].copy()
    nasa = nasa[nasa["itemId"].isin([item_id for _, item_id in NASA_ITEMS])]
    response_mode = nasa["responseMode"].dropna().astype(str)
    result["nasaResponseMode"] = response_mode.iloc[0] if len(response_mode) else ""
    result["nasaItems"] = int(nasa["itemId"].nunique())
    result["nasaComplete"] = int(nasa["itemId"].nunique() == 6)
    ratings: list[float] = []
    confidences: list[float] = []
    for label, item_id in NASA_ITEMS:
        item = nasa[nasa["itemId"].eq(item_id)]
        rating = float(item["selectedScore"].iloc[0]) if len(item) else np.nan
        confidence = float(item["confidence"].iloc[0]) if len(item) else np.nan
        result[f"nasa{label}"] = rating
        result[f"confidence{label}"] = confidence
        if not np.isnan(rating):
            ratings.append(rating)
        if not np.isnan(confidence) and 1 <= confidence <= 5:
            confidences.append(confidence)
    result["nasaRawMean"] = float(np.mean(ratings)) if ratings else np.nan
    result["confidenceComplete"] = int(not collect_confidence or len(confidences) == 6)
    result["confidenceMean"] = float(np.mean(confidences)) if confidences else np.nan
    result["questionnaireMeanReadRt"] = safe_nonnegative_mean(nasa["readRt"])
    result["questionnaireMeanAnswerRt"] = safe_mean(nasa["answerRt"])
    result["questionnaireMeanAnswerDecisionRt"] = safe_mean(nasa["answerDecisionRt"])
    confidence_rt = numeric(nasa["confidenceRt"])
    valid_confidence_rt = confidence_rt[confidence_rt.gt(0)]
    result["questionnaireMeanConfidenceRt"] = (
        float(valid_confidence_rt.mean()) if len(valid_confidence_rt) else np.nan
    )
    total_rt = (
        numeric(nasa["readRt"]).clip(lower=0).fillna(0).sum()
        + numeric(nasa["answerRt"]).clip(lower=0).fillna(0).sum()
        + numeric(nasa["confidenceRt"]).clip(lower=0).fillna(0).sum()
    )
    answer_page_rt = page_stage_total(pages, f"comparison_{method}", "Answer")
    if not np.isnan(answer_page_rt):
        confidence_page_rt = page_stage_total(pages, f"comparison_{method}", "Confidence")
        total_rt = answer_page_rt + (0.0 if np.isnan(confidence_page_rt) else confidence_page_rt)
    result["questionnaireTotalRt"] = float(total_rt) if len(nasa) else np.nan
    result["answerConfirmAttempts"] = int(numeric(nasa["answerConfirmAttemptCount"]).fillna(0).sum())
    result["answerConfirmCancels"] = int(numeric(nasa["answerConfirmCancelCount"]).fillna(0).sum())
    return result


def quality_flags(
    row: dict[str, object], expected_trials: int, collect_confidence: bool
) -> str:
    flags: list[str] = []
    if row["inputItems"] != EXPECTED_INPUT_ITEMS:
        flags.append(f"input_items_{row['inputItems']}_of_{EXPECTED_INPUT_ITEMS}")
    if row["inputIncompleteItems"]:
        flags.append(f"input_incomplete_{row['inputIncompleteItems']}")
    if row["susAnsweredItems"] != 10:
        flags.append(f"sus_items_{row['susAnsweredItems']}_of_10")
    if row["taskTrials"] != expected_trials:
        flags.append(f"task_items_{row['taskTrials']}_of_{expected_trials}")
    if row["nasaItems"] != 6:
        flags.append(f"nasa_items_{row['nasaItems']}_of_6")
    if collect_confidence and row["confidenceComplete"] != 1:
        flags.append("confidence_incomplete")
    return ";".join(flags) if flags else "none"


def build(root: Path, output: Path | None) -> Path:
    root = root.resolve()
    manifest_path = newest_completed(root, "PAXSMComparison_Manifest_")
    arithmetic_path = newest_completed(root, "PAXSMComparison_Arithmetic_")
    manifest = pd.read_csv(manifest_path).iloc[0]
    participant_id = str(manifest["participantId"])
    questionnaire_path = newest_completed(root, f"CAREXR_Questionnaire_{participant_id}_")
    pages_path = newest_completed(root, "PAXSMComparison_PointClickPages_")
    arithmetic = pd.read_csv(arithmetic_path)
    questionnaire = pd.read_csv(questionnaire_path)
    pages = pd.read_csv(pages_path)

    suffix = manifest_path.name[len("PAXSMComparison_Manifest_") : -len(".csv")]
    output = output.resolve() if output else root.parent / f"PAXSMComparison_AnalysisReady_{suffix}.csv"
    trials_field = "arithmeticTrialsPerMethod" if "arithmeticTrialsPerMethod" in manifest else "arithmeticTrialsPerBlock"
    expected_trials = int(manifest[trials_field])
    collect_confidence = bool(int(manifest["confidenceAfterWorkload"]))
    generated_utc = datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")

    rows: list[dict[str, object]] = []
    for method in METHODS:
        row: dict[str, object] = {
            "analysisSchemaVersion": SCHEMA_VERSION,
            "participantId": participant_id,
            "sessionNumber": int(manifest["sessionNumber"]),
            "conditionLabel": str(manifest["conditionLabel"]),
            "runId": run_id_from(root),
            "sequenceCode": str(manifest["sequenceCode"]),
            "method": method,
            "exportReason": "completed",
            "generatedUtc": generated_utc,
        }
        row.update(method_summary(arithmetic, questionnaire, pages, method, collect_confidence))
        row.update(input_summary(questionnaire, method))
        row.update(sus_summary(questionnaire, pages, method))
        flags = quality_flags(row, expected_trials, collect_confidence)
        row["methodComplete"] = int(flags == "none")
        row["qualityFlags"] = flags
        rows.append(row)

    base_columns = [
        "analysisSchemaVersion", "participantId", "sessionNumber", "conditionLabel", "runId",
        "sequenceCode", "method", "methodPresentationOrder", "taskForm", "taskDemand",
        "methodComplete", "qualityFlags", "exportReason", "generatedUtc",
        "inputResponseMode", "inputItems", "inputExactMatches", "inputAccuracy",
        "inputMeanAbsoluteError", "inputMaxAbsoluteError", "inputMeanAnswerRt",
        "inputMedianAnswerRt", "inputValidFirstInteractionItems", "inputMeanFirstInteractionRt",
        "inputConfirmAttempts", "inputConfirmCancels", "inputIncompleteItems",
        "susAnsweredItems", "susScore", "susComplete", "susTotalRt",
        *[f"sus{i:02d}" for i in range(1, 11)],
        "taskTrials", "taskCorrect", "taskAccuracy", "taskTimeouts", "taskMeanDecisionRt",
        "taskMedianDecisionRt", "taskSdDecisionRt", "taskTotalDecisionRt",
        "taskMeanPointerPath", "taskMeanPointerPeakSpeed", "taskMeanHoverChanges",
        "nasaResponseMode", "nasaItems", "nasaComplete",
        *[f"nasa{label}" for label, _ in NASA_ITEMS],
        "nasaRawMean", "confidenceComplete", "confidenceMean",
        *[f"confidence{label}" for label, _ in NASA_ITEMS],
        "questionnaireMeanReadRt", "questionnaireMeanAnswerRt",
        "questionnaireMeanAnswerDecisionRt", "questionnaireMeanConfidenceRt",
        "questionnaireTotalRt", "answerConfirmAttempts", "answerConfirmCancels",
    ]
    frame = pd.DataFrame(rows, columns=base_columns)
    output.parent.mkdir(parents=True, exist_ok=True)
    frame.to_csv(output, index=False, encoding="utf-8-sig", float_format="%.4f")
    return output


def main() -> None:
    args = parse_args()
    output = build(args.input_dir, args.output)
    print(output)


if __name__ == "__main__":
    main()

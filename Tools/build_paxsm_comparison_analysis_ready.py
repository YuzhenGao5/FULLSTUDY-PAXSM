#!/usr/bin/env python3
"""Rebuild the two-row Study 1 PAXSM input-method comparison table."""

from __future__ import annotations

import argparse
import json
from datetime import datetime, timezone
from pathlib import Path

import numpy as np
import pandas as pd


SCHEMA_VERSION = "CAREXR_PAXSMComparison_AnalysisReady_v4"
METHODS = ("paxsm", "point_click")
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
        description="Create the Study 1 PAXSMComparison_AnalysisReady CSV."
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


def formal_summary(formal: pd.DataFrame, method: str) -> dict[str, object]:
    data = formal[formal["method"].eq(method)].copy()
    completed = numeric(data["completed"]).fillna(0).gt(0)
    errors = numeric(data.loc[completed, "absoluteError"]).dropna()
    exact = numeric(data.loc[completed, "exactMatch"]).fillna(0).gt(0)
    correction = numeric(data.loc[completed, "correctionOccurred"]).fillna(0).gt(0)
    correction_events = numeric(data["correctionCount"]).fillna(0)
    first = numeric(data["firstInteractionRt"])
    first = first[first.ge(0)]
    completion_times = numeric(data.loc[completed, "completionTime"]).dropna()
    response_mode = data["responseMode"].dropna().astype(str)
    return {
        "formalResponseMode": response_mode.iloc[0] if len(response_mode) else "",
        "formalItems": int(len(data)),
        "formalCompletedItems": int(completed.sum()),
        "formalCompletionRate": float(completed.mean()) if len(data) else np.nan,
        "formalExactMatches": int(exact.sum()),
        "formalAccuracy": float(exact.mean()) if len(exact) else np.nan,
        "formalMeanAbsoluteError": float(errors.mean()) if len(errors) else np.nan,
        "formalMaxAbsoluteError": int(errors.max()) if len(errors) else np.nan,
        "formalMeanCompletionTime": float(completion_times.mean()) if len(completion_times) else np.nan,
        "formalMedianCompletionTime": float(completion_times.median()) if len(completion_times) else np.nan,
        "formalSdCompletionTime": float(completion_times.std(ddof=1)) if len(completion_times) > 1 else np.nan,
        "formalTotalCompletionTime": float(completion_times.sum()) if len(completion_times) else np.nan,
        "formalValidFirstInteractionItems": int(len(first)),
        "formalMeanFirstInteractionRt": float(first.mean()) if len(first) else np.nan,
        "formalCorrectedTrials": int(correction.sum()),
        "formalCorrectionRate": float(correction.mean()) if len(correction) else np.nan,
        "formalCorrectionEvents": int(correction_events.sum()),
        "formalConfirmAttempts": int(numeric(data["confirmAttempts"]).fillna(0).sum()),
        "formalConfirmCancels": int(numeric(data["confirmCancels"]).fillna(0).sum()),
        "formalIncompleteItems": int((~completed).sum()),
    }


def sus_summary(questionnaire: pd.DataFrame, pages: pd.DataFrame, method: str) -> dict[str, object]:
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


def nasa_summary(questionnaire: pd.DataFrame, pages: pd.DataFrame, method: str) -> dict[str, object]:
    nasa = questionnaire[questionnaire["blockId"].eq(f"comparison_{method}")].copy()
    nasa = nasa[nasa["itemId"].isin([item_id for _, item_id in NASA_ITEMS])]
    response_mode = nasa["responseMode"].dropna().astype(str)
    result: dict[str, object] = {
        "nasaResponseMode": response_mode.iloc[0] if len(response_mode) else "",
        "nasaItems": int(nasa["itemId"].nunique()),
        "nasaComplete": int(nasa["itemId"].nunique() == 6),
    }
    workload_coded: list[float] = []
    for label, item_id in NASA_ITEMS:
        item = nasa[nasa["itemId"].eq(item_id)]
        rating = float(item["selectedScore"].iloc[0]) if len(item) else np.nan
        result[f"nasa{label}"] = rating
        if not np.isnan(rating):
            scale = float(item["scale"].iloc[0]) if "scale" in item else 21.0
            workload_coded.append(scale + 1.0 - rating if label == "Performance" else rating)
    performance = result.get("nasaPerformance", np.nan)
    result["nasaPerformanceWorkloadCoded"] = (
        22.0 - float(performance) if not np.isnan(performance) else np.nan
    )
    result["nasaRawMean"] = float(np.mean(workload_coded)) if workload_coded else np.nan
    result["questionnaireMeanReadRt"] = safe_nonnegative_mean(nasa["readRt"])
    result["questionnaireMeanAnswerRt"] = safe_mean(nasa["answerRt"])
    result["questionnaireMeanAnswerDecisionRt"] = safe_mean(nasa["answerDecisionRt"])
    total_rt = (
        numeric(nasa["readRt"]).clip(lower=0).fillna(0).sum()
        + numeric(nasa["answerRt"]).clip(lower=0).fillna(0).sum()
    )
    answer_page_rt = page_stage_total(pages, f"comparison_{method}", "Answer")
    if not np.isnan(answer_page_rt):
        total_rt = answer_page_rt
    result["questionnaireTotalRt"] = float(total_rt) if len(nasa) else np.nan
    result["answerConfirmAttempts"] = int(numeric(nasa["answerConfirmAttemptCount"]).fillna(0).sum())
    result["answerConfirmCancels"] = int(numeric(nasa["answerConfirmCancelCount"]).fillna(0).sum())
    return result


def quality_flags(row: dict[str, object], expected_formal: int) -> str:
    flags: list[str] = []
    if row["formalItems"] != expected_formal:
        flags.append(f"formal_items_{row['formalItems']}_of_{expected_formal}")
    if row["formalIncompleteItems"]:
        flags.append(f"formal_incomplete_{row['formalIncompleteItems']}")
    if row["susAnsweredItems"] != 10:
        flags.append(f"sus_items_{row['susAnsweredItems']}_of_10")
    if row["nasaItems"] != 6:
        flags.append(f"nasa_items_{row['nasaItems']}_of_6")
    return ";".join(flags) if flags else "none"


def build(root: Path, output: Path | None) -> Path:
    root = root.resolve()
    manifest_path = newest_completed(root, "PAXSMComparison_Manifest_")
    formal_path = newest_completed(root, "PAXSMComparison_FormalInput_")
    manifest = pd.read_csv(manifest_path).iloc[0]
    participant_id = str(manifest["participantId"])
    questionnaire_path = newest_completed(root, f"CAREXR_Questionnaire_{participant_id}_")
    pages_path = newest_completed(root, "PAXSMComparison_PointClickPages_")
    formal = pd.read_csv(formal_path)
    questionnaire = pd.read_csv(questionnaire_path)
    pages = pd.read_csv(pages_path)

    suffix = manifest_path.name[len("PAXSMComparison_Manifest_") : -len(".csv")]
    output = output.resolve() if output else root.parent / f"PAXSMComparison_AnalysisReady_{suffix}.csv"
    expected_formal = int(manifest["formalItemsPerMethod"])
    generated_utc = datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")

    rows: list[dict[str, object]] = []
    for method in METHODS:
        method_formal = formal[formal["method"].eq(method)]
        row: dict[str, object] = {
            "analysisSchemaVersion": SCHEMA_VERSION,
            "participantId": participant_id,
            "sessionNumber": int(manifest["sessionNumber"]),
            "conditionLabel": str(manifest["conditionLabel"]),
            "runId": run_id_from(root),
            "sequenceCode": str(manifest["sequenceCode"]),
            "method": method,
            "methodPresentationOrder": int(numeric(method_formal["presentationOrder"]).min()) if len(method_formal) else np.nan,
            "targetOrderForm": str(method_formal["targetOrderForm"].iloc[0]) if len(method_formal) else "",
            "exportReason": "completed",
            "generatedUtc": generated_utc,
        }
        row.update(formal_summary(formal, method))
        row.update(sus_summary(questionnaire, pages, method))
        row.update(nasa_summary(questionnaire, pages, method))
        flags = quality_flags(row, expected_formal)
        row["methodComplete"] = int(flags == "none")
        row["qualityFlags"] = flags
        rows.append(row)

    columns = [
        "analysisSchemaVersion", "participantId", "sessionNumber", "conditionLabel", "runId",
        "sequenceCode", "method", "methodPresentationOrder", "targetOrderForm",
        "methodComplete", "qualityFlags", "exportReason", "generatedUtc",
        "formalResponseMode", "formalItems", "formalCompletedItems", "formalCompletionRate",
        "formalExactMatches", "formalAccuracy", "formalMeanAbsoluteError", "formalMaxAbsoluteError",
        "formalMeanCompletionTime", "formalMedianCompletionTime", "formalSdCompletionTime",
        "formalTotalCompletionTime", "formalValidFirstInteractionItems", "formalMeanFirstInteractionRt",
        "formalCorrectedTrials", "formalCorrectionRate", "formalCorrectionEvents",
        "formalConfirmAttempts", "formalConfirmCancels", "formalIncompleteItems",
        "susAnsweredItems", "susScore", "susComplete", "susTotalRt",
        *[f"sus{i:02d}" for i in range(1, 11)],
        "nasaResponseMode", "nasaItems", "nasaComplete",
        *[f"nasa{label}" for label, _ in NASA_ITEMS[:4]],
        "nasaPerformanceWorkloadCoded", "nasaEffort", "nasaFrustration", "nasaRawMean",
        "questionnaireMeanReadRt", "questionnaireMeanAnswerRt",
        "questionnaireMeanAnswerDecisionRt", "questionnaireTotalRt",
        "answerConfirmAttempts", "answerConfirmCancels",
    ]
    frame = pd.DataFrame(rows, columns=columns)
    output.parent.mkdir(parents=True, exist_ok=True)
    frame.to_csv(output, index=False, encoding="utf-8-sig", float_format="%.4f")
    return output


def main() -> None:
    args = parse_args()
    print(build(args.input_dir, args.output))


if __name__ == "__main__":
    main()

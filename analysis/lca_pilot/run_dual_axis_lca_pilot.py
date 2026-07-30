"""Run an exploratory, two-axis latent class analysis for CARE-XR data.

This script deliberately keeps the two evidence streams separate:

* X / response-process LCA: participant-relative PAXSM Answer traces.
* Y / task-context LCA: participant-relative workload-probe block behaviour.

It is designed for transparent pilot exploration, not for assigning a
"careless" label.  The current four-participant dataset is too small for a
publication-grade LCA; this script preserves all derived inputs and posterior
probabilities so the same procedure can be rerun once the study is complete.

Only Python's standard library is required.
"""

from __future__ import annotations

import argparse
import csv
import html
import math
import random
from collections import Counter, defaultdict
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Iterable


DEFAULT_DATA_ROOT = Path(
    r"C:\Users\ygao930\OneDrive - The University of Auckland\Desktop\CARE-XR Data"
)
DEFAULT_PARTICIPANTS = ("P002", "P003", "P004", "P515", "P516")
EPSILON = 1e-9


def read_csv(path: Path) -> list[dict[str, str]]:
    with path.open("r", encoding="utf-8-sig", newline="") as handle:
        return list(csv.DictReader(handle))


def write_csv(path: Path, rows: list[dict[str, Any]]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    fieldnames = sorted({key for row in rows for key in row}) if rows else []
    with path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=fieldnames, extrasaction="ignore")
        writer.writeheader()
        writer.writerows(rows)


def as_float(value: Any) -> float | None:
    try:
        if value is None or str(value).strip() == "":
            return None
        return float(value)
    except (TypeError, ValueError):
        return None


def fmt(value: float | None, digits: int = 3) -> str:
    return "" if value is None else f"{value:.{digits}f}"


def latest_matching(root: Path, pattern: str, required_path_parts: Iterable[str] = ()) -> Path | None:
    required = tuple(part.lower() for part in required_path_parts)
    matches = []
    for candidate in root.rglob(pattern):
        rendered = str(candidate).lower()
        if all(part in rendered for part in required):
            matches.append(candidate)
    return max(matches, key=lambda item: item.stat().st_mtime) if matches else None


def questionnaire_file(run_dir: Path, participant_id: str) -> Path | None:
    candidates = [
        path
        for path in run_dir.glob(f"CAREXR_Questionnaire_{participant_id}_*_completed.csv")
        if "metadata" not in path.name.lower()
        and "interaction" not in path.name.lower()
        and "rawtrace" not in path.name.lower()
        and "speed" not in path.name.lower()
    ]
    return max(candidates, key=lambda item: item.stat().st_mtime) if candidates else None


def relative_state(value: float | None, p10: float | None, p90: float | None) -> str:
    """Three-way state relative to this participant's calibration distribution."""
    if value is None or p10 is None or p90 is None:
        return "missing"
    if abs(p90 - p10) < EPSILON:
        if value < p10 - EPSILON:
            return "low"
        if value > p90 + EPSILON:
            return "high"
        return "typical"
    if value <= p10:
        return "low"
    if value >= p90:
        return "high"
    return "typical"


def change_state(value: float | None, baseline: float | None, tolerance: float = 0.10) -> str:
    """A simple within-person practical change state relative to the baseline block."""
    if value is None or baseline is None:
        return "missing"
    denominator = max(abs(baseline), 0.01)
    relative_change = (value - baseline) / denominator
    if relative_change > tolerance:
        return "higher"
    if relative_change < -tolerance:
        return "lower"
    return "same"


def aggregate_change(states: Iterable[str]) -> str:
    score = {"lower": -1, "same": 0, "higher": 1}
    observed = [score[state] for state in states if state in score]
    if not observed:
        return "missing"
    average = sum(observed) / len(observed)
    if average >= 0.5:
        return "higher"
    if average <= -0.5:
        return "lower"
    return "same"


def profile_reference(profile_rows: list[dict[str, str]]) -> dict[tuple[str, str], dict[str, float | None]]:
    references: dict[tuple[str, str], dict[str, float | None]] = {}
    for row in profile_rows:
        if row.get("distanceBin") != "global":
            continue
        stage = row.get("stage", "")
        metric = row.get("metric", "")
        if stage not in {"Answer", "Confidence"}:
            continue
        references[(stage, metric)] = {
            "median": as_float(row.get("median")),
            "p10": as_float(row.get("p10")),
            "p90": as_float(row.get("p90")),
        }
    return references


def reference_state(
    references: dict[tuple[str, str], dict[str, float | None]],
    stage: str,
    metric: str,
    value: float | None,
) -> str:
    reference = references.get((stage, metric), {})
    return relative_state(value, reference.get("p10"), reference.get("p90"))


def make_x_rows(
    participant_id: str,
    profile_path: Path,
    combined_questionnaire_path: Path,
) -> list[dict[str, Any]]:
    references = profile_reference(read_csv(profile_path))
    rows: list[dict[str, Any]] = []
    for item in read_csv(combined_questionnaire_path):
        if not item.get("itemId") or as_float(item.get("selectedScore")) is None:
            continue

        answer_decision = as_float(item.get("answerDecisionRt"))
        answer_pause = as_float(item.get("answerPauseCount"))
        answer_pause_rate = (
            answer_pause / answer_decision
            if answer_pause is not None and answer_decision is not None and answer_decision > EPSILON
            else None
        )
        speed_state = reference_state(
            references, "Answer", "max_abs_velocity", as_float(item.get("answerMaxAbsVel"))
        )
        path_base = reference_state(
            references, "Answer", "path_ratio", as_float(item.get("answerPathRatio"))
        )
        decision_base = reference_state(references, "Answer", "decision_rt", answer_decision)
        reverse_base = reference_state(
            references, "Answer", "reverse_count", as_float(item.get("answerReverseCount"))
        )
        pause_base = reference_state(references, "Answer", "pause_rate", answer_pause_rate)
        confidence_base = reference_state(
            references, "Confidence", "decision_rt", as_float(item.get("confidenceDecisionRt"))
        )

        rows.append(
            {
                "participant_id": participant_id,
                "block_id": item.get("blockId", ""),
                "task_type": item.get("taskType", ""),
                "item_id": item.get("itemId", ""),
                "dimension": item.get("itemDimension", ""),
                "rating": as_float(item.get("selectedScore")),
                "confidence": as_float(item.get("confidence")),
                "answer_speed_state": speed_state,
                "answer_path_state": {"low": "direct", "high": "extended"}.get(path_base, path_base),
                "answer_decision_state": {"low": "fast", "high": "slow"}.get(decision_base, decision_base),
                "answer_reverse_state": {"low": "few", "high": "many"}.get(reverse_base, reverse_base),
                "answer_pause_state": pause_base,
                "confidence_decision_state": {"low": "fast", "high": "slow"}.get(confidence_base, confidence_base),
                "answer_max_abs_velocity": as_float(item.get("answerMaxAbsVel")),
                "answer_path_ratio": as_float(item.get("answerPathRatio")),
                "answer_decision_rt": answer_decision,
                "answer_pause_rate": answer_pause_rate,
                "answer_reverse_count": as_float(item.get("answerReverseCount")),
                "answer_micro_adjust_count": as_float(item.get("answerMicroAdjustCount")),
                "confidence_decision_rt": as_float(item.get("confidenceDecisionRt")),
                "profile_path": str(profile_path),
                "questionnaire_path": str(combined_questionnaire_path),
            }
        )
    return rows


def metric_values(metrics_path: Path) -> dict[str, float | None]:
    values: dict[str, float | None] = {}
    for metric in read_csv(metrics_path):
        if metric.get("valid") not in {"1", "true", "True"}:
            continue
        values[metric.get("metricId", "")] = as_float(metric.get("value"))
    return values


def blocks_and_metrics(run_dir: Path, participant_id: str) -> dict[str, dict[str, float | None]]:
    block_files = list(run_dir.glob(f"WorkloadProbe_Blocks_{participant_id}_*_completed.csv"))
    if not block_files:
        return {}
    blocks_file = max(block_files, key=lambda item: item.stat().st_mtime)
    result: dict[str, dict[str, float | None]] = {}
    for row in read_csv(blocks_file):
        block_id = row.get("blockId", "")
        if not block_id:
            continue
        result[block_id] = {
            "accuracy": as_float(row.get("accuracy")),
            "mean_decision_rt": as_float(row.get("meanDecisionRt")),
            "pause_rate": (
                as_float(row.get("totalPauseCount")) / max(as_float(row.get("trials")) or 0, 1)
            ),
            "hover_rate": (
                as_float(row.get("totalHoverChangeCount")) / max(as_float(row.get("trials")) or 0, 1)
            ),
            "task_type": row.get("taskType", ""),
            "target_dimension": row.get("targetDimension", ""),
            "source_blocks_path": str(blocks_file),
        }

    for metrics_file in run_dir.glob("*Metrics.csv"):
        metric_rows = read_csv(metrics_file)
        if not metric_rows:
            continue
        block_id = metric_rows[0].get("blockId", "")
        if block_id not in result:
            continue
        values = metric_values(metrics_file)
        result[block_id].update(
            {
                "response_speed": values.get("ResponseSpeed"),
                "ray_movement_distance": values.get("RayMovementDistance"),
                "head_rotation": values.get("HeadRotation"),
                "head_movement": values.get("HeadMovement"),
                "metric_path": str(metrics_file),
            }
        )
    return result


def make_y_rows(
    participant_id: str,
    calibration_run_dir: Path,
    combined_run_dir: Path,
) -> list[dict[str, Any]]:
    calibration = blocks_and_metrics(calibration_run_dir, participant_id)
    combined = blocks_and_metrics(combined_run_dir, participant_id)
    baseline = calibration.get("baseline")
    if baseline is None:
        raise ValueError(f"{participant_id}: baseline block was not found in {calibration_run_dir}")

    raw_blocks: list[tuple[str, str, dict[str, Any]]] = [
        ("calibration", block_id, values) for block_id, values in calibration.items()
    ] + [("combined", block_id, values) for block_id, values in combined.items()]

    rows: list[dict[str, Any]] = []
    for scope, block_id, current in raw_blocks:
        pause_change = change_state(current.get("pause_rate"), baseline.get("pause_rate"))
        hover_change = change_state(current.get("hover_rate"), baseline.get("hover_rate"))
        ray_change = change_state(current.get("ray_movement_distance"), baseline.get("ray_movement_distance"))
        rotation_change = change_state(current.get("head_rotation"), baseline.get("head_rotation"))
        head_change = change_state(current.get("head_movement"), baseline.get("head_movement"))
        rows.append(
            {
                "participant_id": participant_id,
                "scope": scope,
                "block_id": block_id,
                "task_type": current.get("task_type", ""),
                "target_dimension": current.get("target_dimension", ""),
                "decision_time_change": change_state(current.get("mean_decision_rt"), baseline.get("mean_decision_rt")),
                "interaction_exploration_change": aggregate_change((pause_change, hover_change)),
                "response_speed_change": change_state(current.get("response_speed"), baseline.get("response_speed")),
                "body_movement_change": aggregate_change((ray_change, rotation_change, head_change)),
                "accuracy_change": change_state(current.get("accuracy"), baseline.get("accuracy")),
                "mean_decision_rt": current.get("mean_decision_rt"),
                "pause_rate": current.get("pause_rate"),
                "hover_rate": current.get("hover_rate"),
                "response_speed": current.get("response_speed"),
                "ray_movement_distance": current.get("ray_movement_distance"),
                "head_rotation": current.get("head_rotation"),
                "head_movement": current.get("head_movement"),
                "accuracy": current.get("accuracy"),
                "baseline_mean_decision_rt": baseline.get("mean_decision_rt"),
                "baseline_pause_rate": baseline.get("pause_rate"),
                "baseline_hover_rate": baseline.get("hover_rate"),
                "baseline_response_speed": baseline.get("response_speed"),
                "baseline_ray_movement_distance": baseline.get("ray_movement_distance"),
                "baseline_head_rotation": baseline.get("head_rotation"),
                "baseline_head_movement": baseline.get("head_movement"),
                "baseline_accuracy": baseline.get("accuracy"),
                "blocks_path": current.get("source_blocks_path", ""),
                "metrics_path": current.get("metric_path", ""),
            }
        )
    return rows


@dataclass
class LcaFit:
    k: int
    log_likelihood: float
    bic: float
    aic: float
    entropy: float
    weights: list[float]
    theta: list[list[list[float]]]
    categories: list[list[str]]
    posteriors: list[list[float]]


def log_sum_exp(values: list[float]) -> float:
    maximum = max(values)
    return maximum + math.log(sum(math.exp(value - maximum) for value in values))


def fit_lca(
    records: list[dict[str, Any]],
    features: list[str],
    k: int,
    starts: int = 96,
    max_iterations: int = 1200,
    alpha: float = 0.05,
) -> LcaFit:
    categories = [sorted({str(record[feature]) for record in records}) for feature in features]
    category_index = [{value: index for index, value in enumerate(levels)} for levels in categories]
    observed = [
        [category_index[feature_index][str(record[feature])] for feature_index, feature in enumerate(features)]
        for record in records
    ]
    n_rows = len(observed)
    if n_rows < k:
        raise ValueError(f"Cannot fit {k} classes to only {n_rows} observations.")

    best: LcaFit | None = None
    for seed in range(starts):
        rng = random.Random(20260727 + k * 1000 + seed)
        assignments = [rng.randrange(k) for _ in range(n_rows)]
        for class_index in range(k):
            assignments[class_index] = class_index

        gamma = [[1.0 if assignments[row] == class_index else 0.0 for class_index in range(k)] for row in range(n_rows)]
        previous_log_likelihood: float | None = None
        for _ in range(max_iterations):
            masses = [sum(gamma[row][class_index] for row in range(n_rows)) for class_index in range(k)]
            weights = [(mass + alpha) / (n_rows + alpha * k) for mass in masses]
            theta: list[list[list[float]]] = []
            for class_index in range(k):
                class_parameters: list[list[float]] = []
                for feature_index, levels in enumerate(categories):
                    counts = [alpha] * len(levels)
                    for row in range(n_rows):
                        counts[observed[row][feature_index]] += gamma[row][class_index]
                    total = sum(counts)
                    class_parameters.append([count / total for count in counts])
                theta.append(class_parameters)

            next_gamma: list[list[float]] = []
            log_likelihood = 0.0
            for row in range(n_rows):
                log_probabilities = []
                for class_index in range(k):
                    probability = math.log(weights[class_index])
                    for feature_index in range(len(features)):
                        probability += math.log(theta[class_index][feature_index][observed[row][feature_index]])
                    log_probabilities.append(probability)
                normalizer = log_sum_exp(log_probabilities)
                log_likelihood += normalizer
                next_gamma.append([math.exp(value - normalizer) for value in log_probabilities])

            if previous_log_likelihood is not None and abs(log_likelihood - previous_log_likelihood) < 1e-8:
                gamma = next_gamma
                break
            gamma = next_gamma
            previous_log_likelihood = log_likelihood

        parameter_count = (k - 1) + k * sum(len(levels) - 1 for levels in categories)
        bic = -2 * log_likelihood + parameter_count * math.log(n_rows)
        aic = -2 * log_likelihood + 2 * parameter_count
        raw_entropy = -sum(
            probability * math.log(probability)
            for row in gamma
            for probability in row
            if probability > EPSILON
        )
        entropy = 1.0 if k == 1 else 1.0 - raw_entropy / (n_rows * math.log(k))
        candidate = LcaFit(k, log_likelihood, bic, aic, entropy, weights, theta, categories, gamma)
        if best is None or candidate.log_likelihood > best.log_likelihood:
            best = candidate

    assert best is not None
    return best


def model_rows(fits: list[LcaFit], axis: str) -> list[dict[str, Any]]:
    return [
        {
            "axis": axis,
            "classes": fit.k,
            "log_likelihood": fit.log_likelihood,
            "bic": fit.bic,
            "aic": fit.aic,
            "entropy": fit.entropy,
            "smallest_effective_class_n": min(weight * len(fit.posteriors) for weight in fit.weights),
        }
        for fit in fits
    ]


def profile_rows(fit: LcaFit, features: list[str], axis: str) -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []
    for class_index in range(fit.k):
        for feature_index, feature in enumerate(features):
            probabilities = fit.theta[class_index][feature_index]
            modal_index = max(range(len(probabilities)), key=lambda index: probabilities[index])
            for category_index, category in enumerate(fit.categories[feature_index]):
                rows.append(
                    {
                        "axis": axis,
                        "class_id": f"{axis}{class_index + 1}",
                        "feature": feature,
                        "state": category,
                        "conditional_probability": probabilities[category_index],
                        "is_modal_state": int(category_index == modal_index),
                        "class_weight": fit.weights[class_index],
                    }
                )
    return rows


def class_description(fit: LcaFit, features: list[str], class_index: int) -> str:
    modes: list[str] = []
    for feature_index, feature in enumerate(features):
        probabilities = fit.theta[class_index][feature_index]
        state = fit.categories[feature_index][max(range(len(probabilities)), key=lambda index: probabilities[index])]
        modes.append(f"{feature}={state}")
    return "; ".join(modes)


def assignments_rows(
    records: list[dict[str, Any]], fit: LcaFit, axis: str, features: list[str]
) -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []
    for record, posterior in zip(records, fit.posteriors):
        class_index = max(range(fit.k), key=lambda index: posterior[index])
        row = dict(record)
        row.update(
            {
                f"{axis}_class": f"{axis}{class_index + 1}",
                f"{axis}_posterior": posterior[class_index],
                f"{axis}_class_description": class_description(fit, features, class_index),
            }
        )
        for probability_index, probability in enumerate(posterior):
            row[f"{axis}_posterior_{probability_index + 1}"] = probability
        rows.append(row)
    return rows


def markdown_table(headers: list[str], rows: list[list[str]]) -> str:
    if not rows:
        return "_No rows._\n"
    divider = ["---"] * len(headers)
    lines = ["| " + " | ".join(headers) + " |", "| " + " | ".join(divider) + " |"]
    lines.extend("| " + " | ".join(str(value) for value in row) + " |" for row in rows)
    return "\n".join(lines) + "\n"


def write_report(
    output_dir: Path,
    participants: list[str],
    x_features: list[str],
    y_features: list[str],
    x_fits: list[LcaFit],
    y_fits: list[LcaFit],
    x_selected: LcaFit,
    y_selected: LcaFit,
    x_assignment_rows: list[dict[str, Any]],
    y_assignment_rows: list[dict[str, Any]],
    audit_rows: list[dict[str, Any]],
) -> None:
    def model_table(fits: list[LcaFit]) -> list[list[str]]:
        return [
            [
                str(fit.k),
                fmt(fit.log_likelihood),
                fmt(fit.bic),
                fmt(fit.aic),
                fmt(fit.entropy),
                fmt(min(weight * len(fit.posteriors) for weight in fit.weights), 2),
            ]
            for fit in fits
        ]

    def profiles(fit: LcaFit, features: list[str]) -> list[list[str]]:
        result = []
        for class_index in range(fit.k):
            effective_n = fit.weights[class_index] * len(fit.posteriors)
            result.append(
                [
                    f"Class {class_index + 1}",
                    fmt(fit.weights[class_index], 2),
                    fmt(effective_n, 2),
                    class_description(fit, features, class_index),
                ]
            )
        return result

    x_counts = Counter(row["X_class"] for row in x_assignment_rows)
    y_counts = Counter(row["Y_class"] for row in y_assignment_rows)
    conditions_by_y: dict[str, Counter[str]] = defaultdict(Counter)
    for row in y_assignment_rows:
        conditions_by_y[row["Y_class"]][row.get("task_type", "")] += 1

    report = [
        "# CARE-XR Four-Participant Dual-Axis LCA Pilot",
        "",
        "## Claim boundary",
        "",
        "This is an exploratory technical pilot based on four participants (P002, P003, P515, P516). It is **not** a publishable latent-class solution and it does not label any participant or response as careless/careful. It tests whether the stored CARE-XR data can be transformed into transparent categorical inputs, fit with separate X and Y LCAs, and projected to a contextual evidence matrix.",
        "",
        "The analysis excludes ratings and confidence values from model fitting. Ratings and confidence remain attributes used after class assignment for researcher review.",
        "",
        "## Data-integrity audit",
        "",
        markdown_table(
            ["Participant", "Personal profile", "Calibration blocks", "Combined blocks", "X rows", "Y rows", "Status"],
            [
                [
                    row["participant_id"],
                    row["profile_found"],
                    row["calibration_found"],
                    row["combined_found"],
                    row["x_rows"],
                    row["y_rows"],
                    row["status"],
                ]
                for row in audit_rows
            ],
        ),
        "",
        "## X: participant-relative response-process LCA",
        "",
        "Input features: " + ", ".join(f"`{feature}`" for feature in x_features) + ".",
        "",
        markdown_table(
            ["K", "Log likelihood", "BIC", "AIC", "Entropy", "Smallest effective n"], model_table(x_fits)
        ),
        "",
        f"Selected by the smallest BIC in this pilot: **K={x_selected.k}**. Treat this selection as a pipeline demonstration only.",
        "",
        markdown_table(["X class", "Weight", "Effective n", "Modal response-process states"], profiles(x_selected, x_features)),
        "",
        "Assigned X-row counts: " + ", ".join(f"{key}={value}" for key, value in sorted(x_counts.items())) + ".",
        "",
        "## Y: participant-relative task-context LCA",
        "",
        "Input features: " + ", ".join(f"`{feature}`" for feature in y_features) + ". Each is a within-participant change from the workload-probe baseline block; `same` means a change within a pre-registered 10% practical tolerance in this pilot script.",
        "",
        markdown_table(
            ["K", "Log likelihood", "BIC", "AIC", "Entropy", "Smallest effective n"], model_table(y_fits)
        ),
        "",
        f"Selected by the smallest BIC in this pilot: **K={y_selected.k}**. This is not a test of construct validity.",
        "",
        markdown_table(["Y class", "Weight", "Effective n", "Modal task-context states"], profiles(y_selected, y_features)),
        "",
        "Task conditions represented in each Y class:",
        "",
        markdown_table(
            ["Y class", "Observed task types"],
            [[class_id, ", ".join(f"{name} ({count})" for name, count in sorted(counts.items()))] for class_id, counts in sorted(conditions_by_y.items())],
        ),
        "",
        "## How to interpret this pilot",
        "",
        "1. If a compact class has a very small effective n, it is a local pattern in these four participants, not a stable population class.",
        "2. The X LCA summarizes how ratings were entered relative to each person's calibration distribution. It does not infer motivation or careless responding.",
        "3. The Y LCA summarizes how task behaviour shifted relative to baseline. It does not use NASA-TLX scores to assign the class, so a later analysis can inspect whether classes align with expected task/context changes without circularity.",
        "4. The final study should rerun this analysis with the full cohort, select K using BIC/AIC/entropy plus stability checks, and report participant-aware resampling because questionnaire items within a participant are not independent people.",
        "",
        "## Files",
        "",
        "- `data_integrity_audit.csv`: source-file and row-count audit.",
        "- `x_lca_input.csv` / `y_lca_input.csv`: all derived categorical LCA inputs with raw values and source paths.",
        "- `x_model_comparison.csv` / `y_model_comparison.csv`: K=1–3 fit diagnostics.",
        "- `x_class_profiles.csv` / `y_class_profiles.csv`: conditional probabilities for every state.",
        "- `x_lca_assignments.csv` / `y_lca_assignments.csv`: posterior membership for every record.",
        "- `matrix_projection_records.csv`: joined X/Y pilot projection for Combined questionnaire items.",
    ]
    (output_dir / "PILOT_LCA_REPORT.md").write_text("\n".join(report), encoding="utf-8")

    html_rows = []
    for line in report:
        if line.startswith("# "):
            html_rows.append(f"<h1>{html.escape(line[2:])}</h1>")
        elif line.startswith("## "):
            html_rows.append(f"<h2>{html.escape(line[3:])}</h2>")
        elif line.startswith("| "):
            # The CSV outputs are the complete source; Markdown is preserved in <pre> for a lossless local preview.
            html_rows.append(f"<pre>{html.escape(line)}</pre>")
        elif line:
            html_rows.append(f"<p>{html.escape(line)}</p>")
    (output_dir / "PILOT_LCA_REPORT.html").write_text(
        "<!doctype html><html><head><meta charset='utf-8'><title>CARE-XR LCA Pilot</title>"
        "<style>body{font-family:Segoe UI,Arial,sans-serif;max-width:1120px;margin:36px auto;padding:0 24px;color:#172536;line-height:1.55}"
        "h1{color:#0f5f63}h2{margin-top:32px}pre{font-family:Consolas,monospace;background:#f3f7f8;padding:3px 8px;margin:0;white-space:pre-wrap}"
        "</style></head><body>" + "\n".join(html_rows) + "</body></html>",
        encoding="utf-8",
    )


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--data-root", type=Path, default=DEFAULT_DATA_ROOT)
    parser.add_argument("--participants", nargs="+", default=list(DEFAULT_PARTICIPANTS))
    parser.add_argument(
        "--output-dir",
        type=Path,
        default=Path("outputs") / "lca_pilot_current_cohort",
    )
    args = parser.parse_args()
    output_dir: Path = args.output_dir.resolve()
    output_dir.mkdir(parents=True, exist_ok=True)

    audit_rows: list[dict[str, Any]] = []
    x_rows: list[dict[str, Any]] = []
    y_rows: list[dict[str, Any]] = []
    for participant_id in args.participants:
        participant_root = args.data_root / participant_id
        profile = latest_matching(participant_root, f"PAXSM_PersonalKnobProfile_{participant_id}_*_completed.csv")
        calibration_blocks = latest_matching(
            participant_root,
            f"WorkloadProbe_Blocks_{participant_id}_*_completed.csv",
            ("workload-probe",),
        )
        combined_blocks = latest_matching(
            participant_root,
            f"WorkloadProbe_Blocks_{participant_id}_*_completed.csv",
            ("combined-probe",),
        )
        status = "PASS"
        errors: list[str] = []
        participant_x: list[dict[str, Any]] = []
        participant_y: list[dict[str, Any]] = []
        if profile is None:
            errors.append("missing personal profile")
        if calibration_blocks is None:
            errors.append("missing workload-probe blocks")
        if combined_blocks is None:
            errors.append("missing combined-probe blocks")

        combined_questionnaire = questionnaire_file(combined_blocks.parent, participant_id) if combined_blocks else None
        if combined_questionnaire is None:
            errors.append("missing combined questionnaire")

        if not errors:
            try:
                participant_x = make_x_rows(participant_id, profile, combined_questionnaire)
                participant_y = make_y_rows(participant_id, calibration_blocks.parent, combined_blocks.parent)
                if not participant_x:
                    errors.append("no valid Combined questionnaire rows")
                if not participant_y:
                    errors.append("no valid probe blocks")
            except Exception as error:  # The audit preserves per-participant failures instead of hiding them.
                errors.append(str(error))
        if errors:
            status = "FAIL: " + "; ".join(errors)
        else:
            x_rows.extend(participant_x)
            y_rows.extend(participant_y)

        audit_rows.append(
            {
                "participant_id": participant_id,
                "profile_found": str(profile) if profile else "",
                "calibration_found": str(calibration_blocks) if calibration_blocks else "",
                "combined_found": str(combined_blocks) if combined_blocks else "",
                "combined_questionnaire_found": str(combined_questionnaire) if combined_questionnaire else "",
                "x_rows": len(participant_x),
                "y_rows": len(participant_y),
                "status": status,
            }
        )

    write_csv(output_dir / "data_integrity_audit.csv", audit_rows)
    if any(row["status"] != "PASS" for row in audit_rows):
        raise RuntimeError("At least one participant could not enter the pilot. See data_integrity_audit.csv.")

    x_features = [
        "answer_speed_state",
        "answer_path_state",
        "answer_decision_state",
        "answer_reverse_state",
        "answer_pause_state",
        "confidence_decision_state",
    ]
    y_features = [
        "decision_time_change",
        "interaction_exploration_change",
        "response_speed_change",
        "body_movement_change",
        "accuracy_change",
    ]
    write_csv(output_dir / "x_lca_input.csv", x_rows)
    write_csv(output_dir / "y_lca_input.csv", y_rows)

    x_fits = [fit_lca(x_rows, x_features, k) for k in (1, 2, 3)]
    y_fits = [fit_lca(y_rows, y_features, k) for k in (1, 2, 3)]
    x_selected = min(x_fits, key=lambda fit: fit.bic)
    y_selected = min(y_fits, key=lambda fit: fit.bic)
    write_csv(output_dir / "x_model_comparison.csv", model_rows(x_fits, "X"))
    write_csv(output_dir / "y_model_comparison.csv", model_rows(y_fits, "Y"))
    write_csv(output_dir / "x_class_profiles.csv", profile_rows(x_selected, x_features, "X"))
    write_csv(output_dir / "y_class_profiles.csv", profile_rows(y_selected, y_features, "Y"))
    x_assignments = assignments_rows(x_rows, x_selected, "X", x_features)
    y_assignments = assignments_rows(y_rows, y_selected, "Y", y_features)
    write_csv(output_dir / "x_lca_assignments.csv", x_assignments)
    write_csv(output_dir / "y_lca_assignments.csv", y_assignments)

    y_by_block = {(row["participant_id"], row["block_id"]): row for row in y_assignments}
    projected: list[dict[str, Any]] = []
    for x_row in x_assignments:
        y_row = y_by_block.get((x_row["participant_id"], x_row["block_id"]))
        if y_row is None:
            continue
        projected.append(
            {
                "participant_id": x_row["participant_id"],
                "block_id": x_row["block_id"],
                "item_id": x_row["item_id"],
                "dimension": x_row["dimension"],
                "rating": x_row["rating"],
                "confidence": x_row["confidence"],
                "X_class": x_row["X_class"],
                "X_posterior": x_row["X_posterior"],
                "Y_class": y_row["Y_class"],
                "Y_posterior": y_row["Y_posterior"],
                "X_description": x_row["X_class_description"],
                "Y_description": y_row["Y_class_description"],
            }
        )
    write_csv(output_dir / "matrix_projection_records.csv", projected)
    write_report(
        output_dir,
        list(args.participants),
        x_features,
        y_features,
        x_fits,
        y_fits,
        x_selected,
        y_selected,
        x_assignments,
        y_assignments,
        audit_rows,
    )
    print(f"LCA pilot complete: {output_dir}")
    print(f"X selected K={x_selected.k}; Y selected K={y_selected.k}")


if __name__ == "__main__":
    main()

"""Reproducible sensitivity analysis for the CARE-XR study plan.

The calculations deliberately avoid treating item-level observations as
independent participants. Pilot effects are estimated after aggregating items
within participant and condition. Required sample sizes use Monte Carlo power
for paired contrasts. The researcher-review calculation simulates paired
condition-level accuracy for each researcher and should be updated after an
internal researcher pilot provides realistic baseline accuracy and ICCs.
"""

from __future__ import annotations

import argparse
import csv
import json
import math
import re
from pathlib import Path

import numpy as np
import pandas as pd


FEATURES = [
    "rt_answer(A)",
    "rt_conf(C)",
    "maxAbsVel",
    "maxAbsVel(C)",
    "totalAbsAngle",
    "totalAbsAngle(C)",
    "reverseCount",
    "reverseCount(C)",
    "microAdjustCount",
    "microAdjustCount(C)",
    "fastFlickCount",
    "fastFlickCount(C)",
]


def _continued_beta_fraction(a: float, b: float, x: float) -> float:
    max_iterations = 250
    epsilon = 3e-14
    tiny = 1e-300
    qab = a + b
    qap = a + 1.0
    qam = a - 1.0
    c = 1.0
    d = 1.0 - qab * x / qap
    if abs(d) < tiny:
        d = tiny
    d = 1.0 / d
    result = d
    for iteration in range(1, max_iterations + 1):
        m2 = 2 * iteration
        aa = iteration * (b - iteration) * x / ((qam + m2) * (a + m2))
        d = 1.0 + aa * d
        if abs(d) < tiny:
            d = tiny
        c = 1.0 + aa / c
        if abs(c) < tiny:
            c = tiny
        d = 1.0 / d
        result *= d * c
        aa = -(a + iteration) * (qab + iteration) * x / ((a + m2) * (qap + m2))
        d = 1.0 + aa * d
        if abs(d) < tiny:
            d = tiny
        c = 1.0 + aa / c
        if abs(c) < tiny:
            c = tiny
        d = 1.0 / d
        delta = d * c
        result *= delta
        if abs(delta - 1.0) < epsilon:
            break
    return result


def regularized_incomplete_beta(a: float, b: float, x: float) -> float:
    if x <= 0.0:
        return 0.0
    if x >= 1.0:
        return 1.0
    log_term = (
        math.lgamma(a + b)
        - math.lgamma(a)
        - math.lgamma(b)
        + a * math.log(x)
        + b * math.log1p(-x)
    )
    front = math.exp(log_term)
    if x < (a + 1.0) / (a + b + 2.0):
        return front * _continued_beta_fraction(a, b, x) / a
    return 1.0 - front * _continued_beta_fraction(b, a, 1.0 - x) / b


def student_t_cdf(value: float, degrees_freedom: int) -> float:
    x = degrees_freedom / (degrees_freedom + value * value)
    tail = 0.5 * regularized_incomplete_beta(degrees_freedom / 2.0, 0.5, x)
    return 1.0 - tail if value >= 0 else tail


def student_t_ppf(probability: float, degrees_freedom: int) -> float:
    low, high = -20.0, 20.0
    for _ in range(100):
        midpoint = (low + high) / 2.0
        if student_t_cdf(midpoint, degrees_freedom) < probability:
            low = midpoint
        else:
            high = midpoint
    return (low + high) / 2.0


def paired_power(n: int, effect_dz: float, alpha: float, simulations: int, seed: int) -> float:
    rng = np.random.default_rng(seed + n * 1009 + round(effect_dz * 1000))
    critical = student_t_ppf(1.0 - alpha / 2.0, n - 1)
    rejected = 0
    completed = 0
    batch_size = min(5000, simulations)
    while completed < simulations:
        batch = min(batch_size, simulations - completed)
        differences = rng.normal(effect_dz, 1.0, size=(batch, n))
        means = differences.mean(axis=1)
        standard_deviations = differences.std(axis=1, ddof=1)
        t_statistics = means / (standard_deviations / math.sqrt(n))
        rejected += int(np.count_nonzero(np.abs(t_statistics) >= critical))
        completed += batch
    return rejected / simulations


def required_paired_n(effect_dz: float, alpha: float, target_power: float, simulations: int) -> tuple[int, float]:
    for n in range(8, 161):
        power = paired_power(n, effect_dz, alpha, simulations, seed=20260712)
        if power >= target_power:
            return n, power
    return 160, paired_power(160, effect_dz, alpha, simulations, seed=20260712)


def detectable_effect(n: int, alpha: float, target_power: float, simulations: int) -> float:
    low, high = 0.05, 1.5
    for _ in range(12):
        midpoint = (low + high) / 2.0
        power = paired_power(n, midpoint, alpha, simulations, seed=20260713)
        if power >= target_power:
            high = midpoint
        else:
            low = midpoint
    return high


def load_pilot_effects(root: Path) -> pd.DataFrame:
    records: list[dict[str, float | int | str]] = []
    for path in root.rglob("KnobBehavior_Merged*.csv"):
        match = re.search(r"P(\d+).*?S(\d+)", str(path), re.IGNORECASE)
        if not match:
            continue
        participant, session = map(int, match.groups())
        if session not in (3, 4):
            continue
        frame = pd.read_csv(path)
        record: dict[str, float | int | str] = {
            "participant": participant,
            "condition": "CR" if session == 3 else "Non-CR",
            "items": len(frame),
        }
        for feature in FEATURES:
            if feature in frame.columns:
                record[feature] = pd.to_numeric(frame[feature], errors="coerce").mean()
        records.append(record)
    participant_means = pd.DataFrame(records)
    results: list[dict[str, float | int | str]] = []
    for feature in FEATURES:
        if feature not in participant_means.columns:
            continue
        wide = participant_means.pivot(index="participant", columns="condition", values=feature).dropna()
        if not {"CR", "Non-CR"}.issubset(wide.columns) or len(wide) < 3:
            continue
        differences = wide["CR"] - wide["Non-CR"]
        standard_deviation = differences.std(ddof=1)
        effect = differences.mean() / standard_deviation if standard_deviation > 0 else float("nan")
        results.append(
            {
                "feature": feature,
                "paired_participants": len(wide),
                "cr_mean": wide["CR"].mean(),
                "non_cr_mean": wide["Non-CR"].mean(),
                "mean_difference_cr_minus_non_cr": differences.mean(),
                "paired_dz": effect,
            }
        )
    return pd.DataFrame(results)


def logit(probability: float) -> float:
    return math.log(probability / (1.0 - probability))


def review_accuracy_power(
    researchers: int,
    cases_per_condition: int,
    baseline_accuracy: float,
    carexr_accuracy: float,
    simulations: int,
    seed: int,
) -> float:
    """Conservative paired-accuracy simulation.

    Reviewer ability is shared across conditions. Case-to-case difficulty is
    sampled within condition. The final analysis should use a crossed mixed
    logistic model with researcher and case random intercepts.
    """
    rng = np.random.default_rng(seed + researchers * 101 + cases_per_condition)
    critical = student_t_ppf(0.975, researchers - 1)
    baseline_intercept = logit(baseline_accuracy)
    carexr_shift = logit(carexr_accuracy) - baseline_intercept
    rejected = 0
    for _ in range(simulations):
        reviewer_effect = rng.normal(0.0, 0.55, size=(researchers, 1))
        baseline_case_noise = rng.normal(0.0, 0.65, size=(researchers, cases_per_condition))
        carexr_case_noise = rng.normal(0.0, 0.65, size=(researchers, cases_per_condition))
        baseline_p = 1.0 / (1.0 + np.exp(-(baseline_intercept + reviewer_effect + baseline_case_noise)))
        carexr_p = 1.0 / (1.0 + np.exp(-(baseline_intercept + carexr_shift + reviewer_effect + carexr_case_noise)))
        baseline_scores = rng.binomial(1, baseline_p).mean(axis=1)
        carexr_scores = rng.binomial(1, carexr_p).mean(axis=1)
        differences = carexr_scores - baseline_scores
        sd = differences.std(ddof=1)
        if sd <= 0:
            continue
        statistic = differences.mean() / (sd / math.sqrt(researchers))
        rejected += int(abs(statistic) >= critical)
    return rejected / simulations


def write_csv(path: Path, rows: list[dict[str, object]]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", newline="", encoding="utf-8-sig") as handle:
        writer = csv.DictWriter(handle, fieldnames=list(rows[0].keys()))
        writer.writeheader()
        writer.writerows(rows)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--pilot-root", type=Path, required=True)
    parser.add_argument("--output", type=Path, default=Path(__file__).parent / "outputs")
    parser.add_argument("--simulations", type=int, default=30000)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)

    pilot = load_pilot_effects(args.pilot_root)
    pilot.to_csv(args.output / "pilot_cr_noncr_paired_effects.csv", index=False, encoding="utf-8-sig")

    paired_rows: list[dict[str, object]] = []
    for alpha_label, alpha in (("single_primary", 0.05), ("six_tests_bonferroni", 0.05 / 6.0)):
        for effect in (0.3, 0.4, 0.5, 0.6, 0.7):
            n, achieved = required_paired_n(effect, alpha, 0.80, args.simulations)
            paired_rows.append(
                {
                    "alpha_plan": alpha_label,
                    "alpha": alpha,
                    "paired_dz": effect,
                    "required_complete_n": n,
                    "achieved_power_mc": round(achieved, 4),
                    "recruit_with_15pct_buffer": math.ceil(n / 0.85),
                }
            )
    write_csv(args.output / "paired_continuous_required_n.csv", paired_rows)

    sensitivity_rows: list[dict[str, object]] = []
    for n in (18, 24, 30, 36, 40, 48, 60):
        sensitivity_rows.append(
            {
                "complete_n": n,
                "detectable_dz_80pct_alpha_05": round(detectable_effect(n, 0.05, 0.80, 18000), 3),
                "detectable_dz_80pct_alpha_0083": round(detectable_effect(n, 0.05 / 6.0, 0.80, 18000), 3),
            }
        )
    write_csv(args.output / "paired_continuous_sensitivity.csv", sensitivity_rows)

    review_rows: list[dict[str, object]] = []
    for cases in (4, 6):
        for baseline, carexr in ((0.65, 0.75), (0.65, 0.80), (0.65, 0.85)):
            for researchers in (18, 24, 30, 36, 42, 48, 60, 66, 72):
                power = review_accuracy_power(
                    researchers,
                    cases,
                    baseline,
                    carexr,
                    simulations=max(6000, args.simulations // 3),
                    seed=20260714,
                )
                review_rows.append(
                    {
                        "cases_per_condition": cases,
                        "baseline_accuracy": baseline,
                        "carexr_accuracy": carexr,
                        "absolute_improvement": carexr - baseline,
                        "researchers": researchers,
                        "simulated_power": round(power, 4),
                    }
                )
    write_csv(args.output / "researcher_review_accuracy_power.csv", review_rows)

    recommendations = {
        "assumptions": {
            "two_sided_alpha": 0.05,
            "target_power": 0.80,
            "attrition_or_invalid_data_buffer": 0.15,
            "six_dimension_conservative_alpha": 0.05 / 6.0,
            "pilot_note": "P1-P6 has only six CR/Non-CR pairs; effects are descriptive and not used as the sole recruitment basis.",
        },
        "planning_recommendation": {
            "probe_or_combined_participant_study": "A simple paired contrast at dz=0.5 requires 34 complete participants (recruit 40 with 15% loss). Because the planned study includes interactions and mixed-effects analyses, target 40 complete and recruit 47 if resources allow.",
            "six_nasa_dimensions_as_equal_primary_tests": "Use the conservative corrected table; approximately 60 complete participants may be needed for dz near 0.5. Prefer one primary dimension/contrast and treat others as secondary.",
            "researcher_review": "Target 30 complete researchers with 6 cases per interface condition only if a 15-percentage-point accuracy improvement (0.65 to 0.80) is the smallest effect of interest. A 10-point improvement needs about 66-72 complete researchers under these assumptions. Run an internal 6-8 researcher pilot and update the simulation.",
        },
    }
    (args.output / "power_analysis_summary.json").write_text(
        json.dumps(recommendations, ensure_ascii=False, indent=2), encoding="utf-8"
    )

    print(json.dumps(recommendations, ensure_ascii=False, indent=2))
    print(f"Outputs: {args.output.resolve()}")


if __name__ == "__main__":
    main()

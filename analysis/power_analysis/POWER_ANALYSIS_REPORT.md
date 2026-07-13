# CARE-XR Power Analysis

## Scope

This analysis covers two confirmatory study components:

1. Participant-level repeated-measures contrasts in the Probe/combined study.
2. Researcher-level comparison of Baseline versus CARE-XR review accuracy.

Questionnaire items and reviewed cases are repeated observations, not independent
participants. The final models should include participant/researcher and task/case
random effects.

## Assumptions

- Two-sided alpha: 0.05.
- Target power: 0.80.
- Invalid session/withdrawal buffer: 15%.
- Paired, within-person primary contrasts.
- Conservative family-wise alpha for six equal primary NASA-TLX tests: 0.05 / 6 = 0.0083.
- Monte Carlo paired-test calculations: 12,000 simulations per scenario.

## Existing P1-P6 Pilot

The existing CR/Non-CR pilot contains only six paired participants. Items were
first averaged within participant and condition. The resulting effects are
descriptive and should not be used as the sole recruitment basis.

Selected paired CR-minus-Non-CR effect estimates were:

| Feature | Paired dz |
|---|---:|
| Answer RT | 0.175 |
| Confidence RT | 0.592 |
| Answer total path | 0.238 |
| Confidence total path | 0.519 |
| Answer reversals | 0.487 |
| Confidence reversals | 0.902 |
| Answer micro-adjustments | 0.239 |
| Confidence micro-adjustments | 0.749 |

The large Confidence-stage estimates are unstable with n=6 and may include order,
task, or session effects. They support collecting the features, not a small full-study
sample size.

## Participant Study: Paired Continuous Outcomes

For one preregistered primary paired contrast:

| Paired effect dz | Complete N | Recruit with 15% loss |
|---:|---:|---:|
| 0.30 | 90 | 106 |
| 0.40 | 51 | 60 |
| 0.50 | 34 | 40 |
| 0.60 | 25 | 30 |
| 0.70 | 19 | 23 |

With six equal primary tests and Bonferroni alpha 0.0083:

| Paired effect dz | Complete N | Recruit with 15% loss |
|---:|---:|---:|
| 0.40 | 80 | 95 |
| 0.50 | 53 | 63 |
| 0.60 | 38 | 45 |
| 0.70 | 29 | 35 |

At 80% power, 24 complete participants detect approximately dz=0.60 for one
primary contrast, but only approximately dz=0.76 under six-test correction.

### Recommendation

- If one target dimension/contrast is primary: 34 complete, recruit 40.
- For the planned mixed-effects 2 x 2 combined study: target 40 complete and
  recruit 47 if feasible, because interaction estimates require more information
  than the simple paired contrast used for planning.
- Do not designate all six NASA-TLX dimensions as equal confirmatory outcomes.
  Select one or a small number of primary contrasts; treat the others as secondary
  with FDR control.

## Researcher Review Study

The simulation assumes each researcher reviews six different cases in Baseline and
six in CARE-XR, with shared reviewer ability and heterogeneous case difficulty.
The same researcher does not see the same case twice. Baseline accuracy is assumed
to be 0.65.

For an expected improvement to 0.80 (15 percentage points):

| Complete researchers | Simulated power |
|---:|---:|
| 18 | 0.600 |
| 24 | 0.723 |
| 30 | 0.824 |
| 36 | 0.892 |

For an improvement to only 0.75 (10 percentage points), approximately 66-72
complete researchers are required under the current variance assumptions.

### Recommendation

- Define 15 percentage points as the smallest effect of interest only if the team
  agrees that a smaller improvement would not justify the added workflow.
- Under that assumption: target 30 complete researchers and recruit 35.
- Give each researcher six cases per interface condition. Four per condition gives
  materially lower power.
- Before preregistration, run an internal pilot with 6-8 researchers to estimate
  baseline accuracy, review-case variance, review time variance, and missingness;
  then rerun the simulation.
- Analyze final decision accuracy with a crossed mixed-effects logistic model:
  `correct ~ interface * CR_condition * task_demand + (1|researcher) + (1|case)`.

## Confidence Outcome

Confidence alone is not an adequate primary endpoint. Analyze confidence together
with correctness through calibration error or a confidence-by-correctness model.
The paired continuous sensitivity table can plan a preregistered confidence or
calibration contrast. With 30 complete researchers, the study detects approximately
dz=0.53 at 80% power and alpha 0.05.

## Final Planning Numbers

| Component | Complete target | Recruitment target |
|---|---:|---:|
| Participant Probe/combined study | 40 | 47 |
| Researcher review, 6 cases/condition | 30 | 35 |

These numbers are defensible planning targets, not guarantees. They must be updated
after the new Probe pilot and the internal researcher-review pilot because neither
outcome currently has a directly matched pilot estimate.

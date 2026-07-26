#  Synthetic Evidence-Matrix Demonstration

## Status and boundary

This is a pipeline demonstration generated from the P999999 synthetic calibration and Combined runs. It shows that the current exports can be turned into a task-probe plugin and item-level X/Y contextual records. It is **not** human-participant evidence, a validated workload detector, or a careless-response classifier.

## Inputs

- Personal knob reference: `C:\Users\ygao930\OneDrive - The University of Auckland\Desktop\CARE-XR Data\P999999\20260724T074858Z_0fe223\paxsm-response-calibration\PAXSMPersonalKnobReference_Data\PAXSM_PersonalKnobProfile_P999999_20260724_195021_completed.json`
- Task-probe calibration run: `C:\Users\ygao930\OneDrive - The University of Auckland\Desktop\CARE-XR Data\P999999\20260725T021356Z_5db20d\workload-probe\XRWorkloadProbe_Data`
- Combined target run: `C:\Users\ygao930\OneDrive - The University of Auckland\Desktop\CARE-XR Data\P999999\20260725T021758Z_e4aa6f\combined-probe\XRCombinedProbe_Data\XRWorkloadProbe_Behavior_01_combined_high_repeat_1_P999999_20260725_141759_Metrics.csv`
- Integrity status: all Combined-run checks passed before this report was generated.

## Demo Probe Plugin

**DEMO ONLY - P999999 target-selection workload probe v0** contains three provisional task-context rule cards:

| Dimension | Calibration comparison | Selected behavioral features | Expected direction |
|---|---|---|---|
| Mental Demand | cognitive_heavy minus Baseline | Completion Time; Ray Movement Distance; Head Direction Entropy | Completion Time higher; Ray Movement Distance higher; Head Direction Entropy higher |
| Physical Demand | physical_heavy minus Baseline | Gesture Distance; Head Rotation; Head Distance | Gesture Distance higher; Head Rotation higher; Head Distance higher |
| Temporal Demand | temporal_heavy minus Baseline | Completion Time; Error Rate; Response Speed | Completion Time higher; Error Rate higher; Response Speed lower |

## Y axis: task-context probe support

For each Combined block, the script compares the selected behavioral features with that participant's Baseline. The current Console rule is: 2/3 or 3/3 directions matching = **strong**; 1/3 = **partial**; 0/3 = **insufficient**. The NASA-TLX score is displayed after this comparison and is not used to calculate the Y value.

| Dimension | Baseline score | Combined score(s) | Y result | What drove the result |
|---|---:|---:|---|---|
| Mental Demand | 4 | 17, 17 | Strong Probe support (2/3) | Completion Time: 12.158 vs Baseline 9.308 (higher; expected higher); Ray Movement Distance: 9.005 vs Baseline 7.289 (higher; expected higher); Head Direction Entropy: 1.341 vs Baseline 1.349 (near Baseline; expected higher) |
| Physical Demand | 2 | 14, 14 | Strong Probe support (3/3) | Gesture Distance: 5.170 vs Baseline 4.123 (higher; expected higher); Head Rotation: 139.802 vs Baseline 111.816 (higher; expected higher); Head Distance: 0.175 vs Baseline 0.136 (higher; expected higher) |
| Temporal Demand | 3 | 18, 18 | Strong Probe support (2/3) | Completion Time: 12.158 vs Baseline 9.308 (higher; expected higher); Error Rate: 0.000 vs Baseline 0.125 (lower; expected higher); Response Speed: 1.238 vs Baseline 1.303 (lower; expected lower) |

## X axis: participant-relative Answer-stage pattern

The X axis uses the completed personal knob reference profile. A record is `accelerated_direct` only when all three conditions are present: direct path (ratio <= 1.2), speed above the relevant personal p90/reference, and low correction. It is `hesitant_corrective` only when at least two of long path, high correction, or slow decision time are present. Otherwise it stays `reviewable`.

All 6 supported Combined item records were classified as **reviewable**. None met the complete accelerated-direct pattern and none met at least two hesitant-corrective criteria.

This is the appropriate result for this particular synthetic run: it was not designed to inject a rapid/direct or a strongly hesitant/corrective Answer pattern. The matrix therefore demonstrates data linkage, not X-axis discrimination.

## Evidence Matrix

| Task-probe Y axis / Answer-process X axis | Accelerated-direct | Reviewable | Hesitant-corrective |
|---|---:|---:|---:|
| Strong Probe support | 0 | 6 | 0 |
| Partial Probe support | 0 | 0 | 0 |
| Insufficient Probe support | 0 | 0 | 0 |

The populated cell is **Strong Probe support x Reviewable Answer process (n=6)**. It contains Mental, Physical, and Temporal NASA-TLX items from both Combined repetitions.

## Interpretation

This test demonstrates the intended separation of evidence channels. The task side can say that the Combined task behavior still follows the calibrated workload-context directions, while the response side can say that the answer interaction did not trigger a dominant rapid/direct or hesitant/corrective cue. A researcher would therefore review a high score as a score with strong task-context evidence and no prominent answer-process warning, rather than treating the matrix as one combined quality score.

## What is still required before research use

1. Build the Probe Plugin from multiple human calibration runs and report the direction agreement and uncertainty for each feature.
2. Retain the participant-relative knob reference but test whether X-axis cues behave sensibly under actual instructed response conditions.
3. Test whether researchers can understand and appropriately use the matrix without treating it as an automated exclusion decision.

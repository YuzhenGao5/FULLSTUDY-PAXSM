# CARE-XR Four-Participant Dual-Axis LCA Pilot

## Claim boundary

This is an exploratory technical pilot based on four participants (P002, P003, P515, P516). It is **not** a publishable latent-class solution and it does not label any participant or response as careless/careful. It tests whether the stored CARE-XR data can be transformed into transparent categorical inputs, fit with separate X and Y LCAs, and projected to a contextual evidence matrix.

The analysis excludes ratings and confidence values from model fitting. Ratings and confidence remain attributes used after class assignment for researcher review.

## Data-integrity audit

| Participant | Personal profile | Calibration blocks | Combined blocks | X rows | Y rows | Status |
| --- | --- | --- | --- | --- | --- | --- |
| P002 | C:\Users\ygao930\OneDrive - The University of Auckland\Desktop\CARE-XR Data\P002\20260726T051950Z_2c7171\paxsm-response-calibration\PAXSMPersonalKnobReference_Data\PAXSM_PersonalKnobProfile_P002_20260726_172234_completed.csv | C:\Users\ygao930\OneDrive - The University of Auckland\Desktop\CARE-XR Data\P002\20260726T052257Z_c0dd1d\workload-probe\XRWorkloadProbe_Data\WorkloadProbe_Blocks_P002_20260726_172907_completed.csv | C:\Users\ygao930\OneDrive - The University of Auckland\Desktop\CARE-XR Data\P002\20260726T052920Z_8b141b\combined-probe\XRCombinedProbe_Data\WorkloadProbe_Blocks_P002_20260726_173151_completed.csv | 12 | 6 | PASS |
| P003 | C:\Users\ygao930\OneDrive - The University of Auckland\Desktop\CARE-XR Data\P003\20260727T011357Z_482564\paxsm-response-calibration\PAXSMPersonalKnobReference_Data\PAXSM_PersonalKnobProfile_P003_20260727_131633_completed.csv | C:\Users\ygao930\OneDrive - The University of Auckland\Desktop\CARE-XR Data\P003\20260727T011646Z_1bc1fc\workload-probe\XRWorkloadProbe_Data\WorkloadProbe_Blocks_P003_20260727_132532_completed.csv | C:\Users\ygao930\OneDrive - The University of Auckland\Desktop\CARE-XR Data\P003\20260727T013038Z_c7d003\combined-probe\XRCombinedProbe_Data\WorkloadProbe_Blocks_P003_20260727_133038_completed.csv | 12 | 6 | PASS |
| P515 | C:\Users\ygao930\OneDrive - The University of Auckland\Desktop\CARE-XR Data\P515\20260725T034237Z_2d4013\paxsm-response-calibration\PAXSMPersonalKnobReference_Data\PAXSM_PersonalKnobProfile_P515_20260725_154525_completed.csv | C:\Users\ygao930\OneDrive - The University of Auckland\Desktop\CARE-XR Data\P515\20260725T034553Z_c7eaef\workload-probe\XRWorkloadProbe_Data\WorkloadProbe_Blocks_P515_20260725_155216_completed.csv | C:\Users\ygao930\OneDrive - The University of Auckland\Desktop\CARE-XR Data\P515\20260725T035234Z_7f0618\combined-probe\XRCombinedProbe_Data\WorkloadProbe_Blocks_P515_20260725_155554_completed.csv | 12 | 6 | PASS |
| P516 | C:\Users\ygao930\OneDrive - The University of Auckland\Desktop\CARE-XR Data\P516\20260726T041142Z_0d723b\paxsm-response-calibration\PAXSMPersonalKnobReference_Data\PAXSM_PersonalKnobProfile_P516_20260726_161407_completed.csv | C:\Users\ygao930\OneDrive - The University of Auckland\Desktop\CARE-XR Data\P516\20260726T041424Z_2532d9\workload-probe\XRWorkloadProbe_Data\WorkloadProbe_Blocks_P516_20260726_162017_completed.csv | C:\Users\ygao930\OneDrive - The University of Auckland\Desktop\CARE-XR Data\P516\20260726T042034Z_bf45ba\combined-probe\XRCombinedProbe_Data\WorkloadProbe_Blocks_P516_20260726_162343_completed.csv | 12 | 6 | PASS |


## X: participant-relative response-process LCA

Input features: `answer_speed_state`, `answer_path_state`, `answer_decision_state`, `answer_reverse_state`, `answer_pause_state`, `confidence_decision_state`.

| K | Log likelihood | BIC | AIC | Entropy | Smallest effective n |
| --- | --- | --- | --- | --- | --- |
| 1 | -278.384 | 603.223 | 580.768 | 1.000 | 48.00 |
| 2 | -238.858 | 574.495 | 527.715 | 1.000 | 20.01 |
| 3 | -222.169 | 591.443 | 520.337 | 0.999 | 6.03 |


Selected by the smallest BIC in this pilot: **K=2**. Treat this selection as a pipeline demonstration only.

| X class | Weight | Effective n | Modal response-process states |
| --- | --- | --- | --- |
| Class 1 | 0.42 | 20.01 | answer_speed_state=typical; answer_path_state=direct; answer_decision_state=fast; answer_reverse_state=few; answer_pause_state=typical; confidence_decision_state=fast |
| Class 2 | 0.58 | 27.99 | answer_speed_state=typical; answer_path_state=extended; answer_decision_state=slow; answer_reverse_state=many; answer_pause_state=low; confidence_decision_state=fast |


Assigned X-row counts: X1=20, X2=28.

## Y: participant-relative task-context LCA

Input features: `decision_time_change`, `interaction_exploration_change`, `response_speed_change`, `body_movement_change`, `accuracy_change`. Each is a within-participant change from the workload-probe baseline block; `same` means a change within a pre-registered 10% practical tolerance in this pilot script.

| K | Log likelihood | BIC | AIC | Entropy | Smallest effective n |
| --- | --- | --- | --- | --- | --- |
| 1 | -99.273 | 227.148 | 216.546 | 1.000 | 24.00 |
| 2 | -69.897 | 200.178 | 177.795 | 0.998 | 10.01 |
| 3 | -56.700 | 205.564 | 171.400 | 1.000 | 4.03 |


Selected by the smallest BIC in this pilot: **K=2**. This is not a test of construct validity.

| Y class | Weight | Effective n | Modal task-context states |
| --- | --- | --- | --- |
| Class 1 | 0.42 | 10.01 | decision_time_change=same; interaction_exploration_change=same; response_speed_change=same; body_movement_change=same; accuracy_change=same |
| Class 2 | 0.58 | 13.99 | decision_time_change=higher; interaction_exploration_change=higher; response_speed_change=lower; body_movement_change=higher; accuracy_change=lower |


Task conditions represented in each Y class:

| Y class | Observed task types |
| --- | --- |
| Y1 | baseline (4), combined_high_repeat_1 (1), combined_high_repeat_2 (1), physical_heavy (2), temporal_heavy (2) |
| Y2 | cognitive_heavy (4), combined_high_repeat_1 (3), combined_high_repeat_2 (3), physical_heavy (2), temporal_heavy (2) |


## How to interpret this pilot

1. If a compact class has a very small effective n, it is a local pattern in these four participants, not a stable population class.
2. The X LCA summarizes how ratings were entered relative to each person's calibration distribution. It does not infer motivation or careless responding.
3. The Y LCA summarizes how task behaviour shifted relative to baseline. It does not use NASA-TLX scores to assign the class, so a later analysis can inspect whether classes align with expected task/context changes without circularity.
4. The final study should rerun this analysis with the full cohort, select K using BIC/AIC/entropy plus stability checks, and report participant-aware resampling because questionnaire items within a participant are not independent people.

## Files

- `data_integrity_audit.csv`: source-file and row-count audit.
- `x_lca_input.csv` / `y_lca_input.csv`: all derived categorical LCA inputs with raw values and source paths.
- `x_model_comparison.csv` / `y_model_comparison.csv`: K=1–3 fit diagnostics.
- `x_class_profiles.csv` / `y_class_profiles.csv`: conditional probabilities for every state.
- `x_lca_assignments.csv` / `y_lca_assignments.csv`: posterior membership for every record.
- `matrix_projection_records.csv`: joined X/Y pilot projection for Combined questionnaire items.
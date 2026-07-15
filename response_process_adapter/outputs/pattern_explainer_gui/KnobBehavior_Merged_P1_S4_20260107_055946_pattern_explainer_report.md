# PAXSM Pattern-Library Explainer Report

These are researcher-facing response-process review cues, not automatic careless-response decisions.

## l1_overview_check

- Final cue: `careful / efficient`
- Score: `0.06`
- Top pattern: `PAXSM_EFFICIENT_DIRECT` - Efficient direct response
- Pattern source: PAXSM-specific efficient-response pattern
- Matched patterns: `PAXSM_EFFICIENT_DIRECT`
- Evidence 1: Answer path is direct (path_ratio=1.0).
- Evidence 2: Answer speed is not high (maxAbsVel=6.6694).
- Evidence 3: Answer correction is low (reverseCount=0.0, microAdjustCount=0.0).
- Explanation: The answer was direct but not high-speed, correction was low, and confidence was high. Evidence: Answer path is direct (path_ratio=1.0). Answer speed is not high (maxAbsVel=6.6694). Answer correction is low (reverseCount=0.0, microAdjustCount=0.0).

## l2_compare_options

- Final cue: `indecisive / difficult item`
- Score: `0.49`
- Top pattern: `PAXSM_UNSTABLE_EXTRA_PATH` - PAXSM unstable or extra-adjustment item process
- Pattern source: PAXSM-specific item process
- Matched patterns: `PAXSM_UNSTABLE_EXTRA_PATH`
- Evidence 1: Answer path shows extra movement (path_ratio=7.0).
- Evidence 2: Answer speed is high or flick-like (maxAbsVel=37.4545, fastFlickCount=0.0).
- Evidence 3: Answer correction is high (reverseCount=3.0, microAdjustCount=1.0).
- Explanation: This item showed extra movement, repeated correction, or hesitant confidence. This is better interpreted as uncertainty, item difficulty, or interaction instability than a simple rushed response. Evidence: Answer path shows extra movement (path_ratio=7.0). Answer speed is high or flick-like (maxAbsVel=37.4545, fastFlickCount=0.0). Answer correction is high (reverseCount=3.0, microAdjustCount=1.0).

## l3_avoid_random

- Final cue: `normal / reviewable`
- Score: `0.12`
- Top pattern: `PAXSM_NORMAL_REVIEWABLE` - No strong pattern match
- Pattern source: PAXSM default
- Matched patterns: `PAXSM_NORMAL_REVIEWABLE`
- Evidence 1: Answer speed is not high (maxAbsVel=7.9949).
- Evidence 2: Answer correction is low (reverseCount=1.0, microAdjustCount=0.0).
- Evidence 3: Read RT is not short (7.5019s).
- Explanation: No major fixed-rule response-process concern was detected. Evidence: Answer speed is not high (maxAbsVel=7.9949). Answer correction is low (reverseCount=1.0, microAdjustCount=0.0). Read RT is not short (7.5019s).

## l4_pause_if_unsure

- Final cue: `normal / reviewable`
- Score: `0.12`
- Top pattern: `PAXSM_NORMAL_REVIEWABLE` - No strong pattern match
- Pattern source: PAXSM default
- Matched patterns: `PAXSM_NORMAL_REVIEWABLE`
- Evidence 1: Answer path is direct (path_ratio=1.0).
- Evidence 2: Answer speed is not high (maxAbsVel=9.022).
- Evidence 3: Answer correction is low (reverseCount=0.0, microAdjustCount=0.0).
- Explanation: No major fixed-rule response-process concern was detected. Evidence: Answer path is direct (path_ratio=1.0). Answer speed is not high (maxAbsVel=9.022). Answer correction is low (reverseCount=0.0, microAdjustCount=0.0).

## l5_read_details

- Final cue: `careful / efficient`
- Score: `0.06`
- Top pattern: `PAXSM_EFFICIENT_DIRECT` - Efficient direct response
- Pattern source: PAXSM-specific efficient-response pattern
- Matched patterns: `PAXSM_EFFICIENT_DIRECT`
- Evidence 1: Answer path is direct (path_ratio=1.0).
- Evidence 2: Answer speed is not high (maxAbsVel=6.571).
- Evidence 3: Answer correction is low (reverseCount=0.0, microAdjustCount=0.0).
- Explanation: The answer was direct but not high-speed, correction was low, and confidence was high. Evidence: Answer path is direct (path_ratio=1.0). Answer speed is not high (maxAbsVel=6.571). Answer correction is low (reverseCount=0.0, microAdjustCount=0.0).

## l6_break_down_complex

- Final cue: `indecisive / difficult item`
- Score: `0.335`
- Top pattern: `PAXSM_UNSTABLE_EXTRA_PATH` - PAXSM unstable or extra-adjustment item process
- Pattern source: PAXSM-specific item process
- Matched patterns: `PAXSM_UNSTABLE_EXTRA_PATH`
- Evidence 1: Answer speed is not high (maxAbsVel=1.7108).
- Evidence 2: Answer correction is high (reverseCount=2.0, microAdjustCount=1.0).
- Evidence 3: Confidence stage shows hesitation or uncertainty (confidence=4.0, reverseCount(C)=3.0, microAdjustCount(C)=1.0, rt_conf=3.7166s).
- Explanation: This item showed extra movement, repeated correction, or hesitant confidence. This is better interpreted as uncertainty, item difficulty, or interaction instability than a simple rushed response. Evidence: Answer speed is not high (maxAbsVel=1.7108). Answer correction is high (reverseCount=2.0, microAdjustCount=1.0). Confidence stage shows hesitation or uncertainty (confidence=4.0, reverseCount(C)=3.0, microAdjustCount(C)=1.0, rt_conf=3.7166s).

## l7_submit_when_confident

- Final cue: `careful / efficient`
- Score: `0.06`
- Top pattern: `PAXSM_EFFICIENT_DIRECT` - Efficient direct response
- Pattern source: PAXSM-specific efficient-response pattern
- Matched patterns: `PAXSM_EFFICIENT_DIRECT`
- Evidence 1: Answer path is direct (path_ratio=1.0).
- Evidence 2: Answer speed is not high (maxAbsVel=14.5862).
- Evidence 3: Answer correction is low (reverseCount=0.0, microAdjustCount=0.0).
- Explanation: The answer was direct but not high-speed, correction was low, and confidence was high. Evidence: Answer path is direct (path_ratio=1.0). Answer speed is not high (maxAbsVel=14.5862). Answer correction is low (reverseCount=0.0, microAdjustCount=0.0).

## s1_focused

- Final cue: `possible rushed-like`
- Score: `0.772`
- Top pattern: `MC_E_S_LOW_CORRECTION` - Mouse-Chase-informed fast low-effort with low correction
- Pattern source: Mouse-Chase ablation pattern E+S plus low backtracking
- Matched patterns: `MC_E_S_LOW_CORRECTION; MC_E_S_ABRUPT; MC_E_S_CORE; PAXSM_QUICK_ACCEPTABLE`
- Evidence 1: Answer path is direct (path_ratio=0.0).
- Evidence 2: Answer speed is high or flick-like (maxAbsVel=0.0, fastFlickCount=1.0).
- Evidence 3: Answer correction is low (reverseCount=0.0, microAdjustCount=0.0).
- Explanation: The Answer stage matched the Mouse-Chase-informed fast low-effort pattern: low path effort, high speed, and little correction. Evidence: Answer path is direct (path_ratio=0.0). Answer speed is high or flick-like (maxAbsVel=0.0, fastFlickCount=1.0). Answer correction is low (reverseCount=0.0, microAdjustCount=0.0).

## s2_distracted

- Final cue: `indecisive / difficult item`
- Score: `0.306`
- Top pattern: `PAXSM_UNSTABLE_EXTRA_PATH` - PAXSM unstable or extra-adjustment item process
- Pattern source: PAXSM-specific item process
- Matched patterns: `PAXSM_UNSTABLE_EXTRA_PATH`
- Evidence 1: Answer path is direct (path_ratio=1.0).
- Evidence 2: Answer speed is not high (maxAbsVel=3.1264).
- Evidence 3: Answer correction is low (reverseCount=0.0, microAdjustCount=0.0).
- Explanation: This item showed extra movement, repeated correction, or hesitant confidence. This is better interpreted as uncertainty, item difficulty, or interaction instability than a simple rushed response. Evidence: Answer path is direct (path_ratio=1.0). Answer speed is not high (maxAbsVel=3.1264). Answer correction is low (reverseCount=0.0, microAdjustCount=0.0).

## s3_casual

- Final cue: `indecisive / difficult item`
- Score: `0.416`
- Top pattern: `PAXSM_UNSTABLE_EXTRA_PATH` - PAXSM unstable or extra-adjustment item process
- Pattern source: PAXSM-specific item process
- Matched patterns: `PAXSM_UNSTABLE_EXTRA_PATH`
- Evidence 1: Answer path shows extra movement (path_ratio=5.0).
- Evidence 2: Answer speed is not high (maxAbsVel=2.3167).
- Evidence 3: Answer correction is low (reverseCount=1.0, microAdjustCount=1.0).
- Explanation: This item showed extra movement, repeated correction, or hesitant confidence. This is better interpreted as uncertainty, item difficulty, or interaction instability than a simple rushed response. Evidence: Answer path shows extra movement (path_ratio=5.0). Answer speed is not high (maxAbsVel=2.3167). Answer correction is low (reverseCount=1.0, microAdjustCount=1.0).

## s4_double_check

- Final cue: `indecisive / difficult item`
- Score: `0.272`
- Top pattern: `PAXSM_UNSTABLE_EXTRA_PATH` - PAXSM unstable or extra-adjustment item process
- Pattern source: PAXSM-specific item process
- Matched patterns: `PAXSM_UNSTABLE_EXTRA_PATH`
- Evidence 1: Answer path is direct (path_ratio=1.0).
- Evidence 2: Answer speed is not high (maxAbsVel=12.0316).
- Evidence 3: Answer correction is low (reverseCount=0.0, microAdjustCount=0.0).
- Explanation: This item showed extra movement, repeated correction, or hesitant confidence. This is better interpreted as uncertainty, item difficulty, or interaction instability than a simple rushed response. Evidence: Answer path is direct (path_ratio=1.0). Answer speed is not high (maxAbsVel=12.0316). Answer correction is low (reverseCount=0.0, microAdjustCount=0.0).

## s5_speed_over_accuracy

- Final cue: `indecisive / difficult item`
- Score: `0.296`
- Top pattern: `PAXSM_UNSTABLE_EXTRA_PATH` - PAXSM unstable or extra-adjustment item process
- Pattern source: PAXSM-specific item process
- Matched patterns: `PAXSM_UNSTABLE_EXTRA_PATH`
- Evidence 1: Answer speed is high or flick-like (maxAbsVel=36.5307, fastFlickCount=0.0).
- Evidence 2: Answer correction is low (reverseCount=1.0, microAdjustCount=1.0).
- Evidence 3: Confidence stage shows hesitation or uncertainty (confidence=2.0, reverseCount(C)=0.0, microAdjustCount(C)=0.0, rt_conf=2.2271s).
- Explanation: This item showed extra movement, repeated correction, or hesitant confidence. This is better interpreted as uncertainty, item difficulty, or interaction instability than a simple rushed response. Evidence: Answer speed is high or flick-like (maxAbsVel=36.5307, fastFlickCount=0.0). Answer correction is low (reverseCount=1.0, microAdjustCount=1.0). Confidence stage shows hesitation or uncertainty (confidence=2.0, reverseCount(C)=0.0, microAdjustCount(C)=0.0, rt_conf=2.2271s).

## s6_read_every_sentence

- Final cue: `normal / reviewable`
- Score: `0.12`
- Top pattern: `PAXSM_NORMAL_REVIEWABLE` - No strong pattern match
- Pattern source: PAXSM default
- Matched patterns: `PAXSM_NORMAL_REVIEWABLE`
- Evidence 1: Answer speed is high or flick-like (maxAbsVel=35.7925, fastFlickCount=0.0).
- Evidence 2: Answer correction is low (reverseCount=1.0, microAdjustCount=1.0).
- Evidence 3: Read RT is not short (4.0084s).
- Explanation: No major fixed-rule response-process concern was detected. Evidence: Answer speed is high or flick-like (maxAbsVel=35.7925, fastFlickCount=0.0). Answer correction is low (reverseCount=1.0, microAdjustCount=1.0). Read RT is not short (4.0084s).

## s7_answer_by_gut

- Final cue: `indecisive / difficult item`
- Score: `0.386`
- Top pattern: `PAXSM_UNSTABLE_EXTRA_PATH` - PAXSM unstable or extra-adjustment item process
- Pattern source: PAXSM-specific item process
- Matched patterns: `PAXSM_UNSTABLE_EXTRA_PATH`
- Evidence 1: Answer speed is not high (maxAbsVel=17.9011).
- Evidence 2: Answer correction is high (reverseCount=3.0, microAdjustCount=1.0).
- Evidence 3: Confidence stage shows hesitation or uncertainty (confidence=3.0, reverseCount(C)=1.0, microAdjustCount(C)=0.0, rt_conf=2.338s).
- Explanation: This item showed extra movement, repeated correction, or hesitant confidence. This is better interpreted as uncertainty, item difficulty, or interaction instability than a simple rushed response. Evidence: Answer speed is not high (maxAbsVel=17.9011). Answer correction is high (reverseCount=3.0, microAdjustCount=1.0). Confidence stage shows hesitation or uncertainty (confidence=3.0, reverseCount(C)=1.0, microAdjustCount(C)=0.0, rt_conf=2.338s).

## imc_select_6

- Final cue: `indecisive / difficult item`
- Score: `0.455`
- Top pattern: `PAXSM_UNSTABLE_EXTRA_PATH` - PAXSM unstable or extra-adjustment item process
- Pattern source: PAXSM-specific item process
- Matched patterns: `PAXSM_UNSTABLE_EXTRA_PATH`
- Evidence 1: Answer path shows extra movement (path_ratio=7.0).
- Evidence 2: Answer speed is high or flick-like (maxAbsVel=70.2422, fastFlickCount=0.0).
- Evidence 3: Answer correction is high (reverseCount=2.0, microAdjustCount=2.0).
- Explanation: This item showed extra movement, repeated correction, or hesitant confidence. This is better interpreted as uncertainty, item difficulty, or interaction instability than a simple rushed response. Evidence: Answer path shows extra movement (path_ratio=7.0). Answer speed is high or flick-like (maxAbsVel=70.2422, fastFlickCount=0.0). Answer correction is high (reverseCount=2.0, microAdjustCount=2.0).

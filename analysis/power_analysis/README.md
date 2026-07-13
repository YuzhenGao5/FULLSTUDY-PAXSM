# CARE-XR power and sensitivity analysis

This analysis separates participant-level repeated-measures effects from the
researcher-review comparison. It never counts questionnaire items as independent
participants.

Run from the Unity project root:

```powershell
& "C:\Users\ygao930\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe" `
  "analysis\power_analysis\carexr_power_analysis.py" `
  --pilot-root "C:\Users\ygao930\OneDrive - The University of Auckland\Desktop\pILOT TESTING" `
  --output "analysis\power_analysis\outputs"
```

The P1-P6 pilot contributes descriptive paired CR/Non-CR effect estimates only.
With six pairs, these estimates are too unstable to be the sole basis of a full
study sample size. Probe and researcher-review planning therefore use sensitivity
and simulation analyses across defensible effect scenarios.

The researcher simulation is a planning approximation. The final analysis should
use a crossed mixed-effects model with researcher and review-case random effects.
After an internal pilot with 6-8 researchers, update baseline accuracy, expected
improvement, case count, and variance assumptions before preregistration.

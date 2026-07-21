# PAXSM Researcher Console

This is an optional researcher-facing control and data-monitoring layer for the
existing Unity experiment. Unity scenes remain the sole producers of raw task,
questionnaire, probe, and behavior data.

The first version provides:

- a mandatory participant/session gate;
- scene-level launch requests for Comparison, Workload, and Combined;
- a read-only Workload block monitor based on Unity CSV checkpoints;
- participant-scoped data summaries and recent-file inspection;
- a stable entry point for the existing Questionnaire Agent;
- additive console manifests and launch audits stored separately from raw data.

If the console is not used, `ExperimentSetup` behaves as before. The optional
Unity bridge consumes a launch request only when one is present.

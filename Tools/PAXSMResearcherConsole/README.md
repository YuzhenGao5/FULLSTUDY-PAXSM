# PAXSM Researcher Console

This is an optional researcher-facing control and data-monitoring layer for the
existing Unity experiment. Unity scenes remain the sole producers of raw task,
questionnaire, probe, and behavior data.

On Windows, launch the packaged, self-contained application with
`Open_PAXSMResearcherConsole.bat` or
`Launcher/PAXSMResearcherConsole.exe`. The packaged executable does not require
a separate .NET installation and locates the Unity project from its repository
location.

The first version provides:

- a mandatory participant/session gate;
- scene-level launch requests for Comparison, Personal knob reference, Workload, and Combined;
- a participant- and session-bound Personal reference page that displays the Read-calibration profile and its distance-sensitive response-process thresholds;
- a read-only Workload block monitor based on Unity CSV checkpoints;
- participant-scoped data summaries and recent-file inspection;
- a stable entry point for the existing Questionnaire Agent;
- additive console manifests and launch audits stored separately from raw data.

If the console is not used, `ExperimentSetup` behaves as before. The optional
Unity bridge consumes a launch request only when one is present.

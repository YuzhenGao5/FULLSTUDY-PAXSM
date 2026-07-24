namespace PAXSMResearcherConsole;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        string projectRoot = ProjectLocator.FindProjectRoot(AppContext.BaseDirectory);
        if (args.Any(argument => argument.Equals("--self-test", StringComparison.OrdinalIgnoreCase)))
            return RunSelfTest(projectRoot);

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(projectRoot));
        return 0;
    }

    private static int RunSelfTest(string projectRoot)
    {
        string temporaryRoot = Path.Combine(Path.GetTempPath(), $"PAXSMConsoleSelfTest_{Guid.NewGuid():N}");
        try
        {
            if (!File.Exists(Path.Combine(projectRoot, "Assets", "Scenes", "ExperimentSetup.unity")))
                return 11;
            var services = new ProjectServices(projectRoot);
            if (!services.TryCreateSession("P888", 1, temporaryRoot, out ResearchSession? session, out _))
                return 12;
            DataSnapshot snapshot = new DataScanner().Scan(session!);
            if (snapshot.CalibrationBlocks.Count != 3 ||
                snapshot.CalibrationBlocks.Any(block => block.State != CalibrationBlockState.Queued))
                return 13;

            string existingParticipant = Path.Combine(services.DefaultOutputRoot, "P888");
            if (Directory.Exists(existingParticipant))
            {
                var existingSession = new ResearchSession
                {
                    ParticipantId = "P888",
                    SessionNumber = 1,
                    OutputRoot = services.DefaultOutputRoot
                };
                DataSnapshot existingSnapshot = new DataScanner().Scan(existingSession);
                if (existingSnapshot.Runs.Count == 0)
                    return 14;
            }
            return 0;
        }
        catch
        {
            return 19;
        }
        finally
        {
            try
            {
                if (Directory.Exists(temporaryRoot))
                    Directory.Delete(temporaryRoot, true);
            }
            catch
            {
                // A failed cleanup does not change the functional test result.
            }
        }
    }
}

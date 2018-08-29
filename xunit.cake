/*
 * Execute DotNetCore tests
 * Script uses MiniCover to provide code coverage
 * MiniCover requires setting  [SetMiniCoverToolsProject("");] to a project that contains the MiniCover cli tool
 */

#region Tools

#tool nuget:?package=OpenCover

#endregion

#region AddIns

#addin Cake.MiniCover&version=0.29.0-next20180721071547

#endregion

#region Variables

// Indicate if the unit tests passed
var testPassed = false;
// Path where coverage results should be saved
var coverPath = "./coverage.xml";
// Test result output file
var testResultFile = "./TestResult.xml";
// Filter used to locate unit test csproj files
var unitTestProjectFilter = "./*Tests/*.Tests.csproj";
// Filter used to locate unit test dlls
var unitTestFilter = "./*Tests/bin/Release/netcoreapp2.1/*.Tests.dll";
// Coverall token
var coverallRepoToken = EnvironmentVariable("CoverallRepoToken");

#endregion

#region Tasks

// Execute unit tests
Task ("UnitTests")
    .Does (() =>
    {
        var blockText = "Unit Tests";
        StartBlock(blockText);

        RemoveCoverageResults();
        ExecuteUnitTests();
        PushTestResults(testResultFile);

        EndBlock(blockText);
    });

Task ("FailBuildIfTestFailed")
    .Does(() => {
        var blockText = "Build Success Check";
        StartBlock(blockText);

        if(!testPassed)
        {
            throw new CakeException("Unit test have failed - Failing the build");
        }

        EndBlock(blockText);
    });

#endregion

#region Private Methods

// Delete the coverage results if it already exists
private void RemoveCoverageResults()
{
    if(FileExists(coverPath))
    {
        Information("Clearing existing coverage results");
        DeleteFile(coverPath);
    }
}

// Execute NUnit tests
private void ExecuteUnitTests()
{
    Information("Running tests");
    var testAssemblies = GetFiles(unitTestProjectFilter);
    
           
        try
        {
            MiniCover(tool => {
                foreach(var project in testAssemblies)
                {
                    Information($"Running tests for {project}");
                    tool.DotNetCoreTest(project.FullPath, GetTestSettings());
                }
            },
            GetMiniCoverSettings());
            MiniCoverUninstrument();

            if(string.IsNullOrEmpty(coverallRepoToken))
            {
                MiniCoverReport(new MiniCoverSettings().GenerateReport(ReportType.XML));
            }
            else
            {
                MiniCoverReport(new MiniCoverSettings
                {
                    Coveralls = GetCoverallSettings()
                }.GenerateReport(ReportType.COVERALLS | ReportType.XML));
            }
            testPassed = true;
        }
        catch(Exception)
        {
            Error("There was an error while executing tests");
        }    
}

// Get settings for Coverall result posting
private CoverallsSettings GetCoverallSettings()
{
    return new CoverallsSettings
    {
        Branch = branch,
        CommitHash = gitHash,
        RepoToken = coverallRepoToken
    };
}

// Get settings for DotNetCoreTests
private DotNetCoreTestSettings GetTestSettings()
{
    return new DotNetCoreTestSettings
    {
        NoBuild = true,
        Configuration = buildConfiguration,
        ResultsDirectory = ".",
        Logger = $"trx;LogFileName={testResultFile}"
    };
}

// Get setting for MiniCover
private MiniCoverSettings GetMiniCoverSettings()
{
    return new MiniCoverSettings()
        .WithAssembliesMatching(unitTestFilter);
}

#endregion
using System.Security.Cryptography;
using TheSingularityWorkshop.CiAi;

var passed = 0;
passed += CiPipelineDefinitionContractTests.Run();
passed += CiWorkflowDomainCatalogContractTests.Run();
passed += CiWorkflowDomainEvaluatorContractTests.Run();
passed += CiWorkflowPlanContractTests.Run();
passed += BenchmarkBuildDispatchTests.Run();
passed += SuccessfulBuildBenchmarkCoordinatorTests.Run();
passed += NuGetPackageArtifactVerifierContractTests.Run();
passed += NuGetV3VersionCatalogContractTests.Run();
passed += NuGetVersionUpdateClassifierContractTests.Run();
passed += await NuGetDependencyUpdateEvaluatorContractTests.RunAsync();

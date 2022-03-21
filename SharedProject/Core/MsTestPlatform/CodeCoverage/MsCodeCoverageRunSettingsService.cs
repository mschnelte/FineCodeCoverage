﻿using FineCodeCoverage.Core.Utilities;
using FineCodeCoverage.Engine.Model;
using FineCodeCoverage.Impl;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Threading;
using Task = System.Threading.Tasks.Task;
using Microsoft.VisualStudio.TestWindow.Extensibility;
using System.Xml.XPath;
using FineCodeCoverage.Options;
using FineCodeCoverage.Engine.ReportGenerator;
using System.Threading.Tasks;
using System.Xml.Linq;
using FineCodeCoverage.Core.Utilities.VsThreading;

namespace FineCodeCoverage.Engine.MsTestPlatform.CodeCoverage
{
    [Export(typeof(IMsCodeCoverageRunSettingsService))]
    [Export(typeof(IRunSettingsService))]
    internal class MsCodeCoverageRunSettingsService : IMsCodeCoverageRunSettingsService, IRunSettingsService
    {
        public string Name => "Fine Code Coverage MsCodeCoverageRunSettingsService";

        internal class UserRunSettingsProjectDetails : IUserRunSettingsProjectDetails
        {
            public IMsCodeCoverageOptions Settings { get; set; }
            public string OutputFolder { get; set; }
            public string TestDllFile { get; set; }
            public List<string> ExcludedReferencedProjects { get; set; }
            public List<string> IncludedReferencedProjects { get; set; }
        }

        private class CoverageProjectRunSettings : ICoverageProjectRunSettings
        {
            public ICoverageProject CoverageProject { get; set; }
            public string RunSettings { get; set; }
            public string CustomTemplatePath { get; internal set; }
            public bool ReplacedTestAdapter { get; internal set; }
        }

        private class TemplateReplaceResult : ITemplateReplaceResult
        {
            public string Replaced { get; set; }

            public bool ReplacedTestAdapter { get; set; }
        }

        private readonly IToolFolder toolFolder;
        private readonly IToolZipProvider toolZipProvider;
        private readonly IAppOptionsProvider appOptionsProvider;
        private readonly ICoverageToolOutputManager coverageOutputManager;
        private readonly IRunSettingsTemplate runSettingsTemplate;
        private readonly ICustomRunSettingsTemplateProvider customRunSettingsTemplateProvider;
        private readonly IRunSettingsTemplateReplacementsFactory runSettingsTemplateReplacementsFactory;
        private readonly IShimCopier shimCopier;
        private readonly ILogger logger;
        private readonly IReportGeneratorUtil reportGeneratorUtil;
        private IFCCEngine fccEngine;
        private const string zipPrefix = "microsoft.codecoverage";
        private const string zipDirectoryName = "msCodeCoverage";
        private const string msCodeCoverageMessage = "Ms code coverage";
        internal Dictionary<string, IUserRunSettingsProjectDetails> userRunSettingsProjectDetailsLookup;
        private readonly IProjectRunSettingsGenerator projectRunSettingsGenerator;
        private readonly IUserRunSettingsService userRunSettingsService;
        private string msCodeCoveragePath;
        private string shimPath;

        [ImportingConstructor]
        public MsCodeCoverageRunSettingsService(
            IToolFolder toolFolder, 
            IToolZipProvider toolZipProvider, 
            IAppOptionsProvider appOptionsProvider,
            ICoverageToolOutputManager coverageOutputManager,
            IProjectRunSettingsGenerator projectRunSettingsGenerator,
            IUserRunSettingsService userRunSettingsService,
            IRunSettingsTemplate runSettingsTemplate,
            ICustomRunSettingsTemplateProvider customRunSettingsTemplateProvider,
            IRunSettingsTemplateReplacementsFactory runSettingsTemplateReplacementsFactory,
            IShimCopier shimCopier,
            ILogger logger,
            IReportGeneratorUtil reportGeneratorUtil
            )
        {
            this.toolFolder = toolFolder;
            this.toolZipProvider = toolZipProvider;
            this.appOptionsProvider = appOptionsProvider;
            this.coverageOutputManager = coverageOutputManager;
            this.runSettingsTemplate = runSettingsTemplate;
            this.customRunSettingsTemplateProvider = customRunSettingsTemplateProvider;
            this.runSettingsTemplateReplacementsFactory = runSettingsTemplateReplacementsFactory;
            this.shimCopier = shimCopier;
            this.logger = logger;
            this.reportGeneratorUtil = reportGeneratorUtil;
            this.projectRunSettingsGenerator = projectRunSettingsGenerator;
            this.userRunSettingsService = userRunSettingsService;
        }

        public void Initialize(string appDataFolder, IFCCEngine fccEngine, CancellationToken cancellationToken)
        {
            this.fccEngine = fccEngine;
            var zipDestination = toolFolder.EnsureUnzipped(appDataFolder, zipDirectoryName, toolZipProvider.ProvideZip(zipPrefix), cancellationToken);
            msCodeCoveragePath = Path.Combine(zipDestination, "build", "netstandard1.0");
            shimPath = Path.Combine(zipDestination, "build", "netstandard1.0", "CodeCoverage", "coreclr", "Microsoft.VisualStudio.CodeCoverage.Shim.dll");
        }
        
        #region set up for collection
       
        public async Task<MsCodeCoverageCollectionStatus> IsCollectingAsync(ITestOperation testOperation)
        {
            var collectionStatus = MsCodeCoverageCollectionStatus.NotCollecting;

            var (coverageProjects,coverageProjectsWithRunSettings, coverageProjectsWithoutRunSettings) = await GetCoverageProjectsAsync(testOperation);

            var useMsCodeCoverage = appOptionsProvider.Get().MsCodeCoverage;

            IUserRunSettingsAnalysisResult analysisResult = null;
            try
            {
                analysisResult = userRunSettingsService.Analyse(
                    coverageProjectsWithRunSettings,
                    useMsCodeCoverage,
                    runSettingsTemplate,
                    msCodeCoveragePath
                );
            }
            catch (Exception exc)
            {
                await CombinedLogExceptionAsync(exc, "Exception analysing runsettings files");
                return MsCodeCoverageCollectionStatus.Error;
            }

            var coverageProjectsForShim = analysisResult.ProjectsWithFCCMsTestAdapter;

            if (analysisResult.Suitable)
            {
                await PrepareCoverageProjectsAsync(coverageProjects);
                SetUserRunSettingsProjectDetails(coverageProjectsWithRunSettings);
                if (coverageProjectsWithoutRunSettings.Any())
                {
                    if (useMsCodeCoverage || analysisResult.SpecifiedMsCodeCoverage)
                    {
                        var (successfullyGeneratedRunSettings, customTemplatePaths, templateCoverageProjectsWithFCCMsTestAdapter) = await GenerateProjectsRunSettingsAsync(coverageProjectsWithoutRunSettings, testOperation.SolutionDirectory);
                        if (successfullyGeneratedRunSettings)
                        {
                            coverageProjectsForShim.AddRange(templateCoverageProjectsWithFCCMsTestAdapter);
                            await CombinedLogAsync(() =>
                            {
                                var leadingMessage = customTemplatePaths.Any() ? $"{msCodeCoverageMessage} - custom template paths" : msCodeCoverageMessage;
                                var loggerMessages = new List<string> { leadingMessage }.Concat(customTemplatePaths.Distinct());
                                logger.Log(loggerMessages);
                                reportGeneratorUtil.LogCoverageProcess(msCodeCoverageMessage);
                            });
                            collectionStatus = MsCodeCoverageCollectionStatus.Collecting;
                        }
                        else
                        {
                            collectionStatus = MsCodeCoverageCollectionStatus.Error;
                        }
                    }
                }
                else
                {
                    collectionStatus = MsCodeCoverageCollectionStatus.Collecting;
                    await CombinedLogAsync($"{msCodeCoverageMessage} with user runsettings");
                }
            }

            CopyShimWhenCollecting(coverageProjectsForShim, collectionStatus);

            return collectionStatus;
        }

        private async Task<(List<ICoverageProject> allCoverageProjects,List<ICoverageProject> coverageProjectsWithRunSettings, List<ICoverageProject> coverageProjectsWithoutRunSettings)> GetCoverageProjectsAsync(ITestOperation testOperation)
        {
            var coverageProjects = await testOperation.GetCoverageProjectsAsync();
            var coverageProjectsWithRunSettings = coverageProjects.Where(coverageProject => coverageProject.RunSettingsFile != null).ToList();
            var coverageProjectsWithoutRunSettings = coverageProjects.Except(coverageProjectsWithRunSettings).ToList();
            return (coverageProjects, coverageProjectsWithRunSettings, coverageProjectsWithoutRunSettings);
        }

        private void CopyShimWhenCollecting(List<ICoverageProject> coverageProjectsForShim, MsCodeCoverageCollectionStatus collectionStatus)
        {
            if (collectionStatus == MsCodeCoverageCollectionStatus.Collecting)
            {
                shimCopier.Copy(shimPath, coverageProjectsForShim);
            }
        }

        private async Task PrepareCoverageProjectsAsync(List<ICoverageProject> coverageProjects)
        {
            coverageOutputManager.SetProjectCoverageOutputFolder(coverageProjects);
            foreach (var coverageProject in coverageProjects)
            {
                await coverageProject.PrepareForCoverageAsync(CancellationToken.None, false);
            }
        }

        private void SetUserRunSettingsProjectDetails(List<ICoverageProject> coverageProjectsWithRunSettings)
        {
            userRunSettingsProjectDetailsLookup = new Dictionary<string, IUserRunSettingsProjectDetails>();
            foreach (var coverageProjectWithRunSettings in coverageProjectsWithRunSettings)
            {
                var userRunSettingsProjectDetails = new UserRunSettingsProjectDetails
                {
                    Settings = coverageProjectWithRunSettings.Settings,
                    OutputFolder = coverageProjectWithRunSettings.ProjectOutputFolder,
                    TestDllFile = coverageProjectWithRunSettings.TestDllFile,
                    ExcludedReferencedProjects = coverageProjectWithRunSettings.ExcludedReferencedProjects,
                    IncludedReferencedProjects = coverageProjectWithRunSettings.IncludedReferencedProjects
                };
                userRunSettingsProjectDetailsLookup.Add(coverageProjectWithRunSettings.TestDllFile, userRunSettingsProjectDetails);
            }
        }

        private async Task<(bool Success, List<string> CustomTemplatePaths, List<ICoverageProject> coverageProjectsWithFCCMsTestAdapter)> GenerateProjectsRunSettingsAsync(IEnumerable<ICoverageProject> coverageProjectsWithoutRunSettings, string solutionDirectory)
        {
            IEnumerable<CoverageProjectRunSettings> projectsRunSettings;
            try
            {
                projectsRunSettings = GetCoverageProjectsRunSettings(coverageProjectsWithoutRunSettings, solutionDirectory);
            }
            catch (Exception ex)
            {
                await CombinedLogExceptionAsync(ex, "Exception generating ms runsettings");
                return (false, null, null);
            }
            
            try
            {
                await projectRunSettingsGenerator.WriteProjectsRunSettingsAsync(projectsRunSettings);
            }
            catch (Exception ex)
            {
                await CombinedLogExceptionAsync(ex, "Exception writing ms runsettings");
                try
                {
                    await projectRunSettingsGenerator.RemoveGeneratedProjectSettingsAsync(coverageProjectsWithoutRunSettings);
                }
                catch { }
                return (false, null,null);
            }
            List<string> customTemplatePaths = new List<string>();
            List<ICoverageProject> coverageProjectsWithFCCMsTestAdapter = new List<ICoverageProject>();
            foreach(var projectRunSettings in projectsRunSettings)
            {
                if (projectRunSettings.ReplacedTestAdapter)
                {
                    coverageProjectsWithFCCMsTestAdapter.Add(projectRunSettings.CoverageProject);
                }
                if (projectRunSettings.CustomTemplatePath != null)
                {
                    customTemplatePaths.Add(projectRunSettings.CustomTemplatePath);
                }
            }
            
            return (true, customTemplatePaths, coverageProjectsWithFCCMsTestAdapter);
        }

        private List<CoverageProjectRunSettings> GetCoverageProjectsRunSettings(IEnumerable<ICoverageProject> coverageProjects, string solutionDirectory)
        {
            return coverageProjects.Select(coverageProject => 
            {
                var projectDirectory = Path.GetDirectoryName(coverageProject.ProjectFile);
                var (runSettingsTemplate, customTemplatePath) = GetRunSettingsTemplate(projectDirectory, solutionDirectory);
                var templateReplaceResult = CreateProjectRunSettings(coverageProject, runSettingsTemplate);

                return new CoverageProjectRunSettings { 
                    CoverageProject = coverageProject, 
                    RunSettings = templateReplaceResult.Replaced, 
                    CustomTemplatePath = customTemplatePath,
                    ReplacedTestAdapter = templateReplaceResult.ReplacedTestAdapter
                };
                
            }).ToList();
        }

        private (string Template, string CustomPath) GetRunSettingsTemplate(string projectDirectory, string solutionDirectory)
        {
            string customPath = null;
            string template;
            var customRunSettingsTemplateDetails = customRunSettingsTemplateProvider.Provide(projectDirectory, solutionDirectory);
            if (customRunSettingsTemplateDetails != null)
            {
                customPath = customRunSettingsTemplateDetails.Path;
                template = runSettingsTemplate.ConfigureCustom(customRunSettingsTemplateDetails.Template);
            }
            else
            {
                template = runSettingsTemplate.ToString();
            }
            return (template, customPath);
        }

        private TemplateReplaceResult CreateProjectRunSettings(ICoverageProject coverageProject, string runSettingsTemplate)
        {
            var replacements = runSettingsTemplateReplacementsFactory.Create(coverageProject, msCodeCoveragePath);
            
            var templateReplaceResult = this.runSettingsTemplate.Replace(runSettingsTemplate, replacements);
            return new TemplateReplaceResult
            {
                Replaced = XDocument.Parse(templateReplaceResult.Replaced).FormatXml(),
                ReplacedTestAdapter = templateReplaceResult.ReplacedTestAdapter
            };
        }
        #endregion

        #region IRunSettingsService
        public IXPathNavigable AddRunSettings(IXPathNavigable inputRunSettingDocument, IRunSettingsConfigurationInfo configurationInfo, Microsoft.VisualStudio.TestWindow.Extensibility.ILogger log)
        {
            if (configurationInfo.RequestState == RunSettingConfigurationInfoState.Execution && !runSettingsTemplate.FCCGenerated(inputRunSettingDocument))
            {
                var replacements = runSettingsTemplateReplacementsFactory.Create(configurationInfo.TestContainers, userRunSettingsProjectDetailsLookup, msCodeCoveragePath);
                return userRunSettingsService.AddFCCRunSettings(runSettingsTemplate, replacements, inputRunSettingDocument);
            }
            return null;
        }
        #endregion

        internal IThreadHelper threadHelper = new VsThreadHelper();

        public async Task CollectAsync(IOperation operation, ITestOperation testOperation)
        {
            var resultsUris = operation.GetRunSettingsMsDataCollectorResultUri();
            var coberturaFiles = new string[0];
            if (resultsUris != null)
            {
                coberturaFiles = resultsUris.Select(uri => uri.LocalPath).Where(f => f.EndsWith(".cobertura.xml")).ToArray();
            }

            if (coberturaFiles.Length == 0)
            {
                await CombinedLogAsync("No cobertura files for ms code coverage.");
            }

            fccEngine.RunAndProcessReport(coberturaFiles,() =>
            {
                threadHelper.JoinableTaskFactory.Run(async () =>
                {
                    List<ICoverageProject> coverageProjects = await testOperation.GetCoverageProjectsAsync();
                    await projectRunSettingsGenerator.RemoveGeneratedProjectSettingsAsync(coverageProjects);
                });
            });
        }

        public void StopCoverage()
        {
            fccEngine.StopCoverage();
        }

        #region Logging
        private async Task CombinedLogAsync(string message)
        {
            await CombinedLogAsync(() =>
            {
                logger.Log(message);
                reportGeneratorUtil.LogCoverageProcess(message);
            });
        }

        private async Task CombinedLogAsync(Action action)
        {
            await threadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            action();
        }

        private Task CombinedLogExceptionAsync(Exception ex, string reason)
        {
            return CombinedLogAsync(() =>
            {
                logger.Log(reason, ex.ToString());
                reportGeneratorUtil.LogCoverageProcess(reason);
            });
        }
        #endregion

    }
}

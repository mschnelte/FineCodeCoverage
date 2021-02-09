﻿using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using FineCodeCoverage.Engine;
using FineCodeCoverage.Options;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.TestWindow.Extensibility;
using Microsoft.VisualStudio.Utilities;
using ReflectObject;

namespace FineCodeCoverage.Impl
{
    [Name(Vsix.TestContainerDiscovererName)]
    [Export(typeof(TestContainerDiscoverer))]
    [Export(typeof(ITestContainerDiscoverer))]
    internal class TestContainerDiscoverer : ITestContainerDiscoverer
    {
#pragma warning disable 67
        public event EventHandler TestContainersUpdated;
#pragma warning restore 67
        private readonly IFCCEngine fccEngine;
        private readonly ITestOperationFactory testOperationFactory;
        internal System.Threading.Thread initializeThread;

        public Uri ExecutorUri => new Uri($"executor://{Vsix.Code}.Executor/v1");
        public IEnumerable<ITestContainer> TestContainers => Enumerable.Empty<ITestContainer>();


        [ImportingConstructor]
        public TestContainerDiscoverer
        (
            [Import(typeof(IOperationState))]
            IOperationState operationState,

            [Import(typeof(SVsServiceProvider))]
            IServiceProvider serviceProvider,

            IFCCEngine fccEngine,
            
            IInitializer initializer,
            ITestOperationFactory testOperationFactory
        )
        {
            this.fccEngine = fccEngine;
            
            this.testOperationFactory = testOperationFactory;
            initializeThread = new Thread(() =>
            {
                initializer.Initialize(serviceProvider);
                // important this comes last - ensures when reload coverage everything in place
                operationState.StateChanged += OperationState_StateChanged;

            });
            initializeThread.Start();
            
        }
        
        private void OperationState_StateChanged(object sender, OperationStateChangedEventArgs e)
        {
            try
            {
                if(e.State == TestOperationStates.TestExecutionCanceling)
                {
                    fccEngine.StopCoverage();
                }

                if (e.State == TestOperationStates.TestExecutionStarting)
                {
                    fccEngine.TryReloadCoverage(async settings =>
                    {
                        if (!settings.RunInParallel)
                        {
                            return ReloadCoverageRequest.Cancel();
                        }
                        var testOperation = testOperationFactory.Create(e.Operation);
                        var coverageProjects = await testOperation.GetCoverageProjectsAsync();
                        return ReloadCoverageRequest.Cover(coverageProjects);
                    });
                }

                if (e.State == TestOperationStates.TestExecutionFinished)
                {
                    fccEngine.TryReloadCoverage(async settings =>
                    {
                        if (settings.RunInParallel)
                        {
                            return ReloadCoverageRequest.Cancel();
                        }

                        var testOperation = testOperationFactory.Create(e.Operation);
                        if (!settings.RunWhenTestsFail && testOperation.FailedTests > 0)
                        {
                            Logger.Log($"Skipping coverage due to failed tests.  Option {nameof(AppOptions.RunWhenTestsFail)} is false");
                            return ReloadCoverageRequest.Cancel();
                        }

                        var totalTests = testOperation.TotalTests;
                        var runWhenTestsExceed = settings.RunWhenTestsExceed;
                        if (totalTests > 0) // in case this changes to not reporting total tests
                        {
                            if (totalTests <= runWhenTestsExceed)
                            {
                                Logger.Log($"Skipping coverage as total tests ({totalTests}) <= {nameof(AppOptions.RunWhenTestsExceed)} ({runWhenTestsExceed})");
                                return ReloadCoverageRequest.Cancel();
                            }
                        }
                        var coverageProjects = await testOperation.GetCoverageProjectsAsync();
                        return ReloadCoverageRequest.Cover(coverageProjects);
                    });
                }
            }
            catch (PropertyDoesNotExistException propertyDoesNotExistException)
            {
                Logger.Log("Error test container discoverer reflection");
                throw new Exception(propertyDoesNotExistException.Message);
            }
            catch (Exception exception)
            {
                Logger.Log("Error processing unit test events", exception);
            }
        }
    }
}
﻿using System;
using System.Linq;
using FineCodeCoverage;
using System.Diagnostics;
using Microsoft.VisualStudio;
using System.Collections.Generic;
using Microsoft.VisualStudio.Shell;
using System.Diagnostics.CodeAnalysis;
using Microsoft.VisualStudio.Shell.Interop;
using System.ComponentModel.Composition;
using Microsoft;
using EnvDTE;

interface IShowFCCOutputPane
{
    System.Threading.Tasks.Task ShowAsync();
}
[Export(typeof(IShowFCCOutputPane))]
[Export(typeof(ILogger))]
public class Logger : ILogger, IShowFCCOutputPane
{
    private IVsOutputWindowPane _pane;
    private IVsOutputWindow _outputWindow;
    private DTE dte;
    private readonly IServiceProvider _serviceProvider;
    private Guid fccPaneGuid = Guid.Parse("3B3C775A-0050-445D-9022-0230957805B2");

    [ImportingConstructor]
    public Logger(
        [Import(typeof(SVsServiceProvider))]
        IServiceProvider serviceProvider
    )
    {
        this._serviceProvider = serviceProvider;
        staticLogger = this;
    }

    IVsOutputWindowPane CreatePane(Guid paneGuid, string title,
        bool visible, bool clearWithSolution)
    {
        
        ThreadHelper.ThrowIfNotOnUIThread();
        _outputWindow = (IVsOutputWindow)_serviceProvider.GetService(typeof(SVsOutputWindow));
        Assumes.Present(_outputWindow);
        dte = (EnvDTE.DTE)_serviceProvider.GetService(typeof(EnvDTE.DTE));
        Assumes.Present(dte);

        // Create a new pane.
        _outputWindow.CreatePane(
            ref paneGuid,
            title,
            Convert.ToInt32(visible),
            Convert.ToInt32(clearWithSolution));

        // Retrieve the new pane.
        _outputWindow.GetPane(ref paneGuid, out IVsOutputWindowPane pane);
        return pane;
    }

    private void SetPane()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        // do not clear with solution otherwise will not get initialize methods
        _pane = CreatePane(fccPaneGuid, "FCC", true, false);
    }

    [SuppressMessage("Usage", "VSTHRD102:Implement internal logic asynchronously")]
    private void LogImpl(object[] message, bool withTitle)
    {
        try
        {
            var messageList = new List<string>(message?.Select(x => x?.ToString()?.Trim(' ', '\r', '\n')).Where(x => !string.IsNullOrWhiteSpace(x)));

            if (!messageList.Any())
            {
                return;
            }

            ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                if(_pane == null)
                {
                    SetPane();
                }

                if(_pane == null)
                {
                    return;
                }

                var logs = string.Join(Environment.NewLine, messageList);

                if (withTitle)
                {
                    _pane.OutputStringThreadSafe($"{Environment.NewLine}{Vsix.Name} : {logs}{Environment.NewLine}");
                }
                else
                {
                    _pane.OutputStringThreadSafe($"{logs}{Environment.NewLine}");
                }
            });
        }
        catch (Exception ex)
        {
            Debug.Write(ex);
        }
    }

    private static Logger staticLogger;
    public static void Log(params string[] message)
    {
        (staticLogger as ILogger).Log(message);
    }

    public void Log(params object[] message)
    {
        LogImpl(message, true);
    }

    void ILogger.Log(params string[] message)
    {
        LogImpl(message, true);
    }

    public void Log(IEnumerable<object> message)
    {
        LogImpl(message.ToArray(), true);
    }

    public void Log(IEnumerable<string> message)
    {
        LogImpl(message.ToArray(), true);
    }

    public void LogWithoutTitle(params object[] message)
    {
        LogImpl(message, false);
    }

    public void LogWithoutTitle(params string[] message)
    {
        LogImpl(message, false);
    }

    public void LogWithoutTitle(IEnumerable<object> message)
    {
        LogImpl(message.ToArray(), false);
    }

    public void LogWithoutTitle(IEnumerable<string> message)
    {
        LogImpl(message.ToArray(), false);
    }

    public async System.Threading.Tasks.Task ShowAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        if (_pane == null)
        {
            SetPane();
        }

        if (_pane != null)
        {
            EnvDTE.Window window = dte.Windows.Item(EnvDTE.Constants.vsWindowKindOutput);
            window.Activate();
            _pane.Activate();
        }
    }
}
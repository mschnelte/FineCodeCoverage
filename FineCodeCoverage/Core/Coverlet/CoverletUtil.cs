﻿using FineCodeCoverage.Engine.Model;
using FineCodeCoverage.Engine.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace FineCodeCoverage.Engine.Coverlet
{
	internal class CoverletUtil
	{
		public const string CoverletName = "coverlet.console";
		public static string CoverletExePath { get; private set; }
		public static string AppDataCoverletFolder { get; private set; }
		public static Version CurrentCoverletVersion { get; private set; }
		public static Version MimimumCoverletVersion { get; } = Version.Parse("1.7.2");

		public static void Initialize(string appDataFolder)
		{
			AppDataCoverletFolder = Path.Combine(appDataFolder, "coverlet");
			Directory.CreateDirectory(AppDataCoverletFolder);
			GetCoverletVersion();

			if (CurrentCoverletVersion == null)
			{
				InstallCoverlet();
			}
			else if (CurrentCoverletVersion < MimimumCoverletVersion)
			{
				UpdateCoverlet();
			}
		}

		public static Version GetCoverletVersion()
		{
			var title = "Coverlet Get Info";

			var processStartInfo = new ProcessStartInfo
			{
				FileName = "dotnet",
				CreateNoWindow = true,
				UseShellExecute = false,
				RedirectStandardError = true,
				RedirectStandardOutput = true,
				WindowStyle = ProcessWindowStyle.Hidden,
				WorkingDirectory = AppDataCoverletFolder,
				Arguments = $"tool list --tool-path \"{AppDataCoverletFolder}\"",
			};

			var process = Process.Start(processStartInfo);

			process.WaitForExit();

			var processOutput = process.GetOutput();

			if (process.ExitCode != 0)
			{
				Logger.Log($"{title} Error", processOutput);
				return null;
			}

			var outputLines = processOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
			var coverletLine = outputLines.FirstOrDefault(x => x.Trim().StartsWith(CoverletName, StringComparison.OrdinalIgnoreCase));

			if (string.IsNullOrWhiteSpace(coverletLine))
			{
				// coverlet is not installed
				CoverletExePath = null;
				CurrentCoverletVersion = null;
				return null;
			}

			var coverletLineTokens = coverletLine.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
			var coverletVersion = coverletLineTokens[1].Trim();

			CurrentCoverletVersion = Version.Parse(coverletVersion);

			CoverletExePath = Directory.GetFiles(AppDataCoverletFolder, "coverlet.exe", SearchOption.AllDirectories).FirstOrDefault()
						   ?? Directory.GetFiles(AppDataCoverletFolder, "*coverlet*.exe", SearchOption.AllDirectories).FirstOrDefault();

			return CurrentCoverletVersion;
		}

		public static void UpdateCoverlet()
		{
			var title = "Coverlet Update";

			var processStartInfo = new ProcessStartInfo
			{
				FileName = "dotnet",
				CreateNoWindow = true,
				UseShellExecute = false,
				RedirectStandardError = true,
				RedirectStandardOutput = true,
				WindowStyle = ProcessWindowStyle.Hidden,
				WorkingDirectory = AppDataCoverletFolder,
				Arguments = $"tool update {CoverletName} --verbosity normal --version {MimimumCoverletVersion} --tool-path \"{AppDataCoverletFolder}\"",
			};

			var process = Process.Start(processStartInfo);

			process.WaitForExit();

			var processOutput = process.GetOutput();

			if (process.ExitCode != 0)
			{
				Logger.Log($"{title} Error", processOutput);
				return;
			}

			GetCoverletVersion();

			Logger.Log(title, processOutput);
		}

		public static void InstallCoverlet()
		{
			var title = "Coverlet Install";

			var processStartInfo = new ProcessStartInfo
			{
				FileName = "dotnet",
				CreateNoWindow = true,
				UseShellExecute = false,
				RedirectStandardError = true,
				RedirectStandardOutput = true,
				WindowStyle = ProcessWindowStyle.Hidden,
				WorkingDirectory = AppDataCoverletFolder,
				Arguments = $"tool install {CoverletName} --verbosity normal --version {MimimumCoverletVersion} --tool-path \"{AppDataCoverletFolder}\"",
			};

			var process = Process.Start(processStartInfo);

			process.WaitForExit();

			var processOutput = process.GetOutput();

			if (process.ExitCode != 0)
			{
				Logger.Log($"{title} Error", processOutput);
				return;
			}

			GetCoverletVersion();

			Logger.Log(title, processOutput);
		}

		[SuppressMessage("Usage", "VSTHRD002:Avoid problematic synchronous waits")]
		public static bool RunCoverlet(CoverageProject project, bool throwError = false)
		{
			var title = $"Coverlet Run ({project.ProjectName})";

			if (File.Exists(project.CoverToolOutputFile))
			{
				File.Delete(project.CoverToolOutputFile);
			}

			if (Directory.Exists(project.WorkOutputFolder))
			{
				Directory.Delete(project.WorkOutputFolder, true);
			}

			Directory.CreateDirectory(project.WorkOutputFolder);

			var coverletSettings = new List<string>();

			coverletSettings.Add($@"""{project.TestDllFileInWorkFolder}""");

			coverletSettings.Add($@"--format ""cobertura""");

			foreach (var value in (project.Settings.Exclude ?? new string[0]).Where(x => !string.IsNullOrWhiteSpace(x)))
			{
				coverletSettings.Add($@"--exclude ""{value.Replace("\"", "\\\"").Trim(' ', '\'')}""");
			}

			foreach (var value in (project.Settings.Include ?? new string[0]).Where(x => !string.IsNullOrWhiteSpace(x)))
			{
				coverletSettings.Add($@"--include ""{value.Replace("\"", "\\\"").Trim(' ', '\'')}""");
			}

			foreach (var value in (project.Settings.ExcludeByFile ?? new string[0]).Where(x => !string.IsNullOrWhiteSpace(x)))
			{
				coverletSettings.Add($@"--exclude-by-file ""{value.Replace("\"", "\\\"").Trim(' ', '\'')}""");
			}

			foreach (var value in (project.Settings.ExcludeByAttribute ?? new string[0]).Where(x => !string.IsNullOrWhiteSpace(x)))
			{
				coverletSettings.Add($@"--exclude-by-attribute ""{value.Replace("\"", "\\\"").Trim(' ', '\'', '[', ']')}""");
			}

			if (project.Settings.IncludeTestAssembly)
			{
				coverletSettings.Add("--include-test-assembly");
			}

			coverletSettings.Add($@"--target ""dotnet""");

			coverletSettings.Add($@"--output ""{ project.CoverToolOutputFile }""");

			coverletSettings.Add($@"--targetargs ""test  """"{project.TestDllFileInWorkFolder}"""" --nologo --blame --results-directory """"{project.WorkOutputFolder}"""" --diag """"{project.WorkOutputFolder}/diagnostics.log""""  """);

			Logger.Log($"{title} Arguments {Environment.NewLine}{string.Join($"{Environment.NewLine}", coverletSettings)}");

			var processStartInfo = new ProcessStartInfo
			{
				FileName = CoverletExePath,
				CreateNoWindow = true,
				UseShellExecute = false,
				RedirectStandardError = true,
				RedirectStandardOutput = true,
				WorkingDirectory = project.WorkFolder,
				WindowStyle = ProcessWindowStyle.Hidden,
				Arguments = string.Join(" ", coverletSettings),
			};

			var process = Process.Start(processStartInfo);

			if (!process.HasExited)
			{
				var stopWatch = new Stopwatch();
				stopWatch.Start();

				if (!Task.Run(() => process.WaitForExit()).Wait(TimeSpan.FromSeconds(project.Settings.CoverToolTimeout)))
				{
					stopWatch.Stop();
					Task.Run(() => { try { process.Kill(); } catch { } }).Wait(TimeSpan.FromSeconds(10));

					var errorMessage = $"Coverlet timed out after {stopWatch.Elapsed.TotalSeconds} seconds ({nameof(project.Settings.CoverToolTimeout)} is {project.Settings.CoverToolTimeout} seconds)";

					if (throwError)
					{
						throw new Exception(errorMessage);
					}

					Logger.Log($"{title} Error", errorMessage);
					return false;
				}
			}

			var processOutput = process.GetOutput();

			if (process.ExitCode != 0)
			{
				if (throwError)
				{
					throw new Exception(processOutput);
				}

				Logger.Log($"{title} Error", processOutput);
				return false;
			}

			Logger.Log(title, processOutput);
			return true;
		}
	}
}
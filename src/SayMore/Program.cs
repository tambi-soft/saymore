using System;
using System.Drawing;
using System.Linq;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using Localization;
using Palaso.IO;
using Palaso.Progress;
using Palaso.Reporting;
using SayMore.Media;
using SayMore.Properties;
using SayMore.Utilities.ProjectWindow;
using SilTools;

namespace SayMore
{
	static class Program
	{
		/// <summary>
		/// We have one project open at a time, and this helps us bootstrap the project and
		/// properly dispose of various things when the project is closed.
		/// </summary>
		private static ProjectContext _projectContext;

		private static string _pathOfLoadedProjectFile;
		private static ApplicationContainer _applicationContainer;
		private static Font _dialogFont;

		/// ------------------------------------------------------------------------------------
		/// <summary>
		/// The main entry point for the application.
		/// </summary>
		/// ------------------------------------------------------------------------------------
		[STAThread]
		static void Main()
		{
			// This is pretty annoying: When, because .Net doesn't have a font style of SemiBold
			// (e.g. Segoe UI SemiBold), fonts having that style are assumed to be bold, but
			// when some controls (e.g. Label) are set to a SemiBold font, they are displayed as
			// bold, so we'll create our own, forcing the style to regular, which seems to work.
			// Don't use SystemFonts.DefaultFont because that always returns "Microsoft Sans Serif"
			// and SystemFonts.DialogFont always returns "Tahoma", regardless of OS.
			// See: http://benhollis.net/blog/2007/04/11/setting-the-correct-default-font-in-net-windows-forms-apps/
			_dialogFont = new Font(SystemFonts.MessageBoxFont, FontStyle.Regular);

			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);

			//bring in settings from any previous version
			if (Settings.Default.NeedUpgrade)
			{
				//see http://stackoverflow.com/questions/3498561/net-applicationsettingsbase-should-i-call-upgrade-every-time-i-load
				Settings.Default.Upgrade();
				Settings.Default.NeedUpgrade = false;
				Settings.Default.Save();
			}

			Settings.Default.MRUList = MruFiles.Initialize(Settings.Default.MRUList, 4);
			_applicationContainer = new ApplicationContainer(false);

			SetUpLocalization();
			SetUpErrorHandling();
			SetUpReporting();

			var args = Environment.GetCommandLineArgs();
			var firstTimeArg = args.FirstOrDefault(x => x.ToLower().StartsWith("-i"));
			if (firstTimeArg != null)
			{
				using (var dlg = new FirstTimeRunDialog("put filename here"))
					dlg.ShowDialog();
			}

			StartUpShellBasedOnMostRecentUsedIfPossible();

			Application.Run();
			Settings.Default.Save();

			if (_projectContext != null)
				_projectContext.Dispose();
		}

		/// ------------------------------------------------------------------------------------
		public static string AppDataFolder
		{
			get
			{
				var path = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
				path = Path.Combine(path, "SIL");
				return Path.Combine(path, Application.ProductName);
			}
		}

		/// ------------------------------------------------------------------------------------
		public static Font DialogFont
		{
			get { return _dialogFont ?? SystemFonts.MessageBoxFont; }
		}

		/// ------------------------------------------------------------------------------------
		private static void StartUpShellBasedOnMostRecentUsedIfPossible()
		{
			// In Windows 7, just holding down the shift key while starting the app. works to
			// prevent the last project from being loaded. However, I found on XP (at least
			// running XP mode) that holding the shift key while starting an app. does
			// nothing... as in the app. is not launched. Therefore, running SayMore with an
			// 'nl' command-line option will also suppress loading the last project.
			var noLoadArg = Environment.GetCommandLineArgs().FirstOrDefault(a => "-nl-NL/nl/NL".Contains(a));

			if (MruFiles.Latest == null || !File.Exists(MruFiles.Latest) ||
				(Control.ModifierKeys == Keys.Shift) || noLoadArg != null ||
				!OpenProjectWindow(MruFiles.Latest))
			{
				//since the message pump hasn't started yet, show the UI for choosing when it is
				Application.Idle += ChooseAnotherProject;
			}
		}

		/// ------------------------------------------------------------------------------------
		private static bool OpenProjectWindow(string projectPath)
		{
			Debug.Assert(_projectContext == null);

			try
			{
				// Remove this call if we end only wanting to show the splash screen except
				// at app. startup. Right now it's shown whenever a project is loaded.
				_applicationContainer.ShowSplashScreen();

				_applicationContainer.SetProjectNameOnSplashScreen(projectPath);
				_projectContext = _applicationContainer.CreateProjectContext(projectPath);
				_projectContext.ProjectWindow.Closed += HandleProjectWindowClosed;
				_projectContext.ProjectWindow.Activated += HandleProjectWindowActivated;
				_projectContext.ProjectWindow.Show();
				_pathOfLoadedProjectFile = projectPath;
				Application.Idle += SaveLastOpenedProjectInMRUList;
				return true;
			}
			catch (Exception e)
			{
				HandleErrorOpeningProjectWindow(e, projectPath);
			}

			return false;
		}

		/// ------------------------------------------------------------------------------------
		/// <summary>
		/// By the time we get here, we know the app. has settled down after loading a project.
		/// Now that the project has been loaded without crashing, save the project as the
		/// most recently opened project. xref: SP-186.
		/// </summary>
		/// ------------------------------------------------------------------------------------
		private static void SaveLastOpenedProjectInMRUList(object sender, EventArgs e)
		{
			Application.Idle -= SaveLastOpenedProjectInMRUList;
			MruFiles.AddNewPath(_pathOfLoadedProjectFile);
			Settings.Default.Save();
		}

		/// ------------------------------------------------------------------------------------
		private static void HandleProjectWindowActivated(object sender, EventArgs e)
		{
			_projectContext.ProjectWindow.Activated -= HandleProjectWindowActivated;
			_applicationContainer.CloseSplashScreen();

			// Sometimes after closing the splash screen the project window
			// looses focus, so do this.
			_projectContext.ProjectWindow.Activate();
		}

		/// ------------------------------------------------------------------------------------
		private static void HandleErrorOpeningProjectWindow(Exception error, string projectPath)
		{
			if (_projectContext != null)
			{
				if (_projectContext.ProjectWindow != null)
				{
					_projectContext.ProjectWindow.Closed -= HandleProjectWindowClosed;
					_projectContext.ProjectWindow.Close();
				}

				_projectContext.Dispose();
				_projectContext = null;
			}

			_applicationContainer.CloseSplashScreen();

			var msg = LocalizationManager.GetString("MainWindow.LoadingProjectErrorMsg",
				"{0} had a problem loading the {1} project. Please report this problem " +
				"to the developers by clicking 'Details' below.");

			ErrorReport.NotifyUserOfProblem(new ShowAlwaysPolicy(), error, msg,
				Application.ProductName, Path.GetFileNameWithoutExtension(projectPath));

			Settings.Default.MRUList.Remove(projectPath);
			MruFiles.Initialize(Settings.Default.MRUList);
		}

		/// ------------------------------------------------------------------------------------
		static void ChooseAnotherProject(object sender, EventArgs e)
		{
			Application.Idle -= ChooseAnotherProject;
			_applicationContainer.CloseSplashScreen();

			while (true)
			{
				using (var dlg = _applicationContainer.CreateWelcomeDialog())
				{
					if (dlg.ShowDialog() != DialogResult.OK)
					{
						Application.Exit();
						return;
					}

					if (OpenProjectWindow(dlg.Model.ProjectSettingsFilePath))
						return;
				}
			}
		}

		/// ------------------------------------------------------------------------------------
		static void HandleProjectWindowClosed(object sender, EventArgs e)
		{
			_projectContext.Dispose();
			_projectContext = null;

			if (((ProjectWindow)sender).UserWantsToOpenADifferentProject)
			{
				Application.Idle += ChooseAnotherProject;
			}
			else
			{
				Application.Exit();
			}
		}

		/// ------------------------------------------------------------------------------------
		public static void SuspendBackgroundProcesses()
		{
			if (_projectContext != null)
				_projectContext.SuspendBackgroundProcesses();
		}

		/// ------------------------------------------------------------------------------------
		public static void ResumeBackgroundProcesses(bool processAllPendingEventsNow)
		{
			if (_projectContext == null)
				return;

			if (processAllPendingEventsNow)
				WaitCursor.Show();

			_projectContext.ResumeBackgroundProcesses(processAllPendingEventsNow);

			if (processAllPendingEventsNow)
				WaitCursor.Hide();
		}

		/// ------------------------------------------------------------------------------------
		public static void ShowHelpTopic(string topicLink)
		{
			var path = FileLocator.GetFileDistributedWithApplication("SayMore.chm");
			Help.ShowHelp(new Label(), path, topicLink);
			UsageReporter.SendNavigationNotice("Help: " + topicLink);
		}

		/// ------------------------------------------------------------------------------------
		public static void SetUpLocalization()
		{
			var installedStringFileFolder = FileLocator.GetDirectoryDistributedWithApplication("mplayer");
			installedStringFileFolder = Path.GetDirectoryName(installedStringFileFolder);

			LocalizationManager.Create(Settings.Default.UserInterfaceLanguage,
				"SayMore", "SayMore", Application.ProductVersion,
				installedStringFileFolder, AppDataFolder, "SayMore");

			Settings.Default.UserInterfaceLanguage = LocalizationManager.UILanguageId;
		}

		/// ------------------------------------------------------------------------------------
		private static void SetUpErrorHandling()
		{
			Application.ApplicationExit += (sender, args) => ExternalProcess.CleanUpAllProcesses();
			Application.ThreadException += (sender, args) => ExternalProcess.CleanUpAllProcesses();
			AppDomain.CurrentDomain.UnhandledException += (sender, args) => ExternalProcess.CleanUpAllProcesses();

			ErrorReport.EmailAddress = "issues@saymore.palaso.org";
			ErrorReport.AddStandardProperties();
			ExceptionHandler.Init();
		}

		/// ------------------------------------------------------------------------------------
		private static void SetUpReporting()
		{
			if (Settings.Default.Reporting == null)
			{
				Settings.Default.Reporting = new ReportingSettings();
				Settings.Default.Save();
			}

			UsageReporter.Init(Settings.Default.Reporting, "saymore.palaso.org", "UA-22170471-3",
#if DEBUG
 true
#else
				false
#endif
);
		}
	}
}

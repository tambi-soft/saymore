using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Palaso.Code;

namespace SayMore.UI.NewEventsFromFiles
{
	/// ----------------------------------------------------------------------------------------
	/// <summary>
	/// Handles the logic and functionality of copying large files and giving visual feedback.
	/// Used with a view which does the actual
	/// </summary>
	/// ----------------------------------------------------------------------------------------
	public class CopyFilesViewModel : IDisposable
	{
		private const int kUpdateStatus = -1;

		private readonly KeyValuePair<string, string>[] _sourceDestinationPathPairs;
		private readonly long _totalBytes;
		private BackgroundWorker _worker;
		private long _totalBytesCopied;
		private Exception _encounteredError;

		public event EventHandler OnFinished;
		public event EventHandler OnUpdateProgress;
		public event EventHandler OnUpdateStatus;

		public int IndexOfCurrentFile { get; private set; }

		/// ------------------------------------------------------------------------------------
		public CopyFilesViewModel(IEnumerable<KeyValuePair<string, string>> sourceDestinationPathPairs)
		{
			Guard.Against(sourceDestinationPathPairs.Count() == 0, "No Files To Copy");
			_sourceDestinationPathPairs = sourceDestinationPathPairs.ToArray();
			_totalBytes = 0;

			foreach (var pair in sourceDestinationPathPairs)
				_totalBytes += new FileInfo(pair.Key).Length;

			IndexOfCurrentFile = -1;
			BeforeFileCopiedAction = source => { };
		}

		/// ------------------------------------------------------------------------------------
		public void Dispose()
		{
			if (_worker != null)
				_worker.Dispose();
		}

		/// ------------------------------------------------------------------------------------
		public int TotalPercentage
		{
			get { return (int)(_totalBytes == 0 ? 0 : 100 * (_totalBytesCopied/(double)_totalBytes)); }
		}

		/// ------------------------------------------------------------------------------------
		public bool Copying
		{
			get { return IndexOfCurrentFile >= 0; }
		}

		/// ------------------------------------------------------------------------------------
		public bool Finished
		{
			get { return IndexOfCurrentFile == -2; }
		}

		/// ------------------------------------------------------------------------------------
		public string StatusString
		{
			get
			{
				if (_encounteredError != null)
				{
					return string.Format("Copying failed: {0}", _encounteredError.Message);//todo: these won't be user friendly
				}

				if (Copying)
				{
					return string.Format("Copying {0} of {1} Files, ({2} of {3} Megabytes)",
						1 + IndexOfCurrentFile, _sourceDestinationPathPairs.Count(),
						Megs(_totalBytesCopied), Megs(_totalBytes));
				}

				return (Finished ? "Finished" : "Waiting to start...");
			}

			set { throw new NotImplementedException(); }
		}

		/// ------------------------------------------------------------------------------------
		/// <summary>
		/// Called just before a file is copied.
		/// </summary>
		/// ------------------------------------------------------------------------------------
		public Action<string> BeforeFileCopiedAction { get; set; }

		/// ------------------------------------------------------------------------------------
		private string Megs(long bytes)
		{
			return (bytes / (1024 * 1024)).ToString();
		}

		/// ------------------------------------------------------------------------------------
		public void Start()
		{
			_worker = new BackgroundWorker();
			_worker.WorkerSupportsCancellation = true;
			_worker.WorkerReportsProgress = true;
			_worker.ProgressChanged += HandleWorkerProgressChanged;
			_worker.RunWorkerCompleted += HandleWorkerFinished;
			_worker.DoWork += DoCopying;
			_worker.RunWorkerAsync();
			while (_worker.IsBusy) { Application.DoEvents(); }
		}

		/// ------------------------------------------------------------------------------------
		public void DoCopying(object sender, DoWorkEventArgs args)
		{
			try
			{
				for (IndexOfCurrentFile = 0; IndexOfCurrentFile < _sourceDestinationPathPairs.Count(); IndexOfCurrentFile++)
				{
					if (_worker.CancellationPending)
						continue;

					var pair = _sourceDestinationPathPairs[IndexOfCurrentFile];
					_worker.ReportProgress(kUpdateStatus, pair.Key);
					var sourceFileInfo = new FileInfo(pair.Key);

					if (CheckIfDestFileExists(sourceFileInfo, pair.Value))
						continue;

					if (_encounteredError != null)
					{
						_worker.CancelAsync();
						return;
					}

					try
					{
						CopyFile(sourceFileInfo, pair.Value);
					}
					catch (Exception)
					{
						if (File.Exists(pair.Value))
							File.Delete(pair.Value);

						throw;
					}
				}

				IndexOfCurrentFile = -2;
			}
			catch(Exception e)
			{
				_encounteredError = e;
				_worker.CancelAsync();
			}
			finally
			{
				IndexOfCurrentFile = -2;
			}
		}

		/// ------------------------------------------------------------------------------------
		private bool CheckIfDestFileExists(FileInfo srcFile, string dstFile)
		{
			if (!File.Exists(dstFile))
				return false;

			var finfo = new FileInfo(dstFile);
			if (finfo.CreationTimeUtc == srcFile.CreationTimeUtc &&
				finfo.Length == srcFile.Length &&
				finfo.LastWriteTimeUtc == srcFile.LastWriteTimeUtc)
			{
				// enhance.. would be better if we have a reporting method we could talk to,
				// that this would be up to the UI how/when/whether to display something.
				Palaso.Reporting.ErrorReport.NotifyUserOfProblem(
					"The file {0} appears unchanged since it was copied before, so it will be skipped.",
					Path.GetFileName(dstFile));

				_totalBytesCopied += finfo.Length;
				_worker.ReportProgress(1);
				return true;
			}

			_encounteredError = new ApplicationException(string.Format("The file '{0}' already exists", dstFile));
			_worker.CancelAsync();
			return false;
		}

		/// ------------------------------------------------------------------------------------
		private void CopyFile(FileInfo srcFile, string dstFile)
		{
			if (BeforeFileCopiedAction != null)
				BeforeFileCopiedAction(srcFile.FullName);

			var buffer = new byte[1000 * 1024];

			using (var source = new FileStream(srcFile.FullName, FileMode.Open))
			using (var dest = new FileStream(dstFile, FileMode.CreateNew))
			{
				int bytesRead;
				do
				{
					bytesRead = source.Read(buffer, 0, buffer.Length);

					if (bytesRead > 0)
					{
						dest.Write(buffer, 0, bytesRead);
						_totalBytesCopied += bytesRead;
					}

					_worker.ReportProgress(1);

				} while (bytesRead > 0);
			}

			new FileInfo(dstFile)
			{
				CreationTimeUtc = srcFile.CreationTimeUtc,
				LastWriteTimeUtc = srcFile.LastWriteTimeUtc,
			};

			srcFile.Attributes = (FileAttributes)(srcFile.Attributes - FileAttributes.Archive);//enhance... could be under control of the client
		}

		/// ------------------------------------------------------------------------------------
		void HandleWorkerFinished(object sender, RunWorkerCompletedEventArgs e)
		{
			if (OnFinished != null)
				OnFinished.Invoke(_encounteredError, null);
		}

		/// ------------------------------------------------------------------------------------
		void HandleWorkerProgressChanged(object sender, ProgressChangedEventArgs e)
		{
			if (e.ProgressPercentage == kUpdateStatus)
			{
				if (OnUpdateStatus != null)
					OnUpdateStatus(this, EventArgs.Empty);
			}
			else if (OnUpdateProgress != null)
			{
				OnUpdateProgress(this, EventArgs.Empty);
			}
		}
	}
}

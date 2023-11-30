// Copyright © 2004 Triamec Motion AG

//using log4net;
using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using Triamec.FrequencyResponse;
using Triamec.Diagnostics;
using Triamec.Tam.Samples;
using System.Diagnostics;

namespace Triamec.Tam.FrequencyResponse.NUnit {
	/// <summary>
	/// Handler of events from a FrequencyResponse measurement instance.
	/// </summary>
	internal class FrequencyResponseLogicCallback {

		#region Read-only fields
		/// <summary>
		/// Signal to set when the Frequency Response measurement has completed.
		/// </summary>
		readonly AutoResetEvent _signal;

		/// <summary>
		/// The format provider to use.
		/// </summary>
		readonly CultureInfo _formatProvider;

		/// <summary>
		/// The log to log to.
		/// </summary>
		//readonly ILog _log;

		#endregion Read-only fields

		#region Constructor / Disposing
		/// <summary>
		/// Initializes a new instance of the <see cref="FrequencyResponseLogicCallback"/> class.
		/// </summary>
		/// <param name="logic">The Frequency Response measurement instance.</param>
		/// <param name="signal">The signal to set when the Frequency Response measurement has completed.</param>
		/// <param name="testFixture">A Reference to test data.</param>
		/// <param name="formatProvider">The format provider.</param>
		/// <param name="log">The log.</param>
		/// <exception cref="ArgumentNullException">One of the arguments is <see langword="null"/>.</exception>
		public FrequencyResponseLogicCallback(IFrequencyResponseLogic logic, AutoResetEvent signal,
			CultureInfo formatProvider) {

			if (logic == null) throw new ArgumentNullException(nameof(logic));
			logic.GetFrequencyResponseResultCompleted += OnGetFrequencyResponseResultCompletedEvent;

			_signal = signal ?? throw new ArgumentNullException(nameof(signal));
			//if (testFixture == null) throw new ArgumentNullException(nameof(testFixture));
			_formatProvider = formatProvider;
			string testDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Frequency Response");
			if (!Directory.Exists(testDir)) {
				Directory.CreateDirectory(testDir);
			}
			ResultFile = Path.Combine(testDir, "result.csv");
		}

		#endregion Constructor / Disposing

		#region Properties
		/// <summary>
		/// Gets the file to save the Frequency Response result to.
		/// </summary>
		internal string ResultFile { get; }

		#endregion Properties

		#region Frequency Response measurement callbacks
		/// <summary>
		/// Called when the Frequency Response measurement has completed.
		/// </summary>
		/// <param name="sender">The sender.</param>
		/// <param name="args">The <see cref="FrequencyResponseResultCompletedEventArgs"/> instance containing the event data.
		/// </param>
		void OnGetFrequencyResponseResultCompletedEvent(object sender, FrequencyResponseResultCompletedEventArgs args) {
			// use custom culture to save the file
			Thread.CurrentThread.CurrentCulture = _formatProvider;

			FrequencyResponseResult result = args.Result;
			if (result != null) {
				for (int i = 0; i < result.ResponseCount; ++i) {
					var builder = new StringBuilder();
					builder.AppendFormat(_formatProvider, "{0}", result.GetFrequency(i));
					for (int j = 0; j < result.Count; j++) {
						builder.AppendFormat(_formatProvider, ",{0},{1}", result.GetFrequency(i),
							result.Getresult(i)[j].Real, result.Getresult(i)[j].Imaginary);
					}
					//_log.Info(builder);
				}
				args.Result.Save(ResultFile);

				_signal.Set();
			}
		}
		#endregion Frequency Response measurement callbacks
	}
}

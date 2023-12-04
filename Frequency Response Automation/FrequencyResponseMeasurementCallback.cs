// Copyright © 2004 Triamec Motion AG

//using log4net;
using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using Triamec.FrequencyResponseAnalysis;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Triamec.Tam.Samples {
    /// <summary>
    /// Handler of events from a FrequencyResponse measurement instance.
    /// </summary>
    internal class FrequencyResponseMeasurementCallback {

        #region Read-only fields
        readonly TaskCompletionSource<object> _tcs;
        #endregion Read-only fields

        #region Constructor / Disposing
        /// <summary>
        /// Initializes a new instance of the <see cref="FrequencyResponseMeasurementCallback"/> class.
        /// </summary>
        /// <param name="measurement">The Frequency Response measurement instance.</param>
        /// <exception cref="ArgumentNullException">One of the arguments is <see langword="null"/>.</exception>
        public FrequencyResponseMeasurementCallback(TaskCompletionSource<object> tcs, FrequencyResponseMeasurement measurement) {

            if (measurement == null) throw new ArgumentNullException(nameof(measurement));
            measurement.MeasureFrequencyResponseProgressChanged += OnGetFrequencyResponseResultProgressChanged;
            measurement.MeasureFrequencyResponseCompleted += OnGetFrequencyResponseResultCompletedEvent;
            _tcs = tcs;

            string testDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Frequency Response");
            if (!Directory.Exists(testDir)) {
                Directory.CreateDirectory(testDir);
            }
            ResultFile = Path.Combine(testDir, $"result_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
        }
        #endregion Constructor / Disposing

        #region Properties
        /// <summary>
        /// Gets the file to save the Frequency Response result to.
        /// </summary>
        internal string ResultFile { get; }

        #endregion Properties

        #region Frequency Response measurement callbacks
        void OnGetFrequencyResponseResultProgressChanged(object sender, MeasureFrequencyResponseProgressChangedEventArgs e) {
            if (e.Error == null) {
                Debug.WriteLine($"Measured at {e.Parameters.Frequency}Hz.");
            } else {

                // this may be just a warning, but may also be an unrecoverable error.
                Debug.WriteLine($"{e.Parameters.Frequency}Hz: {e.Error.FullMessage()}");
            }
        }

        /// <summary>
        /// Called when the Frequency Response measurement has completed.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="args">The instance containing the event data.
        /// </param>
        void OnGetFrequencyResponseResultCompletedEvent(object sender, MeasureFrequencyResponseCompletedEventArgs args) {
            Task.Run(() => {
                if (args.Canceled) {
                    _tcs.TrySetCanceled();
                } else {

                    // measurement may fail, but we can still save the partial result
                    if (args.Failure != null) {
                        Debug.WriteLine($"Measurement failed: {args.Failure.FullMessage()}");
                    }

                    FrequencyResponse result = args.Result;
                    if (result != null) {

                        args.Result.Save(ResultFile);

                        _tcs.SetResult(null);
                    } else {
                        _tcs.SetException(new Exception("Measurement failed."));
                    }
                }
            });
        }
        #endregion Frequency Response measurement callbacks
    }
}

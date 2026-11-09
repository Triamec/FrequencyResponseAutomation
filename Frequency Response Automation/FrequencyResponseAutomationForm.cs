using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Triamec.Tam.Commissioning.FrequencyResponseAnalysis;
using Triamec.Tam.Commissioning.FrequencyResponseAnalysis.Configuration;
using Triamec.Tam.Motion;
using Triamec.Tam.Samples.Properties;

namespace Triamec.Tam.Samples {
    /// <summary>
    /// The main form of the TAM "Frequency Response Automation" application.
    /// </summary>
    internal partial class FrequencyResponseAutomationForm : Form {
        #region Constructor
        /// <summary>
        /// Initializes a new instance of the <see cref="FrequencyResponseAutomationForm"/> class.
        /// </summary>
        public FrequencyResponseAutomationForm() {
            InitializeComponent();
        }
        #endregion Constructor

        #region Frequency Response measurement parameters

        const int MeasurementFrequency = 100000; // [Hz]
        const int MinimumFrequency = 300; // [Hz]
        const int MaximumFrequency = 400; // [Hz]
        const int NumberOfSamples = 3; // [-]
        static readonly FrequencySpacing FrequencySpacing = FrequencySpacing.Optimized;
        const string SelectedMethod = "Closed Loop";
        static readonly double[] ExcitationLimits = [13.8, 0.5, 0.5];
        static readonly double[] MeasurementPositions = [30.0, 90.0, 120.0];
        const float MoveToPositionVelocity = 60;
        static readonly bool DoBackAndForthMove = true;
        const float BackAndForthDistance = 30;
        const float BackAndForthVelocity = 10;

        #endregion Frequency Response measurement parameters


        #region Frequency Response Automation code
        /// <summary>
        /// The name of the axis this demo works with.
        /// </summary>
        // CAUTION!
        // Selecting the wrong axis can have unintended consequences.
        const string AxisName = "Axis 1";

        /// <summary>
        /// The distance to move when pressing one of the move buttons.
        /// </summary>
        // CAUTION!
        // The unit of this constant depends on the PositionUnit parameter provided with the TAM configuration.
        // Additionally, the encoder must be correctly configured.
        // Consider any limit stops.
        const double Distance = 0.5 * Math.PI;

        IMotionSystem19 _system;
        IAxis19 _axis;

        /// <summary>
        /// Prepares the TAM system.
        /// </summary>
        /// <exception cref="ConnectionException">Startup failed.</exception>
        /// <remarks>
        /// 	<list type="bullet">
        /// 		<item><description>Creates a TAM topology,</description></item>
        /// 		<item><description>boots the Tria-Link,</description></item>
        /// 		<item><description>searches for a servo-drive,</description></item>
        /// 		<item><description>loads and applies a TAM configuration.</description></item>
        /// 	</list>
        /// </remarks>
        async Task Startup() {
            _system = await MotionSystem.Connect19();

            // Find the axis with the configured name.
            _axis = _system.Axes.FirstOrDefault(a => a.Name == AxisName) ??
                throw new ConnectionException(Resources.NoAxisMessage);

            // Start displaying the position in regular intervals.
            _timer.Start();
        }

        /// <summary>
        /// Moves in the specified direction.
        /// </summary>
        /// <param name="sign">A positive or negative value indicating the direction of the motion.</param>
        async void MoveAxis(int sign) {
            try {
                // Move a distance with dedicated velocity.
                // If the axis is just moving, it is reprogrammed with this command.
                await _axis.MoveRelative(Math.Sign(sign) * Distance);
            } catch (Exception ex) when (ex is InvalidOperationException || ex is System.TimeoutException) {
                MessageBox.Show(ex.Message, Resources.MoveErrorCaption, MessageBoxButtons.OK,
                    MessageBoxIcon.Information, MessageBoxDefaultButton.Button1, 0);
            }
        }

        async Task Measure() {
            // Does not contain any asserts, but ensures the principal Frequency Response acquirement mechanism is tested
            Debug.WriteLine("Starting measure");

            var controlSystem = SetupControlSystem();

            var (measurement, task) = StartFrequencyResponse(controlSystem);
            var wait = new TimeSpan(0, 0, 3, 0, 0);
            try {
                await task.WaitAsync(wait);
            } catch (System.TimeoutException) {
                measurement.MeasureFrequencyResponseCancel();
                Console.WriteLine(string.Format("The test duration exceeded {0} minutes", wait.TotalMinutes));
                return;
            } finally {
                measurement.Dispose();
                controlSystem.Tidy();
            }
        }

        async Task MoveBackAndForth(float backAndForthDistance, float backAndForthVelocity, CancellationToken cancellationToken) {
            Debug.WriteLine("Starting back and forth move");
            float currentReferencePosition = _axis.Signals.PathPlanner.PositionFloat.Read();
            while (!cancellationToken.IsCancellationRequested) {
                try {
                    await _axis.MoveAbsolute(currentReferencePosition + backAndForthDistance / 2,
                                                     backAndForthVelocity, direction: ModuloDirection.Positive);
                    if (cancellationToken.IsCancellationRequested) break;
                    await _axis.MoveAbsolute(currentReferencePosition - backAndForthDistance / 2,
                                                 -backAndForthVelocity, direction: ModuloDirection.Negative);

                } catch (TaskCanceledException) {
                    // reprogramming comes from Stop, this is OK
                }
            }
        }

        #endregion Frequency Response Automation code

        #region GUI handler methods
        #region Form handler methods

        protected override async void OnLoad(EventArgs e) {
            base.OnLoad(e);

            try {
                await Startup();
                _driveGroupBox.Enabled = true;
            } catch (ConnectionException ex) {
                MessageBox.Show(this, ex.FullMessage(), Resources.StartupErrorCaption, MessageBoxButtons.OK,
                    MessageBoxIcon.Error, MessageBoxDefaultButton.Button1, 0);
            }
        }

        protected override async void OnFormClosed(FormClosedEventArgs e) {
            base.OnFormClosed(e);
            if (_axis != null) {
                try {
                    await _axis.Disable();
                } catch (Exception ex) when (ex is InvalidOperationException || ex is System.TimeoutException) {
                    MessageBox.Show(this, ex.Message, Resources.StartupErrorCaption, MessageBoxButtons.OK,
                        MessageBoxIcon.Error, MessageBoxDefaultButton.Button1, 0);
                }
            }
        }
        #endregion Form handler methods

        #region Button handler methods

        async void OnEnableButtonClick(object sender, EventArgs e) {
            try {
                await _axis.Enable();
            } catch (Exception ex) when (ex is InvalidOperationException || ex is System.TimeoutException) {
                MessageBox.Show(ex.Message, Resources.EnablingErrorCaption, MessageBoxButtons.OK,
                    MessageBoxIcon.Information, MessageBoxDefaultButton.Button1, 0);
            }

            // Note: a more elaborated application would change button states depending on what's the drive reporting,
            // following the MVC concept.
            _moveNegativeButton.Enabled = true;
            _movePositiveButton.Enabled = true;
        }

        async void OnDisableButtonClick(object sender, EventArgs e) {
            _moveNegativeButton.Enabled = false;
            _movePositiveButton.Enabled = false;
            try {
                await _axis.Disable();
            } catch (Exception ex) when (ex is InvalidOperationException || ex is System.TimeoutException) {
                MessageBox.Show(ex.Message, Resources.DisablingErrorCaption, MessageBoxButtons.OK,
                    MessageBoxIcon.Information, MessageBoxDefaultButton.Button1, 0);
            }
        }

        void OnMoveNegativeButtonClick(object sender, EventArgs e) => MoveAxis(-1);

        void OnMovePositiveButtonClick(object sender, EventArgs e) => MoveAxis(1);

        async void OnMeasureButtonClick(object sender, EventArgs e) {
            try {
                _measureButton.Enabled = false;

                for (int i = 0; i < MeasurementPositions.Length; i++) {
                    CancellationTokenSource cts = new();
                    CancellationToken cancellationToken = cts.Token;

                    await _axis.MoveAbsolute(MeasurementPositions[i], MoveToPositionVelocity);

                    Task moveTask;
                    if (DoBackAndForthMove) {
                        if (SelectedMethod == "Closed Loop") {
                            moveTask = MoveBackAndForth(BackAndForthDistance, BackAndForthVelocity, cancellationToken);
                        } else {
                            throw new Exception("Back and Forth move is only possible in Closed Loop");
                        }
                    } else {
                        moveTask = null;
                    }
                    await Measure();
                    cts.Cancel();
                    await _axis.Stop();
                    if (moveTask != null) {
                        await moveTask;
                    }
                }

            } catch (TamException ex) {
                MessageBox.Show(ex.Message, Resources.MoveErrorCaption, MessageBoxButtons.OK,
                    MessageBoxIcon.Information, MessageBoxDefaultButton.Button1, 0);
            } finally {
                _measureButton.Enabled = true;
            }
        }

        Commissioning.FrequencyResponseAnalysis.IControlSystem SetupControlSystem() {
            var result = _axis.AsTamAxis().AsControlSystem();

            // open loop
            result.MeasurementMethod = FrequencyResponseConfig.Read().MeasurementMethods[0];

            return result;
        }
        #endregion SetupFrequencyResponseAxis

        #region FrequencyResponseLogic helpers
        /// <summary>
        /// Create the Frequency Response logic on another thread than the thread who will wait for completion
        /// </summary>
        /// <param name="system">The axis.</param>
        /// <param name="log">The log.</param>
        /// <returns>
        /// The created resource that must be managed by the caller.
        /// </returns>
        static (FrequencyResponseMeasurement measurement, Task task) StartFrequencyResponse(
            Commissioning.FrequencyResponseAnalysis.IControlSystem system) {

            string desiredMethod = SelectedMethod;
            var methods = FrequencyResponseConfig.Read()
                                   .MeasurementMethods
                                   .Where(system.SupportsMethod)
                                   .ToArray();
            system.MeasurementMethod = methods.Single(method => method.Name == desiredMethod);
            system.SamplingTime = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / MeasurementFrequency);

            var measurement = new FrequencyResponseMeasurement();
            var parameters = new FrequencyResponseMeasurementParameters(system) {
                FrequencyRangeMinimum = MinimumFrequency,
                FrequencyRangeMaximum = MaximumFrequency,
                FrequencySteps = NumberOfSamples,
                Spacing = FrequencySpacing,
                SettlingTime = TimeSpan.FromSeconds(0.2),
            };
            for (int i = 0; i < ExcitationLimits.Length; i++) {
                parameters.SetMeasuringPointMaximum(i, ExcitationLimits[i]);
            }

            var tcs = new TaskCompletionSource<object>();

            _ = new FrequencyResponseMeasurementCallback(tcs, measurement);
            measurement.MeasureFrequencyResponseAsync(system, parameters);
            return (measurement, tcs.Task);
        }

        #endregion FrequencyResponseLogic helpers
        #endregion Button handler methods

        #region Menu handler methods

        void OnExitMenuItemClick(object sender, EventArgs e) => Close();
        #endregion Menu handler methods

        #region Timer methods
        //void OnTimerTick(object sender, EventArgs e) => ReadPosition();

        #endregion Timer methods
    }
}
